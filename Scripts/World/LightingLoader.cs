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

                // Apply inheritance: only fields where inherit flag bit is set
                if (inheritFlags != 0xFFFFFFFF && template != null)
                {
                    // (In a full implementation, we'd merge with defaults)
                }

                return template;
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
            light.LightEnergy = 1.0f;

            // Convert FO3 radius to Godot units (apply world scale)
            float radius = lightData.Radius * 0.015f;
            if (light is OmniLight3D omni)
                omni.OmniRange = Mathf.Max(radius, 1f);
            else if (light is SpotLight3D spot)
                spot.SpotRange = Mathf.Max(radius, 1f);

            // Position: FO3 -> Godot coordinate conversion
            float px = position.X;
            float py = position.Z;
            float pz = -position.Y;
            light.Position = new Vector3(px, py, pz);

            return light;
        }
    }
}
