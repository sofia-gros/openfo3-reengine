using Godot;
using System;

namespace OpenFo3.Core
{
    public partial class FlyCam : Camera3D
    {
        [Export] public float Sensitivity { get; set; } = 0.15f;
        [Export] public float DefaultSpeed { get; set; } = 15.0f;
        [Export] public float FastSpeedMultiplier { get; set; } = 3.5f;

        private float _pitch;
        private float _yaw;

        public override void _Ready()
        {
            _yaw = Rotation.Y;
            _pitch = Rotation.X;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseMotion mouse && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                _yaw -= mouse.Relative.X * Sensitivity * 0.01f;
                _pitch = Mathf.Clamp(_pitch - mouse.Relative.Y * Sensitivity * 0.01f, -Mathf.Pi * 0.48f, Mathf.Pi * 0.48f);

                Rotation = new Vector3(_pitch, _yaw, 0);
            }
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            Vector3 direction = Vector3.Zero;
            Basis b = Transform.Basis;

            if (Input.IsKeyPressed(Key.W)) direction -= b.Z;
            if (Input.IsKeyPressed(Key.S)) direction += b.Z;
            if (Input.IsKeyPressed(Key.A)) direction -= b.X;
            if (Input.IsKeyPressed(Key.D)) direction += b.X;
            if (Input.IsKeyPressed(Key.Q)) direction -= Vector3.Up;
            if (Input.IsKeyPressed(Key.E)) direction += Vector3.Up;

            if (direction.LengthSquared() > 0.001f)
            {
                float speed = DefaultSpeed;
                if (Input.IsKeyPressed(Key.Shift))
                    speed *= FastSpeedMultiplier;

                Position += direction.Normalized() * speed * dt;
            }
        }
    }
}
