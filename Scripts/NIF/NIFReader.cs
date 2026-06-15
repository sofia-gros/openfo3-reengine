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

        public void Parse(byte[] data)
        {
            try
            {
                // DIAG: Dump first 128 bytes of decompressed NIF
                string hex = "";
                for(int i=0; i<Math.Min(data.Length, 128); i++) hex += $"{data[i]:X2} ";
                // GD.Print($"[NIFReader] DIAG DECOMPRESSED: {hex}");

                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // 1. Header
                string headerStr = ReadHeaderString(br);
                // GD.Print($"[NIFReader] Header: {headerStr.Trim()} Pos: {ms.Position}");

                uint version = br.ReadUInt32();
                // GD.Print($"[NIFReader] Version: {version:X} Pos: {ms.Position}");

                byte endian = br.ReadByte();
                // GD.Print($"[NIFReader] Endian: {endian} Pos: {ms.Position}");

                uint userVersion = br.ReadUInt32();
                // GD.Print($"[NIFReader] UserVersion: {userVersion} Pos: {ms.Position}");

                uint numBlocks = br.ReadUInt32();
                // GD.Print($"[NIFReader] NumBlocks: {numBlocks} Pos: {ms.Position}");

                uint bsHeader = br.ReadUInt32();
                // GD.Print($"[NIFReader] BSHeader: {bsHeader} Pos: {ms.Position}");

                // 2. Creator Strings (Variable number in FO3)
                // Heuristic: Skip up to 3 strings, but stop if we see a sane numBlockTypes
                for (int i = 0; i < 3; i++)
                {
                    long currentPos = ms.Position;
                    byte len = br.ReadByte();
                    if (len > 50) { // Too long for a creator name, might be numBlockTypes or something else
                        ms.Position = currentPos;
                        break;
                    }
                    br.ReadBytes(len);
                }

                // 3. Block Types
                int numBlockTypes = (int)br.ReadUInt16();
                GD.Print($"[NIFReader] NumBlockTypes: {numBlockTypes} Pos: {ms.Position}");

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

                // 6. FO3 String Table (Global)
                if (ms.Position + 8 <= ms.Length)
                {
                    uint numStrings = br.ReadUInt32();
                    uint maxStringLen = br.ReadUInt32();
                    // // GD.Print($"[NIFReader] StringTable: {numStrings} strings, maxLen: {maxStringLen} at Pos: {ms.Position}");
                    
                    for (int i = 0; i < numStrings; i++)
                    {
                        uint len = br.ReadUInt32();
                        br.ReadBytes((int)len); // Skip strings (they are names/paths)
                    }
                }

                // GD.Print($"[NIFReader] Header info done. Starting block extraction at Pos: {ms.Position}");

                // 7. Each block
                for (int i = 0; i < (int)numBlocks; i++)
                {
                    uint size = blockSizes[i];
                    
                    // Safety: Off-by-one padding in some FO3 NIFs
                    if (ms.Position + size > ms.Length)
                    {
                        uint remaining = (uint)(ms.Length - ms.Position);
                        if (size - remaining < 5) { // Small padding discrepancy
                            size = remaining;
                        } else {
                            // GD.PrintErr($"[NIFReader] Block {i} out of bounds: Pos {ms.Position} + Size {size} > Length {ms.Length}");
                            break;
                        }
                    }

                    string type = (blockTypeIndices[i] < blockTypes.Count) ? blockTypes[blockTypeIndices[i]] : "Unknown";
                    byte[] blockData = br.ReadBytes((int)size);

                    // DIAG: Check if block actually matches type
                    string blockHex = "";
                    for(int j=0; j<Math.Min(blockData.Length, 8); j++) blockHex += $"{blockData[j]:X2} ";
                    // // GD.Print($"[NIFReader] Block {i} ({type}) Size: {size} Data: {blockHex}");

                    var block = new NIFBlock { Index = i, Type = type, Data = blockData };
                    Blocks.Add(block);
                }
            }
            catch (Exception e)
            {
                // GD.PrintErr($"[NIFReader] FAILED Parse ({data.Length} bytes): {e.Message}");
            }
        }

        private string ReadHeaderString(BinaryReader br)
        {
            List<byte> bytes = new List<byte>();
            long startPos = br.BaseStream.Position;
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                bytes.Add(b);
                if (b == 0x0A) break; // \n
                if (bytes.Count > 100) break; // Safety
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private string ReadNifString(BinaryReader br)
        {
            uint len = br.ReadUInt32();
            if (len == 0) return "";
            return Encoding.ASCII.GetString(br.ReadBytes((int)len));
        }
    }

    public class NIFBlock
    {
        public int Index;
        public string Type;
        public byte[] Data;
    }
}