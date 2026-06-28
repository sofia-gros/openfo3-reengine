using Godot;
using System;

namespace OpenFo3.World
{
    public partial class HudOverlay : CanvasLayer
    {
        private Label _healthLabel;
        private Label _apLabel;
        private Label _compassLabel;
        private Label _infoLabel;
        private Label _locationLabel;
        private Label _worldLabel;
        private Label _cameraCoordLabel;

        private TextureRect _healthBar;
        private TextureRect _apBar;
        private TextureRect _compassBar;

        private float _health = 100f;
        private float _maxHealth = 100f;
        private float _ap = 80f;
        private float _maxAp = 80f;

        private string _locationName = "";
        private string _infoText = "";

        private const float HudWidth = 200f;
        private const float HudMargin = 20f;

        private string _worldName = "";
        private string _cameraCoords = "";

        public override void _Ready()
        {
            Layer = 100;
            CreateHudElements();
            CreateAmmoLabel();
        }

        private void CreateHudElements()
        {
            var viewport = GetViewport();
            Vector2 viewportSize = viewport != null ? viewport.GetVisibleRect().Size : new Vector2(1920, 1080);
            float bottom = viewportSize.Y - HudMargin;
            float right = viewportSize.X - HudMargin;
            float centerX = viewportSize.X / 2f;

            _healthLabel = new Label();
            _healthLabel.Name = "HealthLabel";
            _healthLabel.Position = new Vector2(HudMargin, bottom - 120);
            _healthLabel.Text = "HP: 100/100";
            _healthLabel.LabelSettings = new LabelSettings
            {
                FontSize = 16,
                FontColor = new Color(0, 1, 0),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_healthLabel);

			_healthBar = new TextureRect();
			_healthBar.Name = "HealthBar";
			_healthBar.Position = new Vector2(HudMargin, bottom - 95);
			_healthBar.Size = new Vector2(200, 12);
			_healthBar.Modulate = new Color(0, 0.6f, 0, 0.8f);
			AddChild(_healthBar);

			_apLabel = new Label();
			_apLabel.Name = "APLabel";
			_apLabel.Position = new Vector2(HudMargin, bottom - 75);
			_apLabel.Text = "AP: 80/80";
			_apLabel.LabelSettings = new LabelSettings
			{
				FontSize = 14,
				FontColor = new Color(0, 0.8f, 1),
				OutlineSize = 1,
				OutlineColor = new Color(0, 0, 0),
			};
			AddChild(_apLabel);

			_apBar = new TextureRect();
			_apBar.Name = "APBar";
			_apBar.Position = new Vector2(HudMargin, bottom - 55);
			_apBar.Size = new Vector2(200, 8);
			_apBar.Modulate = new Color(0, 0.5f, 0.8f, 0.7f);
			AddChild(_apBar);

			_compassBar = new TextureRect();
			_compassBar.Name = "CompassBar";
			_compassBar.Position = new Vector2(centerX - 150, HudMargin + 10);
			_compassBar.Size = new Vector2(300, 16);
			_compassBar.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.6f);
			AddChild(_compassBar);

            _compassLabel = new Label();
            _compassLabel.Name = "CompassLabel";
            _compassLabel.Position = new Vector2(centerX - 100, HudMargin + 10);
            _compassLabel.Size = new Vector2(200, 16);
            _compassLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _compassLabel.Text = "N";
            _compassLabel.LabelSettings = new LabelSettings
            {
                FontSize = 12,
                FontColor = new Color(1, 1, 1),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_compassLabel);

            _locationLabel = new Label();
            _locationLabel.Name = "LocationLabel";
            _locationLabel.Position = new Vector2(centerX - 150, HudMargin + 35);
            _locationLabel.Size = new Vector2(300, 24);
            _locationLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _locationLabel.Text = "";
            _locationLabel.LabelSettings = new LabelSettings
            {
                FontSize = 18,
                FontColor = new Color(1, 1, 0.6f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_locationLabel);

            _infoLabel = new Label();
            _infoLabel.Name = "InfoLabel";
            _infoLabel.Position = new Vector2(HudMargin, bottom - 35);
            _infoLabel.Text = "";
            _infoLabel.LabelSettings = new LabelSettings
            {
                FontSize = 12,
                FontColor = new Color(0.8f, 0.8f, 0.8f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_infoLabel);

            _worldLabel = new Label();
            _worldLabel.Name = "WorldLabel";
            _worldLabel.Position = new Vector2(HudMargin, HudMargin);
            _worldLabel.Text = "";
            _worldLabel.LabelSettings = new LabelSettings
            {
                FontSize = 14,
                FontColor = new Color(1, 1, 0.4f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_worldLabel);

            _cameraCoordLabel = new Label();
            _cameraCoordLabel.Name = "CameraCoordLabel";
            _cameraCoordLabel.Position = new Vector2(HudMargin, HudMargin + 18);
            _cameraCoordLabel.Text = "";
            _cameraCoordLabel.LabelSettings = new LabelSettings
            {
                FontSize = 11,
                FontColor = new Color(0.7f, 0.7f, 0.7f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_cameraCoordLabel);
        }

        public override void _Process(double delta)
        {
            if (Engine.GetFramesPerSecond() > 0)
            {
                UpdateCompass();
                UpdateWorldAndCoordLabels();
            }
        }

        private void UpdateWorldAndCoordLabels()
        {
            _worldLabel.Text = _worldName;

            var cam = GetViewport()?.GetCamera3D();
            if (cam != null)
            {
                var pos = cam.GlobalPosition;
                _cameraCoordLabel.Text = $"Pos: ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})";
            }
        }

        public void SetWorldName(string name)
        {
            _worldName = name;
        }

        private void UpdateCompass()
        {
            var cam = GetViewport()?.GetCamera3D();
            if (cam == null) return;

            float yaw = cam.GlobalTransform.Basis.GetEuler().Y;
            float degrees = Mathf.RadToDeg(yaw);
            string dir;

            if (degrees < -157.5f || degrees >= 157.5f) dir = "S";
            else if (degrees >= -157.5f && degrees < -112.5f) dir = "SW";
            else if (degrees >= -112.5f && degrees < -67.5f) dir = "W";
            else if (degrees >= -67.5f && degrees < -22.5f) dir = "NW";
            else if (degrees >= -22.5f && degrees < 22.5f) dir = "N";
            else if (degrees >= 22.5f && degrees < 67.5f) dir = "NE";
            else if (degrees >= 67.5f && degrees < 112.5f) dir = "E";
            else dir = "SE";

            _compassLabel.Text = dir;
        }

        public void SetHealth(float current, float max)
        {
            _health = current;
            _maxHealth = max;
            _healthLabel.Text = $"HP: {Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
            float ratio = max > 0 ? current / max : 0;
            _healthBar.Size = new Vector2(200 * ratio, 12);
            _healthBar.Modulate = ratio > 0.5f ? new Color(0, 0.6f, 0, 0.8f) :
                               ratio > 0.25f ? new Color(0.8f, 0.6f, 0, 0.8f) :
                                                new Color(0.8f, 0, 0, 0.8f);
        }

        public void SetActionPoints(float current, float max)
        {
            _ap = current;
            _maxAp = max;
            _apLabel.Text = $"AP: {Mathf.RoundToInt(current)}/{Mathf.RoundToInt(max)}";
            float ratio = max > 0 ? current / max : 0;
            _apBar.Size = new Vector2(200 * ratio, 8);
        }

        public void SetLocation(string location)
        {
            _locationName = location;
            _locationLabel.Text = location;
        }

        public void SetInfoText(string text)
        {
            _infoText = text;
            _infoLabel.Text = text;
        }

        public void ShowInfo(string text, float duration = 3f)
        {
            _infoLabel.Text = text;
            var timer = GetTree().CreateTimer(duration);
            timer.Timeout += () =>
            {
                if (_infoLabel.Text == text)
                    _infoLabel.Text = "";
            };
        }

        private Label _ammoLabel;

        private void CreateAmmoLabel()
        {
            var viewport = GetViewport();
            Vector2 viewportSize = viewport != null ? viewport.GetVisibleRect().Size : new Vector2(1920, 1080);
            float bottom = viewportSize.Y - HudMargin;
            float right = viewportSize.X - HudMargin;

            _ammoLabel = new Label();
            _ammoLabel.Name = "AmmoLabel";
            _ammoLabel.Position = new Vector2(right - 120, bottom - 55);
            _ammoLabel.Text = "";
            _ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _ammoLabel.LabelSettings = new LabelSettings
            {
                FontSize = 16,
                FontColor = new Color(1, 1, 1),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            AddChild(_ammoLabel);
        }

        public void SetAmmo(int current, int maxMag, int reserve)
        {
            if (_ammoLabel == null) return;
            _ammoLabel.Text = $"{current}/{maxMag} [{reserve}]";
        }
    }
}
