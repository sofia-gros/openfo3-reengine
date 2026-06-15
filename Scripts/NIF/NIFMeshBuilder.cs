using Godot;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    public static class NIFMeshBuilder
    {
        // Geometry block types used in FO3/Oblivion NIFs
        private static readonly HashSet<string> _geomTriStrips = new() { "NiTriStripsData" };
        private static readonly HashSet<string> _geomTriShape = new() { "NiTriShapeData", "BSTriShape", "BSLODTriShape", "BSMeshLODTriShape", "BSSubIndexTriShape", "BSDynamicTriShape" };

        public static ArrayMesh Build(NIFReader nif)
        {
            var mesh = new ArrayMesh();
            int surfacesBuilt = 0;

            foreach (var block in nif.Blocks)
            {
                if (_geomTriStrips.Contains(block.Type))
                {
                    var (verts, inds) = NiTriStripsDataParser.Parse(block.Data);
                    BuildSurface(mesh, verts, inds);
                    surfacesBuilt++;
                }
                else if (_geomTriShape.Contains(block.Type))
                {
                    var (verts, inds) = NiTriShapeDataParser.Parse(block.Data);
                    BuildSurface(mesh, verts, inds);
                    surfacesBuilt++;
                }
                else
                {
                    // GD.Print($"[NIFBuilder] Skipping: {block.Type} size={block.Data.Length}");
                }
            }
            // GD.Print($"[NIFBuilder] Built {surfacesBuilt}/{nif.Blocks.Count} surfaces.");
            return mesh;
        }

        private static void BuildSurface(ArrayMesh mesh, Vector3[] vertices, int[] indices)
        {
            if (vertices == null || vertices.Length == 0) return;

            // // GD.Print($"[NIFBuilder] BuildSurface: V:{vertices.Length} I:{indices?.Length ?? 0}");

            // FO3 Coordinate Conversion (Gamebryo to Godot) and Scale
            float worldScale = 0.015f;
            Vector3[] godotVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i] * worldScale;
                // Gamebryo: X forward, Y left, Z up? No, usually X, Y, Z.
                // Fallout uses: X, Y, Z. Godot uses: X, Y, -Z (right-handed vs left-handed usually, or just Z/Y swap)
                // Common conversion for FO3 to Godot: (X, Z, -Y)
                godotVertices[i] = new Vector3(v.X, v.Z, -v.Y);
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = godotVertices;
            
            if (indices != null && indices.Length >= 3)
            {
                arrays[(int)Mesh.ArrayType.Index] = indices;
            }
            
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
    }
}