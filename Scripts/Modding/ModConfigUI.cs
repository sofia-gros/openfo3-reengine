using Godot;
using System;
using System.Linq;

namespace OpenFo3.Modding
{
    public partial class ModConfigUI : Control
    {
        private ModManager _modManager;
        private ItemList _modList;
        private Label _detailLabel;
        private Button _toggleButton;
        private Button _moveUpButton;
        private Button _moveDownButton;
        private Button _saveButton;

        public void Setup(ModManager modManager)
        {
            _modManager = modManager;
        }

        public override void _Ready()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Pass;
            CreateUI();
        }

        private void CreateUI()
        {
            var bg = new Panel();
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);
            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0, 0, 0, 0.85f);
            bg.AddThemeStyleboxOverride("panel", bgStyle);

            var title = new Label();
            title.Text = "MOD CONFIGURATION";
            title.Position = new Vector2(20, 10);
            title.LabelSettings = new LabelSettings { FontSize = 24, FontColor = new Color(1, 0.7f, 0) };
            AddChild(title);

            _modList = new ItemList();
            _modList.Position = new Vector2(20, 50);
            _modList.Size = new Vector2(500, 500);
            _modList.MouseFilter = MouseFilterEnum.Ignore;
            _modList.ItemSelected += OnModSelected;
            AddChild(_modList);

            _detailLabel = new Label();
            _detailLabel.Position = new Vector2(540, 50);
            _detailLabel.Size = new Vector2(400, 300);
            _detailLabel.LabelSettings = new LabelSettings { FontSize = 14, FontColor = new Color(0.8f, 0.8f, 0.8f) };
            AddChild(_detailLabel);

            _toggleButton = new Button();
            _toggleButton.Text = "Toggle Enabled";
            _toggleButton.Position = new Vector2(540, 370);
            _toggleButton.Pressed += ToggleSelected;
            AddChild(_toggleButton);

            _moveUpButton = new Button();
            _moveUpButton.Text = "Move Up";
            _moveUpButton.Position = new Vector2(540, 410);
            _moveUpButton.Pressed += MoveUp;
            AddChild(_moveUpButton);

            _moveDownButton = new Button();
            _moveDownButton.Text = "Move Down";
            _moveDownButton.Position = new Vector2(540, 450);
            _moveDownButton.Pressed += MoveDown;
            AddChild(_moveDownButton);

            _saveButton = new Button();
            _saveButton.Text = "Save Load Order";
            _saveButton.Position = new Vector2(540, 490);
            _saveButton.Pressed += SaveLoadOrder;
            AddChild(_saveButton);

            var closeButton = new Button();
            closeButton.Text = "Close (Esc)";
            closeButton.Position = new Vector2(540, 530);
            closeButton.Pressed += () => Visible = false;
            AddChild(closeButton);
        }

        public void RefreshList()
        {
            _modList.Clear();
            if (_modManager == null) return;

            foreach (var plugin in _modManager.Plugins)
            {
                string status = plugin.IsEnabled ? "[X]" : "[ ]";
                string type = plugin.IsMaster ? "ESM" : plugin.IsLight ? "ESL" : "ESP";
                _modList.AddItem($"{status} [{type}] {plugin.FileName} (Order: {plugin.LoadOrder})");
            }
        }

        private void OnModSelected(long index)
        {
            if (_modManager == null || index < 0 || index >= _modManager.Plugins.Count) return;
            var plugin = _modManager.Plugins[(int)index];
            string masters = plugin.MasterNames.Count > 0
                ? string.Join(", ", plugin.MasterNames)
                : "(none)";
            _detailLabel.Text = $"File: {plugin.FileName}\nType: {(plugin.IsMaster ? "ESM" : plugin.IsLight ? "ESL" : "ESP")}\nEnabled: {plugin.IsEnabled}\nLoad Order: {plugin.LoadOrder}\nMasters: {masters}\nFormId Range: 0x{plugin.FormIdStart:X8} - 0x{plugin.FormIdEnd:X8}";
        }

        private void ToggleSelected()
        {
            var selected = _modList.GetSelectedItems();
            if (_modManager == null || selected.Length < 1) return;
            _modManager.SetPluginEnabled(_modManager.Plugins[selected[0]].FileName, !_modManager.Plugins[selected[0]].IsEnabled);
            RefreshList();
        }

        private void MoveUp()
        {
            var selected = _modList.GetSelectedItems();
            if (_modManager == null || selected.Length < 1 || selected[0] <= 0) return;
            _modManager.MovePlugin(selected[0], selected[0] - 1);
            RefreshList();
        }

        private void MoveDown()
        {
            var selected = _modList.GetSelectedItems();
            if (_modManager == null || selected.Length < 1 || selected[0] >= _modList.ItemCount - 1) return;
            _modManager.MovePlugin(selected[0], selected[0] + 1);
            RefreshList();
        }

        private void SaveLoadOrder()
        {
            _modManager?.SaveLoadOrder(ProjectSettings.GlobalizePath("user://load_order.json"));
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Escape && Visible)
                    Visible = false;
            }
        }
    }
}
