using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    public static class NiTriStripsDataParser
    {
        public static (Vector3[] Vertices, int[] Indices) Parse(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            try
            {
                ushort numVertices = br.ReadUInt16();
                byte hasVerticesByte = br.ReadByte();
                
                if ((hasVerticesByte == 0 || hasVerticesByte == 1) && numVertices > 0 && numVertices < 20000 && numVertices * 12 < data.Length)
                {
                    var result = InternalFullParse(data);
                    if (result.Indices.Length % 3 != 0) {
                        int validCount = (result.Indices.Length / 3) * 3;
                        var trimmed = new int[validCount];
                        Array.Copy(result.Indices, trimmed, validCount);
                        result = (result.Vertices, trimmed);
                    }
                    return result;
                }
                else
                {
                    ms.Position = 0;
                    List<int> indicesList = new List<int>();
                    while (ms.Position + 2 <= ms.Length) indicesList.Add(br.ReadUInt16());
                    
                    int count = (indicesList.Count / 3) * 3;
                    var finalIndices = indicesList.GetRange(0, count).ToArray();

                    if (finalIndices.Length > 0)
                    {
                        int maxIdx = 0;
                        foreach (int idx in finalIndices) if (idx > maxIdx) maxIdx = idx;
                        Vector3[] dummyVerts = new Vector3[maxIdx + 1];
                        return (dummyVerts, finalIndices);
                    }
                    return (new Vector3[0], new int[0]);
                }
            }
            catch { return (new Vector3[0], new int[0]); }
        }

        private static (Vector3[] Vertices, int[] Indices) InternalFullParse(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            
            ushort numVertices = br.ReadUInt16();
            bool hasVertices = br.ReadByte() != 0;
            Vector3[] vertices = new Vector3[numVertices];
            if (hasVertices && ms.Position + (numVertices * 12) <= ms.Length) {
                for (int i = 0; i < (int)numVertices; i++)
                    vertices[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }

            if (ms.Position + 2 <= ms.Length) br.ReadUInt16(); // BS Vector Flags
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 12) <= ms.Length) ms.Seek(numVertices * 12, SeekOrigin.Current);
            }
            if (ms.Position + 16 <= ms.Length) ms.Seek(16, SeekOrigin.Current);
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 16) <= ms.Length) ms.Seek(numVertices * 16, SeekOrigin.Current);
            }
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 8) <= ms.Length) ms.Seek(numVertices * 8, SeekOrigin.Current);
            }

            if (ms.Position + 2 > ms.Length) return (vertices, new int[0]);
            ushort numStrips = br.ReadUInt16();
            if (ms.Position + (numStrips * 2) > ms.Length) return (vertices, new int[0]);
            ushort[] stripLengths = new ushort[numStrips];
            for (int i = 0; i < numStrips; i++) stripLengths[i] = br.ReadUInt16();

            List<int> indicesList = new List<int>();
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0)
            {
                for (int s = 0; s < numStrips; s++)
                {
                    int length = stripLengths[s];
                    if (ms.Position + (length * 2) > ms.Length) break;
                    ushort[] strip = new ushort[length];
                    for (int i = 0; i < length; i++) strip[i] = br.ReadUInt16();

                    for (int i = 0; i < length - 2; i++)
                    {
                        int v0 = strip[i], v1 = strip[i+1], v2 = strip[i+2];
                        if (v0 == v1 || v1 == v2 || v0 == v2) continue;
                        if (v0 >= numVertices || v1 >= numVertices || v2 >= numVertices) continue;
                        if (i % 2 == 0) { indicesList.Add(v0); indicesList.Add(v1); indicesList.Add(v2); }
                        else { indicesList.Add(v0); indicesList.Add(v2); indicesList.Add(v1); }
                    }
                }
            }
            return (vertices, indicesList.ToArray());
        }
    }

    public static class NiTriShapeDataParser
    {
        public static (Vector3[] Vertices, int[] Indices) Parse(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            try
            {
                // Heuristic: Check if block starts with vertices at offset 0 or offset 8 (common in FO3)
                // Offset 0 check (USHORT)
                ushort numV16 = br.ReadUInt16();
                byte hasV16 = br.ReadByte();
                if ((hasV16 == 0 || hasV16 == 1) && numV16 > 0 && numV16 < 20000 && numV16 * 12 < data.Length)
                {
                    var res = InternalFullParse(data, 0, false);
                    return EnsureValidIndices(res);
                }

                // Offset 8 check (UINT32)
                ms.Position = 8;
                if (ms.Position + 5 <= ms.Length)
                {
                    uint numV32 = br.ReadUInt32();
                    byte hasV32 = br.ReadByte();
                    if ((hasV32 == 0 || hasV32 == 1) && numV32 > 0 && numV32 < 20000 && numV32 * 12 < data.Length)
                    {
                        var res = InternalFullParse(data, 8, true);
                        return EnsureValidIndices(res);
                    }
                }

                // Fallback scavenger
                ms.Position = 0;
                List<int> indicesList = new List<int>();
                while (ms.Position + 2 <= ms.Length) indicesList.Add(br.ReadUInt16());
                
                int count = (indicesList.Count / 3) * 3;
                var finalIndices = indicesList.GetRange(0, count).ToArray();

                if (finalIndices.Length > 0)
                {
                    int maxIdx = 0;
                    foreach (int idx in finalIndices) if (idx > maxIdx) maxIdx = idx;
                    Vector3[] dummyVerts = new Vector3[maxIdx + 1];
                    return (dummyVerts, finalIndices);
                }
                return (new Vector3[0], new int[0]);
            }
            catch { return (new Vector3[0], new int[0]); }
        }

        private static (Vector3[] Vertices, int[] Indices) EnsureValidIndices((Vector3[] Vertices, int[] Indices) result)
        {
            if (result.Indices.Length % 3 != 0) {
                int validCount = (result.Indices.Length / 3) * 3;
                var trimmed = new int[validCount];
                Array.Copy(result.Indices, trimmed, validCount);
                return (result.Vertices, trimmed);
            }
            return result;
        }

        private static (Vector3[] Vertices, int[] Indices) InternalFullParse(byte[] data, long startPos, bool use32Bit)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            ms.Position = startPos;
            
            uint numVertices = use32Bit ? br.ReadUInt32() : br.ReadUInt16();
            bool hasVertices = br.ReadByte() != 0;
            Vector3[] vertices = new Vector3[numVertices];
            if (hasVertices && ms.Position + (numVertices * 12) <= ms.Length) {
                for (int i = 0; i < (int)numVertices; i++)
                    vertices[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            }

            if (ms.Position + 2 <= ms.Length) br.ReadUInt16(); // BS Vector Flags
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 12) <= ms.Length) ms.Seek(numVertices * 12, SeekOrigin.Current);
            }
            if (ms.Position + 16 <= ms.Length) ms.Seek(16, SeekOrigin.Current);
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 16) <= ms.Length) ms.Seek(numVertices * 16, SeekOrigin.Current);
            }
            if (ms.Position + 1 <= ms.Length && br.ReadByte() != 0) {
                if (ms.Position + (numVertices * 8) <= ms.Length) ms.Seek(numVertices * 8, SeekOrigin.Current);
            }

            if (ms.Position + 6 > ms.Length) return (vertices, new int[0]);
            uint numTriangles = br.ReadUInt16();
            br.ReadUInt32(); // numTrianglePoints
            
            if (br.ReadByte() != 0) {
                if (ms.Position + (numTriangles * 3 * 2) > ms.Length) {
                    numTriangles = (uint)((ms.Length - ms.Position) / 6);
                }
                var indices = new int[numTriangles * 3];
                for (int i = 0; i < numTriangles * 3; i++) indices[i] = br.ReadUInt16();
                return (vertices, indices);
            }
            return (vertices, new int[0]);
        }
    }
}
