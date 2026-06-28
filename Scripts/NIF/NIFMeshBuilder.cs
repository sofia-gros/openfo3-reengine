using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    public class ParticleSystemEntry
    {
        public Transform3D Transform;
        public ShaderTextureInfo ShaderInfo;
        public AlphaPropertyInfo AlphaInfo;
    }

    public class SkinnedSurfaceData
    {
        public Vector3[] Vertices;
        public Vector2[] UVs;
        public int[] Indices;
        public Transform3D Transform;
        public Color[] VertexColors;
        public int GeometryNifIndex;  // Block index of the geometry node in NIF
        public string TexturePath;
        public ShaderTextureInfo ShaderInfo;
        public AlphaPropertyInfo AlphaInfo;
        public int[][] BoneIndices;  // [vertex][4] bone IDs
        public float[][] BoneWeights;  // [vertex][4] weights
    }

    public class SkeletonBoneDef
    {
        public string Name;
        public Transform3D LocalTransform;  // In Godot space
        public int ParentIndex = -1;  // Index into the bone list (-1 = root)
    }

    public class SkeletonData
    {
        public List<SkeletonBoneDef> Bones = new();
    }

    public class NIFGeometryData
    {
        public List<(Vector3[] Vertices, Vector2[] UVs, int[] Indices, Transform3D Transform, string TexturePath, ShaderTextureInfo Shader, AlphaPropertyInfo Alpha, Color[] VertexColors)> Surfaces = new();
        public SkinDataStore SkinData;
        public List<ParticleSystemEntry> ParticleSystems = new();
        public SkeletonData Skeleton;
        public List<SkinnedSurfaceData> SkinnedSurfaces = new();
    }

    public static class NIFMeshBuilder
    {
        private static readonly Basis R_fo3_to_godot = new Basis(
            new Vector3(1, 0, 0),
            new Vector3(0, 0, -1),
            new Vector3(0, 1, 0)
        );

        /// <summary>
        /// Parse NIF hierarchy and extract geometry data. Thread-safe (no Godot API calls).
        /// </summary>
        public static NIFGeometryData ExtractGeometry(NIFReader nif)
        {
            var geom = new NIFGeometryData();
            geom.SkinData = nif.SkinData;
            if (nif.Blocks.Count == 0) return geom;

            foreach (int rootIdx in nif.RootBlockIndices)
            {
                TraverseExtract(nif, rootIdx, Transform3D.Identity, geom);
            }

            // Post-process skinning data
            if (nif.SkinData.SkinInstances.Count > 0)
            {
                PostProcessSkinning(nif, geom);
            }

            return geom;
        }

        private static void PostProcessSkinning(NIFReader nif, NIFGeometryData geom)
        {
            if (geom.SkinnedSurfaces.Count == 0) return;

            // Build unique skeleton from first skinned surface's skin instance
            // FO3 typically has one skeleton per NIF
            var firstSurface = geom.SkinnedSurfaces[0];
            if (!nif.SkinData.SkinInstances.TryGetValue(firstSurface.GeometryNifIndex, out var skinInstance))
                return;

            int skeletonRootIdx = skinInstance.SkeletonRootIndex;
            if (skeletonRootIdx < 0 || skeletonRootIdx >= nif.Blocks.Count)
                return;

            // Build skeleton bone hierarchy by traversing from skeleton root
            var skeleton = new SkeletonData();
            var visited = new HashSet<int>();
            var nifToBoneIndex = new Dictionary<int, int>();

            // Find all NiBone nodes under the skeleton root and build parent-child
            int nextBoneId = BuildBoneHierarchy(nif, skeletonRootIdx, -1, skeleton, visited, nifToBoneIndex, skeletonRootIdx);

            if (skeleton.Bones.Count == 0) return;
            geom.Skeleton = skeleton;

            // Process each skinned surface to convert weights
            foreach (var surface in geom.SkinnedSurfaces)
            {
                if (!nif.SkinData.SkinInstances.TryGetValue(surface.GeometryNifIndex, out var si))
                    continue;
                if (!nif.SkinData.SkinDatas.TryGetValue(si.SkinDataIndex, out var skinData))
                    continue;

                // Convert per-bone vertex weights to per-vertex {bone_idx, weight} arrays
                int numVerts = surface.Vertices.Length;
                var vertBoneMap = new List<(int BoneId, float Weight)>[numVerts];
                for (int i = 0; i < numVerts; i++)
                    vertBoneMap[i] = new List<(int, float)>();

                // Map NiSkinData bone entries to skeleton bone indices
                for (int b = 0; b < skinData.Bones.Length && b < si.BoneIndices.Length; b++)
                {
                    int nifBoneIdx = si.BoneIndices[b];
                    if (!nifToBoneIndex.TryGetValue(nifBoneIdx, out int godotBoneId))
                        continue;

                    var boneEntry = skinData.Bones[b];
                    foreach (var w in boneEntry.VertexWeights)
                    {
                        if (w.VertexIndex < numVerts)
                            vertBoneMap[w.VertexIndex].Add((godotBoneId, w.Weight));
                    }
                }

                // For each vertex, take top 4 weights and normalize
                int[][] boneIds = new int[numVerts][];
                float[][] boneWts = new float[numVerts][];
                for (int v = 0; v < numVerts; v++)
                {
                    var list = vertBoneMap[v];
                    list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
                    int count = Mathf.Min(list.Count, 4);
                    boneIds[v] = new int[4];
                    boneWts[v] = new float[4];
                    float total = 0;
                    for (int k = 0; k < count; k++)
                    {
                        boneIds[v][k] = list[k].BoneId;
                        boneWts[v][k] = list[k].Weight;
                        total += list[k].Weight;
                    }
                    // Normalize
                    if (total > 0)
                    {
                        for (int k = 0; k < count; k++)
                            boneWts[v][k] /= total;
                    }
                    for (int k = count; k < 4; k++)
                    {
                        boneIds[v][k] = 0;
                        boneWts[v][k] = 0;
                    }
                }

                surface.BoneIndices = boneIds;
                surface.BoneWeights = boneWts;
            }
        }

        private static int BuildBoneHierarchy(NIFReader nif, int blockIdx, int parentBoneIndex,
            SkeletonData skeleton, HashSet<int> visited, Dictionary<int, int> nifToBoneIndex, int rootIdx)
        {
            if (blockIdx < 0 || blockIdx >= nif.Blocks.Count) return 0;
            if (visited.Contains(blockIdx)) return 0;
            visited.Add(blockIdx);

            var block = nif.Blocks[blockIdx];
            var node = NIFBlockResolver.Resolve(block, nif);
            if (node == null) return 0;

            // Check if this is a NiBone or contains NiBone children
            bool isBone = block.Type == "NiBone";

            int localBoneIdx = -1;
            if (isBone)
            {
                Transform3D fo3Transform = new Transform3D(node.Rotation, node.Translation);
                fo3Transform.Basis = fo3Transform.Basis.Scaled(new Vector3(node.Scale, node.Scale, node.Scale));

                // Convert FO3 transform to Godot space
                Basis godotBasis = R_fo3_to_godot * fo3Transform.Basis;
                Vector3 godotOrigin = R_fo3_to_godot * fo3Transform.Origin;
                Transform3D godotTransform = new Transform3D(godotBasis, godotOrigin);

                var boneDef = new SkeletonBoneDef
                {
                    Name = GetNodeName(nif, blockIdx),
                    LocalTransform = godotTransform,
                    ParentIndex = parentBoneIndex,
                };
                skeleton.Bones.Add(boneDef);
                localBoneIdx = skeleton.Bones.Count - 1;
                nifToBoneIndex[blockIdx] = localBoneIdx;
            }

            // Recurse into children
            foreach (int childIdx in node.Children)
            {
                BuildBoneHierarchy(nif, childIdx, localBoneIdx, skeleton, visited, nifToBoneIndex, rootIdx);
            }

            return skeleton.Bones.Count;
        }

        private static string GetNodeName(NIFReader nif, int blockIdx)
        {
            try
            {
                var block = nif.Blocks[blockIdx];
                using var ms = new MemoryStream(block.Data);
                using var br = new BinaryReader(ms);
                uint nameIdx = br.ReadUInt32();
                if (nameIdx < nif.Strings.Count)
                    return nif.Strings[(int)nameIdx];
            }
            catch { }
            return $"Bone_{blockIdx}";
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
                    if (surface.VertexColors != null && vi < surface.VertexColors.Length)
                        st.SetColor(surface.VertexColors[vi]);
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
        /// <summary>
        /// Build Skeleton3D and skinned MeshInstance3D nodes from geometry data.
        /// Must be called on main thread.
        /// </summary>
        public static List<Node3D> BuildSkinned(NIFGeometryData geom, Func<string, Texture2D> loadTexture)
        {
            var result = new List<Node3D>();

            if (geom.Skeleton == null || geom.SkinnedSurfaces.Count == 0)
                return result;

            var skeleton = new Skeleton3D();
            skeleton.Name = "Skeleton";

            // Add bones to skeleton
            for (int i = 0; i < geom.Skeleton.Bones.Count; i++)
            {
                var boneDef = geom.Skeleton.Bones[i];
                int boneIdx = skeleton.AddBone(boneDef.Name);
                skeleton.SetBoneRest(boneIdx, boneDef.LocalTransform);
            }

            // Set parent-child relationships
            for (int i = 0; i < geom.Skeleton.Bones.Count; i++)
            {
                int parent = geom.Skeleton.Bones[i].ParentIndex;
                if (parent >= 0 && parent < i)
                {
                    skeleton.SetBoneParent(i, parent);
                }
            }

            result.Add(skeleton);

            // Build skinned meshes
            foreach (var surface in geom.SkinnedSurfaces)
            {
                if (surface.Vertices == null || surface.Indices == null || surface.Indices.Length < 3)
                    continue;

                // Create temp SurfaceTool to generate normals and tangents automatically
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);

                // Convert FO3 vertex to Godot space
                var fo3Basis  = surface.Transform.Basis;
                var fo3Origin = surface.Transform.Origin;
                var godotBasis  = R_fo3_to_godot * fo3Basis;
                var godotOrigin = R_fo3_to_godot * fo3Origin;

                for (int vi = 0; vi < surface.Vertices.Length; vi++)
                {
                    var v = godotBasis * surface.Vertices[vi] + godotOrigin;
                    Vector3 godotVert = v * 0.015f; // WorldScale

                    if (surface.UVs != null && vi < surface.UVs.Length)
                        st.SetUV(surface.UVs[vi]);
                    if (surface.VertexColors != null && vi < surface.VertexColors.Length)
                        st.SetColor(surface.VertexColors[vi]);
                    st.AddVertex(godotVert);
                }

                foreach (int idx in surface.Indices)
                    st.AddIndex(idx);

                st.GenerateNormals();
                st.GenerateTangents();

                var tempMesh = st.Commit();
                var arrays = tempMesh.SurfaceGetArrays(0);

                int numVerts = surface.Vertices.Length;
                var bonesArray = new int[numVerts * 4];
                var weightsArray = new float[numVerts * 4];

                for (int vi = 0; vi < numVerts; vi++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        if (surface.BoneIndices != null && vi < surface.BoneIndices.Length && surface.BoneIndices[vi] != null && k < surface.BoneIndices[vi].Length)
                            bonesArray[vi * 4 + k] = surface.BoneIndices[vi][k];
                        else
                            bonesArray[vi * 4 + k] = 0;

                        if (surface.BoneWeights != null && vi < surface.BoneWeights.Length && surface.BoneWeights[vi] != null && k < surface.BoneWeights[vi].Length)
                            weightsArray[vi * 4 + k] = surface.BoneWeights[vi][k];
                        else
                            weightsArray[vi * 4 + k] = 0.0f;
                    }
                }

                arrays[(int)Mesh.ArrayType.Bones] = bonesArray;
                arrays[(int)Mesh.ArrayType.Weights] = weightsArray;

                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

                var mi = new MeshInstance3D { Mesh = mesh };
                mi.Name = "SkinnedMesh";

                // Setup Skin and Skeleton settings for GPU Skining
                var skin = new Skin();
                int boneCount = skeleton.GetBoneCount();
                skin.SetBindCount(boneCount);
                for (int i = 0; i < boneCount; i++)
                {
                    skin.SetBindPose(i, skeleton.GetBoneRest(i).AffineInverse());
                }
                mi.Skin = skin;
                mi.Skeleton = ".."; // Points to parent Skeleton3D node

                // Apply material
                if (surface.ShaderInfo != null)
                {
                    var mat = NIFMaterialBuilder.BuildMaterial(surface.ShaderInfo, surface.AlphaInfo, loadTexture);
                    if (mat.AlbedoTexture == null && !string.IsNullOrEmpty(surface.TexturePath))
                    {
                        var tex = loadTexture(surface.TexturePath);
                        if (tex != null) mat.AlbedoTexture = tex;
                    }
                    mesh.SurfaceSetMaterial(0, mat);
                }
                else if (!string.IsNullOrEmpty(surface.TexturePath))
                {
                    var tex = loadTexture(surface.TexturePath);
                    if (tex != null)
                    {
                        var mat = new StandardMaterial3D { AlbedoTexture = tex };
                        mesh.SurfaceSetMaterial(0, mat);
                    }
                }

                skeleton.AddChild(mi);
            }

            return result;
        }

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

                if (node.IsParticleSystem)
                {
                    geom.ParticleSystems.Add(new ParticleSystemEntry
                    {
                        Transform = globalTransform,
                        ShaderInfo = node.ShaderInfo,
                        AlphaInfo = node.AlphaInfo,
                    });
                }
                else if (node.DataIndex != -1)
                {
                    if (node.DataIndex >= 0 && node.DataIndex < nif.Blocks.Count)
                    {
                        var dataBlock = nif.Blocks[node.DataIndex];
                        Vector3[] verts = null;
                        Vector2[] uvs = null;
                        int[] inds = null;
                        Color[] vcols = null;

                        if (dataBlock.Type == "NiTriStripsData")
                        {
                            (verts, uvs, inds, vcols) = NiTriStripsDataParser.Parse(dataBlock.Data);
                        }
                        else if (dataBlock.Type == "NiTriShapeData")
                        {
                            (verts, uvs, inds, vcols) = NiTriShapeDataParser.Parse(dataBlock.Data);
                        }

                        if (verts != null && inds != null && inds.Length >= 3)
                        {
                            string texPath = node.ShaderInfo?.TexturePaths != null && node.ShaderInfo.TexturePaths.Length > 0 ? node.ShaderInfo.TexturePaths[0] : null;

                            if (node.SkinInstanceIndex != -1 &&
                                nif.SkinData.SkinInstances.ContainsKey(blockIdx))
                            {
                                // Skinned geometry: store separately for post-processing
                                geom.SkinnedSurfaces.Add(new SkinnedSurfaceData
                                {
                                    Vertices = verts,
                                    UVs = uvs,
                                    Indices = inds,
                                    VertexColors = vcols,
                                    Transform = globalTransform,
                                    TexturePath = texPath,
                                    ShaderInfo = node.ShaderInfo,
                                    AlphaInfo = node.AlphaInfo,
                                    GeometryNifIndex = blockIdx,
                                });
                            }
                            else
                            {
                                geom.Surfaces.Add((verts, uvs, inds, globalTransform, texPath, node.ShaderInfo, node.AlphaInfo, vcols));
                            }
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
