using Godot;
using System;

namespace OpenFo3.NIF
{
    public static class NIFMaterialBuilder
    {
        private const int SlotDiffuse = 0;
        private const int SlotNormalGloss = 1;
        private const int SlotGlowSkinHair = 2;
        private const int SlotHeightParallax = 3;
        private const int SlotEnvironment = 4;
        private const int SlotEnvironmentMask = 5;
        private const int SlotSubsurface = 6;
        private const int SlotBackLighting = 7;

        const int ShaderTypeDefault = 0;
        const int ShaderTypeDefaultAlt = 1;
        const int ShaderTypeEnvironmentMap = 2;
        const int ShaderTypeGlowShader = 3;
        const int ShaderTypeHeightmap = 4;
        const int ShaderTypeZBufferWrite = 5;
        const int ShaderTypeLODLandscape = 6;
        const int ShaderTypeLODBuilding = 7;
        const int ShaderTypeMultiLayerParallax = 10;
        const int ShaderTypeParallaxOcc = 11;
        const int ShaderTypeSnowShader = 14;
        const int ShaderTypeMultiLayerParallaxOcc = 15;
        const int ShaderTypeEnvironmentMapFO3 = 17;
        const int ShaderTypeWing = 29;
        const int ShaderTypeSkinTint = 31;
        const int ShaderTypeHairTint = 32;
        const int ShaderTypeParallaxOccInner = 33;
        const int ShaderTypeTallGrass = 34;
        const int ShaderTypeLODLandscapeNoGrass = 35;

        const int ShaderTypeFO3Sky = 40;
        const int ShaderTypeFO3Water = 41;
        const int ShaderTypeFO3Unlit = 42;

        private static bool IsShadowTexture(string[] texPaths)
        {
            if (texPaths == null) return false;
            for (int i = 0; i < texPaths.Length; i++)
            {
                if (string.IsNullOrEmpty(texPaths[i])) continue;
                string p = texPaths[i].ToLowerInvariant();
                if (p.Contains("shadow") || p.Contains("shdw") || p.Contains("ambient_occlusion"))
                    return true;
            }
            return false;
        }

        public static StandardMaterial3D BuildMaterial(ShaderTextureInfo shader, AlphaPropertyInfo alpha,
            Func<string, Texture2D> loadTexture, Func<string, bool> textureHasAlpha = null)
        {
            var mat = new StandardMaterial3D();

            if (shader == null)
            {
                mat.AlbedoColor = new Color(0.8f, 0.8f, 0.8f);
                mat.Roughness = 0.8f;
                mat.Metallic = 0.0f;
                return mat;
            }

            var texPaths = shader.TexturePaths;

            // Check if this is a shadow/ambient occlusion surface (white mesh fix)
            if (IsShadowTexture(texPaths))
            {
                BuildShadow(mat, shader, texPaths, loadTexture);
                return mat;
            }

            ApplyAlpha(mat, shader, alpha, texPaths, textureHasAlpha);

            if ((shader.ShaderFlags2 & (1 << 5)) != 0)
                mat.VertexColorUseAsAlbedo = true;

            switch (shader.ShaderType)
            {
                case ShaderTypeGlowShader:
                    BuildGlowShader(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeEnvironmentMap:
                case ShaderTypeEnvironmentMapFO3:
                    BuildEnvironmentMap(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeSkinTint:
                    BuildSkinTint(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeHairTint:
                    BuildHairTint(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeTallGrass:
                    BuildTallGrass(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeMultiLayerParallax:
                case ShaderTypeParallaxOcc:
                case ShaderTypeMultiLayerParallaxOcc:
                case ShaderTypeParallaxOccInner:
                    BuildMultiLayerParallax(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeWing:
                    BuildWing(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeSnowShader:
                    BuildSnowShader(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeZBufferWrite:
                    BuildZBufferWrite(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeHeightmap:
                    BuildHeightmap(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeFO3Sky:
                    BuildFO3Sky(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeFO3Water:
                    BuildFO3Water(mat, shader, texPaths, loadTexture);
                    break;
                case ShaderTypeFO3Unlit:
                    BuildFO3Unlit(mat, shader, texPaths, loadTexture);
                    break;
                default:
                    BuildDefault(mat, shader, texPaths, loadTexture);
                    break;
            }

            ApplyHeightmap(mat, shader, texPaths, loadTexture);
            ApplyDetail(mat, texPaths, loadTexture);
            ApplyRefraction(mat, shader);
            return mat;
        }

        private static void BuildShadow(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            // FO3 pre-baked shadow/ambient occlusion geometry.
            // These are dark translucent meshes placed underneath building edges
            // to simulate soft shadows without real-time lighting.
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.Roughness = 0.8f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.DisableAmbientLight = true;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.AlbedoColor = new Color(0.05f, 0.05f, 0.08f, 0.4f);

            if (texPaths != null && texPaths.Length > 0 && !string.IsNullOrEmpty(texPaths[0]))
            {
                var tex = loadTexture(texPaths[0]);
                if (tex != null)
                    mat.AlbedoTexture = tex;
            }
        }

        private static void ApplyAlpha(StandardMaterial3D mat, ShaderTextureInfo shader,
            AlphaPropertyInfo alpha, string[] texPaths, Func<string, bool> textureHasAlpha)
        {
            if (alpha != null)
            {
                ushort f = alpha.Flags;
                bool hasAlphaBlend = (f & 0x0001) != 0;
                bool hasAlphaTest = (f & 0x0200) != 0;

                bool texActuallyHasAlpha = textureHasAlpha != null && texPaths != null &&
                    (TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotDiffuse) ||
                     TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotGlowSkinHair) ||
                     TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotHeightParallax));

                if (texActuallyHasAlpha)
                {
                    mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    if (hasAlphaBlend)
                        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    else if (hasAlphaTest)
                    {
                        mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                        mat.AlphaScissorThreshold = alpha.Threshold / 255f;
                    }
                }
            }
            else if ((shader.ShaderFlags & 0x00000100) != 0)
            {
                if (textureHasAlpha != null && texPaths != null &&
                    (TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotDiffuse) ||
                     TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotGlowSkinHair) ||
                     TextureHasAlphaForSlot(texPaths, textureHasAlpha, SlotHeightParallax)))
                {
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    mat.AlphaScissorThreshold = 0.5f;
                    mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                }
            }
        }

        private static bool TextureHasAlphaForSlot(string[] texPaths, Func<string, bool> textureHasAlpha, int slot)
        {
            return slot < texPaths.Length && !string.IsNullOrEmpty(texPaths[slot]) && textureHasAlpha(texPaths[slot]);
        }

        private static void ApplyDiffuse(StandardMaterial3D mat,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;
            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null) mat.AlbedoTexture = tex;
            }
        }

        private static void ApplyNormalMap(StandardMaterial3D mat,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;
            if (texPaths.Length > SlotNormalGloss && !string.IsNullOrEmpty(texPaths[SlotNormalGloss]))
            {
                var tex = loadTexture(texPaths[SlotNormalGloss]);
                if (tex != null)
                {
                    mat.NormalTexture = tex;
                    mat.NormalEnabled = true;
                }
            }
        }

        private static void ApplyEmission(StandardMaterial3D mat,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;
            if (texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
            {
                var tex = loadTexture(texPaths[SlotGlowSkinHair]);
                if (tex != null)
                {
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }
        }

        private static void ApplyEnvironmentMask(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null || texPaths.Length <= SlotEnvironmentMask) return;
            if (string.IsNullOrEmpty(texPaths[SlotEnvironmentMask])) return;

            var tex = loadTexture(texPaths[SlotEnvironmentMask]);
            if (tex == null) return;

            // Environment Mask (Slot 5) channel layout (FO3 convention):
            //   Red   = Gloss / Smoothness (inverse of roughness)
            //   Green = Specular intensity
            //   Blue  = Environment map reflection amount
            //
            // StandardMaterial3D mapping:
            //   RoughnessTexture (Red channel) — higher red = less rough (glossy)
            //   MetallicTexture  (Green channel) — green channel approximates specular intensity
            mat.RoughnessTexture = tex;
            mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
            mat.MetallicTexture = tex;
            mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Green;
        }

        private static void ApplyEnvironmentMap(StandardMaterial3D mat,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null || texPaths.Length <= SlotEnvironment) return;
            if (string.IsNullOrEmpty(texPaths[SlotEnvironment])) return;

            var tex = loadTexture(texPaths[SlotEnvironment]);
            if (tex == null) return;

            // Environment Map (Slot 4): Used as an additional metallic/reflectivity hint.
            // When present, it overrides the base metallic value with its red channel.
            mat.MetallicTexture = tex;
            mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Red;
        }

        private static void ApplyCommonTextures(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null) return;

            ApplyDiffuse(mat, texPaths, loadTexture);

            mat.Roughness = 0.6f;
            mat.Metallic = 0.0f;
        }

        private static void BuildDefault(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.6f;
            mat.Metallic = 0.0f;

            if ((shader.ShaderFlags & 0x00000001) != 0)
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
            else
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            if ((shader.ShaderFlags & (1 << 7)) != 0)
            {
                mat.Metallic = 0.3f;
                mat.Roughness = 0.5f;
            }

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEmission(mat, texPaths, loadTexture);

            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
            ApplyEnvironmentMap(mat, texPaths, loadTexture);
        }

        private static void BuildGlowShader(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.DisableAmbientLight = true;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.Roughness = 0.8f;
            mat.Metallic = 0.0f;

            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }

            ApplyEmission(mat, texPaths, loadTexture);
        }

        private static void BuildEnvironmentMap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.3f;
            mat.Metallic = 0.8f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;

            ApplyDiffuse(mat, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
            ApplyEnvironmentMap(mat, texPaths, loadTexture);
            ApplyEmission(mat, texPaths, loadTexture);
        }

        private static void BuildSkinTint(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.VertexColorUseAsAlbedo = true;
            mat.Roughness = 0.5f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            mat.SubsurfScatterEnabled = true;
            mat.SubsurfScatterStrength = 0.3f;
            mat.SubsurfScatterSkinMode = true;
            mat.SubsurfScatterTransmittanceEnabled = true;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);

            if (texPaths != null && texPaths.Length > SlotGlowSkinHair && !string.IsNullOrEmpty(texPaths[SlotGlowSkinHair]))
            {
                var tex = loadTexture(texPaths[SlotGlowSkinHair]);
                if (tex != null)
                {
                    var img = tex.GetImage();
                    if (img != null)
                    {
                        if (img.IsCompressed())
                        {
                            img.Decompress();
                        }
                        
                        if (!img.IsCompressed() && img.GetWidth() > 0 && img.GetHeight() > 0)
                        {
                            var sample = img.GetPixel(0, 0);
                            mat.SubsurfScatterTransmittanceColor = sample;
                        }
                    }
                }
            }
        }

        private static void BuildHairTint(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.VertexColorUseAsAlbedo = true;
            mat.Roughness = 0.7f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
        }

        private static void BuildTallGrass(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
            mat.AlphaScissorThreshold = 0.3f;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            mat.Roughness = 0.9f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
        }

        private static void BuildMultiLayerParallax(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            BuildDefault(mat, shader, texPaths, loadTexture);
        }

        private static void BuildHeightmap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            BuildDefault(mat, shader, texPaths, loadTexture);
        }

        private static void BuildWing(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.6f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
            ApplyDiffuse(mat, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
        }

        private static void BuildSnowShader(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.8f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);

            if (texPaths == null) return;

            if (texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                    mat.AlbedoColor = new Color(1.3f, 1.3f, 1.35f);
                }
            }

            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
        }

        private static void BuildZBufferWrite(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Roughness = 0.6f;
            mat.Metallic = 0.0f;

            if ((shader.ShaderFlags & 0x00000001) != 0)
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
            else
                mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;

            ApplyCommonTextures(mat, shader, texPaths, loadTexture);
            ApplyDiffuse(mat, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
        }

        private static void ApplyHeightmap(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null || texPaths.Length <= SlotHeightParallax) return;
            if (string.IsNullOrEmpty(texPaths[SlotHeightParallax])) return;

            var tex = loadTexture(texPaths[SlotHeightParallax]);
            if (tex == null) return;

            mat.HeightmapTexture = tex;
            mat.HeightmapEnabled = true;
            mat.HeightmapScale = shader.ParallaxScale > 0 ? shader.ParallaxScale : 0.05f;
            mat.HeightmapMaxLayers = Mathf.Max(1, (int)shader.ParallaxMaxPasses);
        }

        private static void ApplyDetail(StandardMaterial3D mat,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            if (texPaths == null || texPaths.Length <= SlotSubsurface) return;
            if (string.IsNullOrEmpty(texPaths[SlotSubsurface])) return;

            var tex = loadTexture(texPaths[SlotSubsurface]);
            if (tex == null) return;

            mat.DetailAlbedo = tex;
            mat.DetailEnabled = true;
            mat.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mix;
            mat.Uv1Scale = new Vector3(4f, 4f, 1f);
        }

        private static void ApplyRefraction(StandardMaterial3D mat, ShaderTextureInfo shader)
        {
            if ((shader.ShaderFlags & (1 << 15)) != 0 || (shader.ShaderFlags & (1 << 16)) != 0)
            {
                mat.RefractionEnabled = true;
                mat.RefractionScale = shader.RefractionStrength > 0 ? shader.RefractionStrength : 0.05f;
            }
        }

        private static void BuildFO3Sky(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.DisableAmbientLight = true;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.Roughness = 0.8f;
            mat.Metallic = 0.0f;

            ApplyDiffuse(mat, texPaths, loadTexture);

            if (texPaths != null && texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null)
                {
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }
        }

        private static void BuildFO3Water(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.Roughness = 0.1f;
            mat.Metallic = 0.0f;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
            mat.RefractionEnabled = true;
            mat.RefractionScale = shader.RefractionStrength > 0 ? shader.RefractionStrength : 0.1f;

            ApplyDiffuse(mat, texPaths, loadTexture);
            ApplyNormalMap(mat, texPaths, loadTexture);
            ApplyEnvironmentMask(mat, shader, texPaths, loadTexture);
            ApplyEnvironmentMap(mat, texPaths, loadTexture);
        }

        private static void BuildFO3Unlit(StandardMaterial3D mat, ShaderTextureInfo shader,
            string[] texPaths, Func<string, Texture2D> loadTexture)
        {
            mat.DisableAmbientLight = true;
            mat.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            mat.Roughness = 1.0f;
            mat.Metallic = 0.0f;

            if (texPaths != null && texPaths.Length > SlotDiffuse && !string.IsNullOrEmpty(texPaths[SlotDiffuse]))
            {
                var tex = loadTexture(texPaths[SlotDiffuse]);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                    mat.EmissionTexture = tex;
                    mat.EmissionEnabled = true;
                    mat.Emission = new Color(1f, 1f, 1f);
                }
            }
        }

        /// <summary>
        /// Post-process material based on material classification (Task 6).
        /// Adjusts roughness/metallic/emission/etc. for more realistic surface rendering.
        /// </summary>
        public static void ApplyMaterialClass(StandardMaterial3D mat, MaterialClass materialClass)
        {
            if (materialClass == MaterialClass.Unclassified) return;

            var def = MaterialClassifier.GetDefinition(materialClass);

            // Only override if the texture didn't already set roughness/metallic
            // (we use the classification as a fallback hint)
            if (mat.RoughnessTexture == null)
                mat.Roughness = def.Roughness;
            if (mat.MetallicTexture == null)
                mat.Metallic = def.Metallic;

            mat.SpecularMode = def.SpecularMode;

            if (def.Transparent && mat.Transparency == BaseMaterial3D.TransparencyEnum.Disabled)
            {
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }

            if (def.SubsurfaceScattering)
            {
                mat.SubsurfScatterEnabled = true;
                mat.SubsurfScatterStrength = def.SubsurfaceStrength;
            }

            if (def.EmissionStrength > 0 && mat.EmissionTexture == null)
            {
                mat.EmissionEnabled = true;
                mat.Emission = new Color(def.EmissionStrength, def.EmissionStrength, def.EmissionStrength);
            }
        }
    }
}
