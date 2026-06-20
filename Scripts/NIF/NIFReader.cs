using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace OpenFo3.NIF
{
    public class NIFReader
    {
        public List<NIFBlock> Blocks = new List<NIFBlock>();
        public List<int> RootBlockIndices = new List<int>();
        public List<string> Strings = new List<string>();
        public SkinDataStore SkinData = new SkinDataStore();

        public void Parse(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // 1. Header String (newline terminated)
                string headerStr = ReadHeaderString(br);

                // 2. Fixed header fields
                uint version = br.ReadUInt32();
                byte endian = br.ReadByte();
                uint userVersion = br.ReadUInt32();
                uint numBlocks = br.ReadUInt32();

                // 3. BSHeader: BSVersion (uint32) + 3 byte-length-prefixed strings
                //    (Author, ProcessScript, ExportScript)
                //    FO3 20.2.0.7 bsver=34: always 3 export info strings
                uint bsVersion = br.ReadUInt32();
                for (int i = 0; i < 3; i++)
                {
                    byte len = br.ReadByte();
                    if (len > 0) br.ReadBytes(len);
                }

                // デバッグ: 最初の1件のみ出力
                // GD.Print($"[NIFReader] bsver={bsVersion} numBlocks={numBlocks} posAfterHdr={ms.Position}");

                // 3. Block Types
                int numBlockTypes = (int)br.ReadUInt16();
                var blockTypes = new List<string>();
                for (int i = 0; i < numBlockTypes; i++)
                {
                    uint len = br.ReadUInt32();
                    blockTypes.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)));
                }

                // 4. Block Type Indices
                var blockTypeIndices = new ushort[numBlocks];
                for (int i = 0; i < (int)numBlocks; i++)
                    blockTypeIndices[i] = br.ReadUInt16();

                // 5. Block Sizes
                var blockSizes = new uint[numBlocks];
                for (int i = 0; i < (int)numBlocks; i++)
                    blockSizes[i] = br.ReadUInt32();

                // 6. FO3 String Table (bsver >= 34 で存在)
                if (ms.Position + 8 <= ms.Length)
                {
                    uint numStrings = br.ReadUInt32();
                    uint maxStringLen = br.ReadUInt32();
                    if (numStrings < 100000)
                    {
                        for (int i = 0; i < numStrings; i++)
                        {
                            uint len = br.ReadUInt32();
                            if (len > 512 || ms.Position + len > ms.Length) break;
                            Strings.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)).TrimEnd('\0'));
                        }
                    }
                }

                // 7. Root Blocks - FO3 20.2.0.7: numRoots(uint32) + refs(int32 * numRoots)
                if (ms.Position + 4 <= ms.Length)
                {
                    uint numRoots = br.ReadUInt32();
                    for (int i = 0; i < numRoots && ms.Position + 4 <= ms.Length; i++)
                    {
                        int rootIdx = br.ReadInt32();
                        if (rootIdx != -1) RootBlockIndices.Add(rootIdx);
                    }
                }

                // 8. Blocks
                for (int i = 0; i < (int)numBlocks; i++)
                {
                    uint size = blockSizes[i];
                    string type = (blockTypeIndices[i] < blockTypes.Count) ? blockTypes[blockTypeIndices[i]] : "Unknown";
                    byte[] blockData = br.ReadBytes((int)size);
                    Blocks.Add(new NIFBlock { Index = i, Type = type, Data = blockData });
                }

                // If no groups found, default to block 0
                if (RootBlockIndices.Count == 0 && numBlocks > 0) RootBlockIndices.Add(0);

                // Parse skinning blocks
                ParseSkinningBlocks();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NIFReader] Parse error: {e.Message}");
            }
        }

        private void ParseSkinningBlocks()
        {
            for (int i = 0; i < Blocks.Count; i++)
            {
                var block = Blocks[i];
                try
                {
                    if (block.Type == "NiSkinInstance" || block.Type == "BSDismemberSkinInstance")
                    {
                        var info = NIFSkinningParser.ParseSkinInstance(block.Data);
                        SkinData.SkinInstances[i] = info;
                    }
                    else if (block.Type == "NiSkinData")
                    {
                        var info = NIFSkinningParser.ParseSkinData(block.Data);
                        SkinData.SkinDatas[i] = info;
                    }
                    else if (block.Type == "NiSkinPartition")
                    {
                        var info = NIFSkinningParser.ParseSkinPartition(block.Data);
                        SkinData.SkinPartitions[i] = info;
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[NIFReader] Error parsing skinning block {i} ({block.Type}): {e.Message}");
                }
            }
        }

        private string ReadHeaderString(BinaryReader br)
        {
            List<byte> bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                bytes.Add(b);
                if (b == 0x0A) break; // \n
                if (bytes.Count > 100) break; // Safety
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }

    public class NIFBlock
    {
        public int Index;
        public string Type;
        public byte[] Data;
    }
}