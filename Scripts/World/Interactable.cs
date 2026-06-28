using Godot;
using System;
using OpenFo3.Player;

namespace OpenFo3.World
{
    public partial class Interactable : Area3D
    {
        [Signal]
        public delegate void InteractedEventHandler();

        [Signal]
        public delegate void PlayerEnteredEventHandler();

        [Signal]
        public delegate void PlayerExitedEventHandler();

        private Label3D _promptLabel;
        private ChildController _currentPlayer;

        public string InteractionText { get; set; } = "Use";
        public bool IsEnabled { get; set; } = true;
        public bool PlayerInRange => _currentPlayer != null;

        public override void _Ready()
        {
            _promptLabel = new Label3D();
            _promptLabel.Name = "InteractionPrompt";
            _promptLabel.Text = "";
            _promptLabel.Position = new Vector3(0, 2.5f, 0);
            _promptLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            _promptLabel.PixelSize = 0.008f;
            _promptLabel.Modulate = new Color(1, 1, 1);
            _promptLabel.OutlineModulate = new Color(0, 0, 0, 0.8f);
            _promptLabel.OutlineSize = 2;
            AddChild(_promptLabel);

            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
        }

        private void OnBodyEntered(Node body)
        {
            if (!IsEnabled) return;
            if (body is ChildController child)
            {
                _currentPlayer = child;
                _currentPlayer.CurrentInteractable = this;
                ShowPrompt();
                EmitSignal(SignalName.PlayerEntered);
            }
        }

        private void OnBodyExited(Node body)
        {
            if (body is ChildController child)
            {
                if (_currentPlayer != null && _currentPlayer.CurrentInteractable == this)
                    _currentPlayer.CurrentInteractable = null;
                _currentPlayer = null;
                HidePrompt();
                EmitSignal(SignalName.PlayerExited);
            }
        }

        public void ShowPrompt()
        {
            if (_promptLabel != null && IsEnabled)
                _promptLabel.Text = $"[E] {InteractionText}";
        }

        public void HidePrompt()
        {
            if (_promptLabel != null)
                _promptLabel.Text = "";
        }

        public virtual void Interact()
        {
            if (!IsEnabled) return;
            EmitSignal(SignalName.Interacted);
        }

        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            if (!enabled)
                HidePrompt();
        }
    }
}
