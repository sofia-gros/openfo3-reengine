using Godot;
using System;

namespace OpenFo3.Rendering
{
    public enum TonemapMode
    {
        Linear, Filmic, ACES, Reinhardt
    }

    public partial class PostProcessing : Node
    {
        [Export] public TonemapMode Tonemapping { get; set; } = TonemapMode.ACES;
        [Export] public bool EnableBloom { get; set; } = true;
        [Export] public bool EnableSSAO { get; set; } = true;
        [Export] public bool EnableSSR { get; set; } = true;
        [Export] public bool EnableDoF { get; set; } = false;
        [Export] public bool EnableMotionBlur { get; set; } = true;
        [Export] public bool EnableVolumetricFog { get; set; } = true;
        [Export] public bool EnableColorGrading { get; set; } = true;

        [Export] public float BloomIntensity { get; set; } = 1.0f;
        [Export] public float SSAOIntensity { get; set; } = 1.0f;
        [Export] public float SSRMaxSteps { get; set; } = 64;
        [Export] public float DoFFarDistance { get; set; } = 100f;
        [Export] public float DoFAmount { get; set; } = 0.1f;
        [Export] public float MotionBlurIntensity { get; set; } = 0.1f;

        [Export] public Color GradeShadows { get; set; } = new Color(0.2f, 0.15f, 0.05f);
        [Export] public Color GradeMidtones { get; set; } = new Color(0.6f, 0.55f, 0.4f);
        [Export] public Color GradeHighlights { get; set; } = new Color(1f, 0.95f, 0.8f);

        private WorldEnvironment _env;
        private Godot.Environment _envResource;
        private CameraAttributesPractical _cameraAttributes;

        public override void _Ready()
        {
            _env = GetNodeOrNull<WorldEnvironment>("../WorldEnvironment");
            if (_env == null)
                _env = GetNodeOrNull<WorldEnvironment>("/root/Megaton/WorldEnvironment");
            if (_env == null)
            {
                _env = new WorldEnvironment();
                _env.Name = "PostProcessEnvironment";
                GetParent()?.AddChild(_env);
            }

            if (_env.Environment == null)
            {
                _envResource = new Godot.Environment();
                _env.Environment = _envResource;
            }
            else
            {
                _envResource = _env.Environment;
            }

            _cameraAttributes = new CameraAttributesPractical();
            ApplyAllEffects();
            SetupCameraAttributes();
        }

        private void SetupCameraAttributes()
        {
            var camera = GetViewport()?.GetCamera3D();
            if (camera == null)
            {
                camera = GetNodeOrNull<Camera3D>("/root/Megaton/PlayerController/Camera3D");
            }
            if (camera != null)
            {
                camera.Attributes = _cameraAttributes;
            }
        }

        public void ApplyAllEffects()
        {
            if (_envResource == null) return;

            _envResource.BackgroundMode = Godot.Environment.BGMode.Sky;

            // === TONEMAPPING ===
            _envResource.TonemapMode = Tonemapping switch
            {
                TonemapMode.Linear => Godot.Environment.ToneMapper.Linear,
                TonemapMode.Filmic => Godot.Environment.ToneMapper.Filmic,
                TonemapMode.ACES => Godot.Environment.ToneMapper.Aces,
                TonemapMode.Reinhardt => Godot.Environment.ToneMapper.Reinhardt,
                _ => Godot.Environment.ToneMapper.Aces,
            };
            _envResource.TonemapExposure = 1.0f;
            _envResource.TonemapWhite = 10.0f;

            // === BLOOM ===
            _envResource.GlowEnabled = EnableBloom;
            _envResource.GlowIntensity = BloomIntensity;
            _envResource.GlowStrength = 0.8f;
            _envResource.GlowBloom = BloomIntensity * 0.3f;
            _envResource.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;
            _envResource.GlowHdrThreshold = 0.8f;
            _envResource.GlowHdrScale = 2.0f;

            // === SSAO ===
            _envResource.SsaoEnabled = EnableSSAO;
            _envResource.SsaoIntensity = SSAOIntensity;
            _envResource.SsaoRadius = 1.0f;
            _envResource.SsaoPower = 0.5f;
            _envResource.SsaoDetail = 0.5f;
            _envResource.SsaoHorizon = 0.01f;
            _envResource.SsaoSharpness = 4.0f;
            _envResource.SsaoLightAffect = 0.2f;
            _envResource.SsaoAOChannelAffect = 0.0f;

            // === SSR ===
            _envResource.SsrEnabled = EnableSSR;
            _envResource.SsrMaxSteps = (int)SSRMaxSteps;
            _envResource.SsrFadeIn = 0.1f;
            _envResource.SsrFadeOut = 1.0f;
            _envResource.SsrDepthTolerance = 0.2f;

            // === DEPTH OF FIELD (via CameraAttributesPractical) ===
            if (_cameraAttributes != null)
            {
                _cameraAttributes.DofBlurFarEnabled = EnableDoF;
                _cameraAttributes.DofBlurFarDistance = DoFFarDistance;
                _cameraAttributes.DofBlurFarTransition = DoFFarDistance * 0.5f;
                _cameraAttributes.DofBlurAmount = DoFAmount;
                _cameraAttributes.DofBlurNearEnabled = false;
            }

            // === MOTION BLUR (not available in Redot 26.1 Environment API) ===
            if (EnableMotionBlur)
            {
                GD.Print("[PostProcessing] Motion blur not supported in Redot 26.1 Environment. Skipping.");
            }

            // === VOLUMETRIC FOG ===
            _envResource.VolumetricFogEnabled = EnableVolumetricFog;
            _envResource.VolumetricFogDensity = 0.01f;
            _envResource.VolumetricFogAlbedo = new Color(1, 0.9f, 0.8f);
            _envResource.VolumetricFogEmission = new Color(0.1f, 0.1f, 0.05f);
            _envResource.VolumetricFogEmissionEnergy = 1.0f;
            _envResource.VolumetricFogAnisotropy = 0.5f;
            _envResource.VolumetricFogAmbientInject = 0.1f;
            _envResource.VolumetricFogGIInject = 0.3f;
            _envResource.VolumetricFogLength = 50.0f;
            _envResource.VolumetricFogDetailSpread = 2.0f;
            _envResource.VolumetricFogSkyAffect = 0.5f;
            _envResource.VolumetricFogTemporalReprojectionEnabled = true;
            _envResource.VolumetricFogTemporalReprojectionAmount = 0.5f;

            // === ADJUSTMENT (Color Grading) ===
            _envResource.AdjustmentEnabled = EnableColorGrading;
            _envResource.AdjustmentBrightness = 1.0f;
            _envResource.AdjustmentContrast = 1.1f;
            _envResource.AdjustmentSaturation = 0.85f;
            _envResource.AdjustmentColorCorrection = null;

            GD.Print("[PostProcessing] Applied all effects");
        }

        public void SetBloomIntensity(float intensity)
        {
            BloomIntensity = intensity;
            if (_envResource == null) return;
            _envResource.GlowIntensity = intensity;
            _envResource.GlowBloom = intensity * 0.3f;
        }

        public void SetTonemap(TonemapMode mode)
        {
            Tonemapping = mode;
            if (_envResource == null) return;
            _envResource.TonemapMode = mode switch
            {
                TonemapMode.Linear => Godot.Environment.ToneMapper.Linear,
                TonemapMode.Filmic => Godot.Environment.ToneMapper.Filmic,
                TonemapMode.ACES => Godot.Environment.ToneMapper.Aces,
                TonemapMode.Reinhardt => Godot.Environment.ToneMapper.Reinhardt,
                _ => Godot.Environment.ToneMapper.Aces,
            };
        }

        public void SetDOF(float distance, float amount)
        {
            DoFFarDistance = distance;
            DoFAmount = amount;
            if (_cameraAttributes == null) return;
            _cameraAttributes.DofBlurFarEnabled = distance > 0;
            _cameraAttributes.DofBlurFarDistance = distance;
            _cameraAttributes.DofBlurFarTransition = distance * 0.5f;
            _cameraAttributes.DofBlurAmount = amount;
        }

        public void ToggleEffect(string name)
        {
            switch (name.ToLower())
            {
                case "bloom":
                    EnableBloom = !EnableBloom;
                    if (_envResource != null) _envResource.GlowEnabled = EnableBloom;
                    break;
                case "ssao":
                    EnableSSAO = !EnableSSAO;
                    if (_envResource != null) _envResource.SsaoEnabled = EnableSSAO;
                    break;
                case "ssr":
                    EnableSSR = !EnableSSR;
                    if (_envResource != null) _envResource.SsrEnabled = EnableSSR;
                    break;
                case "dof":
                    EnableDoF = !EnableDoF;
                    if (_cameraAttributes != null) _cameraAttributes.DofBlurFarEnabled = EnableDoF;
                    break;
                case "motionblur":
                    EnableMotionBlur = !EnableMotionBlur;
                    GD.Print("[PostProcessing] Motion blur toggled but not supported in Redot 26.1");
                    break;
                case "fog":
                    EnableVolumetricFog = !EnableVolumetricFog;
                    if (_envResource != null) _envResource.VolumetricFogEnabled = EnableVolumetricFog;
                    break;
                case "color":
                    EnableColorGrading = !EnableColorGrading;
                    if (_envResource != null) _envResource.AdjustmentEnabled = EnableColorGrading;
                    break;
            }
            GD.Print($"[PostProcessing] Toggled {name}");
        }
    }
}
