using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace OpenFo3.NIF
{
    public struct NiTransformData
    {
        public Basis Rotation;
        public Vector3 Translation;
        public float Scale;
    }

    public struct BoneVertWeight
    {
        public ushort VertexIndex;
        public float Weight;
    }

    public struct BoneDataEntry
    {
        public NiTransformData SkinTransform;
        public Vector3 BoundingSphereCenter;
        public float BoundingSphereRadius;
        public BoneVertWeight[] VertexWeights;
    }

    public struct SkinDataInfo
    {
        public NiTransformData SkinTransform;
        public BoneDataEntry[] Bones;
    }

    public struct SkinInstanceInfo
    {
        public int SkinDataIndex;
        public int SkinPartitionIndex;
        public int SkeletonRootIndex;
        public int[] BoneIndices;
    }

    public static class NIFSkinningParser
    {
        public static SkinInstanceInfo ParseSkinInstance(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            int dataRef = br.ReadInt32();
            int skinPartitionRef = br.ReadInt32();
            int skeletonRootPtr = br.ReadInt32();
            uint numBones = br.ReadUInt32();

            int[] bonePtrs = new int[numBones];
            for (int i = 0; i < numBones; i++)
                bonePtrs[i] = br.ReadInt32();

            return new SkinInstanceInfo
            {
                SkinDataIndex = dataRef,
                SkinPartitionIndex = skinPartitionRef,
                SkeletonRootIndex = skeletonRootPtr,
                BoneIndices = bonePtrs,
            };
        }

        public static SkinDataInfo ParseSkinData(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            var skinTransform = ReadNiTransform(br);

            uint numBones = br.ReadUInt32();

            // Skin Partition Ref: until="10.1.0.0", NOT present for FO3 (20.2.0.7)
            // Has Vertex Weights: since="4.2.1.0", present for FO3
            bool hasVertexWeights = br.ReadByte() != 0;

            var bones = new BoneDataEntry[numBones];
            for (int i = 0; i < numBones; i++)
            {
                var bt = ReadNiTransform(br);
                float sphereX = br.ReadSingle();
                float sphereY = br.ReadSingle();
                float sphereZ = br.ReadSingle();
                float sphereRadius = br.ReadSingle();

                ushort numVerts = hasVertexWeights ? br.ReadUInt16() : (ushort)0;
                var weights = new BoneVertWeight[numVerts];
                for (int v = 0; v < numVerts; v++)
                {
                    weights[v] = new BoneVertWeight
                    {
                        VertexIndex = br.ReadUInt16(),
                        Weight = br.ReadSingle(),
                    };
                }

                bones[i] = new BoneDataEntry
                {
                    SkinTransform = bt,
                    BoundingSphereCenter = new Vector3(sphereX, sphereY, sphereZ),
                    BoundingSphereRadius = sphereRadius,
                    VertexWeights = weights,
                };
            }

            return new SkinDataInfo
            {
                SkinTransform = skinTransform,
                Bones = bones,
            };
        }

        private static NiTransformData ReadNiTransform(BinaryReader br)
        {
            float m00 = br.ReadSingle(); float m01 = br.ReadSingle(); float m02 = br.ReadSingle();
            float m10 = br.ReadSingle(); float m11 = br.ReadSingle(); float m12 = br.ReadSingle();
            float m20 = br.ReadSingle(); float m21 = br.ReadSingle(); float m22 = br.ReadSingle();

            float tx = br.ReadSingle();
            float ty = br.ReadSingle();
            float tz = br.ReadSingle();

            float scale = br.ReadSingle();

            return new NiTransformData
            {
                Rotation = new Basis(
                    new Vector3(m00, m01, m02),
                    new Vector3(m10, m11, m12),
                    new Vector3(m20, m21, m22)
                ),
                Translation = new Vector3(tx, ty, tz),
                Scale = scale,
            };
        }

        public static SkinPartitionInfo ParseSkinPartition(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            uint numPartitions = br.ReadUInt32();
            // FO3 (20.2.0.7, non-SSE): no Data Size / Vertex Size / Vertex Desc / Vertex Data

            var entries = new SkinPartitionEntry[numPartitions];
            for (int p = 0; p < numPartitions; p++)
            {
                var entry = new SkinPartitionEntry();
                entry.NumVertices = br.ReadUInt16();
                entry.NumTriangles = br.ReadUInt16();
                entry.NumBones = br.ReadUInt16();
                entry.NumStrips = br.ReadUInt16();
                entry.NumWeightsPerVertex = br.ReadUInt16();

                entry.Bones = new ushort[entry.NumBones];
                for (int b = 0; b < entry.NumBones; b++)
                    entry.Bones[b] = br.ReadUInt16();

                // HasVertexMap: since="10.1.0.0" — present for FO3
                entry.HasVertexMap = br.ReadByte() != 0;
                entry.VertexMap = new ushort[entry.NumVertices];
                for (int v = 0; v < entry.NumVertices; v++)
                    entry.VertexMap[v] = br.ReadUInt16();

                // HasVertexWeights: since="10.1.0.0" — present for FO3
                entry.HasVertexWeights = br.ReadByte() != 0;
                if (entry.HasVertexWeights)
                {
                    entry.VertexWeights = new float[entry.NumVertices][];
                    for (int v = 0; v < entry.NumVertices; v++)
                    {
                        entry.VertexWeights[v] = new float[entry.NumWeightsPerVertex];
                        for (int w = 0; w < entry.NumWeightsPerVertex; w++)
                            entry.VertexWeights[v][w] = br.ReadSingle();
                    }
                }

                entry.StripLengths = new ushort[entry.NumStrips];
                for (int s = 0; s < entry.NumStrips; s++)
                    entry.StripLengths[s] = br.ReadUInt16();

                // HasFaces: since="10.1.0.0" — present for FO3
                entry.HasFaces = br.ReadByte() != 0;
                if (entry.HasFaces)
                {
                    if (entry.NumStrips > 0)
                    {
                        entry.Strips = new ushort[entry.NumStrips][];
                        for (int s = 0; s < entry.NumStrips; s++)
                        {
                            entry.Strips[s] = new ushort[entry.StripLengths[s]];
                            for (int i = 0; i < entry.StripLengths[s]; i++)
                                entry.Strips[s][i] = br.ReadUInt16();
                        }
                    }
                    else
                    {
                        entry.Triangles = new ushort[entry.NumTriangles][];
                        for (int t = 0; t < entry.NumTriangles; t++)
                        {
                            entry.Triangles[t] = new ushort[] {
                                br.ReadUInt16(), br.ReadUInt16(), br.ReadUInt16()
                            };
                        }
                    }
                }

                entry.HasBoneIndices = br.ReadByte() != 0;
                if (entry.HasBoneIndices)
                {
                    entry.BoneIndices = new byte[entry.NumVertices][];
                    for (int v = 0; v < entry.NumVertices; v++)
                    {
                        entry.BoneIndices[v] = new byte[entry.NumWeightsPerVertex];
                        for (int w = 0; w < entry.NumWeightsPerVertex; w++)
                            entry.BoneIndices[v][w] = br.ReadByte();
                    }
                }

                // FO3: LOD Level / Global VB / Vertex Desc / Triangles Copy are absent (BS_GT_FO3 / BS_SSE is false)
                entries[p] = entry;
            }

            return new SkinPartitionInfo
            {
                NumPartitions = numPartitions,
                Partitions = entries,
            };
        }
    }

    public struct SkinPartitionEntry
    {
        public ushort NumVertices;
        public ushort NumTriangles;
        public ushort NumBones;
        public ushort NumStrips;
        public ushort NumWeightsPerVertex;
        public ushort[] Bones;
        public bool HasVertexMap;
        public ushort[] VertexMap;
        public bool HasVertexWeights;
        public float[][] VertexWeights; // [numVertices][numWeightsPerVertex]
        public ushort[] StripLengths;
        public bool HasFaces;
        public ushort[][] Strips; // only if NumStrips > 0
        public ushort[][] Triangles; // only if NumStrips == 0 (each triangle = 3 ushorts)
        public bool HasBoneIndices;
        public byte[][] BoneIndices; // [numVertices][numWeightsPerVertex]
    }

    public struct SkinPartitionInfo
    {
        public uint NumPartitions;
        public SkinPartitionEntry[] Partitions;
    }

    public class SkinDataStore
    {
        public Dictionary<int, SkinInstanceInfo> SkinInstances = new();
        public Dictionary<int, SkinDataInfo> SkinDatas = new();
        public Dictionary<int, SkinPartitionInfo> SkinPartitions = new();
    }
}
