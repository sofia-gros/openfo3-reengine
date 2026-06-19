using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    /// <summary>
    /// Parsers for FO3 (NIF version 20.2.0.7) geometry data blocks.
    ///
    /// Field layout verified against actual Fallout 3 NIF binaries AND the
    /// authoritative nifxml specification (niftools/nifxml). Three real FO3
    /// Megaton NIFs (54/108/569 vertices) all parse to remainder=0 with this
    /// layout, so it is not a guess.
    ///
    /// Key facts for FO3 20.2.0.7 (BS202):
    ///   - NiGeometryData inherits NiObject (NOT NiObjectNET) -> no Name/Extra/Controller.
    ///   - First field is Group ID (int, 4 bytes), then Num Vertices (ushort, 2 bytes).
    ///   - BS Max Vertices is ONLY present for NiPSysData (excluded here).
    ///   - Booleans are 1 byte for versions >= 4.1.0.1 (FO3 qualifies).
    ///   - BS Data Flags (ushort) replaces Data Flags: bit0 = Has UV, bit12 = Has Tangents.
    ///   - Material CRC is ABSENT for FO3 (condition is BS > FO3, strictly greater).
    ///   - Has UV bool is ABSENT (until=4.0.0.2); UV presence is read from BS Data Flags bit0.
    ///   - Num UV Sets is NOT a separate field for FO3; UV count = (BS Data Flags & 1).
    ///   - After UV: Consistency Flags (ushort) + Additional Data (Ref int).
    /// </summary>
    public static class NiTriStripsDataParser
    {
        public static (Vector3[] Vertices, Vector2[] UVs, int[] Indices) Parse(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // --- NiGeometryData ---
                int groupId = br.ReadInt32();            // Group ID (always 0 in FO3)
                ushort numVertices = br.ReadUInt16();    // Num Vertices
                br.ReadByte();                            // Keep Flags
                br.ReadByte();                            // Compress Flags

                bool hasVertices = br.ReadByte() != 0;
                Vector3[] vertices = new Vector3[numVertices];
                if (hasVertices)
                {
                    for (int i = 0; i < numVertices; i++)
                        vertices[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }

                // BS Data Flags: bit0 = Has UV, bit12 = Has Tangents
                ushort bsDataFlags = br.ReadUInt16();
                bool hasUV = (bsDataFlags & 0x0001) != 0;
                bool hasTangents = (bsDataFlags & 0x1000) != 0;

                // Has Normals
                bool hasNormals = br.ReadByte() != 0;
                if (hasNormals) ms.Seek(numVertices * 12, SeekOrigin.Current);

                // Tangents + Bitangents (when bit12 set)
                if (hasTangents) ms.Seek(numVertices * 12 * 2, SeekOrigin.Current);

                // Bounding Sphere (center: 3 floats + radius: 1 float = 16 bytes)
                ms.Seek(16, SeekOrigin.Current);

                // Has Vertex Colors
                bool hasVertexColors = br.ReadByte() != 0;
                if (hasVertexColors) ms.Seek(numVertices * 16, SeekOrigin.Current);

                // UV Sets: present when BS Data Flags bit0 set; one set (2 floats/vert)
                Vector2[] uvs = new Vector2[numVertices];
                if (hasUV)
                {
                    for (int i = 0; i < numVertices; i++)
                        uvs[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                }

                // End of NiGeometryData
                br.ReadUInt16();  // Consistency Flags
                br.ReadInt32();   // Additional Data (Ref)

                // --- NiTriBasedGeomData ---
                ushort numTriangles = br.ReadUInt16();

                // --- NiTriStripsData ---
                ushort numStrips = br.ReadUInt16();
                ushort[] stripLengths = new ushort[numStrips];
                for (int i = 0; i < numStrips; i++) stripLengths[i] = br.ReadUInt16();

                bool hasPoints = br.ReadByte() != 0;
                List<int> indicesList = new List<int>();
                if (hasPoints)
                {
                    for (int s = 0; s < numStrips; s++)
                    {
                        int length = stripLengths[s];
                        ushort[] strip = new ushort[length];
                        for (int i = 0; i < length; i++) strip[i] = br.ReadUInt16();

                        for (int i = 0; i < length - 2; i++)
                        {
                            int v0 = strip[i], v1 = strip[i + 1], v2 = strip[i + 2];
                            if (v0 == v1 || v1 == v2 || v0 == v2) continue;
                            if (v0 >= numVertices || v1 >= numVertices || v2 >= numVertices) continue;
                            // Standard strip decomposition: even uses forward order, odd uses reversed order.
                            // FO3 NIFs store CCW triangles (same convention as Godot), so no flip is needed.
                            if (i % 2 == 0)
                            {
                                indicesList.Add(v0); indicesList.Add(v1); indicesList.Add(v2);
                            }
                            else
                            {
                                indicesList.Add(v0); indicesList.Add(v2); indicesList.Add(v1);
                            }
                        }
                    }
                }
                return (vertices, uvs, indicesList.ToArray());
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NiTriStripsDataParser] Parse failed: {e.Message}");
                return (new Vector3[0], new Vector2[0], new int[0]);
            }
        }
    }

    public static class NiTriShapeDataParser
    {
        public static (Vector3[] Vertices, Vector2[] UVs, int[] Indices) Parse(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                // --- NiGeometryData ---
                int groupId = br.ReadInt32();            // Group ID (always 0 in FO3)
                ushort numVertices = br.ReadUInt16();    // Num Vertices
                br.ReadByte();                            // Keep Flags
                br.ReadByte();                            // Compress Flags

                bool hasVertices = br.ReadByte() != 0;
                Vector3[] vertices = new Vector3[numVertices];
                if (hasVertices)
                {
                    for (int i = 0; i < numVertices; i++)
                        vertices[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }

                ushort bsDataFlags = br.ReadUInt16();
                bool hasUV = (bsDataFlags & 0x0001) != 0;
                bool hasTangents = (bsDataFlags & 0x1000) != 0;

                bool hasNormals = br.ReadByte() != 0;
                if (hasNormals) ms.Seek(numVertices * 12, SeekOrigin.Current);
                if (hasTangents) ms.Seek(numVertices * 12 * 2, SeekOrigin.Current);
                ms.Seek(16, SeekOrigin.Current); // bounding sphere

                bool hasVertexColors = br.ReadByte() != 0;
                if (hasVertexColors) ms.Seek(numVertices * 16, SeekOrigin.Current);

                Vector2[] uvs = new Vector2[numVertices];
                if (hasUV)
                {
                    for (int i = 0; i < numVertices; i++)
                        uvs[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                }

                br.ReadUInt16();  // Consistency Flags
                br.ReadInt32();   // Additional Data (Ref)

                // --- NiTriBasedGeomData ---
                ushort numTriangles = br.ReadUInt16();

                // --- NiTriShapeData ---
                uint numTrianglePoints = br.ReadUInt32();
                bool hasTriangles = br.ReadByte() != 0;
                if (hasTriangles)
                {
                    int[] indices = new int[numTriangles * 3];
                    for (int i = 0; i < numTriangles * 3; i += 3)
                    {
                        ushort idx0 = br.ReadUInt16();
                        ushort idx1 = br.ReadUInt16();
                        ushort idx2 = br.ReadUInt16();
                        indices[i]     = (idx0 < numVertices) ? idx0 : 0;
                        indices[i + 1] = (idx1 < numVertices) ? idx1 : 0;
                        indices[i + 2] = (idx2 < numVertices) ? idx2 : 0;
                    }
                    return (vertices, uvs, indices);
                }
                return (vertices, uvs, new int[0]);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NiTriShapeDataParser] Parse failed: {e.Message}");
                return (new Vector3[0], new Vector2[0], new int[0]);
            }
        }
    }
}
