using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFo3.NIF;

namespace OpenFo3.World
{
    public partial class NpcAgent : CharacterBody3D
    {
        private NavigationAgent3D _navAgent;
        private AnimationPlayer _animPlayer;
        private Skeleton3D _skeleton;
        private Node3D _meshRoot;
        private Label3D _nameLabel;

        private Vector3 _targetPosition;
        private bool _hasTarget;
        private float _idleTimer;
        private Random _rng = new();
        private float _currentHealth = 100f;
        private float _maxHealth = 100f;
        private bool _isDead;
        private string _npcName = "NPC";

        private List<string> _idleAnimNames = new();
        private List<string> _walkAnimNames = new();
        private string _currentAnimState = "idle";

        [Export]
        public float MovementSpeed { get; set; } = 2.5f;

        [Export]
        public float IdleMinTime { get; set; } = 4.0f;

        [Export]
        public float IdleMaxTime { get; set; } = 12.0f;

        [Export]
        public float WanderRadius { get; set; } = 20.0f;

        [Export]
        public float DetectionRadius { get; set; } = 30.0f;

        [Export]
        public string NpcName
        {
            get => _npcName;
            set
            {
                _npcName = value;
                if (_nameLabel != null)
                    _nameLabel.Text = value;
            }
        }

        public override void _Ready()
        {
            _navAgent = GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
            if (_navAgent == null)
            {
                _navAgent = new NavigationAgent3D();
                _navAgent.Name = "NavigationAgent3D";
                AddChild(_navAgent);
            }

            _navAgent.TargetReached += OnTargetReached;
            _navAgent.NavigationFinished += OnNavigationFinished;
            _navAgent.VelocityComputed += OnVelocityComputed;
            _navAgent.MaxSpeed = MovementSpeed;

            _nameLabel = GetNodeOrNull<Label3D>("NameLabel");
            if (_nameLabel == null)
            {
                _nameLabel = new Label3D();
                _nameLabel.Name = "NameLabel";
                _nameLabel.PixelSize = 0.005f;
                _nameLabel.Position = new Vector3(0, 2.2f, 0);
                _nameLabel.Modulate = new Color(1, 1, 0.6f);
                _nameLabel.OutlineModulate = new Color(0, 0, 0, 0.8f);
                _nameLabel.OutlineSize = 3;
                AddChild(_nameLabel);
            }
            _nameLabel.Text = _npcName;

            _idleTimer = GetRandomIdleTime();

            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            if (_animPlayer != null)
                PlayIdleAnimation();

            CollisionLayer = 2;
            CollisionMask = 1 | 2;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_isDead) return;

            if (_navAgent == null) return;

            if (_navAgent.IsNavigationFinished())
            {
                _idleTimer -= (float)delta;
                if (_idleTimer <= 0)
                {
                    PickRandomDestination();
                    _idleTimer = GetRandomIdleTime();
                }

                if (_currentAnimState != "idle")
                {
                    _currentAnimState = "idle";
                    PlayIdleAnimation();
                }
                return;
            }

            _currentAnimState = "walk";
            PlayWalkAnimation();

            Vector3 nextPos = _navAgent.GetNextPathPosition();
            Vector3 currentPos = GlobalPosition;
            Vector3 direction = (nextPos - currentPos).Normalized();
            direction.Y = 0;

            if (direction.LengthSquared() > 0.001f)
            {
                Vector3 velocity = direction * MovementSpeed;

                float targetYaw = Mathf.Atan2(direction.X, direction.Z);
                Rotation = new Vector3(0, targetYaw, 0);

                _navAgent.Velocity = velocity;
            }
            else
            {
                _navAgent.Velocity = Vector3.Zero;
            }
        }

        private void OnTargetReached()
        {
            _navAgent.Velocity = Vector3.Zero;
        }

        private void OnNavigationFinished()
        {
            _navAgent.Velocity = Vector3.Zero;
        }

        private void OnVelocityComputed(Vector3 safeVelocity)
        {
            Velocity = safeVelocity;
            MoveAndSlide();
        }

        private void PickRandomDestination()
        {
            if (_navAgent == null) return;

            Vector3 origin = GlobalPosition;
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float dist = (float)(_rng.NextDouble() * WanderRadius);

            Vector3 randomTarget = origin + new Vector3(
                Mathf.Cos(angle) * dist,
                0,
                Mathf.Sin(angle) * dist
            );

            _navAgent.TargetPosition = randomTarget;
        }

        private float GetRandomIdleTime()
        {
            return IdleMinTime + (float)_rng.NextDouble() * (IdleMaxTime - IdleMinTime);
        }

        public void SetTargetPosition(Vector3 target)
        {
            if (_navAgent != null)
            {
                _navAgent.TargetPosition = target;
                _idleTimer = float.MaxValue;
            }
        }

        public void AttachSkinnedMesh(Node3D skinnedRoot)
        {
            if (skinnedRoot == null) return;

            // 既存の NPCVisual を削除（二重アタッチ防止）
            var existing = GetNodeOrNull<Node3D>("NPCVisual");
            if (existing != null)
            {
                existing.QueueFree();
            }

            // skinnedRoot が既に別の親を持つ場合は先に切り離す
            if (skinnedRoot.GetParent() != null)
            {
                skinnedRoot.GetParent().RemoveChild(skinnedRoot);
            }

            _meshRoot = skinnedRoot;
            _meshRoot.Name = "NPCVisual";

            _skeleton = skinnedRoot.GetNodeOrNull<Skeleton3D>("Skeleton3D");
            if (_skeleton == null)
                _skeleton = skinnedRoot.GetChildren().OfType<Skeleton3D>().FirstOrDefault();

            AddChild(skinnedRoot);

            if (_skeleton != null)
            {
                _skeleton.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

                if (_animPlayer == null)
                {
                    _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
                }
            }

            Aabb bounds = CalculateBounds(skinnedRoot);
            float height = bounds.Size.Y > 0 ? bounds.Size.Y : 1.8f;
            float radius = Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.5f;

            var colShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
            if (colShape == null)
            {
                colShape = new CollisionShape3D();
                colShape.Name = "CollisionShape3D";
                AddChild(colShape);
            }

            var capsule = new CapsuleShape3D();
            capsule.Radius = Mathf.Max(radius, 0.3f);
            capsule.Height = Mathf.Max(height, 1.2f);
            colShape.Shape = capsule;
            colShape.Position = new Vector3(0, capsule.Height * 0.5f, 0);

            if (_nameLabel != null)
                _nameLabel.Position = new Vector3(0, height + 0.3f, 0);
        }

        private Aabb CalculateBounds(Node3D root)
        {
            var bounds = new Aabb();
            bool hasBounds = false;

            foreach (var mi in root.GetChildren().OfType<MeshInstance3D>())
            {
                if (mi.Mesh == null) continue;
                var meshBounds = mi.Mesh.GetAabb();
                meshBounds = new Aabb(mi.Transform * meshBounds.Position, meshBounds.Size);
                if (!hasBounds)
                {
                    bounds = meshBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds = bounds.Merge(meshBounds);
                }
            }

            return hasBounds ? bounds : new Aabb(new Vector3(-0.3f, 0, -0.3f), new Vector3(0.6f, 1.8f, 0.6f));
        }

        public void AttachAnimationPlayer(AnimationPlayer player)
        {
            _animPlayer = player;
            if (_animPlayer != null && !_isDead)
                PlayIdleAnimation();
        }

        public void LoadAnimations(List<(string Name, Animation Anim)> animations)
        {
            if (animations == null || animations.Count == 0) return;

            if (_animPlayer == null)
            {
                _animPlayer = new AnimationPlayer();
                _animPlayer.Name = "AnimationPlayer";
                AddChild(_animPlayer);
            }

            var lib = _animPlayer.GetAnimationLibrary("default");
            if (lib == null)
            {
                lib = new AnimationLibrary();
                _animPlayer.AddAnimationLibrary("default", lib);
            }

            foreach (var (name, anim) in animations)
            {
                string lower = name.ToLowerInvariant();

                if (lower.Contains("idle") || lower.Contains("stand"))
                    _idleAnimNames.Add(name);
                else if (lower.Contains("walk") || lower.Contains("run") || lower.Contains("forward"))
                    _walkAnimNames.Add(name);

                if (!lib.HasAnimation(name))
                    lib.AddAnimation(name, anim);
            }

            if (_idleAnimNames.Count == 0)
            {
                foreach (string aname in lib.GetAnimationList())
                {
                    string lower = aname.ToLowerInvariant();
                    if (lower.Contains("idle") || lower.Contains("stand"))
                        _idleAnimNames.Add(aname);
                }
            }

            if (_idleAnimNames.Count == 0 && lib.GetAnimationList().Count > 0)
                _idleAnimNames.Add(lib.GetAnimationList().First());

            PlayIdleAnimation();
        }

        private void PlayIdleAnimation()
        {
            if (_animPlayer == null || _idleAnimNames.Count == 0) return;

            string animName = _idleAnimNames[_rng.Next(_idleAnimNames.Count)];
            if (_animPlayer.HasAnimation($"default/{animName}"))
                _animPlayer.Play($"default/{animName}");
            else if (_animPlayer.HasAnimation(animName))
                _animPlayer.Play(animName);
        }

        private void PlayWalkAnimation()
        {
            if (_animPlayer == null || _walkAnimNames.Count == 0) return;

            string current = _animPlayer.CurrentAnimation;
            string animName = _walkAnimNames[0];

            if (current == $"default/{animName}" || current == animName) return;

            if (_animPlayer.HasAnimation($"default/{animName}"))
                _animPlayer.Play($"default/{animName}");
            else if (_animPlayer.HasAnimation(animName))
                _animPlayer.Play(animName);
        }

        public void TakeDamage(float amount)
        {
            if (_isDead) return;
            _currentHealth -= amount;

            if (_currentHealth <= 0)
                Die();
        }

        private void Die()
        {
            _isDead = true;
            if (_animPlayer != null)
                _animPlayer.Stop();

            _navAgent.Velocity = Vector3.Zero;
            Velocity = Vector3.Zero;

            var timer = GetTree().CreateTimer(30f);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(this))
                    QueueFree();
            };
        }

        public bool IsDead => _isDead;
    }
}
