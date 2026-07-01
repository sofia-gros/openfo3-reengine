using Godot;
using System;
using OpenFo3.UI;
using OpenFo3.World;

namespace OpenFo3.Player
{
    public partial class ChildController : CharacterBody3D
    {
        private LifeStage _currentStage = LifeStage.Baby;
        private float _crawlSpeed = 1.0f;
        private float _walkSpeed = 3.0f;
        private float _runSpeed = 5.0f;
        private float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        private Camera3D _camera;
        private CollisionShape3D _collisionShape;
        private CapsuleShape3D _capsule;
        private Vector3 _velocity;
        private Vector3 _targetPosition;
        private bool _hasTarget;
        private bool _isEventSequence;
        private float _moveSpeed;
        private float _eventTimer;
        private Action _onEventComplete;

        public Interactable CurrentInteractable { get; set; }
        public LifeStage Stage => _currentStage;

        public override void _Ready()
        {
            _camera = GetNodeOrNull<Camera3D>("Camera3D");
            if (_camera == null)
            {
                _camera = new Camera3D();
                _camera.Name = "Camera3D";
                _camera.Current = true;
                AddChild(_camera);
            }

            _capsule = new CapsuleShape3D();
            _collisionShape = new CollisionShape3D();
            _collisionShape.Shape = _capsule;
            AddChild(_collisionShape);

            ApplyStageCollision();
            _camera.Current = true;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.E && CurrentInteractable != null)
                {
                    CurrentInteractable.Interact();
                }
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_isEventSequence)
            {
                ProcessEventSequence(delta);
                return;
            }

            Vector3 inputDir = Vector3.Zero;

            if (_currentStage >= LifeStage.Child)
            {
                inputDir = new Vector3(
                    Input.GetAxis("move_left", "move_right"),
                    0,
                    Input.GetAxis("move_forward", "move_back")
                );
            }
            else if (_currentStage >= LifeStage.Baby)
            {
                bool pressing = Input.IsActionPressed("move_forward") || Input.IsActionPressed("move_left") ||
                    Input.IsActionPressed("move_right") || Input.IsActionPressed("move_back");
                if (pressing)
                {
                    inputDir = new Vector3(
                        Input.GetAxis("move_left", "move_right"),
                        0,
                        Input.GetAxis("move_forward", "move_back")
                    ).Normalized();
                }
            }

            Vector3 direction = (Transform.Basis * inputDir).Normalized();
            direction.Y = 0;

            _velocity = direction * (_currentStage switch
            {
                LifeStage.Baby => _crawlSpeed,
                LifeStage.Child => _walkSpeed,
                LifeStage.Teen => _runSpeed,
                _ => _walkSpeed
            });

            if (_currentStage >= LifeStage.Child)
            {
                if (Input.IsActionPressed("jump") && IsOnFloor())
                    _velocity.Y = _currentStage == LifeStage.Teen ? 4.0f : 2.0f;
            }

            _velocity.Y -= _gravity * (float)delta;
            Velocity = _velocity;
            MoveAndSlide();
        }

        public void SetStage(LifeStage stage)
        {
            _currentStage = stage;
            ApplyStageCollision();
            // GD.Print($"[ChildController] Stage set to {stage}");
        }

        private void ApplyStageCollision()
        {
            float height = CollisionHeight;
            float radius = CollisionRadius;
            _capsule.Height = height;
            _capsule.Radius = radius;
            _collisionShape.Position = new Vector3(0, height / 2, 0);

            if (_camera != null)
                _camera.Position = new Vector3(0, height * 0.9f, 0);
        }

        public void MoveToPosition(Vector3 target, float speed = -1, Action onComplete = null)
        {
            _targetPosition = target;
            _targetPosition.Y = Position.Y;
            _hasTarget = true;
            _isEventSequence = true;
            _moveSpeed = speed > 0 ? speed : _walkSpeed;
            _onEventComplete = onComplete;
        }

        public void MoveDirection(Vector3 direction, float duration, Action onComplete = null)
        {
            _targetPosition = Position + direction * duration;
            _targetPosition.Y = Position.Y;
            _hasTarget = true;
            _isEventSequence = true;
            _eventTimer = duration;
            _onEventComplete = onComplete;
        }

        public void LookAtTarget(Vector3 target)
        {
            Vector3 direction = (target - GlobalPosition).Normalized();
            direction.Y = 0;
            if (direction.LengthSquared() > 0.001f)
                LookAt(GlobalPosition + direction, Vector3.Up);
        }

        private void ProcessEventSequence(double delta)
        {
            if (_hasTarget)
            {
                Vector3 toTarget = _targetPosition - GlobalPosition;
                float distance = toTarget.Length();
                if (distance > 0.1f)
                {
                    Vector3 direction = toTarget.Normalized();
                    direction.Y = 0;
                    if (direction.LengthSquared() > 0.001f)
                        LookAt(GlobalPosition + direction, Vector3.Up);

                    _velocity = direction * _moveSpeed;
                    _velocity.Y -= _gravity * (float)delta;
                    Velocity = _velocity;
                    MoveAndSlide();
                }
                else
                {
                    _velocity = Vector3.Zero;
                    Velocity = Vector3.Zero;
                    _hasTarget = false;
                    _isEventSequence = false;
                    _onEventComplete?.Invoke();
                }
            }
            else if (_eventTimer > 0)
            {
                _eventTimer -= (float)delta;
                if (_eventTimer <= 0)
                {
                    _isEventSequence = false;
                    _onEventComplete?.Invoke();
                }
            }
        }

        public float CollisionHeight => _currentStage switch
        {
            LifeStage.Baby => 0.4f,
            LifeStage.Child => 0.9f,
            LifeStage.Teen => 1.6f,
            LifeStage.Adult => 1.9f,
            _ => 0.4f
        };

        public float CollisionRadius => _currentStage switch
        {
            LifeStage.Baby => 0.2f,
            LifeStage.Child => 0.3f,
            LifeStage.Teen => 0.4f,
            LifeStage.Adult => 0.5f,
            _ => 0.2f
        };
    }
}
