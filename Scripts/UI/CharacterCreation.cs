using Godot;
using System;
using System.Collections.Generic;
using OpenFo3.Game;

namespace OpenFo3.UI
{
    public enum LifeStage
    {
        Baby,    // Can crawl
        Child,   // Can walk, birthday party
        Teen,    // G.O.A.T test, can run
        Adult    // Escape, full abilities
    }

    public partial class CharacterCreation : CanvasLayer
    {
        [Signal]
        public delegate void CharacterFinalizedEventHandler(string playerName, int[] specialValues, bool isMale);

        private Control _creationPanel;
        private Control _goatPanel;

        // Name input
        private LineEdit _nameInput;
        private Button _confirmNameButton;

        // Gender selection
        private Button _maleButton;
        private Button _femaleButton;
        private bool _isMale = true;

        // SPECIAL allocation (7 attributes, total 40, each 1-10)
        private int[] _specialPoints = new int[7] { 5, 5, 5, 5, 5, 5, 5 };
        private int _remainingPoints = 5; // Can redistribute 5 points
        private Label[] _specialLabels = new Label[7];
        private Button[] _specialPlus = new Button[7];
        private Button[] _specialMinus = new Button[7];

        // Goat test questions and results
        private int _goatQuestionIndex;
        private List<GoatQuestion> _goatQuestions;
        private Label _questionLabel;
        private VBoxContainer _answersContainer;
        private Label _goatTitle;
        private Label _goatResultLabel;

        // Appearance (simplified)
        private ColorPickerButton _skinColorPicker;
        private ColorPickerButton _hairColorPicker;

        // Current stage display
        private Label _titleLabel;

        private class GoatQuestion
        {
            public string Question;
            public List<GoatAnswer> Answers = new();
        }

        private class GoatAnswer
        {
            public string Text;
            public SkillName PrimarySkill; // Skill that gets bonus
            public float Weight = 1.0f;
        }

        // GOAT result: skill scores accumulated
        private float[] _goatSkillScores = new float[13];

        public CharacterCreation()
        {
            InitializeGoatQuestions();
        }

        public override void _Ready()
        {
            Layer = 400;
            Visible = false;
            CreateCreationUI();
            CreateGoatUI();
        }

        private void CreateCreationUI()
        {
            _creationPanel = new Control();
            _creationPanel.Name = "CreationPanel";
            _creationPanel.Visible = false;
            AddChild(_creationPanel);

            _titleLabel = new Label();
            _titleLabel.Text = "CHARACTER CREATION";
            _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _titleLabel.Position = new Vector2(0, 30);
            _titleLabel.Size = new Vector2(1920, 50);
            _titleLabel.LabelSettings = new LabelSettings
            {
                FontSize = 36,
                FontColor = new Color(0, 1f, 0.3f),
                OutlineSize = 2,
                OutlineColor = new Color(0, 0, 0, 0.8f),
            };
            _creationPanel.AddChild(_titleLabel);

            // Name input
            var nameLabel = new Label();
            nameLabel.Text = "NAME:";
            nameLabel.Position = new Vector2(760, 100);
            nameLabel.LabelSettings = new LabelSettings { FontSize = 20, FontColor = new Color(0, 1f, 0.3f) };
            _creationPanel.AddChild(nameLabel);

            _nameInput = new LineEdit();
            _nameInput.PlaceholderText = "Enter your name...";
            _nameInput.Position = new Vector2(760, 135);
            _nameInput.Size = new Vector2(300, 35);
            _nameInput.Text = "Lone Wanderer";
            _creationPanel.AddChild(_nameInput);

            // Gender
            var genderLabel = new Label();
            genderLabel.Text = "GENDER:";
            genderLabel.Position = new Vector2(760, 190);
            genderLabel.LabelSettings = new LabelSettings { FontSize = 20, FontColor = new Color(0, 1f, 0.3f) };
            _creationPanel.AddChild(genderLabel);

            _maleButton = new Button();
            _maleButton.Text = "MALE";
            _maleButton.Position = new Vector2(760, 225);
            _maleButton.Pressed += () => SetGender(true);
            _creationPanel.AddChild(_maleButton);

            _femaleButton = new Button();
            _femaleButton.Text = "FEMALE";
            _femaleButton.Position = new Vector2(880, 225);
            _femaleButton.Pressed += () => SetGender(false);
            _creationPanel.AddChild(_femaleButton);

            SetGender(true);

            // SPECIAL allocation
            var specialTitle = new Label();
            specialTitle.Text = "S.P.E.C.I.A.L. (Click +/- to customize)";
            specialTitle.Position = new Vector2(760, 280);
            specialTitle.LabelSettings = new LabelSettings { FontSize = 16, FontColor = new Color(0, 1f, 0.3f) };
            _creationPanel.AddChild(specialTitle);

            string[] specialNames = { "STR", "PER", "END", "CHA", "INT", "AGI", "LCK" };
            for (int i = 0; i < 7; i++)
            {
                int idx = i;
                var attrLabel = new Label();
                attrLabel.Text = specialNames[i];
                attrLabel.Position = new Vector2(760, 315 + i * 35);
                attrLabel.Size = new Vector2(50, 30);
                attrLabel.LabelSettings = new LabelSettings { FontSize = 16, FontColor = new Color(0, 1f, 0.3f) };
                _creationPanel.AddChild(attrLabel);

                _specialMinus[i] = new Button();
                _specialMinus[i].Text = "-";
                _specialMinus[i].Position = new Vector2(820, 315 + i * 35);
                _specialMinus[i].Size = new Vector2(30, 30);
                _specialMinus[i].Disabled = true;
                _specialMinus[i].Pressed += () => AdjustSpecial(idx, -1);
                _creationPanel.AddChild(_specialMinus[i]);

                _specialLabels[i] = new Label();
                _specialLabels[i].Text = "5";
                _specialLabels[i].Position = new Vector2(860, 315 + i * 35);
                _specialLabels[i].Size = new Vector2(40, 30);
                _specialLabels[i].HorizontalAlignment = HorizontalAlignment.Center;
                _specialLabels[i].LabelSettings = new LabelSettings { FontSize = 18, FontColor = new Color(0, 1f, 0.3f) };
                _creationPanel.AddChild(_specialLabels[i]);

                _specialPlus[i] = new Button();
                _specialPlus[i].Text = "+";
                _specialPlus[i].Position = new Vector2(910, 315 + i * 35);
                _specialPlus[i].Size = new Vector2(30, 30);
                _specialPlus[i].Pressed += () => AdjustSpecial(idx, 1);
                _creationPanel.AddChild(_specialPlus[i]);
            }

            // Remaining points
            var remainingLabel = new Label();
            remainingLabel.Name = "RemainingLabel";
            remainingLabel.Text = "Remaining: 5";
            remainingLabel.Position = new Vector2(760, 570);
            remainingLabel.LabelSettings = new LabelSettings { FontSize = 18, FontColor = new Color(0.8f, 0.8f, 0, 0.3f) };
            _creationPanel.AddChild(remainingLabel);

            // Confirm button
            var confirmButton = new Button();
            confirmButton.Text = "CONFIRM & START";
            confirmButton.Position = new Vector2(800, 620);
            confirmButton.Size = new Vector2(200, 40);
            confirmButton.Pressed += OnCreationConfirmed;
            _creationPanel.AddChild(confirmButton);
        }

        private void CreateGoatUI()
        {
            _goatPanel = new Control();
            _goatPanel.Name = "GoatPanel";
            _goatPanel.Visible = false;
            AddChild(_goatPanel);

            _goatTitle = new Label();
            _goatTitle.Text = "G.O.A.T. - Generalized Occupational Aptitude Test";
            _goatTitle.HorizontalAlignment = HorizontalAlignment.Center;
            _goatTitle.Position = new Vector2(0, 30);
            _goatTitle.Size = new Vector2(1920, 40);
            _goatTitle.LabelSettings = new LabelSettings { FontSize = 24, FontColor = new Color(0, 1f, 0.3f), OutlineSize = 2, OutlineColor = new Color(0, 0, 0, 0.8f) };
            _goatPanel.AddChild(_goatTitle);

            var subTitle = new Label();
            subTitle.Text = "Please answer each question honestly to determine your ideal career path.";
            subTitle.HorizontalAlignment = HorizontalAlignment.Center;
            subTitle.Position = new Vector2(0, 75);
            subTitle.Size = new Vector2(1920, 25);
            subTitle.LabelSettings = new LabelSettings { FontSize = 14, FontColor = new Color(0, 0.8f, 0.2f) };
            _goatPanel.AddChild(subTitle);

            _questionLabel = new Label();
            _questionLabel.Position = new Vector2(460, 130);
            _questionLabel.Size = new Vector2(1000, 60);
            _questionLabel.LabelSettings = new LabelSettings { FontSize = 18, FontColor = new Color(1, 1, 1) };
            _goatPanel.AddChild(_questionLabel);

            _answersContainer = new VBoxContainer();
            _answersContainer.Position = new Vector2(510, 210);
            _answersContainer.Size = new Vector2(900, 400);
            _goatPanel.AddChild(_answersContainer);

            _goatResultLabel = new Label();
            _goatResultLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _goatResultLabel.Position = new Vector2(0, 400);
            _goatResultLabel.Size = new Vector2(1920, 100);
            _goatResultLabel.Visible = false;
            _goatPanel.AddChild(_goatResultLabel);
        }

        private void InitializeGoatQuestions()
        {
            _goatQuestions = new List<GoatQuestion>
            {
                new()
                {
                    Question = "Someone who is quiet and observant, you tend to notice details others miss. When solving a problem, you prefer to:",
                    Answers = new List<GoatAnswer>
                    {
                        new() { Text = "Take it apart and fix it yourself", PrimarySkill = SkillName.Repair, Weight = 1.5f },
                        new() { Text = "Research the problem thoroughly first", PrimarySkill = SkillName.Science, Weight = 1.5f },
                        new() { Text = "Find a creative workaround", PrimarySkill = SkillName.Sneak, Weight = 1.0f },
                        new() { Text = "Ask someone with experience", PrimarySkill = SkillName.Barter, Weight = 1.0f },
                    }
                },
                new()
                {
                    Question = "When faced with a locked door, what do you do?",
                    Answers = new List<GoatAnswer>
                    {
                        new() { Text = "Pick the lock", PrimarySkill = SkillName.Lockpick, Weight = 2.0f },
                        new() { Text = "Find the key or a terminal to unlock it", PrimarySkill = SkillName.Science, Weight = 1.0f },
                        new() { Text = "Kick it down", PrimarySkill = SkillName.MeleeWeapons, Weight = 1.0f },
                        new() { Text = "Look for another way around", PrimarySkill = SkillName.Sneak, Weight = 1.0f },
                    }
                },
                new()
                {
                    Question = "In a combat situation, you prefer to:",
                    Answers = new List<GoatAnswer>
                    {
                        new() { Text = "Use a reliable firearm at range", PrimarySkill = SkillName.SmallGuns, Weight = 2.0f },
                        new() { Text = "Get up close and personal", PrimarySkill = SkillName.MeleeWeapons, Weight = 1.5f },
                        new() { Text = "Use heavy firepower", PrimarySkill = SkillName.BigGuns, Weight = 1.5f },
                        new() { Text = "Avoid combat entirely", PrimarySkill = SkillName.Speech, Weight = 1.0f },
                    }
                },
                new()
                {
                    Question = "An injured traveler needs help. You:",
                    Answers = new List<GoatAnswer>
                    {
                        new() { Text = "Use your medical knowledge to treat them", PrimarySkill = SkillName.Medicine, Weight = 2.0f },
                        new() { Text = "Give them supplies and send them on their way", PrimarySkill = SkillName.Barter, Weight = 1.0f },
                        new() { Text = "Offer to escort them to safety", PrimarySkill = SkillName.EnergyWeapons, Weight = 1.0f },
                        new() { Text = "Check their pockets while they're distracted", PrimarySkill = SkillName.Sneak, Weight = 1.0f },
                    }
                },
                new()
                {
                    Question = "What skill would you most like to improve?",
                    Answers = new List<GoatAnswer>
                    {
                        new() { Text = "Talking my way out of trouble", PrimarySkill = SkillName.Speech, Weight = 2.0f },
                        new() { Text = "Shooting with precision", PrimarySkill = SkillName.SmallGuns, Weight = 1.5f },
                        new() { Text = "Building and fixing things", PrimarySkill = SkillName.Repair, Weight = 1.5f },
                        new() { Text = "Moving unseen", PrimarySkill = SkillName.Sneak, Weight = 1.5f },
                    }
                },
            };
        }

        public void ShowCreation()
        {
            Visible = true;
            _creationPanel.Visible = true;
            _goatPanel.Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        public void ShowGoatTest()
        {
            Visible = true;
            _creationPanel.Visible = false;
            _goatPanel.Visible = true;
            _goatQuestionIndex = 0;
            Array.Clear(_goatSkillScores, 0, _goatSkillScores.Length);
            ShowCurrentQuestion();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        public void HideAll()
        {
            Visible = false;
            _creationPanel.Visible = false;
            _goatPanel.Visible = false;
        }

        private void SetGender(bool isMale)
        {
            _isMale = isMale;
            _maleButton.Modulate = isMale ? new Color(1, 1, 1) : new Color(0.5f, 0.5f, 0.5f);
            _femaleButton.Modulate = isMale ? new Color(0.5f, 0.5f, 0.5f) : new Color(1, 1, 1);
        }

        private void AdjustSpecial(int index, int delta)
        {
            int oldVal = _specialPoints[index];
            int newVal = oldVal + delta;

            if (newVal < 1 || newVal > 10) return;
            if (delta > 0 && _remainingPoints <= 0) return;
            if (delta < 0 && _remainingPoints >= 5) return;

            _specialPoints[index] = newVal;
            _remainingPoints -= delta;
            _specialLabels[index].Text = newVal.ToString();
            _specialMinus[index].Disabled = newVal <= 1;
            _specialPlus[index].Disabled = newVal >= 10 || _remainingPoints <= 0;

            UpdateSpecialDisplay();
        }

        private void UpdateSpecialDisplay()
        {
            foreach (var child in _creationPanel.GetChildren())
            {
                if (child is Label lbl && lbl.Name == "RemainingLabel")
                {
                    lbl.Text = $"Remaining: {_remainingPoints}";
                    lbl.LabelSettings.FontColor = _remainingPoints > 0
                        ? new Color(0.8f, 0.8f, 0, 0.3f)
                        : new Color(0, 1f, 0, 0.3f);
                }
            }
        }

        private void OnCreationConfirmed()
        {
            string name = _nameInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = "Lone Wanderer";
            HideAll();
            EmitSignal(SignalName.CharacterFinalized, name, _specialPoints, _isMale);
        }

        private void ShowCurrentQuestion()
        {
            if (_goatQuestionIndex >= _goatQuestions.Count)
            {
                FinishGoatTest();
                return;
            }

            var q = _goatQuestions[_goatQuestionIndex];
            _questionLabel.Text = $"Q{_goatQuestionIndex + 1}/{_goatQuestions.Count}: {q.Question}";

            // Clear previous answers
            foreach (var child in _answersContainer.GetChildren())
                child.QueueFree();

            for (int i = 0; i < q.Answers.Count; i++)
            {
                int answerIdx = i;
                var btn = new Button();
                btn.Text = $"  {q.Answers[i].Text}";
                btn.Size = new Vector2(800, 40);
                btn.Pressed += () => SelectGoatAnswer(answerIdx);
                _answersContainer.AddChild(btn);
            }
        }

        private void SelectGoatAnswer(int answerIndex)
        {
            if (_goatQuestionIndex >= _goatQuestions.Count) return;

            var answer = _goatQuestions[_goatQuestionIndex].Answers[answerIndex];
            _goatSkillScores[(int)answer.PrimarySkill] += answer.Weight;

            _goatQuestionIndex++;
            ShowCurrentQuestion();
        }

        private void FinishGoatTest()
        {
            // Find best skills
            var sortedSkills = new (SkillName Skill, float Score)[13];
            for (int i = 0; i < 13; i++)
                sortedSkills[i] = ((SkillName)i, _goatSkillScores[i]);
            Array.Sort(sortedSkills, (a, b) => b.Score.CompareTo(a.Score));

            string topSkills = "";
            for (int i = 0; i < 3 && i < sortedSkills.Length; i++)
            {
                if (sortedSkills[i].Score > 0)
                {
                    string skillName = sortedSkills[i].Skill switch
                    {
                        SkillName.Barter => "Barter",
                        SkillName.BigGuns => "Big Guns",
                        SkillName.EnergyWeapons => "Energy Weapons",
                        SkillName.Explosives => "Explosives",
                        SkillName.Lockpick => "Lockpick",
                        SkillName.Medicine => "Medicine",
                        SkillName.MeleeWeapons => "Melee Weapons",
                        SkillName.Repair => "Repair",
                        SkillName.Science => "Science",
                        SkillName.SmallGuns => "Small Guns",
                        SkillName.Sneak => "Sneak",
                        SkillName.Speech => "Speech",
                        SkillName.Unarmed => "Unarmed",
                        _ => "",
                    };
                    topSkills += $"\n- {skillName}";
                }
            }

            _questionLabel.Visible = false;
            _answersContainer.Visible = false;

            _goatResultLabel.Text = $"G.O.A.T. Test Complete!\nYour aptitudes suggest careers in:{topSkills}\n\nPress SPACE to begin your journey.";
            _goatResultLabel.Visible = true;
        }

        public override void _Input(InputEvent @event)
        {
            if (_goatResultLabel.Visible && @event is InputEventKey key && key.Pressed && key.Keycode == Key.Space)
            {
                HideAll();
                EmitSignal(SignalName.CharacterFinalized, _nameInput.Text, _specialPoints, _isMale);
            }
        }

        public string PlayerName => _nameInput.Text;
        public int[] SpecialValues => _specialPoints;
        public bool IsMale => _isMale;
    }
}
