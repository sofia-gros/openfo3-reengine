using Godot;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    public class NIFGeometryData
    {
        public List<(Vector3[] Vertices, Vector2[] UVs, int[] Indices, Transform3D Transform, string TexturePath, ShaderTextureInfo Shader, AlphaPropertyInfo Alpha)> Surfaces = new();
    }

    public static class NIFMeshBuilder
    {
        /// <summary>
        /// Parse NIF hierarchy and extract geometry data. Thread-safe (no Godot API calls).
        /// </summary>
        public static NIFGeometryData ExtractGeometry(NIFReader nif)
        {
            var geom = new NIFGeometryData();
            if (nif.Blocks.Count == 0) return geom;

            foreach (int rootIdx in nif.RootBlockIndices)
            {
                TraverseExtract(nif, rootIdx, Transform3D.Identity, geom);
            }

            return geom;
        }

        /// <summary>
        /// Build an ArrayMesh from pre-extracted geometry. MUST be called on main thread.
        /// </summary>
        public static ArrayMesh BuildArrayMesh(NIFGeometryData geom)
        {
            var mesh = new ArrayMesh();
            float worldScale = 0.015f;

            // FO3座標系 → Godot座標系 変換行列
            // 変換規則: FO3(x, y, z) → Godot(x, z, -y)
            // Godot の Basis(colX, colY, colZ) は列ベクトル形式:
            //   colX = FO3のX軸がGodot空間で向く方向 = (1, 0, 0)
            //   colY = FO3のY軸がGodot空間で向く方向 = (0, 0, -1)  ← GodotのZが-Y
            //   colZ = FO3のZ軸がGodot空間で向く方向 = (0, 1, 0)   ← GodotのYがZ
            var R_fo3_to_godot = new Basis(
                new Vector3(1,  0,  0),   // FO3 X → Godot (1, 0, 0)
                new Vector3(0,  0, -1),   // FO3 Y → Godot (0, 0,-1)
                new Vector3(0,  1,  0)    // FO3 Z → Godot (0, 1, 0)
            );

            foreach (var surface in geom.Surfaces)
            {
                if (surface.Vertices == null || surface.Indices == null || surface.Indices.Length < 3)
                    continue;

                // NIF内部TransformをGodot空間に変換:
                // T_godot = R_conv * T_fo3 * R_conv^-1
                // ただし頂点変換は R_conv * (T_fo3 * v_fo3) = R_conv * T_fo3 * v_fo3
                // なので v_godot = (R_conv * R_fo3) * v_fo3_local + R_conv * t_fo3
                var fo3Basis   = surface.Transform.Basis;
                var fo3Origin  = surface.Transform.Origin;

                // BasisをGodot空間に変換: B_godot = R_conv * B_fo3
                var godotBasis  = R_fo3_to_godot * fo3Basis;
                // 平行移動もGodot空間に変換
                var godotOrigin = R_fo3_to_godot * fo3Origin;

                Vector3[] godotVertices = new Vector3[surface.Vertices.Length];
                for (int i = 0; i < surface.Vertices.Length; i++)
                {
                    // FO3空間での頂点をGodot空間に変換してスケール適用
                    var v = godotBasis * surface.Vertices[i] + godotOrigin;
                    godotVertices[i] = v * worldScale;
                }

                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);

                for (int vi = 0; vi < godotVertices.Length; vi++)
                {
                    if (surface.UVs != null && vi < surface.UVs.Length)
                        st.SetUV(surface.UVs[vi]);
                    st.AddVertex(godotVertices[vi]);
                }

                foreach (int idx in surface.Indices)
                    st.AddIndex(idx);

                st.GenerateNormals();
                st.GenerateTangents();
                st.Commit(mesh);
                int surfaceIdx = mesh.GetSurfaceCount() - 1;
                if (!string.IsNullOrEmpty(surface.TexturePath))
                {
                    GD.Print($"[NIFMeshBuilder] Setting surface {surfaceIdx} name to: '{surface.TexturePath}'");
                    mesh.SurfaceSetName(surfaceIdx, surface.TexturePath);
                }
                else
                {
                    GD.Print($"[NIFMeshBuilder] Surface {surfaceIdx} has no texture path.");
                }
            }

            return mesh;
        }

        /// <summary>
        /// Convenience: parse and build on main thread (for single-threaded usage).
        /// </summary>
        public static ArrayMesh Build(NIFReader nif)
        {
            var geom = ExtractGeometry(nif);
            return BuildArrayMesh(geom);
        }

        private static void TraverseExtract(NIFReader nif, int blockIdx, Transform3D parentTransform, NIFGeometryData geom)
        {
            if (blockIdx < 0 || blockIdx >= nif.Blocks.Count) return;
            var block = nif.Blocks[blockIdx];

            var node = NIFBlockResolver.Resolve(block, nif);
            if (node == null) return;

            // Local transform
            var localTransform = new Transform3D(node.Rotation, node.Translation);
            localTransform.Basis = localTransform.Basis.Scaled(new Vector3(node.Scale, node.Scale, node.Scale));
            var globalTransform = parentTransform * localTransform;

            if (node.DataIndex != -1)
            {
                if (node.DataIndex >= 0 && node.DataIndex < nif.Blocks.Count)
                {
                    var dataBlock = nif.Blocks[node.DataIndex];
                    Vector3[] verts = null;
                    Vector2[] uvs = null;
                    int[] inds = null;

                    if (dataBlock.Type == "NiTriStripsData")
                    {
                        (verts, uvs, inds) = NiTriStripsDataParser.Parse(dataBlock.Data);
                    }
                    else if (dataBlock.Type == "NiTriShapeData")
                    {
                        (verts, uvs, inds) = NiTriShapeDataParser.Parse(dataBlock.Data);
                    }

                    if (verts != null && inds != null && inds.Length >= 3)
                    {
                        string texPath = node.ShaderInfo?.TexturePaths != null && node.ShaderInfo.TexturePaths.Length > 0 ? node.ShaderInfo.TexturePaths[0] : null;
                        geom.Surfaces.Add((verts, uvs, inds, globalTransform, texPath, node.ShaderInfo, node.AlphaInfo));
                    }
                }
            }

            foreach (int childIdx in node.Children)
            {
                TraverseExtract(nif, childIdx, globalTransform, geom);
            }
        }
    }
}
