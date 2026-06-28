using System;
using System.Collections.Generic;
using System.Text;

namespace OpenFo3.ESM
{
    public static class RecordParsers
    {
        private static string GetString(byte[] data, int offset, int length)
        {
            int end = offset + length;
            if (offset < 0 || end > data.Length) return string.Empty;
            return Encoding.ASCII.GetString(data, offset, length).TrimEnd('\0');
        }

        private static string GetZString(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            int len = 0;
            while (len < data.Length && data[len] != 0) len++;
            return Encoding.ASCII.GetString(data, 0, len);
        }

        private static uint GetUInt(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return BitConverter.ToUInt32(data, offset);
        }

        private static ushort GetUShort(byte[] data, int offset)
        {
            if (offset + 2 > data.Length) return 0;
            return BitConverter.ToUInt16(data, offset);
        }

        private static int GetInt(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return BitConverter.ToInt32(data, offset);
        }

        private static float GetFloat(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0f;
            return BitConverter.ToSingle(data, offset);
        }

        private static byte GetByte(byte[] data, int offset)
        {
            if (offset >= data.Length) return 0;
            return data[offset];
        }

        private static List<SubRecord> FindSubRecords(List<SubRecord> subs, string type)
        {
            var result = new List<SubRecord>();
            foreach (var sub in subs)
            {
                if (sub.Type == type) result.Add(sub);
            }
            return result;
        }

        private static SubRecord FindSubRecord(List<SubRecord> subs, string type)
        {
            foreach (var sub in subs)
            {
                if (sub.Type == type) return sub;
            }
            return null;
        }

        // ──────────────────────────────────────────────
        // NPC_ - Non-Player Character
        // ──────────────────────────────────────────────

        public class NpcData
        {
            public string EditorId;
            public string Name;
            public string ModelPath;
            public byte[] ModelTextureHashes;
            public uint RaceFormId;
            public List<uint> FactionFormIds = new();
            public uint Flags;
            public float BaseHealth;
            public float AttackDamage;
            public byte Strength;
            public byte Perception;
            public byte Endurance;
            public byte Charisma;
            public byte Intelligence;
            public byte Agility;
            public byte Luck;
            public byte[] Skills = Array.Empty<byte>();
            public byte Aggression;
            public byte Confidence;
            public byte Energy;
            public byte Responsibility;
            public byte Morale;
            public List<uint> AIPackageFormIds = new();
            public uint AcbsFlags;
            public uint TemplateFlags;
            public uint CombatStyleFormId;
        }

        public static NpcData ParseNpc(List<SubRecord> subs)
        {
            var d = new NpcData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "MODL":
                        d.ModelPath = GetZString(data);
                        break;
                    case "MODT":
                        d.ModelTextureHashes = data;
                        break;
                    case "RNAM":
                        d.RaceFormId = GetUInt(data, 0);
                        break;
                    case "SNAM":
                        d.FactionFormIds.Add(GetUInt(data, 0));
                        break;
                    case "DATA":
                    {
                        int off = 0;
                        d.Flags = GetUInt(data, off); off += 4;
                        d.BaseHealth = GetFloat(data, off); off += 4;
                        d.AttackDamage = GetFloat(data, off); off += 4;
                        d.Strength = GetByte(data, off++);
                        d.Perception = GetByte(data, off++);
                        d.Endurance = GetByte(data, off++);
                        d.Charisma = GetByte(data, off++);
                        d.Intelligence = GetByte(data, off++);
                        d.Agility = GetByte(data, off++);
                        d.Luck = GetByte(data, off++);
                        if (off + 13 <= data.Length)
                        {
                            d.Skills = new byte[13];
                            Array.Copy(data, off, d.Skills, 0, 13);
                        }
                        break;
                    }
                    case "AIDT":
                    {
                        d.Aggression = GetByte(data, 0);
                        d.Confidence = GetByte(data, 1);
                        d.Energy = GetByte(data, 2);
                        d.Responsibility = GetByte(data, 3);
                        d.Morale = GetByte(data, 4);
                        break;
                    }
                    case "PKCU":
                    case "PKCT":
                    case "PKED":
                        if (data.Length >= 4)
                            d.AIPackageFormIds.Add(GetUInt(data, 0));
                        break;
                    case "ACBS":
                    {
                        d.AcbsFlags = GetUInt(data, 0);
                        if (data.Length >= 8) d.TemplateFlags = GetUInt(data, 4);
                        if (data.Length >= 12) d.CombatStyleFormId = GetUInt(data, 8);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // CREA - Creature
        // ──────────────────────────────────────────────

        public class CreatureData
        {
            public string EditorId;
            public string Name;
            public string ModelPath;
            public byte[] ModelTextureHashes;
            public uint RaceFormId;
            public List<uint> FactionFormIds = new();
            public uint Flags;
            public float BoundingRadius;
            public float CombatSkill;
            public float MagicSkill;
            public byte CreatureType;
            public byte Aggression;
            public byte Confidence;
            public byte Energy;
            public byte Responsibility;
            public byte Morale;
            public float BaseHealth;
            public float AttackDamage;
            public byte Strength;
            public byte Perception;
            public byte Endurance;
            public byte Charisma;
            public byte Intelligence;
            public byte Agility;
            public byte Luck;
            public byte[] Skills = Array.Empty<byte>();
            public List<uint> AIPackageFormIds = new();
        }

        public static CreatureData ParseCreature(List<SubRecord> subs)
        {
            var d = new CreatureData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "MODL":
                        d.ModelPath = GetZString(data);
                        break;
                    case "MODT":
                        d.ModelTextureHashes = data;
                        break;
                    case "RNAM":
                        d.RaceFormId = GetUInt(data, 0);
                        break;
                    case "SNAM":
                        d.FactionFormIds.Add(GetUInt(data, 0));
                        break;
                    case "DATA":
                    {
                        int off = 0;
                        d.CreatureType = GetByte(data, off++);
                        d.CombatSkill = GetFloat(data, off); off += 4;
                        d.MagicSkill = GetFloat(data, off); off += 4;
                        d.BaseHealth = GetFloat(data, off); off += 4;
                        d.AttackDamage = GetFloat(data, off); off += 4;
                        d.Strength = GetByte(data, off++);
                        d.Perception = GetByte(data, off++);
                        d.Endurance = GetByte(data, off++);
                        d.Charisma = GetByte(data, off++);
                        d.Intelligence = GetByte(data, off++);
                        d.Agility = GetByte(data, off++);
                        d.Luck = GetByte(data, off++);
                        if (off + 13 <= data.Length)
                        {
                            d.Skills = new byte[13];
                            Array.Copy(data, off, d.Skills, 0, 13);
                        }
                        break;
                    }
                    case "AIDT":
                    {
                        d.Aggression = GetByte(data, 0);
                        d.Confidence = GetByte(data, 1);
                        d.Energy = GetByte(data, 2);
                        d.Responsibility = GetByte(data, 3);
                        d.Morale = GetByte(data, 4);
                        break;
                    }
                    case "PKCU":
                    case "PKCT":
                    case "PKED":
                        if (data.Length >= 4)
                            d.AIPackageFormIds.Add(GetUInt(data, 0));
                        break;
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // RACE - Race
        // ──────────────────────────────────────────────

        public class SkillBonus
        {
            public byte SkillType;
            public byte Bonus;
        }

        public class RaceData
        {
            public string EditorId;
            public string Name;
            public string Description;
            public List<SkillBonus> SkillBonuses = new();
            public uint RaceFlags;
            public List<uint> FacePartIds = new();
            public List<uint> HairColorFormIds = new();
            public List<byte> HairColors = new();
        }

        public static RaceData ParseRace(List<SubRecord> subs)
        {
            var d = new RaceData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DESC":
                        d.Description = GetZString(data);
                        break;
                    case "DATA":
                    {
                        if (data.Length >= 4)
                            d.RaceFlags = GetUInt(data, 0);
                        if (data.Length >= 8)
                        {
                            int count = (data.Length - 4) / 2;
                            for (int i = 0; i < count; i++)
                            {
                                d.SkillBonuses.Add(new SkillBonus
                                {
                                    SkillType = GetByte(data, 4 + i * 2),
                                    Bonus = GetByte(data, 5 + i * 2)
                                });
                            }
                        }
                        break;
                    }
                    case "FNAM":
                    {
                        for (int i = 0; i + 4 <= data.Length; i += 4)
                            d.FacePartIds.Add(GetUInt(data, i));
                        break;
                    }
                    case "HNAM":
                    {
                        for (int i = 0; i + 4 <= data.Length; i += 4)
                            d.HairColorFormIds.Add(GetUInt(data, i));
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // LVLN - Leveled NPC
        // ──────────────────────────────────────────────

        public class LeveledEntry
        {
            public short Level;
            public uint FormId;
            public short Count;
        }

        public class LeveledNpcData
        {
            public string EditorId;
            public byte ChanceNone;
            public byte Flags;
            public List<LeveledEntry> Entries = new();
        }

        public static LeveledNpcData ParseLeveledNpc(List<SubRecord> subs)
        {
            return ParseLeveledBase<LeveledNpcData>(subs);
        }

        private static T ParseLeveledBase<T>(List<SubRecord> subs) where T : LeveledNpcData, new()
        {
            var d = new T();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "LVLD":
                        d.ChanceNone = GetByte(data, 0);
                        break;
                    case "LVLF":
                        d.Flags = GetByte(data, 0);
                        break;
                    case "LVLO":
                    {
                        if (data.Length >= 12)
                        {
                            d.Entries.Add(new LeveledEntry
                            {
                                Level = BitConverter.ToInt16(data, 0),
                                FormId = GetUInt(data, 4),
                                Count = BitConverter.ToInt16(data, 8)
                            });
                        }
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // LVLC - Leveled Creature
        // ──────────────────────────────────────────────

        public class LeveledCreatureData : LeveledNpcData { }

        public static LeveledCreatureData ParseLeveledCreature(List<SubRecord> subs)
        {
            return ParseLeveledBase<LeveledCreatureData>(subs);
        }

        // ──────────────────────────────────────────────
        // QUST - Quest
        // ──────────────────────────────────────────────

        public class Condition
        {
            public float ComparisonValue;
            public byte OpCode;
            public uint Param1;
            public uint Param2;
            public uint RunOnType;
            public uint ReferenceFormId;
            public uint ActorValue;
        }

        public class QuestStage
        {
            public int Index;
            public byte Flags;
            public string LogEntry;
            public List<string> LogEntries = new();
        }

        public class QuestObjective
        {
            public int Index;
            public string Text;
        }

        public class QuestData
        {
            public string EditorId;
            public string Name;
            public ushort QuestFlags;
            public byte Priority;
            public List<Condition> Conditions = new();
            public List<QuestStage> Stages = new();
            public List<QuestObjective> Objectives = new();
        }

        public static QuestData ParseQuest(List<SubRecord> subs)
        {
            var d = new QuestData();
            if (subs == null) return d;

            QuestStage currentStage = null;
            QuestObjective currentObjective = null;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.QuestFlags = GetUShort(data, 0);
                        if (data.Length >= 3) d.Priority = GetByte(data, 2);
                        break;
                    }
                    case "CTDA":
                    {
                        if (data.Length >= 24)
                        {
                            d.Conditions.Add(new Condition
                            {
                                ComparisonValue = GetFloat(data, 0),
                                OpCode = GetByte(data, 4),
                                Param1 = GetUInt(data, 8),
                                Param2 = GetUInt(data, 12),
                                RunOnType = GetUInt(data, 16),
                                ReferenceFormId = GetUInt(data, 20),
                                ActorValue = GetUInt(data, 24)
                            });
                        }
                        break;
                    }
                    case "INDX":
                    {
                        currentStage = new QuestStage
                        {
                            Index = GetInt(data, 0)
                        };
                        d.Stages.Add(currentStage);
                        break;
                    }
                    case "QSDT":
                    {
                        if (currentStage != null)
                            currentStage.Flags = GetByte(data, 0);
                        break;
                    }
                    case "CNAM":
                    {
                        if (currentStage != null)
                        {
                            string entry = GetZString(data);
                            currentStage.LogEntries.Add(entry);
                        }
                        break;
                    }
                    case "QOBJ":
                    {
                        currentObjective = new QuestObjective
                        {
                            Index = GetInt(data, 0)
                        };
                        d.Objectives.Add(currentObjective);
                        break;
                    }
                    case "NNAM":
                    {
                        if (currentObjective != null)
                            currentObjective.Text = GetZString(data);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // DIAL - Dialog Topic
        // ──────────────────────────────────────────────

        public class DialogData
        {
            public string EditorId;
            public string Name;
            public byte DialogType;
            public uint Flags;
            public uint FirstInfoFormId;
        }

        public static DialogData ParseDialog(List<SubRecord> subs)
        {
            var d = new DialogData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.DialogType = GetByte(data, 0);
                        if (data.Length >= 4) d.Flags = GetUInt(data, 4);
                        break;
                    }
                    case "SNAM":
                        d.FirstInfoFormId = GetUInt(data, 0);
                        break;
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // INFO - Dialog Response
        // ──────────────────────────────────────────────

        public class ResponseData
        {
            public string Text;
            public string ActorName;
            public string Speaker;
            public uint EmotionType;
            public uint EmotionValue;
            public string SoundFilename;
        }

        public class InfoData
        {
            public string EditorId;
            public uint QuestFormId;
            public byte ResponseFlags;
            public uint EmotionType;
            public uint EmotionValue;
            public uint PreviousInfoFormId;
            public uint NextInfoFormId;
            public string ResponseText;
            public string ActorName;
            public string Speaker;
            public List<Condition> Conditions = new();
            public List<ResponseData> Responses = new();
            public string SoundFilename;
        }

        public static InfoData ParseInfo(List<SubRecord> subs)
        {
            var d = new InfoData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.QuestFormId = GetUInt(data, 0);
                        if (data.Length >= 8)
                        {
                            d.ResponseFlags = GetByte(data, 4);
                            d.EmotionType = GetUInt(data, 8);
                            d.EmotionValue = GetUInt(data, 12);
                        }
                        break;
                    }
                    case "PNAM":
                        d.PreviousInfoFormId = GetUInt(data, 0);
                        break;
                    case "JNAM":
                        d.NextInfoFormId = GetUInt(data, 0);
                        break;
                    case "TRDT":
                    {
                        if (data.Length >= 16)
                        {
                            d.EmotionType = GetUInt(data, 0);
                            d.EmotionValue = GetUInt(data, 4);
                            d.ResponseFlags = GetByte(data, 8);
                        }
                        break;
                    }
                    case "NAM1":
                        d.ResponseText = GetZString(data);
                        break;
                    case "CTDA":
                    {
                        if (data.Length >= 24)
                        {
                            d.Conditions.Add(new Condition
                            {
                                ComparisonValue = GetFloat(data, 0),
                                OpCode = GetByte(data, 4),
                                Param1 = GetUInt(data, 8),
                                Param2 = GetUInt(data, 12),
                                RunOnType = GetUInt(data, 16),
                                ReferenceFormId = GetUInt(data, 20),
                                ActorValue = GetUInt(data, 24)
                            });
                        }
                        break;
                    }
                    case "SNAM":
                        d.SoundFilename = GetZString(data);
                        break;
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // SCEN - Scene
        // ──────────────────────────────────────────────

        public class SceneActor
        {
            public uint ActorFormId;
            public uint BehaviorFlags;
        }

        public class SceneData
        {
            public string EditorId;
            public uint QuestFormId;
            public uint Flags;
            public List<SceneActor> Actors = new();
        }

        public static SceneData ParseScene(List<SubRecord> subs)
        {
            var d = new SceneData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.QuestFormId = GetUInt(data, 0);
                        if (data.Length >= 8) d.Flags = GetUInt(data, 4);
                        break;
                    }
                    case "ACTV":
                    {
                        if (data.Length >= 4)
                        {
                            d.Actors.Add(new SceneActor
                            {
                                ActorFormId = GetUInt(data, 0),
                                BehaviorFlags = data.Length >= 8 ? GetUInt(data, 4) : 0
                            });
                        }
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // MGEF - Magic Effect
        // ──────────────────────────────────────────────

        public class MagicEffectData
        {
            public string EditorId;
            public string Name;
            public string Description;
            public uint EffectFlags;
            public float BaseCost;
            public uint Archetype;
            public uint ActorEffect;
            public uint School;
            public uint ResistValue;
            public uint LightFormId;
            public uint ProjectileFormId;
            public string IconPath;
            public string IconSmallPath;
        }

        public static MagicEffectData ParseMagicEffect(List<SubRecord> subs)
        {
            var d = new MagicEffectData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DESC":
                        d.Description = GetZString(data);
                        break;
                    case "DATA":
                    {
                        if (data.Length >= 4) d.EffectFlags = GetUInt(data, 0);
                        if (data.Length >= 8) d.BaseCost = GetFloat(data, 4);
                        if (data.Length >= 12) d.Archetype = GetUInt(data, 8);
                        if (data.Length >= 16) d.ActorEffect = GetUInt(data, 12);
                        if (data.Length >= 24) d.School = GetUInt(data, 20);
                        break;
                    }
                    case "LNAM":
                        d.LightFormId = GetUInt(data, 0);
                        break;
                    case "INAM":
                        d.ProjectileFormId = GetUInt(data, 0);
                        break;
                    case "MICN":
                        d.IconPath = GetZString(data);
                        break;
                    case "MICO":
                        d.IconSmallPath = GetZString(data);
                        break;
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // SPEL - Spell
        // ──────────────────────────────────────────────

        public class EffectItem
        {
            public uint EffectFormId;
            public uint Magnitude;
            public uint Area;
            public uint Duration;
            public uint EffectType;
        }

        public class SpellData
        {
            public string EditorId;
            public string Name;
            public uint SpellType;
            public uint Flags;
            public float Cost;
            public List<EffectItem> Effects = new();
        }

        public static SpellData ParseSpell(List<SubRecord> subs)
        {
            var d = new SpellData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.SpellType = GetUInt(data, 0);
                        if (data.Length >= 8) d.Cost = GetFloat(data, 4);
                        if (data.Length >= 12) d.Flags = GetUInt(data, 8);
                        break;
                    }
                    case "EFIT":
                    {
                        if (data.Length >= 12)
                        {
                            d.Effects.Add(new EffectItem
                            {
                                EffectFormId = GetUInt(data, 0),
                                Magnitude = GetUInt(data, 4),
                                Area = GetUInt(data, 8),
                                Duration = GetUInt(data, 12),
                                EffectType = GetUInt(data, 16)
                            });
                        }
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // EFSH - Effect Shader
        // ──────────────────────────────────────────────

        public class EffectShaderData
        {
            public string EditorId;
            public uint FillColor;
            public uint RimColor;
            public float EdgeWidth;
            public float EdgeSoftness;
            public uint EffectFlags;
            public float FillAlpha;
            public float RimAlpha;
            public uint FillTextureFormId;
            public uint RimTextureFormId;
        }

        public static EffectShaderData ParseEffectShader(List<SubRecord> subs)
        {
            var d = new EffectShaderData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        int off = 0;
                        d.FillColor = GetUInt(data, off); off += 4;
                        d.RimColor = GetUInt(data, off); off += 4;
                        d.EdgeWidth = GetFloat(data, off); off += 4;
                        d.EdgeSoftness = GetFloat(data, off); off += 4;
                        d.FillAlpha = GetFloat(data, off); off += 4;
                        d.RimAlpha = GetFloat(data, off); off += 4;
                        d.FillTextureFormId = GetUInt(data, off); off += 4;
                        d.RimTextureFormId = GetUInt(data, off); off += 4;
                        if (data.Length > off) d.EffectFlags = GetUInt(data, off);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // PERK - Perk
        // ──────────────────────────────────────────────

        public class PerkCondition
        {
            public float ComparisonValue;
            public byte OpCode;
            public uint Param1;
            public uint Param2;
            public uint RunOnType;
            public uint ReferenceFormId;
            public uint ActorValue;
        }

        public class PerkEntry
        {
            public uint Priority;
            public List<PerkCondition> Conditions = new();
            public uint FunctionType;
            public uint Param1;
            public uint Param2;
        }

        public class PerkData
        {
            public string EditorId;
            public string Name;
            public string Description;
            public uint Flags;
            public byte MinLevel;
            public byte Rank;
            public bool CanTakeMultiple;
            public List<PerkCondition> Conditions = new();
            public List<PerkEntry> Entries = new();
        }

        public static PerkData ParsePerk(List<SubRecord> subs)
        {
            var d = new PerkData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DESC":
                        d.Description = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.Flags = GetUInt(data, 0);
                        if (data.Length >= 5) d.MinLevel = GetByte(data, 4);
                        if (data.Length >= 6) d.Rank = GetByte(data, 5);
                        break;
                    }
                    case "CTDA":
                    {
                        if (data.Length >= 24)
                        {
                            d.Conditions.Add(new PerkCondition
                            {
                                ComparisonValue = GetFloat(data, 0),
                                OpCode = GetByte(data, 4),
                                Param1 = GetUInt(data, 8),
                                Param2 = GetUInt(data, 12),
                                RunOnType = GetUInt(data, 16),
                                ReferenceFormId = GetUInt(data, 20),
                                ActorValue = GetUInt(data, 24)
                            });
                        }
                        break;
                    }
                    case "ENTY":
                    {
                        var entry = new PerkEntry
                        {
                            Priority = GetUInt(data, 0),
                            FunctionType = data.Length >= 8 ? GetUInt(data, 4) : 0,
                            Param1 = data.Length >= 12 ? GetUInt(data, 8) : 0,
                            Param2 = data.Length >= 16 ? GetUInt(data, 12) : 0
                        };
                        d.Entries.Add(entry);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // AVIF - Actor Value
        // ──────────────────────────────────────────────

        public class ActorValueData
        {
            public string EditorId;
            public string Name;
            public string Description;
            public byte Category;
            public uint Flags;
        }

        public static ActorValueData ParseActorValue(List<SubRecord> subs)
        {
            var d = new ActorValueData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FULL":
                        d.Name = GetZString(data);
                        break;
                    case "DESC":
                        d.Description = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.Category = GetByte(data, 0);
                        if (data.Length >= 4) d.Flags = GetUInt(data, 4);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // GMST - Game Setting
        // ──────────────────────────────────────────────

        public class GameSettingData
        {
            public string EditorId;
            public GameSettingType SettingType;
            public int IntValue;
            public float FloatValue;
            public string StringValue;
        }

        public enum GameSettingType
        {
            Unknown,
            Integer,
            Float,
            String
        }

        public static GameSettingData ParseGameSetting(List<SubRecord> subs)
        {
            var d = new GameSettingData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        string edid = d.EditorId ?? string.Empty;
                        if (edid.Length > 0)
                        {
                            char prefix = edid[0];
                            if (prefix == 'i' || prefix == 'b' || prefix == 'u')
                            {
                                d.SettingType = GameSettingType.Integer;
                                d.IntValue = GetInt(data, 0);
                            }
                            else if (prefix == 'f')
                            {
                                d.SettingType = GameSettingType.Float;
                                d.FloatValue = GetFloat(data, 0);
                            }
                            else if (prefix == 's')
                            {
                                d.SettingType = GameSettingType.String;
                                d.StringValue = GetZString(data);
                            }
                        }
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // SOUN - Sound
        // ──────────────────────────────────────────────

        public class SoundData
        {
            public string EditorId;
            public string Filename;
            public uint Flags;
            public byte MinDistance;
            public byte MaxDistance;
            public byte FrequencyAdjustment;
            public byte ReverbSend;
            public byte StaticAttenuation;
            public bool Loop;
            public bool IsDialogue;
            public bool IsRandom;
            public bool IsMenu;
        }

        public static SoundData ParseSound(List<SubRecord> subs)
        {
            var d = new SoundData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "FNAM":
                        d.Filename = GetZString(data);
                        break;
                    case "SNDD":
                    {
                        if (data.Length >= 48)
                        {
                            d.MinDistance = GetByte(data, 0);
                            d.MaxDistance = GetByte(data, 1);
                            d.FrequencyAdjustment = GetByte(data, 2);
                            d.ReverbSend = GetByte(data, 5);
                            d.StaticAttenuation = GetByte(data, 6);
                            d.Flags = GetUInt(data, 8);
                        }
                        break;
                    }
                    case "SNDX":
                    {
                        if (data.Length >= 16)
                        {
                            d.MinDistance = GetByte(data, 0);
                            d.MaxDistance = GetByte(data, 1);
                            d.FrequencyAdjustment = GetByte(data, 2);
                            d.Flags = GetUInt(data, 8);
                        }
                        break;
                    }
                }
            }

            d.Loop = (d.Flags & 0x10) != 0;
            d.IsDialogue = (d.Flags & 0x100) != 0;
            d.IsRandom = (d.Flags & 0x2) != 0;
            d.IsMenu = (d.Flags & 0x20) != 0;

            return d;
        }

        // ──────────────────────────────────────────────
        // MUSC - Music Track
        // ──────────────────────────────────────────────

        public class MusicData
        {
            public string EditorId;
            public string Filename;
        }

        public static MusicData ParseMusic(List<SubRecord> subs)
        {
            var d = new MusicData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                        d.Filename = GetZString(data);
                        break;
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // WTHR - Weather
        // ──────────────────────────────────────────────

        public class WeatherData
        {
            public string EditorId;
            public float CloudSpeed;
            public float LowerLayerSpeed;
            public float MiddleLayerSpeed;
            public float UpperLayerSpeed;
            public uint CloudColors;
            public float FogDistanceDay;
            public float FogDistanceNight;
            public float FogClipDistance;
            public byte FogColorRed;
            public byte FogColorGreen;
            public byte FogColorBlue;
            public List<uint> SoundFormIds = new();
        }

        public static WeatherData ParseWeather(List<SubRecord> subs)
        {
            var d = new WeatherData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        if (data.Length >= 4) d.CloudSpeed = GetFloat(data, 0);
                        break;
                    }
                    case "NAM0":
                    {
                        if (data.Length >= 4) d.LowerLayerSpeed = GetFloat(data, 0);
                        break;
                    }
                    case "NAM1":
                    {
                        if (data.Length >= 4) d.MiddleLayerSpeed = GetFloat(data, 0);
                        break;
                    }
                    case "NAM2":
                    {
                        if (data.Length >= 4) d.UpperLayerSpeed = GetFloat(data, 0);
                        break;
                    }
                    case "FNAM":
                    {
                        if (data.Length >= 4) d.FogDistanceDay = GetFloat(data, 0);
                        break;
                    }
                    case "FNA2":
                    {
                        if (data.Length >= 4) d.FogDistanceNight = GetFloat(data, 0);
                        break;
                    }
                    case "FNA3":
                    {
                        if (data.Length >= 4) d.FogClipDistance = GetFloat(data, 0);
                        break;
                    }
                    case "CNAM":
                    {
                        if (data.Length >= 3)
                        {
                            d.FogColorRed = GetByte(data, 0);
                            d.FogColorGreen = GetByte(data, 1);
                            d.FogColorBlue = GetByte(data, 2);
                        }
                        break;
                    }
                    case "SNAM":
                    {
                        for (int i = 0; i + 4 <= data.Length; i += 4)
                            d.SoundFormIds.Add(GetUInt(data, i));
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // CSTY - Combat Style
        // ──────────────────────────────────────────────

        public class CombatStyleData
        {
            public string EditorId;
            public byte DodgeChance;
            public byte MinWeaponRange;
            public byte MaxWeaponRange;
            public float ApproachDistance;
            public float FlankDistance;
            public float AdvanceChance;
            public float MeleeRange;
            public float CoverSearchDistance;
            public float RetreatDistance;
        }

        public static CombatStyleData ParseCombatStyle(List<SubRecord> subs)
        {
            var d = new CombatStyleData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        if (data.Length >= 1) d.DodgeChance = GetByte(data, 0);
                        if (data.Length >= 2) d.MinWeaponRange = GetByte(data, 1);
                        if (data.Length >= 3) d.MaxWeaponRange = GetByte(data, 2);
                        if (data.Length >= 7) d.ApproachDistance = GetFloat(data, 3);
                        if (data.Length >= 11) d.FlankDistance = GetFloat(data, 7);
                        if (data.Length >= 23) d.AdvanceChance = GetFloat(data, 19);
                        if (data.Length >= 31) d.MeleeRange = GetFloat(data, 27);
                        if (data.Length >= 35) d.CoverSearchDistance = GetFloat(data, 31);
                        if (data.Length >= 39) d.RetreatDistance = GetFloat(data, 35);
                        break;
                    }
                }
            }
            return d;
        }

        // ──────────────────────────────────────────────
        // PMIS - AI Package
        // ──────────────────────────────────────────────

        public class AIPackageData
        {
            public string EditorId;
            public uint PackageType;
            public uint Flags;
            public uint TargetFormId;
            public uint TargetLocation;
            public float Duration;
            public float Distance;
            public int ScheduleMonth;
            public int ScheduleDay;
            public int ScheduleTime;
        }

        public static AIPackageData ParseAIPackage(List<SubRecord> subs)
        {
            var d = new AIPackageData();
            if (subs == null) return d;

            foreach (var sub in subs)
            {
                var data = sub.Data;
                if (data == null) continue;

                switch (sub.Type)
                {
                    case "EDID":
                        d.EditorId = GetZString(data);
                        break;
                    case "DATA":
                    {
                        d.PackageType = GetUInt(data, 0);
                        if (data.Length >= 8) d.Flags = GetUInt(data, 4);
                        break;
                    }
                    case "TNAM":
                        d.TargetFormId = GetUInt(data, 0);
                        break;
                    case "UNAM":
                        d.TargetLocation = GetUInt(data, 0);
                        break;
                }
            }
            return d;
        }
    }
}
