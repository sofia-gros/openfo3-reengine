using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFo3.World;

namespace OpenFo3.Player
{
    public partial class WeaponSystem : Node3D
    {
        [Export] public float FireRate { get; set; } = 0.1f;
        [Export] public float Damage { get; set; } = 8f;
        [Export] public float Range { get; set; } = 300f;
        [Export] public int MaxAmmo { get; set; } = 24;
        [Export] public int MaxReserveAmmo { get; set; } = 120;
        [Export] public float ReloadTime { get; set; } = 2.0f;
        [Export] public float WeaponBobAmp { get; set; } = 0.006f;
        [Export] public float WeaponBobFreq { get; set; } = 10f;

        private int _currentAmmo;
        private int _reserveAmmo;
        private float _fireCooldown;
        private bool _isReloading;
        private float _reloadTimer;
        private float _bobTime;
        private Vector3 _restPosition;
        private MeshInstance3D _weaponMesh;
        private Node3D _muzzleFlash;
        private AudioStreamPlayer3D _fireSound;
        private AudioStreamPlayer3D _dryFireSound;

        private PackedScene _impactEffect;

        public int CurrentAmmo => _currentAmmo;
        public int ReserveAmmo => _reserveAmmo;
        public int MaxMagAmmo => MaxAmmo;
        public bool IsReloading => _isReloading;

        public override void _Ready()
        {
            _currentAmmo = MaxAmmo;
            _reserveAmmo = MaxReserveAmmo;
            _restPosition = Position;

            CreateWeaponMesh();
            CreateMuzzleFlash();
            CreateAudioPlayers();
            CreateImpactEffect();
        }

        private void CreateWeaponMesh()
        {
            _weaponMesh = new MeshInstance3D();
            _weaponMesh.Name = "WeaponMesh";
            AddChild(_weaponMesh);

            var box = new BoxMesh();
            box.Size = new Vector3(0.04f, 0.02f, 0.5f);
            box.Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.2f, 0.2f, 0.2f),
                Metallic = 0.8f,
                Roughness = 0.3f,
            };
            _weaponMesh.Mesh = box;
            _weaponMesh.Position = new Vector3(0.15f, -0.12f, -0.4f);
            _weaponMesh.Rotation = new Vector3(0, 0, -0.1f);

            var sight = new MeshInstance3D();
            sight.Name = "Sight";
            sight.Mesh = new BoxMesh { Size = new Vector3(0.005f, 0.02f, 0.005f) };
            sight.Position = new Vector3(0, 0.015f, -0.1f);
            _weaponMesh.AddChild(sight);
        }

        private void CreateMuzzleFlash()
        {
            _muzzleFlash = new Node3D();
            _muzzleFlash.Name = "MuzzleFlash";
            _muzzleFlash.Position = new Vector3(0, 0, -0.55f);
            AddChild(_muzzleFlash);

            var flashMesh = new MeshInstance3D();
            flashMesh.Name = "FlashMesh";
            flashMesh.Mesh = new SphereMesh
            {
                Radius = 0.04f,
                Height = 0.08f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.8f, 0.3f, 0.8f),
                    EmissionEnabled = true,
                    EmissionEnergyMultiplier = 2f,
                    Emission = new Color(1f, 0.6f, 0.1f),
                }
            };
            _muzzleFlash.AddChild(flashMesh);
            _muzzleFlash.Visible = false;

            var omni = new OmniLight3D();
            omni.Name = "MuzzleLight";
            omni.LightEnergy = 3f;
            omni.OmniRange = 8f;
            omni.LightColor = new Color(1f, 0.7f, 0.2f);
            _muzzleFlash.AddChild(omni);
        }

        private void CreateAudioPlayers()
        {
            _fireSound = new AudioStreamPlayer3D();
            _fireSound.Name = "FireSound";
            AddChild(_fireSound);

            _dryFireSound = new AudioStreamPlayer3D();
            _dryFireSound.Name = "DryFireSound";
            AddChild(_dryFireSound);

            var fireStream = new AudioStreamWav();
            fireStream.Data = GenerateClickWav(0.05f, 800f, 0.3f);
            _fireSound.Stream = fireStream;

            var dryStream = new AudioStreamWav();
            dryStream.Data = GenerateClickWav(0.03f, 400f, 0.1f);
            _dryFireSound.Stream = dryStream;
        }

        private void CreateImpactEffect()
        {
            var impactRoot = new Node3D();

            var particles = new GpuParticles3D();
            particles.Name = "ImpactParticles";
            particles.Amount = 8;
            particles.Lifetime = 0.3f;
            particles.OneShot = true;
            particles.Explosiveness = 1.0f;
            particles.LocalCoords = false;

            var pm = new ParticleProcessMaterial();
            pm.Direction = new Vector3(0, 1, 0);
            pm.Spread = 180f;
            pm.Gravity = new Vector3(0, -5f, 0);
            pm.InitialVelocityMin = 1f;
            pm.InitialVelocityMax = 3f;
            pm.Color = new Color(0.6f, 0.5f, 0.4f);
            pm.ScaleMin = 0.02f;
            pm.ScaleMax = 0.05f;
            pm.LifetimeRandomness = 0.3f;
            particles.ProcessMaterial = pm;
            impactRoot.AddChild(particles);

            _impactEffect = new PackedScene();
            _impactEffect.Pack(impactRoot);
        }

        public override void _Process(double delta)
        {
            if (_fireCooldown > 0)
                _fireCooldown -= (float)delta;

            if (_isReloading)
            {
                _reloadTimer -= (float)delta;
                if (_reloadTimer <= 0)
                    CompleteReload();
            }
        }

        public void Fire(Camera3D camera)
        {
            if (_fireCooldown > 0 || _isReloading) return;

            if (_currentAmmo <= 0)
            {
                _dryFireSound?.Play();
                return;
            }

            _currentAmmo--;
            _fireCooldown = FireRate;

            ShowMuzzleFlash();

            _fireSound?.Play();

            Vector3 from = camera.GlobalPosition;
            Vector3 dir = -camera.GlobalTransform.Basis.Z;

            var space = camera.GetWorld3D().DirectSpaceState;
            var query = new PhysicsRayQueryParameters3D();
            query.From = from;
            query.To = from + dir * Range;
            query.CollisionMask = 0xFFFFFFFF;

            var result = space.IntersectRay(query);
            if (result != null && result.Count > 0)
            {
                SpawnImpact(result["position"].AsVector3(), result["normal"].AsVector3());

                var collider = result["collider"].AsGodotObject();
                if (collider is NpcAgent npc)
                {
                    npc.TakeDamage(Damage);
                }
                else if (collider is PlayerController player)
                {
                    player.TakeDamage(Damage * 0.3f);
                }
            }

            ApplyRecoil();
        }

        private void ShowMuzzleFlash()
        {
            if (_muzzleFlash == null) return;
            _muzzleFlash.Visible = true;

            var timer = GetTree().CreateTimer(0.05f);
            timer.Timeout += () =>
            {
                if (_muzzleFlash != null)
                    _muzzleFlash.Visible = false;
            };
        }

        private void SpawnImpact(Vector3 position, Vector3 normal)
        {
            if (_impactEffect == null) return;

            var impact = _impactEffect.Instantiate<Node3D>();
            GetTree().Root.AddChild(impact);
            impact.GlobalPosition = position;
            impact.LookAt(position + normal, Vector3.Up);

            var particles = impact.GetNode<GpuParticles3D>("ImpactParticles");
            particles?.Restart();
            particles?.SetEmitting(true);

            var timer = GetTree().CreateTimer(1.0f);
            timer.Timeout += () =>
            {
                if (impact != null)
                    impact.QueueFree();
            };
        }

        private void ApplyRecoil()
        {
            var camera = GetParent()?.GetParent<Camera3D>();
            if (camera == null) return;

            float recoilPitch = -0.003f;
            float recoilYaw = (float)(new Random().NextDouble() - 0.5) * 0.002f;

            camera.Rotation = new Vector3(
                Mathf.Clamp(camera.Rotation.X + recoilPitch, -Mathf.Pi / 2, Mathf.Pi / 2),
                camera.Rotation.Y + recoilYaw,
                0
            );
        }

        public void Reload()
        {
            if (_isReloading || _currentAmmo >= MaxAmmo || _reserveAmmo <= 0) return;
            _isReloading = true;
            _reloadTimer = ReloadTime;
        }

        private void CompleteReload()
        {
            int needed = MaxAmmo - _currentAmmo;
            int available = Mathf.Min(needed, _reserveAmmo);
            _currentAmmo += available;
            _reserveAmmo -= available;
            _isReloading = false;
        }

        public void AddAmmo(int amount)
        {
            _reserveAmmo = Mathf.Min(MaxReserveAmmo, _reserveAmmo + amount);
        }

        public void UpdateBob(float delta, bool isMoving)
        {
            if (!isMoving)
            {
                _bobTime = 0;
                Position = Position.Lerp(_restPosition, delta * 8f);
                return;
            }

            _bobTime += delta * WeaponBobFreq;
            float bobX = Mathf.Sin(_bobTime) * WeaponBobAmp;
            float bobY = Mathf.Abs(Mathf.Cos(_bobTime)) * WeaponBobAmp * 0.5f;

            Position = _restPosition + new Vector3(bobX, bobY, 0);
        }

        private byte[] GenerateClickWav(float duration, float frequency, float amplitude)
        {
            int sampleRate = 22050;
            int numSamples = (int)(sampleRate * duration);
            short[] samples = new short[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Max(0, 1f - t / duration);
                float sample = Mathf.Sin(t * frequency * Mathf.Pi * 2) * envelope * amplitude;
                samples[i] = (short)(sample * short.MaxValue);
            }

            int dataSize = numSamples * 2;
            byte[] wav = new byte[44 + dataSize];

            BitConverter.GetBytes(0x46464952).CopyTo(wav, 0);
            BitConverter.GetBytes(36 + dataSize).CopyTo(wav, 4);
            BitConverter.GetBytes(0x45564157).CopyTo(wav, 8);
            BitConverter.GetBytes(0x20746D66).CopyTo(wav, 12);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((ushort)2).CopyTo(wav, 32);
            BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);
            BitConverter.GetBytes(0x61746164).CopyTo(wav, 36);
            BitConverter.GetBytes(dataSize).CopyTo(wav, 40);

            Buffer.BlockCopy(samples, 0, wav, 44, dataSize);
            return wav;
        }
    }
}
