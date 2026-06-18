using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace OpenFo3.NIF
{
    public class NIFBlockResolver
    {
        public class Node
        {
            public string Name;
            public Vector3 Translation;
            public Basis Rotation;
            public float Scale;
            public List<int> Children = new();
            public int DataIndex = -1;
            public List<int> PropertyIndices = new();
            public string TexturePath; // Primary diffuse texture
        }

        public static Node Resolve(NIFBlock block, NIFReader nif)
        {
            try
            {
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);

                if (block.Type == "NiNode" || block.Type == "BSFadeNode" || block.Type == "BSLeafAnimNode" || block.Type == "BSLODNode")
                {
                    return ParseNode(br, block.Index, block.Type);
                }
                else if (block.Type == "NiTriShape" || block.Type == "NiTriStrips")
                {
                    var node = ParseNode(br, block.Index, block.Type, isGeometry: true);
                    if (node != null)
                    {
                        node.TexturePath = ResolveTexturePath(node, nif);
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

        private static string ResolveTexturePath(Node node, NIFReader nif)
        {
            if (node.PropertyIndices.Count == 0) return null;

            foreach (int propIdx in node.PropertyIndices)
            {
                if (propIdx < 0 || propIdx >= nif.Blocks.Count) continue;
                var propBlock = nif.Blocks[propIdx];

                if (propBlock.Type == "BSShaderPPLightingProperty" || propBlock.Type == "BSShaderNoLightingProperty")
                {
                    try
                    {
                        using var ms = new MemoryStream(propBlock.Data);
                        using var br = new BinaryReader(ms);

                        // --- NiObjectNET fields ---
                        br.ReadUInt32(); // Name index
                        uint numExtra = br.ReadUInt32();
                        for (int i = 0; i < numExtra; i++) br.ReadInt32();
                        br.ReadInt32(); // Controller

                        // --- BSShaderProperty fields (Fixed offset from dump) ---
                        // Dump: FF-FF-FF-FF-00-00-00-00-FF-FF-FF-FF-01-00-01-00-00-00-01-01-00-82-01-00-00-00-00-00-80-3F-03-00-00-00-11-00-00-00
                        br.ReadBytes(18); // Header/Flags adjustment
                        
                        uint shaderType = br.ReadUInt32();
                        int textureSetRef = br.ReadInt32();

                        if (textureSetRef != -1 && textureSetRef < nif.Blocks.Count)
                        {
                            var tsBlock = nif.Blocks[textureSetRef];
                            if (tsBlock.Type == "BSShaderTextureSet")
                            {
                                using var msTS = new MemoryStream(tsBlock.Data);
                                using var brTS = new BinaryReader(msTS);
                                uint numTextures = brTS.ReadUInt32();
                                if (numTextures > 0 && numTextures < 20)
                                {
                                    string texPath = ReadNifString(brTS);
                                    if (!string.IsNullOrEmpty(texPath)) return texPath;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[NIFBlockResolver] Failed to parse shader property {propBlock.Type}: {ex.Message}");
                    }
                }
                else if (propBlock.Type == "NiTexturingProperty")
                {
                    try
                    {
                        using var ms = new MemoryStream(propBlock.Data);
                        using var br = new BinaryReader(ms);
                        
                        br.ReadUInt32(); 
                        uint numExtra = br.ReadUInt32();
                        for (int i = 0; i < numExtra; i++) br.ReadInt32();
                        br.ReadInt32(); 

                        br.ReadUInt16(); // Flags
                        br.ReadUInt16(); // ApplyMode
                        ushort texCount = br.ReadUInt16(); 
                        
                        for (int i = 0; i < texCount; i++)
                        {
                            bool enabled = br.ReadByte() != 0;
                            int texRef = br.ReadInt32();
                            
                            if (enabled && texRef != -1 && texRef < nif.Blocks.Count)
                            {
                                var srcBlock = nif.Blocks[texRef];
                                if (srcBlock.Type == "NiSourceTexture")
                                {
                                    using var msST = new MemoryStream(srcBlock.Data);
                                    using var brST = new BinaryReader(msST);
                                    
                                    brST.ReadUInt32(); uint stNumExtra = brST.ReadUInt32();
                                    for(int j=0; j<stNumExtra; j++) brST.ReadInt32();
                                    brST.ReadInt32();
                                    
                                    string path = ReadNifString(brST);
                                    if (!string.IsNullOrEmpty(path)) return path;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { GD.PrintErr($"[NIFBlockResolver] Failed to parse NiTexturingProperty: {ex.Message}"); }
                }
            }
            return null;
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

        private static Node ParseNode(BinaryReader br, int blockIdx, string blockType, bool isGeometry = false)
        {
            var node = new Node();
            long totalLen = br.BaseStream.Length;
            bool dbg = (blockIdx == 0); // block 0 のみデバッグ出力

            if (dbg) GD.Print($"[NIFBlockResolver] ParseNode block={blockIdx} ({blockType}) dataSize={totalLen} isGeom={isGeometry}");

            // 1. NiObjectNET fields
            uint nameIdx = br.ReadUInt32();
            if (dbg) GD.Print($"  nameIdx={nameIdx} pos={br.BaseStream.Position}");

            uint numExtra = br.ReadUInt32();
            if (dbg) GD.Print($"  numExtra={numExtra} pos={br.BaseStream.Position}");
            for (int i = 0; i < numExtra; i++) br.ReadInt32();

            int controllerRef = br.ReadInt32();

            // 2. NiAVObject fields
            // FO3 20.2.0.7: flags は uint16 の後に Unknown Short (2バイト) が続き、
            // 合計4バイト占有する。uint32 として読むのが正しい。
            uint flags = br.ReadUInt32();
            if (dbg) GD.Print($"  flags={flags & 0xFFFF} (uint32={flags:X8}) pos={br.BaseStream.Position}");

            node.Translation = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            if (dbg) GD.Print($"  Translation={node.Translation} pos={br.BaseStream.Position}");

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
            if (dbg) GD.Print($"  Scale={node.Scale} pos={br.BaseStream.Position}");

            uint numProps = br.ReadUInt32();
            if (dbg) GD.Print($"  numProps={numProps} pos={br.BaseStream.Position}");

            for (int i = 0; i < numProps; i++)
            {
                int propIdx = br.ReadInt32();
                if (propIdx != -1) node.PropertyIndices.Add(propIdx);
            }

            int collisionObject = br.ReadInt32();
            if (dbg) GD.Print($"  collisionRef={collisionObject} pos={br.BaseStream.Position}");


            // 3. NiNode or NiGeometry fields
            if (isGeometry)
            {
                node.DataIndex = br.ReadInt32();

                br.ReadInt32(); // Skin Instance Ref

                uint numMaterials = br.ReadUInt32();
                for (int i = 0; i < numMaterials; i++) br.ReadInt32();
                for (int i = 0; i < numMaterials; i++) br.ReadInt32();

                br.ReadUInt32(); // Active Material
                br.ReadByte();   // Material Needs Update
            }
            else
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
    }
}
