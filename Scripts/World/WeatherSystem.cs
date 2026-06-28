using Godot;
using System;
using System.Collections.Generic;

namespace OpenFo3.World
{
    public enum WeatherType
    {
        Clear, Cloudy, Overcast, Foggy, Rain, Thunderstorm, RadStorm
    }

    public struct WeatherParams
    {
        public WeatherType Type;
        public Color SkyTint;
        public Color FogColor;
        public float FogDistance;
        public float WindSpeed;
        public float RainIntensity;
        public float CloudCover;
        public float RadIntensity;
    }

    public partial class WeatherSystem : Node3D
    {
        [Export] public float DayLength { get; set; } = 14400f;
        [Export] public float WeatherChangeInterval { get; set; } = 300f;
        [Export] public float TransitionDuration { get; set; } = 30f;

        private float _gameTime;
        private float _weatherTimer;
        private bool _isNight;

        private WeatherParams _currentWeather;
        private WeatherParams _targetWeather;
        private float _transitionProgress = 1f;

        private DirectionalLight3D _sun;
        private WorldEnvironment _environment;
        private GpuParticles3D _rainParticles;
        private GpuParticles3D _radstormParticles;
        private AudioStreamPlayer3D _rainAudio;
        private AudioStreamPlayer3D _windAudio;
        private Node3D _cloudLayer;

        private Vector3 _windDirection = new Vector3(1, 0, 0.5f);
        private float _windSpeed;

        private Color _dayColor = new Color(0.7f, 0.7f, 1f);
        private Color _nightColor = new Color(0.05f, 0.05f, 0.15f);
        private Color _sunriseColor = new Color(1f, 0.6f, 0.3f);
        private Color _sunsetColor = new Color(0.8f, 0.3f, 0.1f);

        [Signal]
        public delegate void WeatherChangedEventHandler(WeatherType newWeather);

        [Signal]
        public delegate void TimeOfDayChangedEventHandler(float hour, bool isNight);

        public float GameTime => _gameTime;
        public float Hour => _gameTime / 3600f;
        public bool IsNight => _isNight;
        public WeatherType CurrentWeatherType => _currentWeather.Type;

        public override void _Ready()
        {
            SetupSceneNodes();
            _currentWeather = CreateWeather(WeatherType.Clear);
            _targetWeather = _currentWeather;
            ApplyWeather(_currentWeather, 1f);
            _gameTime = 43200f;
        }

        private void SetupSceneNodes()
        {
            _sun = GetNodeOrNull<DirectionalLight3D>("../DirectionalLight3D");
            if (_sun == null)
            {
                _sun = new DirectionalLight3D();
                _sun.Name = "WeatherSun";
                _sun.ShadowEnabled = true;
                _sun.ShadowBias = 0.05f;
                AddChild(_sun);
            }

            _environment = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment");
            if (_environment == null)
            {
                _environment = new WorldEnvironment();
                _environment.Name = "WeatherEnvironment";
                _environment.Environment = new Godot.Environment();
                AddChild(_environment);
            }

            _rainParticles = new GpuParticles3D();
            _rainParticles.Name = "RainParticles";
            _rainParticles.Emitting = false;
            _rainParticles.Amount = 10000;
            _rainParticles.Lifetime = 2.0f;
            _rainParticles.OneShot = false;
            _rainParticles.LocalCoords = false;
            _rainParticles.Position = new Vector3(0, 20, 0);

            var rainMat = new ParticleProcessMaterial();
            rainMat.Direction = new Vector3(0, -1, 0);
            rainMat.Spread = 10f;
            rainMat.Gravity = new Vector3(0, -20f, 0);
            rainMat.InitialVelocityMin = 5f;
            rainMat.InitialVelocityMax = 15f;
            rainMat.Color = new Color(0.6f, 0.7f, 1f, 0.5f);
            rainMat.ScaleMin = 0.1f;
            rainMat.ScaleMax = 0.3f;
            rainMat.LifetimeRandomness = 0.5f;
            _rainParticles.ProcessMaterial = rainMat;
            AddChild(_rainParticles);

            _radstormParticles = new GpuParticles3D();
            _radstormParticles.Name = "RadStormParticles";
            _radstormParticles.Emitting = false;
            _radstormParticles.Amount = 5000;
            _radstormParticles.Lifetime = 3.0f;
            _radstormParticles.OneShot = false;
            _radstormParticles.Position = new Vector3(0, 25, 0);

            var radMat = new ParticleProcessMaterial();
            radMat.Direction = new Vector3(0, -1, 0);
            radMat.Spread = 30f;
            radMat.Gravity = new Vector3(0, -5f, 0);
            radMat.InitialVelocityMin = 2f;
            radMat.InitialVelocityMax = 8f;
            radMat.Color = new Color(0.2f, 1f, 0.2f, 0.6f);
            radMat.ScaleMin = 0.2f;
            radMat.ScaleMax = 0.5f;
            radMat.LifetimeRandomness = 0.3f;
            _radstormParticles.ProcessMaterial = radMat;
            AddChild(_radstormParticles);

            _rainAudio = new AudioStreamPlayer3D();
            _rainAudio.Name = "RainAudio";
            _rainAudio.MaxDistance = 100f;
            _rainAudio.UnitSize = 50f;
            _rainAudio.VolumeDb = -20f;
            AddChild(_rainAudio);

            _windAudio = new AudioStreamPlayer3D();
            _windAudio.Name = "WindAudio";
            _windAudio.MaxDistance = 100f;
            _windAudio.UnitSize = 50f;
            _windAudio.VolumeDb = -25f;
            AddChild(_windAudio);

            _cloudLayer = new Node3D();
            _cloudLayer.Name = "CloudLayer";
            AddChild(_cloudLayer);

            var rng = new Random();
            for (int i = 0; i < 12; i++)
            {
                var cloud = new MeshInstance3D();
                var quad = new QuadMesh();
                quad.Size = new Vector2(20, 20);
                quad.FlipFaces = true;
                cloud.Mesh = quad;
                cloud.Position = new Vector3(
                    (float)(rng.NextDouble() - 0.5) * 100f,
                    30f + (float)rng.NextDouble() * 10f,
                    (float)(rng.NextDouble() - 0.5) * 100f
                );
                cloud.MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1, 1, 1, 0.4f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                _cloudLayer.AddChild(cloud);
            }
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            _gameTime += dt * (86400f / DayLength);
            if (_gameTime > 86400f) _gameTime -= 86400f;

            UpdateSunPosition();
            UpdateWind(dt);
            UpdateWeatherTransition(dt);

            bool wasNight = _isNight;
            _isNight = Hour < 6f || Hour > 20f;
            if (wasNight != _isNight)
                EmitSignal(nameof(TimeOfDayChangedEventHandler), Hour, _isNight);
        }

        private void UpdateSunPosition()
        {
            if (_sun == null) return;

            float sunAngle = (Hour - 6f) / 14f * Mathf.Pi;
            float sunX = Mathf.Cos(sunAngle) * 100f;
            float sunY = Mathf.Sin(sunAngle) * 100f;
            _sun.Position = new Vector3(sunX, Mathf.Max(sunY, -20f), -20f);
            _sun.LookAt(Vector3.Zero);

            float noon = Mathf.Clamp(sunAngle, 0, Mathf.Pi);
            float intensity = Mathf.Sin(noon);
            _sun.LightEnergy = Mathf.Lerp(0.2f, 1.5f, intensity);

            if (Hour >= 5f && Hour <= 7f)
                _sun.LightColor = _sunriseColor;
            else if (Hour >= 18f && Hour <= 20f)
                _sun.LightColor = _sunsetColor;
            else
                _sun.LightColor = _dayColor;

            if (_environment?.Environment != null)
            {
                float ambientIntensity = Mathf.Lerp(0.05f, 0.3f, intensity);
                _environment.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                _environment.Environment.AmbientLightColor = new Color(
                    ambientIntensity,
                    ambientIntensity,
                    ambientIntensity * 1.2f
                );
            }
        }

        private void UpdateWind(float dt)
        {
            _windSpeed = Mathf.Lerp(_windSpeed, _currentWeather.WindSpeed, dt * 0.1f);
            float angle = _gameTime * 0.0001f;
            _windDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

            foreach (var child in _cloudLayer.GetChildren())
            {
                if (child is MeshInstance3D cloud)
                {
                    cloud.Position += _windDirection * _windSpeed * dt;
                    if (cloud.Position.X > 60f) cloud.Position = new Vector3(-60f, cloud.Position.Y, cloud.Position.Z);
                    if (cloud.Position.X < -60f) cloud.Position = new Vector3(60f, cloud.Position.Y, cloud.Position.Z);
                    if (cloud.Position.Z > 60f) cloud.Position = new Vector3(cloud.Position.X, cloud.Position.Y, -60f);
                    if (cloud.Position.Z < -60f) cloud.Position = new Vector3(cloud.Position.X, cloud.Position.Y, 60f);

                    float alpha = Mathf.Lerp(0.1f, 0.6f, _currentWeather.CloudCover);
                    if (cloud.MaterialOverride is StandardMaterial3D mat)
                        mat.AlbedoColor = new Color(1, 1, 1, alpha);
                }
            }

            float windVol = Mathf.Lerp(-40f, -10f, _windSpeed / 20f);
            _windAudio.VolumeDb = windVol;
        }

        private void UpdateWeatherTransition(float dt)
        {
            _weatherTimer -= dt;
            if (_weatherTimer <= 0f && _transitionProgress >= 1f)
            {
                PickNewWeather();
                _weatherTimer = WeatherChangeInterval;
            }

            if (_transitionProgress < 1f)
            {
                _transitionProgress = Mathf.Min(1f, _transitionProgress + dt / TransitionDuration);
                ApplyWeather(_currentWeather, _transitionProgress);
                if (_transitionProgress >= 1f)
                {
                    _currentWeather = _targetWeather;
                    EmitSignal(nameof(WeatherChangedEventHandler), (int)_currentWeather.Type);
                }
            }
        }

        private void PickNewWeather()
        {
            var rng = new Random();
            double roll = rng.NextDouble();

            WeatherType newType;
            if (Hour >= 6f && Hour <= 20f)
            {
                if (roll < 0.4f) newType = WeatherType.Clear;
                else if (roll < 0.65f) newType = WeatherType.Cloudy;
                else if (roll < 0.8f) newType = WeatherType.Overcast;
                else if (roll < 0.9f) newType = WeatherType.Foggy;
                else if (roll < 0.95f) newType = WeatherType.Rain;
                else newType = WeatherType.Thunderstorm;
            }
            else
            {
                if (roll < 0.3f) newType = WeatherType.Clear;
                else if (roll < 0.5f) newType = WeatherType.Cloudy;
                else if (roll < 0.7f) newType = WeatherType.Foggy;
                else if (roll < 0.85f) newType = WeatherType.Overcast;
                else if (roll < 0.95f) newType = WeatherType.Rain;
                else newType = WeatherType.Thunderstorm;
            }

            if (rng.NextDouble() < 0.02f)
                newType = WeatherType.RadStorm;

            _targetWeather = CreateWeather(newType);
            _transitionProgress = 0f;
        }

        private static WeatherParams CreateWeather(WeatherType type)
        {
            return type switch
            {
                WeatherType.Clear => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.6f, 0.7f, 1f),
                    FogColor = new Color(0.4f, 0.5f, 0.6f), FogDistance = 300f,
                    WindSpeed = 2f, RainIntensity = 0f, CloudCover = 0.1f, RadIntensity = 0f,
                },
                WeatherType.Cloudy => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.5f, 0.55f, 0.7f),
                    FogColor = new Color(0.35f, 0.4f, 0.5f), FogDistance = 200f,
                    WindSpeed = 4f, RainIntensity = 0f, CloudCover = 0.5f, RadIntensity = 0f,
                },
                WeatherType.Overcast => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.3f, 0.35f, 0.45f),
                    FogColor = new Color(0.25f, 0.3f, 0.4f), FogDistance = 120f,
                    WindSpeed = 6f, RainIntensity = 0f, CloudCover = 0.85f, RadIntensity = 0f,
                },
                WeatherType.Foggy => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.4f, 0.45f, 0.5f),
                    FogColor = new Color(0.5f, 0.5f, 0.55f), FogDistance = 40f,
                    WindSpeed = 1f, RainIntensity = 0f, CloudCover = 0.6f, RadIntensity = 0f,
                },
                WeatherType.Rain => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.25f, 0.3f, 0.4f),
                    FogColor = new Color(0.2f, 0.25f, 0.35f), FogDistance = 80f,
                    WindSpeed = 8f, RainIntensity = 0.5f, CloudCover = 0.9f, RadIntensity = 0f,
                },
                WeatherType.Thunderstorm => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.15f, 0.15f, 0.2f),
                    FogColor = new Color(0.1f, 0.1f, 0.15f), FogDistance = 50f,
                    WindSpeed = 14f, RainIntensity = 1.0f, CloudCover = 1.0f, RadIntensity = 0f,
                },
                WeatherType.RadStorm => new WeatherParams
                {
                    Type = type, SkyTint = new Color(0.1f, 0.5f, 0.1f),
                    FogColor = new Color(0.1f, 0.4f, 0.1f), FogDistance = 30f,
                    WindSpeed = 10f, RainIntensity = 0f, CloudCover = 1.0f, RadIntensity = 1.0f,
                },
                _ => new WeatherParams { Type = WeatherType.Clear },
            };
        }

        private void ApplyWeather(WeatherParams weather, float weight)
        {
            float invW = 1f - weight;
            var final = _targetWeather;

            _rainParticles.Emitting = final.RainIntensity > 0.1f;
            _rainParticles.Amount = (int)(10000 * final.RainIntensity);

            float rainVol = Mathf.Lerp(-60f, -10f, final.RainIntensity);
            _rainAudio.VolumeDb = rainVol;

            _radstormParticles.Emitting = final.RadIntensity > 0.1f;

            if (_environment?.Environment != null)
            {
                bool fogEnabled = final.FogDistance < 200f;
                _environment.Environment.FogEnabled = fogEnabled;
                // Convert distance (30-300) to density (0.05-0.002)
                _environment.Environment.FogDensity = fogEnabled
                    ? Mathf.Clamp(15f / final.FogDistance, 0.002f, 0.5f)
                    : 0f;
                _environment.Environment.FogLightColor = final.FogColor;
            }
        }

        public void SetTime(float hour)
        {
            _gameTime = Mathf.Clamp(hour, 0, 24) * 3600f;
        }

        public void ForceWeather(WeatherType type)
        {
            _targetWeather = CreateWeather(type);
            _transitionProgress = 0f;
            _weatherTimer = WeatherChangeInterval;
        }
    }
}
