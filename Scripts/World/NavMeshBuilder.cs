using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenFo3.ESM;

namespace OpenFo3.World
{
    public class NavMeshTriangle
    {
        public ushort Vertex1, Vertex2, Vertex3;
        public short Edge12, Edge23, Edge31;
        public uint Flags;
    }

    public class NavMeshData
    {
        public uint CellFormId;
        public Vector3[] Vertices;
        public NavMeshTriangle[] Triangles;
        public uint[] DoorRefs;
    }

    public class NavMeshBuilder
    {
        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _navmIndex;

        public NavMeshBuilder(ESMReader esm)
        {
            _esm = esm;
            _navmIndex = esm.BuildFormIdIndex(new[] { "NAVM" });
            GD.Print($"[NavMeshBuilder] NAVM index: {_navmIndex.Count} entries");
        }

        public List<NavMeshData> GetNavMeshesForWorld(uint worldFormId)
        {
            var result = new List<NavMeshData>();

            foreach (var kvp in _navmIndex)
            {
                if (kvp.Value.WorldFormId != worldFormId) continue;
                var nav = ParseNavMesh(kvp.Key);
                if (nav != null) result.Add(nav);
            }

            GD.Print($"[NavMeshBuilder] World 0x{worldFormId:X8}: {result.Count} navmeshes");
            return result;
        }

        public List<NavMeshData> GetNavMeshesForCell(uint cellFormId)
        {
            var result = new List<NavMeshData>();

            foreach (var kvp in _navmIndex)
            {
                if (kvp.Value.CellFormId != cellFormId) continue;
                var nav = ParseNavMesh(kvp.Key);
                if (nav != null) result.Add(nav);
            }

            GD.Print($"[NavMeshBuilder] Cell 0x{cellFormId:X8}: {result.Count} navmeshes");
            return result;
        }

        public NavMeshData ParseNavMesh(uint formId)
        {
            if (!_navmIndex.TryGetValue(formId, out var entry)) return null;

            try
            {
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                ushort nver = 0;
                uint cellFormId = 0;
                uint vertexCount = 0, triangleCount = 0;
                List<Vector3> vertices = null;
                List<NavMeshTriangle> triangles = null;
                List<uint> doorRefs = new();

                foreach (var sub in subs)
                {
                    switch (sub.Type)
                    {
                        case "NVER":
                            if (sub.Data.Length >= 2)
                                nver = BitConverter.ToUInt16(sub.Data, 0);
                            break;

                        case "DATA":
                            if (sub.Data.Length >= 20)
                            {
                                cellFormId = BitConverter.ToUInt32(sub.Data, 0);
                                vertexCount = BitConverter.ToUInt32(sub.Data, 4);
                                triangleCount = BitConverter.ToUInt32(sub.Data, 8);
                            }
                            break;

                        case "NVVX":
                            if (sub.Data.Length >= 12)
                            {
                                int count = (int)(sub.Data.Length / 12);
                                vertices = new List<Vector3>(count);
                                for (int i = 0; i < count; i++)
                                {
                                    int off = i * 12;
                                    float x = BitConverter.ToSingle(sub.Data, off);
                                    float y = BitConverter.ToSingle(sub.Data, off + 4);
                                    float z = BitConverter.ToSingle(sub.Data, off + 8);
                                    // FO3 (X, Y, Z) -> Godot (X, Z, -Y)
                                    vertices.Add(new Vector3(x, z, -y));
                                }
                            }
                            break;

                        case "NVTR":
                            if (sub.Data.Length >= 16)
                            {
                                int count = (int)(sub.Data.Length / 16);
                                triangles = new List<NavMeshTriangle>(count);
                                for (int i = 0; i < count; i++)
                                {
                                    int off = i * 16;
                                    triangles.Add(new NavMeshTriangle
                                    {
                                        Vertex1 = BitConverter.ToUInt16(sub.Data, off),
                                        Vertex2 = BitConverter.ToUInt16(sub.Data, off + 2),
                                        Vertex3 = BitConverter.ToUInt16(sub.Data, off + 4),
                                        Edge12 = BitConverter.ToInt16(sub.Data, off + 6),
                                        Edge23 = BitConverter.ToInt16(sub.Data, off + 8),
                                        Edge31 = BitConverter.ToInt16(sub.Data, off + 10),
                                        Flags = BitConverter.ToUInt32(sub.Data, off + 12),
                                    });
                                }
                            }
                            break;

                        case "NVDP":
                            if (sub.Data.Length >= 8)
                            {
                                int count = (int)(sub.Data.Length / 8);
                                for (int i = 0; i < count; i++)
                                {
                                    int off = i * 8;
                                    doorRefs.Add(BitConverter.ToUInt32(sub.Data, off));
                                }
                            }
                            break;
                    }
                }

                if (vertices == null || triangles == null) return null;

                return new NavMeshData
                {
                    CellFormId = cellFormId,
                    Vertices = vertices.ToArray(),
                    Triangles = triangles.ToArray(),
                    DoorRefs = doorRefs.ToArray(),
                };
            }
            catch (Exception e)
            {
                GD.PrintErr($"[NavMeshBuilder] Error parsing NAVM 0x{formId:X8}: {e.Message}");
                return null;
            }
        }

        public static NavigationMesh BuildNavigationMesh(NavMeshData navData, Vector2 worldCenter, float worldScale)
        {
            if (navData == null || navData.Vertices.Length < 3 || navData.Triangles.Length < 1)
                return null;

            var navMesh = new NavigationMesh();
            navMesh.CellSize = 0.5f;

            int numVerts = navData.Vertices.Length;
            Vector3[] verts = new Vector3[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                var v = navData.Vertices[i];
                verts[i] = new Vector3(v.X * worldScale, v.Y * worldScale, v.Z * worldScale);
            }
            navMesh.Vertices = verts;

            foreach (var tri in navData.Triangles)
            {
                navMesh.AddPolygon(new int[] { tri.Vertex1, tri.Vertex2, tri.Vertex3 });
            }

            GD.Print($"[NavMeshBuilder] Built navmesh: {numVerts} verts, {navData.Triangles.Length} tris");
            return navMesh;
        }
    }
}
