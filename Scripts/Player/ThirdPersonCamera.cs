using Godot;
using System;

namespace OpenFo3.Player
{
    public partial class ThirdPersonCamera : Node3D
    {
        [Export] public float Distance { get; set; } = 4.0f;
        [Export] public float MinDistance { get; set; } = 1.0f;
        [Export] public float MaxDistance { get; set; } = 10.0f;
        [Export] public float ZoomSpeed { get; set; } = 0.5f;
        [Export] public float Sensitivity { get; set; } = 0.002f;
        [Export] public float Height { get; set; } = 1.2f;
        [Export] public float CollisionRadius { get; set; } = 0.3f;
        [Export] public bool InvertY { get; set; } = false;

        private bool _rightShoulder = true;

        private CharacterBody3D _target;
        private Camera3D _camera;
        private float _pitch;
        private float _yaw;
        private float _currentDistance;

        private PhysicsRayQueryParameters3D _rayQuery;

        private float _defaultFov = 75f;
        private float _aimFov = 55f;
        private bool _isAiming;

        public Camera3D Camera => _camera;
        public bool IsAiming => _isAiming;

        public void Setup(CharacterBody3D target)
        {
            _target = target;
        }

        public override void _Ready()
        {
            _camera = new Camera3D();
            _camera.Name = "TPSCamera";
            _camera.Current = false;
            _camera.Position = Vector3.Zero;
            AddChild(_camera);

            _currentDistance = Distance;
            _rayQuery = new PhysicsRayQueryParameters3D();
        }

        public void MakeCurrent()
        {
            if (_camera != null)
                _camera.Current = true;
        }

        public void SetNotCurrent()
        {
            if (_camera != null)
                _camera.Current = false;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseMotion mouse && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                float yawDelta = -mouse.Relative.X * Sensitivity;
                float pitchDelta = mouse.Relative.Y * Sensitivity * (InvertY ? -1f : 1f);

                _yaw += yawDelta;
                _pitch = Mathf.Clamp(_pitch + pitchDelta, -Mathf.Pi * 0.45f, Mathf.Pi * 0.45f);

                if (_target != null)
                    _target.Rotation = new Vector3(0, _yaw, 0);
            }

            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
                    _currentDistance = Mathf.Clamp(_currentDistance - ZoomSpeed, MinDistance, MaxDistance);
                if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                    _currentDistance = Mathf.Clamp(_currentDistance + ZoomSpeed, MinDistance, MaxDistance);
            }

            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Q && !key.CtrlPressed)
                    _rightShoulder = !_rightShoulder;
            }
        }

        public override void _Process(double delta)
        {
            if (_target == null) return;

            float shoulderOffset = _rightShoulder ? 0.5f : -0.5f;
            Vector3 targetPos = _target.GlobalPosition + Vector3.Up * Height;

            Vector3 desiredCamPos = targetPos + GetCameraOffset();
            Vector3 collisionPos = CheckCollision(targetPos, desiredCamPos);

            GlobalPosition = collisionPos;
            _camera.LookAt(targetPos + Vector3.Up * 0.5f);

            float targetFov = _isAiming ? _aimFov : _defaultFov;
            _camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, (float)delta * 8f);
        }

        private Vector3 GetCameraOffset()
        {
            float shoulderOffset = _rightShoulder ? 0.5f : -0.5f;
            Vector3 forward = -Vector3.Forward.Rotated(Vector3.Up, _yaw);
            Vector3 right = Vector3.Right.Rotated(Vector3.Up, _yaw);
            Vector3 up = Vector3.Up;

            return (forward * _currentDistance) + (right * shoulderOffset) + (up * 0.3f);
        }

        private Vector3 CheckCollision(Vector3 from, Vector3 to)
        {
            if (_target == null) return to;

            var space = _target.GetWorld3D().DirectSpaceState;
            if (space == null) return to;

            _rayQuery.From = from;
            _rayQuery.To = to;
            _rayQuery.CollisionMask = 0xFFFFFFFF;
            _rayQuery.Exclude = new Godot.Collections.Array<Rid> { _target.GetRid() };

            var result = space.IntersectRay(_rayQuery);
            if (result != null && result.Count > 0)
            {
                Vector3 hitPos = result["position"].AsVector3();
                Vector3 dir = (to - from).Normalized();
                return hitPos - dir * CollisionRadius;
            }

            return to;
        }

        public void SetAiming(bool aiming)
        {
            _isAiming = aiming;
        }

        public void SetFov(float fov)
        {
            _defaultFov = fov;
        }

        public void SwitchShoulder()
        {
            _rightShoulder = !_rightShoulder;
        }
    }
}
