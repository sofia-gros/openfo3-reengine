using Godot;
using System;

namespace OpenFo3.UI
{
    public partial class MainMenu : CanvasLayer
    {
        [Signal]
        public delegate void NewGameEventHandler();

        [Signal]
        public delegate void ContinueGameEventHandler();

        [Signal]
        public delegate void LoadGameEventHandler();

        [Signal]
        public delegate void ModsEventHandler();

        [Signal]
        public delegate void SettingsEventHandler();

        [Signal]
        public delegate void QuitEventHandler();

        private VBoxContainer _menuContainer;
        private Label _titleLabel;
        private Label _subtitleLabel;

        private ColorRect _bgOverlay;
        private float _titleFloatTime;

        public override void _Ready()
        {
            Layer = 300;
            CreateMainMenu();

            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        private void CreateMainMenu()
        {
            _bgOverlay = new ColorRect();
            _bgOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _bgOverlay.Color = new Color(0, 0, 0, 1);
            AddChild(_bgOverlay);

            var scanlines = new ColorRect();
            scanlines.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            scanlines.Color = new Color(0, 0, 0, 0.3f);
            scanlines.Material = new ShaderMaterial();
            AddChild(scanlines);

            _titleLabel = new Label();
            _titleLabel.Text = "OPENFO3";
            _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _titleLabel.Position = new Vector2(0, 100);
            _titleLabel.Size = new Vector2(1920, 80);
            _titleLabel.LabelSettings = new LabelSettings
            {
                FontSize = 64,
                FontColor = new Color(0, 1f, 0.3f),
                OutlineSize = 4,
                OutlineColor = new Color(0, 0.1f, 0, 0.8f),
            };
            AddChild(_titleLabel);

            _subtitleLabel = new Label();
            _subtitleLabel.Text = "RE:ENGINE";
            _subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _subtitleLabel.Position = new Vector2(0, 170);
            _subtitleLabel.Size = new Vector2(1920, 40);
            _subtitleLabel.LabelSettings = new LabelSettings
            {
                FontSize = 28,
                FontColor = new Color(0, 0.8f, 0.2f),
                OutlineSize = 2,
                OutlineColor = new Color(0, 0.1f, 0, 0.8f),
            };
            AddChild(_subtitleLabel);

            var fo3Label = new Label();
            fo3Label.Text = "Fallout 3 Engine Rebuild";
            fo3Label.HorizontalAlignment = HorizontalAlignment.Center;
            fo3Label.Position = new Vector2(0, 210);
            fo3Label.Size = new Vector2(1920, 30);
            fo3Label.LabelSettings = new LabelSettings
            {
                FontSize = 16,
                FontColor = new Color(0.5f, 0.7f, 0.5f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0, 0.5f),
            };
            AddChild(fo3Label);

            _menuContainer = new VBoxContainer();
            _menuContainer.Position = new Vector2(860, 300);
            _menuContainer.Size = new Vector2(200, 300);
            _menuContainer.MouseFilter = Control.MouseFilterEnum.Pass;
            AddChild(_menuContainer);

            AddMenuButton("NEW GAME", () => EmitSignal(SignalName.NewGame));
            AddMenuButton("CONTINUE", () => EmitSignal(SignalName.ContinueGame));
            AddMenuButton("LOAD", () => EmitSignal(SignalName.LoadGame));
            AddMenuButton("MODS", () => EmitSignal(SignalName.Mods));
            AddMenuButton("SETTINGS", () => EmitSignal(SignalName.Settings));
            AddMenuButton("QUIT", () => EmitSignal(SignalName.Quit));

            var versionLabel = new Label();
            versionLabel.Text = "v0.1.0 - OpenFo3 ReEngine";
            versionLabel.Position = new Vector2(20, 1040);
            versionLabel.LabelSettings = new LabelSettings
            {
                FontSize = 12,
                FontColor = new Color(0.3f, 0.4f, 0.3f),
            };
            AddChild(versionLabel);

            var copyrightLabel = new Label();
            copyrightLabel.Text = "Fallout 3 is a trademark of Bethesda Softworks. This is a fan project.";
            copyrightLabel.HorizontalAlignment = HorizontalAlignment.Center;
            copyrightLabel.Position = new Vector2(0, 1060);
            copyrightLabel.Size = new Vector2(1920, 20);
            copyrightLabel.LabelSettings = new LabelSettings
            {
                FontSize = 10,
                FontColor = new Color(0.2f, 0.3f, 0.2f),
            };
            AddChild(copyrightLabel);
        }

        private void AddMenuButton(string text, Action callback)
        {
            var btn = new Button();
            btn.Text = text;
            btn.MouseFilter = Control.MouseFilterEnum.Pass;
            btn.Pressed += callback;

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = new Color(0, 0.1f, 0, 0.7f);
            normalStyle.BorderWidthBottom = 1;
            normalStyle.BorderColor = new Color(0, 0.5f, 0);
            btn.AddThemeStyleboxOverride("normal", normalStyle);

            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = new Color(0, 0.3f, 0, 0.9f);
            hoverStyle.BorderWidthBottom = 2;
            hoverStyle.BorderColor = new Color(0, 1f, 0.3f);
            btn.AddThemeStyleboxOverride("hover", hoverStyle);

            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.AddThemeColorOverride("font_color", new Color(0, 1f, 0.3f));
            btn.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.5f));

            _menuContainer.AddChild(btn);
        }

        public override void _Process(double delta)
        {
            _titleFloatTime += (float)delta;
            float offset = Mathf.Sin(_titleFloatTime * 0.5f) * 5f;
            _titleLabel.Position = new Vector2(0, 100 + offset);

            float pulse = 0.7f + Mathf.Sin(_titleFloatTime * 0.8f) * 0.3f;
            _subtitleLabel.LabelSettings.FontColor = new Color(0, 0.8f * pulse, 0.2f * pulse);

            _bgOverlay.Color = new Color(0, 0, 0, Mathf.Max(0, _bgOverlay.Color.A - (float)delta * 0.2f));
        }

        public void HideMenu()
        {
            Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        public void ShowMenu()
        {
            Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }
}
