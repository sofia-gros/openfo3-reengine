using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace OpenFo3.NIF
{
    public class ShaderTextureInfo
    {
        public int ShaderType;
        public uint ShaderFlags;
        public uint ShaderFlags2;
        public float EnvironmentMapScale;
        public string[] TexturePaths;
        public float RefractionStrength;
        public float ParallaxScale;
        public float ParallaxMaxPasses;
        public uint BsVersion;
    }

    public class AlphaPropertyInfo
    {
        public ushort Flags;
        public byte Threshold;
    }

    public class NIFBlockResolver
    {
        public class ParticleInfo
        {
            public bool WorldSpace;
            public List<int> ModifierRefs = new();
        }

        public class Node
        {
            public string Name;
            public Vector3 Translation;
            public Basis Rotation;
            public float Scale;
            public List<int> Children = new();
            public int DataIndex = -1;
            public int SkinInstanceIndex = -1;
            public List<int> PropertyIndices = new();
            public ShaderTextureInfo ShaderInfo;
            public AlphaPropertyInfo AlphaInfo;
            public bool IsParticleSystem;
            public ParticleInfo ParticleData;
        }

        public static Node Resolve(NIFBlock block, NIFReader nif)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                if (block.Type == "NiNode" || block.Type == "BSFadeNode" || block.Type == "BSLeafAnimNode" || block.Type == "BSLODNode" || block.Type == "NiBillboardNode" || block.Type == "NiSortAdjustNode" || block.Type == "NiSwitchNode" || block.Type == "BSValueNode" || block.Type == "BSOrderedNode" || block.Type == "BSRangeNode" || block.Type == "BSMultiBoundNode" || block.Type == "BSTreeNode" || block.Type == "NiBone" || block.Type == "RoomMarker" || block.Type == "NiRoomGroup" || block.Type == "BSMasterParticleSystem" || block.Type == "BSRefractionFireGlow")
                {
                    return ParseNode(br, block.Index, block.Type);
                }
                else if (block.Type == "NiAmbientLight" || block.Type == "NiDirectionalLight" || block.Type == "NiSpotLight" || block.Type == "NiPointLight")
                {
                    return ParseNode(br, block.Index, block.Type, readNodeChildren: false);
                }
                else if (block.Type == "BSStripParticleSystem")
                {
                    var node = ParseNode(br, block.Index, block.Type, isParticleSystem: true);
                    if (node != null)
                    {
                        ResolveShaderProperty(node, nif);
                        ResolveAlphaProperty(node, nif);
                    }
                    return node;
                }
                else if (block.Type == "NiTriShape" || block.Type == "NiTriStrips")
                {
                    var node = ParseNode(br, block.Index, block.Type, isGeometry: true);
                    if (node != null)
                    {
                        ResolveShaderProperty(node, nif);
                        ResolveAlphaProperty(node, nif);
                    }
                    return node;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFBlockResolver] Error resolving block {block.Index} ({block.Type}): {e.Message}");
            }

            return null;
        }

        private static void ResolveShaderProperty(Node node, NIFReader nif)
        {
            if (node.PropertyIndices.Count == 0) return;

            foreach (int propIdx in node.PropertyIndices)
            {
                if (propIdx < 0 || propIdx >= nif.Blocks.Count) continue;
                var propBlock = nif.Blocks[propIdx];

                if (propBlock.Type == "BSShaderPPLightingProperty")
                {
                    ResolveBSShaderPPLightingProperty(propBlock.Data, node, nif);
                }
                else if (propBlock.Type == "BSShaderNoLightingProperty")
                {
                    ResolveBSShaderNoLightingProperty(propBlock.Data, node, nif);
                }
                else if (propBlock.Type == "NiTexturingProperty")
                {
                    ResolveNiTexturingProperty(propBlock.Data, node, nif);
                }
                else if (propBlock.Type == "TileShaderProperty" || propBlock.Type == "TallGrassShaderProperty")
                {
                    ResolveSimpleShaderProperty(propBlock.Data, node);
                }

                if (node.ShaderInfo != null) break;
            }
        }

        private static void ResolveAlphaProperty(Node node, NIFReader nif)
        {
            foreach (int propIdx in node.PropertyIndices)
            {
                if (propIdx < 0 || propIdx >= nif.Blocks.Count) continue;
                var propBlock = nif.Blocks[propIdx];
                if (propBlock.Type == "NiAlphaProperty" && propBlock.Data.Length >= 2)
                {
                    node.AlphaInfo = new AlphaPropertyInfo
                    {
                        Flags = BitConverter.ToUInt16(propBlock.Data, 0),
                        Threshold = propBlock.Data.Length >= 3 ? propBlock.Data[2] : (byte)128
                    };
                    break;
                }
            }
        }

        private static void ResolveBSShaderPPLightingProperty(byte[] data, Node node, NIFReader nif)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // NiObjectNET
                uint nameIdx = br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                int controller = br.ReadInt32();

                // NiShadeProperty
                ushort shadeFlags = br.ReadUInt16();

                // BSShaderProperty
                uint shaderType = br.ReadUInt32();
                uint shaderFlags = br.ReadUInt32();
                uint shaderFlags2 = br.ReadUInt32();
                float envMapScale = br.ReadSingle();

                // BSShaderLightingProperty
                uint texClampMode = br.ReadUInt32();

                // BSShaderPPLightingProperty
                int textureSetRef = br.ReadInt32();
                float refractionStrength = br.ReadSingle();
                int refractionFirePeriod = br.ReadInt32();
                float parallaxMaxPasses = br.ReadSingle();
                float parallaxScale = br.ReadSingle();

                var info = new ShaderTextureInfo
                {
                    BsVersion = nif.BsVersion,
                    ShaderType = TranslateFO3ShaderType((int)shaderType, nif.BsVersion),
                    ShaderFlags = shaderFlags,
                    ShaderFlags2 = shaderFlags2,
                    EnvironmentMapScale = envMapScale,
                    RefractionStrength = refractionStrength,
                    ParallaxScale = parallaxScale,
                    ParallaxMaxPasses = parallaxMaxPasses,
                };

                if (textureSetRef != -1 && textureSetRef < nif.Blocks.Count)
                {
                    var tsBlock = nif.Blocks[textureSetRef];
                    if (tsBlock.Type == "BSShaderTextureSet")
                    {
                        info.TexturePaths = ReadTextureSet(tsBlock.Data);
                    }
                }

                node.ShaderInfo = info;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NIFBlockResolver] Failed to parse BSShaderPPLightingProperty: {ex.Message}");
            }
        }

        private static void ResolveBSShaderNoLightingProperty(byte[] data, Node node, NIFReader nif)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // NiObjectNET
                br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32();

                // NiShadeProperty
                br.ReadUInt16();

                // BSShaderProperty
                uint shaderType = br.ReadUInt32();
                uint shaderFlags = br.ReadUInt32();
                uint shaderFlags2 = br.ReadUInt32();
                br.ReadSingle(); // envMapScale

                // BSShaderLightingProperty
                br.ReadUInt32(); // texClampMode

                // BSShaderNoLightingProperty
                string fileName = ReadNifString(br);

                var info = new ShaderTextureInfo
                {
                    BsVersion = nif.BsVersion,
                    ShaderType = TranslateFO3ShaderType((int)shaderType, nif.BsVersion),
                    ShaderFlags = shaderFlags,
                    ShaderFlags2 = shaderFlags2,
                };

                if (!string.IsNullOrEmpty(fileName))
                    info.TexturePaths = new[] { fileName };

                node.ShaderInfo = info;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NIFBlockResolver] Failed to parse BSShaderNoLightingProperty: {ex.Message}");
            }
        }

        private static void ResolveNiTexturingProperty(byte[] data, Node node, NIFReader nif)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32();

                ushort flags = br.ReadUInt16();
                ushort applyMode = br.ReadUInt16();
                ushort texCount = br.ReadUInt16();

                List<string> paths = new();
                for (int i = 0; i < texCount; i++)
                {
                    bool enabled = br.ReadByte() != 0;
                    int texRef = br.ReadInt32();

                    if (enabled && texRef != -1 && texRef < nif.Blocks.Count)
                    {
                        var srcBlock = nif.Blocks[texRef];
                        if (srcBlock.Type == "NiSourceTexture")
                        {
                            string path = ReadNiSourceTexturePath(srcBlock.Data);
                            if (!string.IsNullOrEmpty(path))
                                paths.Add(path);
                        }
                    }
                }

                if (paths.Count > 0)
                {
                    node.ShaderInfo = new ShaderTextureInfo
                    {
                        ShaderType = 1,
                        TexturePaths = paths.ToArray(),
                    };
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NIFBlockResolver] Failed to parse NiTexturingProperty: {ex.Message}");
            }
        }

        private static void ResolveSimpleShaderProperty(byte[] data, Node node)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32();
                br.ReadUInt16();
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadSingle();
                br.ReadUInt32();

                string fileName = ReadNifString(br);
                if (!string.IsNullOrEmpty(fileName))
                {
                    node.ShaderInfo = new ShaderTextureInfo
                    {
                        ShaderType = 1,
                        TexturePaths = new[] { fileName },
                    };
                }
            }
            catch { }
        }

        private static string[] ReadTextureSet(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                uint numTextures = br.ReadUInt32();
                if (numTextures == 0 || numTextures > 20)
                {
                    // FO3 may store numTextures = 0 but still have 6 paths right after
                    numTextures = 6;
                }

                List<string> paths = new();
                for (int i = 0; i < numTextures; i++)
                {
                    string path = ReadNifString(br);
                    paths.Add(path ?? "");
                }
                return paths.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadNiSourceTexturePath(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32();
                return ReadNifString(br);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadNifString(BinaryReader br)
        {
            if (br.BaseStream.Position + 4 > br.BaseStream.Length) return null;
            uint len = br.ReadUInt32();
            if (len == 0 || len > 512) return null;
            if (br.BaseStream.Position + len > br.BaseStream.Length) return null;
            byte[] bytes = br.ReadBytes((int)len);
            return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        private static Node ParseNode(BinaryReader br, int blockIdx, string blockType, bool isGeometry = false, bool readNodeChildren = true, bool isParticleSystem = false)
        {
            var node = new Node();
            long totalLen = br.BaseStream.Length;

            // 1. NiObjectNET fields
            uint nameIdx = br.ReadUInt32();
            uint numExtra = br.ReadUInt32();
            for (int i = 0; i < numExtra; i++) br.ReadInt32();
            int controllerRef = br.ReadInt32();

            // 2. NiAVObject fields
            uint flags = br.ReadUInt32();

            node.Translation = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            // Rotation Matrix (3x3, row-major)
            float m11 = br.ReadSingle(), m12 = br.ReadSingle(), m13 = br.ReadSingle();
            float m21 = br.ReadSingle(), m22 = br.ReadSingle(), m23 = br.ReadSingle();
            float m31 = br.ReadSingle(), m32 = br.ReadSingle(), m33 = br.ReadSingle();
            node.Rotation = new Basis(
                new Vector3(m11, m21, m31),
                new Vector3(m12, m22, m32),
                new Vector3(m13, m23, m33)
            );

            node.Scale = br.ReadSingle();

            uint numProps = br.ReadUInt32();
            for (int i = 0; i < numProps; i++)
            {
                int propIdx = br.ReadInt32();
                if (propIdx != -1) node.PropertyIndices.Add(propIdx);
            }

            int collisionObject = br.ReadInt32();

            // 3. NiNode or NiGeometry fields
            if (isParticleSystem)
            {
                // NiParticleSystem inherits from NiGeometry in FO3.
                // After NiAVObject: Data + SkinInstance + MaterialData,
                // then NiParticleSystem-specific fields.
                long pos = br.BaseStream.Position;
                long remaining = br.BaseStream.Length - pos;

                // FO3 (NI_BS_LTE_FO3): NiGeometry has Data + SkinInstance + MaterialData fields
                // SSE+ (#BS_GTE_SSE#): NiParticleSystem has different layout
                if (remaining >= 14)
                {
                    // Skip Data Ref (int32)
                    br.ReadInt32();
                    // Skip SkinInstance Ref (int32)
                    br.ReadInt32();
                    // MaterialData: NumMaterials(uint32) + MaterialName refs + ExtraData int32s + ActiveMaterial(int32) + NeedsUpdate(byte)
                    uint numMaterials = br.ReadUInt32();
                    for (int i = 0; i < numMaterials; i++) br.ReadInt32();
                    for (int i = 0; i < numMaterials; i++) br.ReadInt32();
                    br.ReadInt32(); // Active Material
                    br.ReadByte();  // Material Needs Update
                }

                node.IsParticleSystem = true;
                var pData = new ParticleInfo();

                if (br.BaseStream.Position + 5 <= br.BaseStream.Length)
                {
                    pData.WorldSpace = br.ReadByte() != 0;
                    uint numModifiers = br.ReadUInt32();
                    if (numModifiers < 100)
                    {
                        for (int i = 0; i < numModifiers && br.BaseStream.Position + 4 <= br.BaseStream.Length; i++)
                        {
                            int modRef = br.ReadInt32();
                            if (modRef != -1) pData.ModifierRefs.Add(modRef);
                        }
                    }
                }

                node.ParticleData = pData;
            }
            else if (isGeometry)
            {
                node.DataIndex = br.ReadInt32();

                node.SkinInstanceIndex = br.ReadInt32(); // Skin Instance Ref

                uint numMaterials = br.ReadUInt32();
                for (int i = 0; i < numMaterials; i++) br.ReadInt32();
                for (int i = 0; i < numMaterials; i++) br.ReadInt32();

                br.ReadUInt32(); // Active Material
                br.ReadByte();   // Material Needs Update
            }
            else if (readNodeChildren)
            {
                uint numChildren = br.ReadUInt32();
                for (int i = 0; i < numChildren; i++)
                {
                    int childIdx = br.ReadInt32();
                    if (childIdx != -1) node.Children.Add(childIdx);
                }

                uint numEffects = br.ReadUInt32();
                for (int i = 0; i < numEffects; i++) br.ReadInt32();
            }

            return node;
        }

        public static int TranslateFO3ShaderType(int rawType, uint bsVersion)
        {
            // FO3 uses BSShaderType enum (0,1,10,14,15,17,29,32,33)
            // Internal handlers use Skyrim BSLightingShaderType-style IDs or extended types.
            // Translate FO3 types to canonical internal values so NIFMaterialBuilder
            // produces correct material properties regardless of NIF version.
            if (bsVersion <= 34)
            {
                switch (rawType)
                {
                    case 0:  return 34; // FO3 Tall Grass → TallGrass (34)
                    case 1:  return 0;  // FO3 Default → Default (0)
                    case 10: return 40; // FO3 Sky → internal Sky type
                    case 14: return 31; // FO3 Skin → SkinTint (31)
                    case 15: return 15; // FO3 Unknown/scolbld → keep (MultiLayerParallaxOcc)
                    case 17: return 41; // FO3 Water → internal Water type
                    case 29: return 0;  // FO3 Lighting30 → Default (0)
                    case 32: return 0;  // FO3 Tiled → Default (0)
                    case 33: return 42; // FO3 No Lighting → internal Unlit type
                }
            }
            // Skyrim+ (bsver >= 35): values already match BSLightingShaderType, pass through
            return rawType;
        }
    }
}
