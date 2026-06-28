using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFo3.Player;
using OpenFo3.World;
using OpenFo3.Game;

namespace OpenFo3.UI
{
    public partial class VatsSystem : Control
    {
        [Export] public float VatsTimeScale { get; set; } = 0.05f;
        [Export] public float ActionPointCost { get; set; } = 25f;
        [Export] public float TargetRadius { get; set; } = 400f;

        private PlayerController _player;
        private WeaponSystem _weapon;
        private GameMode _gameMode;
        private Camera3D _camera;
        private bool _isActive;
        private float _savedTimeScale = 1f;

        private List<VatsTarget> _targets = new();
        private int _selectedTargetIdx;
        private VatsBodyPart _selectedBodyPart;

        private Control _vatsPanel;
        private Label _hitChanceLabel;
        private Label _actionPointLabel;
        private Label _targetNameLabel;
        private Label _bodyPartLabel;
        private Control _bodyPartIndicator;

        public bool IsActive => _isActive;

        private class VatsTarget
        {
            public NpcAgent Agent;
            public Vector2 ScreenPosition;
            public float Distance;
            public float HitChance;
            public string Name;
            public List<VatsBodyPart> BodyParts = new();
        }

        private class VatsBodyPart
        {
            public string Name;
            public Vector3 LocalPosition;
            public float HitChanceModifier = 1.0f;
            public float DamageMultiplier = 1.0f;
            public float CritMultiplier = 1.0f;
        }

        public void Setup(PlayerController player, WeaponSystem weapon, Camera3D camera, GameMode gameMode)
        {
            _player = player;
            _weapon = weapon;
            _camera = camera;
            _gameMode = gameMode;
            CreateVatsUI();
        }

        private void CreateVatsUI()
        {
            _vatsPanel = new Control();
            _vatsPanel.Name = "VatsPanel";
            _vatsPanel.Visible = false;
            _vatsPanel.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_vatsPanel);

            _hitChanceLabel = new Label();
            _hitChanceLabel.Name = "HitChanceLabel";
            _hitChanceLabel.Position = new Vector2(10, 10);
            _hitChanceLabel.LabelSettings = new LabelSettings
            {
                FontSize = 24,
                FontColor = new Color(0, 1f, 0.3f),
                OutlineSize = 2,
                OutlineColor = new Color(0, 0, 0, 0.8f),
            };
            _vatsPanel.AddChild(_hitChanceLabel);

            _actionPointLabel = new Label();
            _actionPointLabel.Name = "ActionPointLabel";
            _actionPointLabel.Position = new Vector2(10, 45);
            _actionPointLabel.LabelSettings = new LabelSettings
            {
                FontSize = 18,
                FontColor = new Color(0, 0.8f, 1f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0, 0.8f),
            };
            _vatsPanel.AddChild(_actionPointLabel);

            _targetNameLabel = new Label();
            _targetNameLabel.Name = "TargetNameLabel";
            _targetNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _targetNameLabel.Position = new Vector2(0, 0);
            _targetNameLabel.LabelSettings = new LabelSettings
            {
                FontSize = 20,
                FontColor = new Color(1, 1, 0.6f),
                OutlineSize = 2,
                OutlineColor = new Color(0, 0, 0, 0.8f),
            };
            _vatsPanel.AddChild(_targetNameLabel);

            _bodyPartLabel = new Label();
            _bodyPartLabel.Name = "BodyPartLabel";
            _bodyPartLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _bodyPartLabel.Position = new Vector2(0, 30);
            _bodyPartLabel.LabelSettings = new LabelSettings
            {
                FontSize = 16,
                FontColor = new Color(1, 1, 1),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0, 0.8f),
            };
            _vatsPanel.AddChild(_bodyPartLabel);
        }

        public void Toggle()
        {
            if (_isActive)
                ExitVats();
            else
                EnterVats();
        }

        private void EnterVats()
        {
            if (_player == null || _weapon == null) return;
            if (_player.CurrentAP < ActionPointCost) return;

            _isActive = true;
            _savedTimeScale = (float)Engine.TimeScale;
            Engine.TimeScale = VatsTimeScale;

            _vatsPanel.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;

            ScanTargets();
            if (_targets.Count > 0)
            {
                _selectedTargetIdx = 0;
                SelectTarget(0);
            }
        }

        private void ExitVats()
        {
            _isActive = false;
            Engine.TimeScale = _savedTimeScale;
            _vatsPanel.Visible = false;
            _targets.Clear();

            if (_player != null && IsInstanceValid(_player))
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        private void ScanTargets()
        {
            _targets.Clear();
            if (_camera == null) return;

            Vector3 camPos = _camera.GlobalPosition;
            Vector3 camDir = -_camera.GlobalTransform.Basis.Z;

            var allNpcs = GetTree().GetNodesInGroup("npcs").OfType<NpcAgent>().ToList();
            if (allNpcs.Count == 0)
            {
                var root = GetTree().Root;
                FindNpcsRecursive(root, allNpcs);
            }

            foreach (var npc in allNpcs)
            {
                if (!IsInstanceValid(npc) || npc.IsDead) continue;
                float dist = npc.GlobalPosition.DistanceTo(camPos);
                if (dist > TargetRadius) continue;

                Vector3 toTarget = (npc.GlobalPosition - camPos).Normalized();
                float angle = camDir.AngleTo(toTarget);
                if (angle > Mathf.Pi * 0.75f) continue;

                var space = _camera.GetWorld3D().DirectSpaceState;
                var query = new PhysicsRayQueryParameters3D();
                query.From = camPos;
                query.To = npc.GlobalPosition + Vector3.Up * 1.0f;
                query.CollisionMask = 0xFFFFFFFF;
                var result = space.IntersectRay(query);
                if (result != null && result.Count > 0)
                {
                    var collider = result["collider"].AsGodotObject();
                    if (collider != npc) continue;
                }

                Vector2 screenPos = _camera.UnprojectPosition(npc.GlobalPosition);

                var target = new VatsTarget
                {
                    Agent = npc,
                    ScreenPosition = screenPos,
                    Distance = dist,
                    Name = npc.NpcName,
                    BodyParts = CreateBodyParts(npc),
                };

                float weaponSkill = 50f;
                if (_gameMode?.Stats != null)
                    weaponSkill = _gameMode.Stats[SkillName.SmallGuns];

                float weaponCond = 1.0f;
                target.HitChance = _gameMode != null
                    ? _gameMode.GetHitChance(weaponSkill, dist, weaponCond, true)
                    : Mathf.Clamp(80f - dist * 0.1f, 5f, 95f);

                _targets.Add(target);
            }
        }

        private void FindNpcsRecursive(Node node, List<NpcAgent> results)
        {
            if (node is NpcAgent npc)
                results.Add(npc);
            foreach (var child in node.GetChildren())
                FindNpcsRecursive(child, results);
        }

        private List<VatsBodyPart> CreateBodyParts(NpcAgent npc)
        {
            return new List<VatsBodyPart>
            {
                new() { Name = "Head", LocalPosition = new Vector3(0, 1.7f, 0), HitChanceModifier = 0.5f, DamageMultiplier = 2.0f, CritMultiplier = 3.0f },
                new() { Name = "Torso", LocalPosition = new Vector3(0, 0.9f, 0), HitChanceModifier = 1.0f, DamageMultiplier = 1.0f, CritMultiplier = 1.0f },
                new() { Name = "Left Arm", LocalPosition = new Vector3(-0.3f, 1.2f, 0), HitChanceModifier = 0.6f, DamageMultiplier = 0.7f, CritMultiplier = 1.0f },
                new() { Name = "Right Arm", LocalPosition = new Vector3(0.3f, 1.2f, 0), HitChanceModifier = 0.6f, DamageMultiplier = 0.7f, CritMultiplier = 1.0f },
                new() { Name = "Left Leg", LocalPosition = new Vector3(-0.2f, 0.3f, 0), HitChanceModifier = 0.7f, DamageMultiplier = 0.6f, CritMultiplier = 1.0f },
                new() { Name = "Right Leg", LocalPosition = new Vector3(0.2f, 0.3f, 0), HitChanceModifier = 0.7f, DamageMultiplier = 0.6f, CritMultiplier = 1.0f },
            };
        }

        private void SelectTarget(int index)
        {
            if (index < 0 || index >= _targets.Count) return;
            _selectedTargetIdx = index;
            var target = _targets[index];
            _selectedBodyPart = target.BodyParts[0];

            _targetNameLabel.Text = target.Name;
            UpdateBodyPartDisplay();
        }

        private void UpdateBodyPartDisplay()
        {
            if (_selectedBodyPart == null) return;
            float hitChance = _targets[_selectedTargetIdx].HitChance * _selectedBodyPart.HitChanceModifier;
            hitChance = Mathf.Clamp(hitChance, 5f, 95f);

            _hitChanceLabel.Text = $"{hitChance:F0}%";
            _actionPointLabel.Text = $"AP: {(int)ActionPointCost}";
            _bodyPartLabel.Text = _selectedBodyPart.Name;

            if (hitChance >= 80f)
                _hitChanceLabel.LabelSettings.FontColor = new Color(0, 1f, 0);
            else if (hitChance >= 50f)
                _hitChanceLabel.LabelSettings.FontColor = new Color(1f, 1f, 0);
            else
                _hitChanceLabel.LabelSettings.FontColor = new Color(1f, 0.3f, 0);

            Vector2 vpSize = GetViewportRect().Size;
            _targetNameLabel.Position = new Vector2(vpSize.X / 2f - 100, 60);
            _bodyPartLabel.Position = new Vector2(vpSize.X / 2f - 100, 85);
        }

        public override void _Input(InputEvent @event)
        {
            if (!_isActive) return;

            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                switch (key.Keycode)
                {
                    case Key.Q:
                        ExitVats();
                        return;

                    case Key.Tab:
                        CycleTarget(1);
                        return;

                    case Key.Space:
                        ExecuteAttack();
                        return;

                    case Key.Key1: case Key.Key2: case Key.Key3:
                    case Key.Key4: case Key.Key5: case Key.Key6:
                        int partIdx = (int)(key.Keycode - Key.Key1);
                        var target = _targets[_selectedTargetIdx];
                        if (partIdx < target.BodyParts.Count)
                        {
                            _selectedBodyPart = target.BodyParts[partIdx];
                            UpdateBodyPartDisplay();
                        }
                        return;
                }
            }
        }

        public override void _Process(double delta)
        {
            if (!_isActive || _camera == null || _targets.Count == 0) return;

            Vector2 vpSize = GetViewportRect().Size;
            for (int i = 0; i < _targets.Count; i++)
            {
                var npc = _targets[i].Agent;
                if (!IsInstanceValid(npc))
                {
                    _targets.RemoveAt(i);
                    i--;
                    continue;
                }
                _targets[i].ScreenPosition = _camera.UnprojectPosition(npc.GlobalPosition);
            }

            if (_selectedTargetIdx >= _targets.Count)
                _selectedTargetIdx = 0;
            if (_targets.Count > 0)
                UpdateBodyPartDisplay();
        }

        private void CycleTarget(int direction)
        {
            if (_targets.Count <= 1) return;
            _selectedTargetIdx = (_selectedTargetIdx + direction + _targets.Count) % _targets.Count;
            SelectTarget(_selectedTargetIdx);
        }

        private void ExecuteAttack()
        {
            if (_targets.Count == 0 || _selectedBodyPart == null) return;
            if (_player.CurrentAP < ActionPointCost) return;

            var target = _targets[_selectedTargetIdx];
            _player.UseActionPoints(ActionPointCost);

            float hitChance = target.HitChance * _selectedBodyPart.HitChanceModifier;
            hitChance = Mathf.Clamp(hitChance, 5f, 95f);
            bool hit = new Random().NextDouble() * 100f < hitChance;

            if (hit)
            {
                float damage = _weapon.Damage * _selectedBodyPart.DamageMultiplier;

                float critChance = 5f * _selectedBodyPart.CritMultiplier;
                if (_gameMode?.Stats != null)
                    critChance = _gameMode.Stats.CritChance * _selectedBodyPart.CritMultiplier;
                bool crit = new Random().NextDouble() * 100f < critChance;

                if (crit)
                {
                    damage *= 2f;
                    _gameMode?.OnHitEnemy(damage);
                }

                target.Agent.TakeDamage(damage);
                if (target.Agent.IsDead)
                    _gameMode?.OnKillEnemy(target.Agent);

                ExitVats();
            }
            else
            {
                ExitVats();
            }
        }

        public void DrawVatsOverlay(Control canvas)
        {
            if (!_isActive || _camera == null) return;

            foreach (var t in _targets)
            {
                Vector2 pos = t.ScreenPosition;
                bool isSelected = _targets.IndexOf(t) == _selectedTargetIdx;
            }
        }
    }
}
