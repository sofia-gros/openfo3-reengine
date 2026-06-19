using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenFo3.ESM;
using Environment = Godot.Environment;

namespace OpenFo3.World
{
    public class CellLightingData
    {
        public Color AmbientColor;
        public Color DirectionalColor;
        public Color FogColor;
        public float FogNear;
        public float FogFar;
        public float DirectionalRotationXY;
        public float DirectionalRotationZ;
        public float DirectionalFade;
        public float FogClipDistance;
        public float FogPower;
    }

    public class LightRecordData
    {
        public uint FormId;
        public float Radius;
        public Color Color;
        public uint Flags;
        public float FalloffExponent;
        public float FOV;
    }

    public class LightingLoader
    {
        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _lighIndex;
        private Dictionary<uint, RecordEntry> _lgtmIndex;
        private Dictionary<uint, RecordEntry> _cellIndex;

        public LightingLoader(ESMReader esm)
        {
            _esm = esm;
            _lighIndex = esm.BuildFormIdIndex(new[] { "LIGH" });
            _lgtmIndex = esm.BuildFormIdIndex(new[] { "LGTM" });
            _cellIndex = esm.BuildFormIdIndex(new[] { "CELL" });
        }

        public LightRecordData ParseLight(uint formId)
        {
            if (!_lighIndex.TryGetValue(formId, out var entry)) return null;

            try
            {
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
                if (dataSub == null || dataSub.Data.Length < 40) return null;

                var d = dataSub.Data;
                return new LightRecordData
                {
                    FormId = formId,
                    Radius = BitConverter.ToUInt32(d, 4),
                    Color = new Color(
                        d[9] / 255f,  // R
                        d[10] / 255f, // G
                        d[11] / 255f, // B
                        d[8] / 255f   // A
                    ),
                    Flags = BitConverter.ToUInt32(d, 12),
                    FalloffExponent = BitConverter.ToSingle(d, 16),
                    FOV = BitConverter.ToSingle(d, 20),
                };
            }
            catch (Exception e)
            {
                GD.PrintErr($"[LightingLoader] Error parsing LIGH 0x{formId:X8}: {e.Message}");
                return null;
            }
        }

        public CellLightingData GetCellLighting(uint cellFormId)
        {
            if (!_cellIndex.TryGetValue(cellFormId, out var entry)) return null;

            try
            {
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                // Check for direct XCLL lighting
                var xcll = subs.FirstOrDefault(s => s.Type == "XCLL");
                if (xcll != null && xcll.Data.Length >= 40)
                {
                    return ParseXCLL(xcll.Data);
                }

                // Check for lighting template
                var ltmp = subs.FirstOrDefault(s => s.Type == "LTMP");
                var lnam = subs.FirstOrDefault(s => s.Type == "LNAM");
                if (ltmp != null && ltmp.Data.Length >= 4)
                {
                    uint templateId = BitConverter.ToUInt32(ltmp.Data, 0);
                    uint inheritFlags = (lnam != null && lnam.Data.Length >= 4)
                        ? BitConverter.ToUInt32(lnam.Data, 0) : 0xFFFFFFFF;

                    return GetTemplateLighting(templateId, inheritFlags);
                }

                return null;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[LightingLoader] Error parsing CELL 0x{cellFormId:X8}: {e.Message}");
                return null;
            }
        }

        private static CellLightingData GetDefaultCellLighting()
        {
            return new CellLightingData
            {
                AmbientColor = new Color(0.1f, 0.1f, 0.12f),
                DirectionalColor = new Color(1f, 1f, 1f),
                FogColor = new Color(0.2f, 0.2f, 0.22f),
                FogNear = 0f,
                FogFar = 10000f,
                DirectionalRotationXY = 0f,
                DirectionalRotationZ = 0f,
                DirectionalFade = 0f,
                FogClipDistance = 10000f,
                FogPower = 1f,
            };
        }

        private CellLightingData GetTemplateLighting(uint templateId, uint inheritFlags)
        {
            if (!_lgtmIndex.TryGetValue(templateId, out var entry)) return null;

            try
            {
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);
                var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
                if (dataSub == null || dataSub.Data.Length < 40) return null;

                var template = ParseXCLL(dataSub.Data);
                if (template == null) return null;

                // When inheritFlags == 0xFFFFFFFF, inherit all fields (template used as-is)
                if (inheritFlags == 0xFFFFFFFF) return template;

                // Otherwise, merge: only inherit fields whose flag bit is set;
                // use defaults for fields not inherited.
                var result = GetDefaultCellLighting();

                if ((inheritFlags & 0x00000001) != 0) result.AmbientColor = template.AmbientColor;
                if ((inheritFlags & 0x00000002) != 0) result.DirectionalColor = template.DirectionalColor;
                if ((inheritFlags & 0x00000004) != 0) result.FogColor = template.FogColor;
                if ((inheritFlags & 0x00000008) != 0) result.FogNear = template.FogNear;
                if ((inheritFlags & 0x00000010) != 0) result.FogFar = template.FogFar;
                if ((inheritFlags & 0x00000020) != 0) { result.DirectionalRotationXY = template.DirectionalRotationXY; result.DirectionalRotationZ = template.DirectionalRotationZ; }
                if ((inheritFlags & 0x00000040) != 0) result.DirectionalFade = template.DirectionalFade;
                if ((inheritFlags & 0x00000080) != 0) result.FogClipDistance = template.FogClipDistance;
                if ((inheritFlags & 0x00000100) != 0) result.FogPower = template.FogPower;

                return result;
            }
            catch
            {
                return null;
            }
        }

        private CellLightingData ParseXCLL(byte[] data)
        {
            if (data.Length < 40) return null;

            return new CellLightingData
            {
                AmbientColor = ReadRGBA(data, 0),
                DirectionalColor = ReadRGBA(data, 4),
                FogColor = ReadRGBA(data, 8),
                FogNear = BitConverter.ToSingle(data, 12),
                FogFar = BitConverter.ToSingle(data, 16),
                DirectionalRotationXY = BitConverter.ToInt32(data, 20),
                DirectionalRotationZ = BitConverter.ToInt32(data, 24),
                DirectionalFade = BitConverter.ToSingle(data, 28),
                FogClipDistance = BitConverter.ToSingle(data, 32),
                FogPower = BitConverter.ToSingle(data, 36),
            };
        }

        private Color ReadRGBA(byte[] data, int offset)
        {
            return new Color(
                data[offset + 2] / 255f,
                data[offset + 1] / 255f,
                data[offset + 0] / 255f,
                data[offset + 3] / 255f
            );
        }

        public static void ApplyCellLighting(Node3D root, CellLightingData lighting)
        {
            if (lighting == null) return;

            // Set ambient light
            var ambient = new DirectionalLight3D();
            ambient.Name = "CellAmbientLight";
            ambient.LightColor = lighting.AmbientColor;
            ambient.LightEnergy = 0.3f;
            root.AddChild(ambient);

            // Set directional light
            var dir = new DirectionalLight3D();
            dir.Name = "CellDirectionalLight";
            dir.LightColor = lighting.DirectionalColor;
            dir.LightEnergy = 1.0f;

            // Convert FO3 directional rotation to Godot orientation
            float xyRad = Mathf.DegToRad(lighting.DirectionalRotationXY);
            float zRad = Mathf.DegToRad(lighting.DirectionalRotationZ);
            dir.Rotation = new Vector3(zRad, xyRad, 0);
            root.AddChild(dir);

            // Apply fog via WorldEnvironment
            var existingEnv = root.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
            if (existingEnv == null)
            {
                var env = new WorldEnvironment();
                env.Name = "WorldEnvironment";
                    env.Environment = new Environment();
                root.AddChild(env);
                existingEnv = env;
            }

            var environment = existingEnv.Environment;
            environment.FogEnabled = true;
            environment.FogLightColor = lighting.FogColor;
            environment.FogDensity = lighting.FogPower > 0 ? 1f / lighting.FogFar : 0.001f;
            environment.FogHeightDensity = 0f;
            environment.FogHeight = 1f;

            // Set ambient light in environment
            environment.AmbientLightSource = Environment.AmbientSource.Color;
            environment.AmbientLightColor = lighting.AmbientColor;
        }

        public static Light3D CreateLightNode(LightRecordData lightData, Vector3 position, Vector3 rotation)
        {
            if (lightData == null) return null;

            bool isSpot = (lightData.Flags & 0x00000200) != 0;
            Light3D light;

            if (isSpot)
            {
                var spot = new SpotLight3D();
                spot.SpotAngle = lightData.FOV > 0 ? lightData.FOV : 90f;
                spot.SpotAngle = Mathf.Clamp(spot.SpotAngle, 1f, 180f);
                light = spot;
            }
            else
            {
                light = new OmniLight3D();
            }

            light.Name = $"Light_{lightData.FormId:X8}";
            light.LightColor = lightData.Color;

            // FO3 falloff exponent affects light attenuation.
            float fo3Falloff = lightData.FalloffExponent > 0 ? lightData.FalloffExponent : 1.0f;
            float godotAttenuation = Mathf.Clamp(fo3Falloff * 0.5f, 0.5f, 4.0f);
            float godotEnergy = Mathf.Clamp(1.0f / fo3Falloff, 0.3f, 2.0f);

            if (light is OmniLight3D omni)
            {
                omni.LightEnergy = godotEnergy;
                omni.OmniAttenuation = godotAttenuation;
            }
            else if (light is SpotLight3D spot)
            {
                spot.LightEnergy = godotEnergy;
                spot.SpotAttenuation = godotAttenuation;
            }

            // Convert FO3 radius to Godot units (apply world scale)
            // Minimum range of 0.5 Godot units prevents invisible micro-lights
            float radius = lightData.Radius * 0.015f;
            radius = Mathf.Max(radius, 0.5f);
            if (light is OmniLight3D omni2)
                omni2.OmniRange = radius;
            else if (light is SpotLight3D spot2)
                spot2.SpotRange = radius;

            // Position is already in Godot space (converted in Megaton.cs ProcessRecord).
            // No re-conversion needed. Rotation: same axis mapping as mesh instances.
            var basis = Basis.Identity;
            basis = basis.Rotated(Vector3.Up,     -rotation.Z);
            basis = basis.Rotated(Vector3.Forward, rotation.Y);
            basis = basis.Rotated(Vector3.Right,   rotation.X);
            light.Transform = new Transform3D(basis, position);

            return light;
        }
    }
}
