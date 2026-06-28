using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenFo3.NIF
{
    public class KFControllerSequence
    {
        public string Name;
        public float Weight;
        public float Frequency;
        public float StartTime;
        public float StopTime;
        public uint CycleType;
        public int TextKeyRef = -1;
        public int StringPaletteRef = -1;
        public uint TargetFormId;
        public List<KFAnimatedBone> AnimatedBones = new();
    }

    public class KFAnimatedBone
    {
        public string BoneName;
        public int KeyframeDataRef = -1;
        public List<KFTransformKey> Keys = new();
    }

    public struct KFTransformKey
    {
        public float Time;
        public Quaternion Rotation;
        public Vector3 Translation;
        public float Scale;
    }

    public class KFTextKey
    {
        public float Time;
        public string Text;
    }

    public class KFAnimationData
    {
        public string Name;
        public float Duration;
        public bool Loop;
        public Dictionary<string, List<KFTransformKey>> BoneKeyframes = new();
        public List<KFTextKey> TextKeys = new();
    }

    public static class KFAnimationLoader
    {
        public static List<KFAnimationData> LoadAnimations(byte[] kfData)
        {
            var sequences = new List<KFControllerSequence>();
            var textKeys = new List<KFTextKey>();
            var keyframeDataMap = new Dictionary<int, List<KFTransformKey>>();
            var stringPalette = new Dictionary<int, string>();
            var result = new List<KFAnimationData>();

            try
            {
                using var ms = new MemoryStream(kfData);
                using var br = new BinaryReader(ms);

                string headerStr = ReadHeaderString(br);
                uint version = br.ReadUInt32();
                byte endian = br.ReadByte();
                uint userVersion = br.ReadUInt32();
                uint numBlocks = br.ReadUInt32();

                uint bsVersion = br.ReadUInt32();
                for (int i = 0; i < 3; i++)
                {
                    byte len = br.ReadByte();
                    if (len > 0) br.ReadBytes(len);
                }

                int numBlockTypes = br.ReadUInt16();
                var blockTypes = new List<string>();
                for (int i = 0; i < numBlockTypes; i++)
                {
                    uint len = br.ReadUInt32();
                    blockTypes.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)));
                }

                var blockTypeIndices = new ushort[numBlocks];
                for (int i = 0; i < numBlocks; i++)
                    blockTypeIndices[i] = br.ReadUInt16();

                var blockSizes = new uint[numBlocks];
                for (int i = 0; i < numBlocks; i++)
                    blockSizes[i] = br.ReadUInt32();

                var strings = new List<string>();
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
                            strings.Add(Encoding.ASCII.GetString(br.ReadBytes((int)len)).TrimEnd('\0'));
                        }
                    }
                }

                var rootBlockIndices = new List<int>();
                if (ms.Position + 4 <= ms.Length)
                {
                    uint numRoots = br.ReadUInt32();
                    for (int i = 0; i < numRoots && ms.Position + 4 <= ms.Length; i++)
                    {
                        int rootIdx = br.ReadInt32();
                        if (rootIdx != -1) rootBlockIndices.Add(rootIdx);
                    }
                }

                var blocks = new List<(string Type, byte[] Data)>();
                for (int i = 0; i < numBlocks; i++)
                {
                    uint size = blockSizes[i];
                    string type = blockTypeIndices[i] < blockTypes.Count ? blockTypes[blockTypeIndices[i]] : "Unknown";
                    byte[] blockData = br.ReadBytes((int)size);
                    blocks.Add((type, blockData));
                }

                for (int i = 0; i < blocks.Count; i++)
                {
                    var (type, data) = blocks[i];
                    switch (type)
                    {
                        case "NiStringPalette":
                            ParseStringPalette(data, stringPalette, strings);
                            break;
                        case "NiTextKeyExtraData":
                            textKeys.AddRange(ParseTextKeys(data));
                            break;
                        case "NiKeyframeData":
                            var keys = ParseKeyframeData(data);
                            if (keys.Count > 0)
                                keyframeDataMap[i] = keys;
                            break;
                        case "NiControllerSequence":
                            var seq = ParseControllerSequence(data, strings, blocks);
                            if (seq != null)
                                sequences.Add(seq);
                            break;
                    }
                }

                foreach (var seq in sequences)
                {
                    if (seq.StringPaletteRef >= 0 && seq.StringPaletteRef < blocks.Count)
                    {
                        ParseStringPalette(blocks[seq.StringPaletteRef].Data, stringPalette, strings);
                    }
                }

                foreach (var seq in sequences)
                {
                    foreach (var bone in seq.AnimatedBones)
                    {
                        if (bone.KeyframeDataRef >= 0 && keyframeDataMap.TryGetValue(bone.KeyframeDataRef, out var keys))
                        {
                            bone.Keys = keys;
                        }
                    }
                }

                foreach (var seq in sequences)
                {
                    if (seq.StringPaletteRef >= 0 && seq.StringPaletteRef < blocks.Count)
                    {
                        ParseStringPalette(blocks[seq.StringPaletteRef].Data, stringPalette, strings);
                    }
                }

                foreach (var seq in sequences)
                {
                    var anim = new KFAnimationData
                    {
                        Name = seq.Name,
                        Duration = seq.StopTime - seq.StartTime,
                        Loop = seq.CycleType == 0,
                        TextKeys = textKeys,
                    };

                    foreach (var bone in seq.AnimatedBones)
                    {
                        string boneName = bone.BoneName;
                        int colonIdx = boneName.IndexOf(':');
                        if (colonIdx >= 0)
                            boneName = boneName.Substring(colonIdx + 1);

                        anim.BoneKeyframes[boneName] = bone.Keys;
                    }

                    result.Add(anim);
                }

                return result;
            }
            catch (Exception e)
            {
                GD.PrintErr($"[KFAnimationLoader] Error parsing KF: {e.Message}");
                return result;
            }
        }

        private static string ReadHeaderString(BinaryReader br)
        {
            var bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                bytes.Add(b);
                if (b == 0x0A) break;
                if (bytes.Count > 100) break;
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static KFControllerSequence ParseControllerSequence(byte[] data, List<string> strings, List<(string Type, byte[] Data)> blocks)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                uint nameIdx = br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                int controllerRef = br.ReadInt32();

                string name = nameIdx < strings.Count ? strings[(int)nameIdx] : $"Sequence_{nameIdx}";

                float weight = br.ReadSingle();
                int textKeyRef = br.ReadInt32();
                uint cycleType = br.ReadUInt32();
                float frequency = br.ReadSingle();
                float startTime = br.ReadSingle();
                float stopTime = br.ReadSingle();
                uint targetFormId = br.ReadUInt32();
                int stringPaletteRef = br.ReadInt32();

                uint numAnimBones = br.ReadUInt32();
                var animatedBones = new List<KFAnimatedBone>();

                for (int i = 0; i < numAnimBones; i++)
                {
                    uint boneNameIdx = br.ReadUInt32();
                    string boneName = boneNameIdx < strings.Count ? strings[(int)boneNameIdx] : $"Bone_{boneNameIdx}";

                    int kfDataRef = br.ReadInt32();

                    animatedBones.Add(new KFAnimatedBone
                    {
                        BoneName = boneName,
                        KeyframeDataRef = kfDataRef,
                    });
                }

                return new KFControllerSequence
                {
                    Name = name,
                    Weight = weight,
                    TextKeyRef = textKeyRef,
                    CycleType = cycleType,
                    Frequency = frequency,
                    StartTime = startTime,
                    StopTime = stopTime,
                    TargetFormId = targetFormId,
                    StringPaletteRef = stringPaletteRef,
                    AnimatedBones = animatedBones,
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<KFTransformKey> ParseKeyframeData(byte[] data)
        {
            var keys = new List<KFTransformKey>();

            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                uint numKeys = br.ReadUInt32();
                uint keyType = br.ReadUInt32();

                for (int i = 0; i < numKeys; i++)
                {
                    float time = br.ReadSingle();
                    float qx = br.ReadSingle();
                    float qy = br.ReadSingle();
                    float qz = br.ReadSingle();
                    float qw = br.ReadSingle();

                    float tx = br.ReadSingle();
                    float ty = br.ReadSingle();
                    float tz = br.ReadSingle();
                    float scale = br.ReadSingle();

                    keys.Add(new KFTransformKey
                    {
                        Time = time,
                        Rotation = new Quaternion(qx, qz, -qy, qw),
                        Translation = new Vector3(tx, tz, -ty),
                        Scale = scale,
                    });
                }
            }
            catch { }

            return keys;
        }

        private static void ParseStringPalette(byte[] data, Dictionary<int, string> palette, List<string> strings)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                uint numPalette = br.ReadUInt32();
                for (int i = 0; i < numPalette && br.BaseStream.Position < br.BaseStream.Length; i++)
                {
                    uint strIdx = br.ReadUInt32();
                    uint offset = br.ReadUInt32();
                    if (strIdx < strings.Count && !palette.ContainsKey((int)strIdx))
                    {
                        palette[(int)strIdx] = strings[(int)strIdx];
                    }
                }
            }
            catch { }
        }

        private static List<KFTextKey> ParseTextKeys(byte[] data)
        {
            var keys = new List<KFTextKey>();

            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                uint nameIdx = br.ReadUInt32();
                uint numExtra = br.ReadUInt32();
                for (int i = 0; i < numExtra; i++) br.ReadInt32();
                br.ReadInt32();

                uint numKeys = br.ReadUInt32();

                for (int i = 0; i < numKeys && br.BaseStream.Position + 4 < br.BaseStream.Length; i++)
                {
                    float time = br.ReadSingle();
                    uint strLen = br.ReadUInt32();
                    if (strLen > 256 || br.BaseStream.Position + strLen > br.BaseStream.Length) break;
                    string text = Encoding.ASCII.GetString(br.ReadBytes((int)strLen)).TrimEnd('\0');

                    keys.Add(new KFTextKey { Time = time, Text = text });
                }
            }
            catch { }

            return keys;
        }

        public static Animation BuildGodotAnimation(KFAnimationData animData, SkeletonData skeleton, float worldScale = 0.015f, string trackPrefix = "NPCVisual/Skeleton:")
        {
            var anim = new Animation();
            anim.Length = animData.Duration > 0 ? animData.Duration : 1f;
            anim.LoopMode = animData.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;

            foreach (var kvp in animData.BoneKeyframes)
            {
                string boneName = kvp.Key;
                var keys = kvp.Value;

                if (keys.Count == 0) continue;

                int boneIndex = -1;
                for (int i = 0; i < skeleton.Bones.Count; i++)
                {
                    if (skeleton.Bones[i].Name == boneName)
                    {
                        boneIndex = i;
                        break;
                    }
                }
                if (boneIndex < 0) continue;

                string trackPath = $"{trackPrefix}{boneName}";
                int trackIdx = anim.AddTrack(Animation.TrackType.Position3D);
                anim.TrackSetPath(trackIdx, trackPath);

                for (int k = 0; k < keys.Count; k++)
                {
                    var godotPos = keys[k].Translation * worldScale;
                    anim.TrackInsertKey(trackIdx, keys[k].Time, godotPos);
                }

                int rotTrackIdx = anim.AddTrack(Animation.TrackType.Rotation3D);
                anim.TrackSetPath(rotTrackIdx, trackPath);

                for (int k = 0; k < keys.Count; k++)
                {
                    anim.TrackInsertKey(rotTrackIdx, keys[k].Time, keys[k].Rotation);
                }

                if (keys.Any(k => Math.Abs(k.Scale - 1f) > 0.001f))
                {
                    int scaleTrackIdx = anim.AddTrack(Animation.TrackType.Scale3D);
                    anim.TrackSetPath(scaleTrackIdx, trackPath);

                    for (int k = 0; k < keys.Count; k++)
                    {
                        float s = keys[k].Scale;
                        anim.TrackInsertKey(scaleTrackIdx, keys[k].Time, new Vector3(s, s, s));
                    }
                }
            }

            return anim;
        }
    }
}
