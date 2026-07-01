using Godot;
using System;
using OpenFo3.World;

namespace OpenFo3.Player
{
    public partial class PlayerController : CharacterBody3D
    {
        [Export] public float WalkSpeed { get; set; } = 5.0f;
        [Export] public float SprintSpeed { get; set; } = 8.0f;
        [Export] public float JumpVelocity { get; set; } = 4.5f;
        [Export] public float MouseSensitivity { get; set; } = 0.002f;
        [Export] public float MaxHealth { get; set; } = 100f;
        [Export] public float MaxActionPoints { get; set; } = 80f;
        [Export] public float ApRegenRate { get; set; } = 8f;
        [Export] public float ApSprintDrain { get; set; } = 15f;
        [Export] public bool UseThirdPerson { get; set; } = false;
 
        private Camera3D _camera;
        private ThirdPersonCamera _tpsCamera;
        private WeaponSystem _weapon;
        private HudOverlay _hud;
        private Node3D _weaponHolder;
        private MeshInstance3D _playerVisual;
        private float _pitch;
        private float _currentHealth;
        private float _currentAp;
        private bool _isSprinting;
        private float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        private bool _isLocked = false;
        private OpenFo3.Core.FlyCam _flyCam;
 
        public float CurrentHealth => _currentHealth;
        public float CurrentAP => _currentAp;
        public float HealthRatio => _currentHealth / MaxHealth;
        public float ApRatio => _currentAp / MaxActionPoints;

        public override void _Ready()
        {
            _currentHealth = MaxHealth;
            _currentAp = MaxActionPoints;

            // 明示的に衝突レイヤーとマスクを設定 (1: 地形および静的・動的オブジェクトと衝突させる)
            CollisionLayer = 1;
            CollisionMask = 1;

            // ── コリジョン（必須: ないと地面と衝突せず奈落落下する） ──
            var colShape = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
            if (colShape == null)
            {
                colShape = new CollisionShape3D();
                colShape.Name = "CollisionShape3D";
                var capsule = new CapsuleShape3D();
                capsule.Radius = 0.35f;
                capsule.Height = 1.8f;
                colShape.Shape = capsule;
                // CharacterBody3D の原点は足元。カプセル中心を高さの半分だけ上にオフセット
                colShape.Position = new Vector3(0, capsule.Height * 0.5f, 0);
                AddChild(colShape);
            }

            // ── 簡易プレイヤービジュアル（半透明カプセル、TPS時のみ表示） ──
            _playerVisual = GetNodeOrNull<MeshInstance3D>("PlayerVisual");
            if (_playerVisual == null)
            {
                _playerVisual = new MeshInstance3D();
                _playerVisual.Name = "PlayerVisual";
                var capsuleMesh = new CapsuleMesh();
                capsuleMesh.Radius = 0.35f;
                capsuleMesh.Height = 1.8f;
                _playerVisual.Mesh = capsuleMesh;
                _playerVisual.Position = new Vector3(0, 0.9f, 0); // 足元から中心まで
                var mat = new StandardMaterial3D();
                mat.AlbedoColor = new Color(0.2f, 0.5f, 1.0f, 0.6f);
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                _playerVisual.MaterialOverride = mat;
                // FPSモードでは自分のボディは非表示（TPSのみ表示）
                _playerVisual.Visible = UseThirdPerson;
                AddChild(_playerVisual);
            }

            // ── FPS カメラ（目の高さ 1.7m） ──
            _camera = GetNodeOrNull<Camera3D>("Camera3D");
            if (_camera == null)
            {
                _camera = new Camera3D();
                _camera.Name = "Camera3D";
                _camera.Current = !UseThirdPerson;
                _camera.Position = new Vector3(0, 1.7f, 0); // 目の高さ
                AddChild(_camera);
            }

            _tpsCamera = new ThirdPersonCamera();
            _tpsCamera.Name = "ThirdPersonCamera";
            _tpsCamera.Setup(this);
            if (UseThirdPerson)
                _tpsCamera.MakeCurrent();
            AddChild(_tpsCamera);

            _weaponHolder = new Node3D();
            _weaponHolder.Name = "WeaponHolder";
            _camera.AddChild(_weaponHolder);

            _weapon = new WeaponSystem();
            _weapon.Name = "WeaponSystem";
            _weaponHolder.AddChild(_weapon);

            _hud = GetNodeOrNull<HudOverlay>("../HUD");
            if (_hud == null)
                _hud = GetNodeOrNull<HudOverlay>("/root/Megaton/HUD");

            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.F1)
                {
                    if (!_isLocked)
                    {
                        UseThirdPerson = !UseThirdPerson;
                        if (UseThirdPerson)
                        {
                            _tpsCamera?.MakeCurrent();
                            if (_camera != null) _camera.Current = false;
                        }
                        else
                        {
                            if (_camera != null) _camera.Current = true;
                            _tpsCamera?.SetNotCurrent();
                        }
                        // TPS/FPS切り替え時にビジュアル表示を更新
                        if (_playerVisual != null)
                            _playerVisual.Visible = UseThirdPerson;
                        GD.Print($"[PlayerController] Camera toggled. ThirdPerson = {UseThirdPerson}");
                    }
                    return;
                }

                if (key.Keycode == Key.F2)
                {
                    ToggleFreeCam();
                    return;
                }
            }

            if (_isLocked) return;

            if (@event is InputEventMouseMotion mouse && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                if (!UseThirdPerson)
                {
                    _pitch -= mouse.Relative.Y * MouseSensitivity;
                    _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2, Mathf.Pi / 2);

                    _camera.Rotation = new Vector3(_pitch, 0, 0);
                    RotateY(-mouse.Relative.X * MouseSensitivity);
                }
            }

            if (@event is InputEventKey key2)
            {
                if (key2.Keycode == Key.Escape && key2.Pressed && !key2.Echo)
                {
                    Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                        ? Input.MouseModeEnum.Visible
                        : Input.MouseModeEnum.Captured;
                }

                if (key2.Keycode == Key.R && key2.Pressed && !key2.Echo)
                {
                    _weapon?.Reload();
                }
            }

            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                if (mouseBtn.ButtonIndex == MouseButton.Left && Input.MouseMode == Input.MouseModeEnum.Captured)
                {
                    _weapon?.Fire(_camera);
                }
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_isLocked)
            {
                Velocity = Vector3.Zero;
                return;
            }

            float dt = (float)delta;
            Vector3 velocity = Velocity;

            _isSprinting = Input.IsKeyPressed(Key.Shift) && _currentAp > 0;

            Vector3 direction = Vector3.Zero;
            if (Input.IsKeyPressed(Key.W)) direction -= Transform.Basis.Z;
            if (Input.IsKeyPressed(Key.S)) direction += Transform.Basis.Z;
            if (Input.IsKeyPressed(Key.A)) direction -= Transform.Basis.X;
            if (Input.IsKeyPressed(Key.D)) direction += Transform.Basis.X;
            direction.Y = 0;
            if (direction.LengthSquared() > 0.01f)
                direction = direction.Normalized();

            float speed = _isSprinting ? SprintSpeed : WalkSpeed;

            if (direction.LengthSquared() > 0.01f)
            {
                velocity.X = direction.X * speed;
                velocity.Z = direction.Z * speed;
            }
            else
            {
                velocity.X = Mathf.MoveToward(velocity.X, 0, speed * 3 * dt);
                velocity.Z = Mathf.MoveToward(velocity.Z, 0, speed * 3 * dt);
            }

            if (_isSprinting && direction.LengthSquared() > 0.01f)
            {
                _currentAp = Mathf.Max(0, _currentAp - ApSprintDrain * dt);
            }
            else
            {
                _currentAp = Mathf.Min(MaxActionPoints, _currentAp + ApRegenRate * dt);
            }

            if (IsOnFloor())
            {
                if (Input.IsKeyPressed(Key.Space))
                    velocity.Y = JumpVelocity;
            }
            else
            {
                velocity.Y -= _gravity * dt;
            }

            Velocity = velocity;
            MoveAndSlide();

            // ── 落下防止のセーフティガード（地形ロードの遅れ等によるすり抜け防止） ──
            if (GlobalPosition.Y < -50f)
            {
                GD.PrintErr($"[PlayerController] Fall detected (Y={GlobalPosition.Y}). Safety teleporting player to Vault101 entry.");
                GlobalPosition = new Vector3(156f, 44f, -58f); // Vault 101の開始地点
                Velocity = Vector3.Zero;
            }

            _weapon?.UpdateBob(dt, direction.LengthSquared() > 0.01f && IsOnFloor());

            if (_hud != null)
            {
                _hud.SetHealth(_currentHealth, MaxHealth);
                _hud.SetActionPoints(_currentAp, MaxActionPoints);
            }
        }

        public void TakeDamage(float amount)
        {
            _currentHealth = Mathf.Max(0, _currentHealth - amount);
            if (_hud != null)
                _hud.ShowInfo($"Taken {amount} damage!");

            if (_currentHealth <= 0)
                Die();
        }

        public void Heal(float amount)
        {
            _currentHealth = Mathf.Min(MaxHealth, _currentHealth + amount);
        }

        public void UseActionPoints(float amount)
        {
            _currentAp = Mathf.Max(0, _currentAp - amount);
        }

        private void Die()
        {
            if (_hud != null)
                _hud.ShowInfo("You have died. Respawn at Vault101...");

            _currentHealth = MaxHealth;
            _currentAp = MaxActionPoints;

            GlobalPosition = new Vector3(156f, 44f, -58f);
        }

        public void SetHud(HudOverlay hud)
        {
            _hud = hud;
        }

        public void Spawn(Vector3 position)
        {
            _currentHealth = MaxHealth;
            _currentAp = MaxActionPoints;
            GlobalPosition = position;
            Velocity = Vector3.Zero;
            _pitch = 0;
            Rotation = Vector3.Zero;

            if (_hud != null)
            {
                _hud.SetHealth(_currentHealth, MaxHealth);
                _hud.SetActionPoints(_currentAp, MaxActionPoints);
            }
        }

        public override void _Process(double delta)
        {
            if (_tpsCamera != null && UseThirdPerson)
            {
                bool aiming = Input.IsMouseButtonPressed(MouseButton.Right);
                _tpsCamera.SetAiming(aiming);
            }

            if (_weapon != null && _hud != null)
            {
                _hud.SetAmmo(_weapon.CurrentAmmo, _weapon.MaxMagAmmo, _weapon.ReserveAmmo);
            }
        }

        private void ToggleFreeCam()
        {
            if (_isLocked)
            {
                // Disable Free Camera
                if (GodotObject.IsInstanceValid(_flyCam))
                {
                    _flyCam.QueueFree();
                    _flyCam = null;
                }

                _isLocked = false;
                if (UseThirdPerson)
                {
                    _tpsCamera?.MakeCurrent();
                }
                else
                {
                    if (_camera != null) _camera.Current = true;
                }
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GD.Print("[PlayerController] Free camera disabled.");
            }
            else
            {
                // Enable Free Camera
                _isLocked = true;
                _flyCam = new OpenFo3.Core.FlyCam();
                _flyCam.Name = "DebugFlyCam";

                Camera3D activeCam = UseThirdPerson ? _tpsCamera?.Camera : _camera;
                if (activeCam != null)
                {
                    _flyCam.GlobalTransform = activeCam.GlobalTransform;
                }
                else
                {
                    _flyCam.GlobalPosition = GlobalPosition + Vector3.Up * 1.8f;
                }

                GetParent().AddChild(_flyCam);
                _flyCam.MakeCurrent();
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GD.Print("[PlayerController] Free camera enabled.");
            }
        }
    }
}
