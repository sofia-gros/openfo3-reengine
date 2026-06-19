using Godot;
using System;

namespace OpenFo3.NIF
{
    public static class NIFMaterialBuilder
    {
        // FO3 BSShaderTextureSet slot indices
        private const int SlotDiffuse = 0;
        private const int SlotNormalGloss = 1;
        private const int SlotGlowSkinHair = 2;
        private const int SlotHeightParallax = 3;
        private const int SlotEnvironment = 4;
        private const int SlotEnvironmentMask = 5;
        private const int SlotSubsurface = 6;
        private const int SlotBackLighting = 7;

        public static StandardMaterial3D BuildMaterial(ShaderTextureInfo shader, AlphaPropertyInfo alpha,
            Func<string, Texture2D> loadTexture)
        {
            var mat = new StandardMaterial3D();

            if (shader == null)
            {
                mat.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
                return mat;
            }

            var texPaths = shader.TexturePaths;
            bool hasAlphaBlend = false;
            bool hasAlphaTest = false;
            int alphaThreshold = 128;

            if (alpha != null)
            {
                ushort f = alpha.Flags;
                hasAlphaBlend = (f & 0x0001) != 0;
                hasAlphaTest = (f & 0x0200) != 0;
                alphaThreshold = alpha.Threshold;

                if (hasAlphaBlend)
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                }
                else if (hasAlphaTest)
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    mat.AlphaScissorThreshold = alphaThreshold / 255f;
                }
            }

            // NiAlphaProperty is the authoritative source for alpha behavior.
            // ShaderFlags bit 8 (AlphaTexture) does NOT necessarily mean alpha
            // testing is needed — many FO3 textures store non-transparency data
            // (gloss/roughness) in the alpha channel. Forcing alpha scissor here
            // causes those meshes to become transparent from certain angles.

            if ((shader.ShaderFlags2 & (1 << 5)) != 0)
            {
                mat.VertexColorUseAsAlbedo = true;
            }

            if ((shader.ShaderFlags & 0x00000001) != 0)
            {
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
                mat.Metallic = 0.0f;
            }

            // --- Diffuse / Albedo (Slot 0) ---
            if (texPaths != null && texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                }
            }

            // --- Normal Map (Slot 1, with gloss in alpha) ---
            if (texPaths != null && texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;

                    // FO3 packs gloss in normal map alpha; we use it as roughness
                    mat.RoughnessTexture = tex;
                    mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Alpha;
                }
            }

            // --- Environment Map (Slot 4) ---

            // --- Environment Map (Slot 4) ---
            if (texPaths != null && texPaths.Length > SlotEnvironment && !string.IsNullOrEmpty(texPaths[SlotEnvironment]))
            {
                var tex = loadTexture(texPaths[SlotEnvironment]);
                if (tex != null)
                {
                    // Godot doesn't directly support environment map textures on StandardMaterial3D,
                    // but we can use it as a metallic/roughness hint
                    mat.MetallicTexture = tex;
                    mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
                }
            }

            if ((shader.ShaderFlags & (1 << 7)) != 0)
            {
                mat.Metallic = 0.3f;
                mat.Roughness = 0.4f;
            }

            // --- Emissive / Glow (Slot 2, Slot 3) ---
            string glowPath = null;
            if (texPaths != null && texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
            {
                glowPath = texPaths[SlotGlowSkinHair];
            }

            if (!string.IsNullOrEmpty(glowPath))
            {
                var tex = loadTexture(glowPath);
                if (tex != null)
                {
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }

            // --- Heightmap / Parallax (Slot 3) ---
            if (texPaths != null && texPaths.Length > SlotHeightParallax && !string.IsNullOrEmpty(texPaths[SlotHeightParallax]))
            {
                var tex = loadTexture(texPaths[SlotHeightParallax]);
                if (tex != null)
                {
                    mat.HeightmapTexture = tex;
                    mat.HeightmapEnabled = true;
                    mat.HeightmapScale = shader.ParallaxScale > 0 ? shader.ParallaxScale : 0.05f;
                    mat.HeightmapMaxLayers = Mathf.Max(1, (int)shader.ParallaxMaxPasses);
                }
            }

            // --- Detail Map (multi-layer parallax subsurface slot 6, or use as detail) ---
            if (texPaths != null && texPaths.Length > SlotSubsurface && !string.IsNullOrEmpty(texPaths[SlotSubsurface]))
            {
                var tex = loadTexture(texPaths[SlotSubsurface]);
                if (tex != null)
                {
                    mat.DetailAlbedo = tex;
                    mat.DetailEnabled = true;
                }
            }

            // --- Refraction ---
            if ((shader.ShaderFlags & (1 << 15)) != 0 || (shader.ShaderFlags & (1 << 16)) != 0)
            {
                mat.RefractionEnabled = true;
                mat.RefractionScale = shader.RefractionStrength > 0 ? shader.RefractionStrength : 0.05f;
            }

            return mat;
        }
    }
}
