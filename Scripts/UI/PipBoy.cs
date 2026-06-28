using Godot;
using System;
using System.Collections.Generic;
using OpenFo3.Player;
using OpenFo3.Game;

namespace OpenFo3.UI
{
    public partial class PipBoy : CanvasLayer
    {
        private static readonly Color PipBoyGreen = new Color(0, 1f, 0.3f);
        private static readonly Color PipBoyDark = new Color(0, 0.15f, 0.05f);
        private static readonly Color PipBoyAmber = new Color(1f, 0.7f, 0);

        private PlayerController _player;
        private CharacterStats _stats;
        private bool _isOpen;

        private int _currentTab = 0;
        private readonly string[] _tabNames = { "STAT", "INV", "DATA", "MAP", "RADIO" };
        private List<Control> _tabPanels = new();

        private Panel _bgPanel;
        private HBoxContainer _tabBar;
        private Control _statusTab;
        private Control _inventoryTab;
        private Control _dataTab;
        private Control _mapTab;
        private Control _radioTab;

        private Label _healthLabel, _apLabel, _radLabel;
        private Label _levelLabel, _xpLabel;
        private Label _specialLabels;
        private Label _skillLabels;
        private Label _perkListLabel;
        private Label _damageResistLabel;
        private Label _carryWeightLabel;

        private ItemList _invCategoryList;
        private ItemList _invItemList;
        private Label _invDetailLabel;
        private int _selectedCategory = 0;
        private readonly string[] _invCategories = { "WEAPONS", "APPAREL", "AID", "MISC", "AMMO" };

        private ItemList _questList;
        private Label _questDetailLabel;

        private ItemList _radioStationList;
        private Label _radioNowPlaying;

        private TextureRect _mapDisplay;
        private Label _locationLabel;

        public bool IsOpen => _isOpen;

        public void Setup(PlayerController player, CharacterStats stats)
        {
            _player = player;
            _stats = stats;
        }

        public override void _Ready()
        {
            Layer = 200;
            Visible = false;
            CreatePipBoyUI();
        }

        private void CreatePipBoyUI()
        {
            _bgPanel = new Panel();
            _bgPanel.Name = "PipBoyBg";
            _bgPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_bgPanel);

            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0, 0.02f, 0, 0.85f);
            bgStyle.BorderColor = PipBoyGreen;
            bgStyle.BorderWidthBottom = 2;
            bgStyle.BorderWidthTop = 2;
            bgStyle.BorderWidthLeft = 2;
            bgStyle.BorderWidthRight = 2;
            _bgPanel.AddThemeStyleboxOverride("panel", bgStyle);

            _tabBar = new HBoxContainer();
            _tabBar.Name = "TabBar";
            _tabBar.Position = new Vector2(20, 10);
            _tabBar.Size = new Vector2(0, 30);
            _bgPanel.AddChild(_tabBar);

            for (int i = 0; i < _tabNames.Length; i++)
            {
                int tabIdx = i;
                var tabBtn = new Button();
                tabBtn.Text = _tabNames[i];
                tabBtn.Pressed += () => SwitchTab(tabIdx);

                var normalStyle = new StyleBoxFlat();
                normalStyle.BgColor = Colors.Transparent;
                normalStyle.BorderWidthBottom = 2;
                normalStyle.BorderColor = PipBoyGreen;
                tabBtn.AddThemeStyleboxOverride("normal", normalStyle);

                var fontVar = new LabelSettings
                {
                    FontSize = 14,
                    FontColor = PipBoyGreen,
                    OutlineSize = 1,
                    OutlineColor = new Color(0, 0, 0, 0.5f),
                };
                _tabBar.AddChild(tabBtn);
            }

            _statusTab = CreateStatusTab();
            _inventoryTab = CreateInventoryTab();
            _dataTab = CreateDataTab();
            _mapTab = CreateMapTab();
            _radioTab = CreateRadioTab();

            _tabPanels = new List<Control> { _statusTab, _inventoryTab, _dataTab, _mapTab, _radioTab };
            foreach (var panel in _tabPanels)
                _bgPanel.AddChild(panel);

            SwitchTab(0);
        }

        private Control CreateStatusTab()
        {
            var tab = new Control();
            tab.Name = "StatusTab";
            tab.Position = new Vector2(20, 50);
            tab.Size = new Vector2(900, 600);
            tab.Visible = false;

            _healthLabel = new Label();
            _healthLabel.Position = new Vector2(10, 10);
            _healthLabel.LabelSettings = new LabelSettings { FontSize = 16, FontColor = new Color(0, 1f, 0) };
            tab.AddChild(_healthLabel);

            _apLabel = new Label();
            _apLabel.Position = new Vector2(10, 35);
            _apLabel.LabelSettings = new LabelSettings { FontSize = 16, FontColor = new Color(0, 0.8f, 1f) };
            tab.AddChild(_apLabel);

            _radLabel = new Label();
            _radLabel.Position = new Vector2(10, 60);
            _radLabel.LabelSettings = new LabelSettings { FontSize = 16, FontColor = new Color(0.6f, 1f, 0) };
            tab.AddChild(_radLabel);

            _levelLabel = new Label();
            _levelLabel.Position = new Vector2(10, 95);
            _levelLabel.LabelSettings = new LabelSettings { FontSize = 18, FontColor = PipBoyGreen };
            tab.AddChild(_levelLabel);

            _xpLabel = new Label();
            _xpLabel.Position = new Vector2(10, 120);
            _xpLabel.LabelSettings = new LabelSettings { FontSize = 14, FontColor = PipBoyGreen };
            tab.AddChild(_xpLabel);

            _specialLabels = new Label();
            _specialLabels.Position = new Vector2(10, 155);
            _specialLabels.LabelSettings = new LabelSettings { FontSize = 14, FontColor = PipBoyGreen };
            tab.AddChild(_specialLabels);

            _skillLabels = new Label();
            _skillLabels.Position = new Vector2(350, 10);
            _skillLabels.LabelSettings = new LabelSettings { FontSize = 13, FontColor = PipBoyGreen };
            tab.AddChild(_skillLabels);

            _perkListLabel = new Label();
            _perkListLabel.Position = new Vector2(350, 300);
            _perkListLabel.LabelSettings = new LabelSettings { FontSize = 13, FontColor = PipBoyGreen };
            tab.AddChild(_perkListLabel);

            _damageResistLabel = new Label();
            _damageResistLabel.Position = new Vector2(10, 450);
            _damageResistLabel.LabelSettings = new LabelSettings { FontSize = 13, FontColor = PipBoyGreen };
            tab.AddChild(_damageResistLabel);

            _carryWeightLabel = new Label();
            _carryWeightLabel.Position = new Vector2(10, 475);
            _carryWeightLabel.LabelSettings = new LabelSettings { FontSize = 13, FontColor = PipBoyGreen };
            tab.AddChild(_carryWeightLabel);

            return tab;
        }

        private Control CreateInventoryTab()
        {
            var tab = new Control();
            tab.Name = "InventoryTab";
            tab.Position = new Vector2(20, 50);
            tab.Size = new Vector2(900, 600);
            tab.Visible = false;

            _invCategoryList = new ItemList();
            _invCategoryList.Position = new Vector2(10, 10);
            _invCategoryList.Size = new Vector2(160, 500);
            _invCategoryList.AllowReselect = true;
            foreach (var cat in _invCategories)
                _invCategoryList.AddItem(cat);
            _invCategoryList.ItemSelected += (idx) => { _selectedCategory = (int)idx; RefreshInventoryItems(); };
            tab.AddChild(_invCategoryList);

            _invItemList = new ItemList();
            _invItemList.Position = new Vector2(180, 10);
            _invItemList.Size = new Vector2(320, 500);
            _invItemList.AllowReselect = true;
            tab.AddChild(_invItemList);

            _invDetailLabel = new Label();
            _invDetailLabel.Position = new Vector2(510, 10);
            _invDetailLabel.Size = new Vector2(370, 500);
            _invDetailLabel.LabelSettings = new LabelSettings { FontSize = 13, FontColor = PipBoyGreen };
            tab.AddChild(_invDetailLabel);

            return tab;
        }

        private Control CreateDataTab()
        {
            var tab = new Control();
            tab.Name = "DataTab";
            tab.Position = new Vector2(20, 50);
            tab.Size = new Vector2(900, 600);
            tab.Visible = false;

            _questList = new ItemList();
            _questList.Position = new Vector2(10, 10);
            _questList.Size = new Vector2(400, 500);
            tab.AddChild(_questList);

            _questDetailLabel = new Label();
            _questDetailLabel.Position = new Vector2(420, 10);
            _questDetailLabel.Size = new Vector2(460, 500);
            _questDetailLabel.LabelSettings = new LabelSettings { FontSize = 14, FontColor = PipBoyGreen };
            tab.AddChild(_questDetailLabel);

            return tab;
        }

        private Control CreateMapTab()
        {
            var tab = new Control();
            tab.Name = "MapTab";
            tab.Position = new Vector2(20, 50);
            tab.Size = new Vector2(900, 600);
            tab.Visible = false;

            _mapDisplay = new TextureRect();
            _mapDisplay.Position = new Vector2(20, 10);
            _mapDisplay.Size = new Vector2(600, 500);
            tab.AddChild(_mapDisplay);

            _locationLabel = new Label();
            _locationLabel.Position = new Vector2(20, 520);
            _locationLabel.Size = new Vector2(600, 30);
            _locationLabel.LabelSettings = new LabelSettings { FontSize = 18, FontColor = PipBoyGreen };
            _locationLabel.HorizontalAlignment = HorizontalAlignment.Center;
            tab.AddChild(_locationLabel);

            return tab;
        }

        private Control CreateRadioTab()
        {
            var tab = new Control();
            tab.Name = "RadioTab";
            tab.Position = new Vector2(20, 50);
            tab.Size = new Vector2(900, 600);
            tab.Visible = false;

            _radioStationList = new ItemList();
            _radioStationList.Position = new Vector2(10, 10);
            _radioStationList.Size = new Vector2(400, 400);
            tab.AddChild(_radioStationList);

            _radioNowPlaying = new Label();
            _radioNowPlaying.Position = new Vector2(10, 420);
            _radioNowPlaying.Size = new Vector2(860, 40);
            _radioNowPlaying.LabelSettings = new LabelSettings { FontSize = 16, FontColor = PipBoyAmber };
            tab.AddChild(_radioNowPlaying);

            return tab;
        }

        public void Toggle()
        {
            _isOpen = !_isOpen;
            Visible = _isOpen;
            if (_isOpen)
            {
                RefreshAll();
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            else
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }

        private void SwitchTab(int index)
        {
            _currentTab = index;
            for (int i = 0; i < _tabPanels.Count; i++)
                _tabPanels[i].Visible = i == index;
            RefreshCurrentTab();
        }

        private void RefreshAll()
        {
            RefreshStatus();
            RefreshInventoryItems();
            RefreshQuests();
            RefreshRadio();
            RefreshCurrentTab();
        }

        private void RefreshCurrentTab()
        {
            switch (_currentTab)
            {
                case 0: RefreshStatus(); break;
                case 1: RefreshInventoryItems(); break;
                case 2: RefreshQuests(); break;
                case 4: RefreshRadio(); break;
            }
        }

        private void RefreshStatus()
        {
            if (_player == null) return;
            _healthLabel.Text = $"HP: {_player.CurrentHealth:F0}/{_player.MaxHealth:F0}";
            _apLabel.Text = $"AP: {_player.CurrentAP:F0}/{_player.MaxActionPoints:F0}";
            _radLabel.Text = "RAD: 0/1000";

            if (_stats != null)
            {
                _levelLabel.Text = $"LEVEL: {_stats.Level}";
                _xpLabel.Text = $"XP: {_stats.XP}/{_stats.XPToNext}";

                _specialLabels.Text = string.Join("\n", new[]
                {
                    $"STR: {_stats.GetSpecialValue(SpecialAttribute.Strength),2}  PER: {_stats.GetSpecialValue(SpecialAttribute.Perception),2}  END: {_stats.GetSpecialValue(SpecialAttribute.Endurance),2}",
                    $"CHA: {_stats.GetSpecialValue(SpecialAttribute.Charisma),2}  INT: {_stats.GetSpecialValue(SpecialAttribute.Intelligence),2}  AGI: {_stats.GetSpecialValue(SpecialAttribute.Agility),2}",
                    $"LCK: {_stats.GetSpecialValue(SpecialAttribute.Luck),2}",
                    "",
                    $"Base HP: {_stats.BaseHealth}  AP: {_stats.ActionPoints}",
                });

                _skillLabels.Text = string.Join("\n", new[]
                {
                    $"Barter:      {_stats.GetSkillValue(SkillName.Barter),3}",
                    $"Big Guns:    {_stats.GetSkillValue(SkillName.BigGuns),3}",
                    $"Energy Wpn:  {_stats.GetSkillValue(SkillName.EnergyWeapons),3}",
                    $"Explosives:  {_stats.GetSkillValue(SkillName.Explosives),3}",
                    $"Lockpick:    {_stats.GetSkillValue(SkillName.Lockpick),3}",
                    $"Medicine:    {_stats.GetSkillValue(SkillName.Medicine),3}",
                    $"Melee:       {_stats.GetSkillValue(SkillName.MeleeWeapons),3}",
                    $"Repair:      {_stats.GetSkillValue(SkillName.Repair),3}",
                    $"Science:     {_stats.GetSkillValue(SkillName.Science),3}",
                    $"Small Guns:  {_stats.GetSkillValue(SkillName.SmallGuns),3}",
                    $"Sneak:       {_stats.GetSkillValue(SkillName.Sneak),3}",
                    $"Speech:      {_stats.GetSkillValue(SkillName.Speech),3}",
                    $"Unarmed:     {_stats.GetSkillValue(SkillName.Unarmed),3}",
                });

                _damageResistLabel.Text = $"Damage Resist: 0  Poison Resist: {_stats.PoisonResist:F0}%  Rad Resist: {_stats.RadResist:F0}%";
                _carryWeightLabel.Text = $"Carry Weight: {_stats.CarryWeight}  Melee Dmg: {_stats.MeleeDamage}  Crit: {_stats.CritChance:F1}%";
            }
        }

        private void RefreshInventoryItems()
        {
            _invItemList.Clear();
            _invDetailLabel.Text = "";

            string category = _invCategories[_selectedCategory];
            _invItemList.AddItem($"[{category}] - No items yet");
        }

        private void RefreshQuests()
        {
            _questList.Clear();
            _questList.AddItem("Main: Escape! - Follow the overseer's instructions");
            _questList.AddItem("Side: Explore the Wasteland");
            _questDetailLabel.Text = "No quest selected";
        }

        private void RefreshRadio()
        {
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Tab)
                {
                    Toggle();
                    return;
                }

                if (!_isOpen) return;

                if (key.Keycode == Key.Escape)
                {
                    Toggle();
                    return;
                }

                if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5)
                {
                    int tabIdx = (int)(key.Keycode - Key.Key1);
                    if (tabIdx < _tabPanels.Count)
                        SwitchTab(tabIdx);
                }
            }
        }

        public void Activate() { if (!_isOpen) Toggle(); }
    }
}
