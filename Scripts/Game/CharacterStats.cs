using Godot;
using System.Collections.Generic;

namespace OpenFo3.Game
{
    public enum SpecialAttribute
    {
        Strength, Perception, Endurance, Charisma, Intelligence, Agility, Luck
    }

    public enum SkillName
    {
        Barter, BigGuns, EnergyWeapons, Explosives, Lockpick,
        Medicine, MeleeWeapons, Repair, Science, SmallGuns, Sneak,
        Speech, Unarmed
    }

    public class PerkDef
    {
        public string Name;
        public string Description;
        public int MinLevel;
        public bool CanTakeMultiple;
        public int MaxRank;
        public List<PerkCondition> Conditions = new();
        public List<PerkEffect> Effects = new();
    }

    public class PerkCondition
    {
        public SpecialAttribute? RequiredAttribute;
        public int MinAttributeValue;
        public SkillName? RequiredSkill;
        public int MinSkillValue;
        public int RequiredLevel;
        public uint RequiredPerkFormId;
    }

    public enum PerkEffectType
    {
        AddSkillBonus, AddAttributeBonus, AddDamageResist, AddPoisonResist,
        AddRadResist, ModifySkillRate, AddPerk, ModifyHitChance, ModifyCritChance,
        ModifyDamage, SpecialPower
    }

    public class PerkEffect
    {
        public PerkEffectType Type;
        public SkillName? TargetSkill;
        public SpecialAttribute? TargetAttribute;
        public float Value;
        public string SpecialFunction;
    }

    public partial class CharacterStats : Node
    {
        private int[] _special = new int[7] { 5, 5, 5, 5, 5, 5, 5 };
        private int[] _skills = new int[13];
        private List<(PerkDef Def, int Rank)> _perks = new();
        private int _level = 1;
        private int _xp = 0;
        private int _xpToNext = 200;
        private int _carryWeight;
        private int _baseHealth;
        private int _actionPoints;

        public int Level => _level;
        public int XP => _xp;
        public int XPToNext => _xpToNext;
        public IReadOnlyList<(PerkDef Def, int Rank)> Perks => _perks.AsReadOnly();

        public int this[SpecialAttribute attr]
        {
            get => _special[(int)attr];
            set => SetSpecial(attr, Mathf.Clamp(value, 1, 10));
        }

        public int this[SkillName skill]
        {
            get => _skills[(int)skill];
            set => SetSkill(skill, Mathf.Clamp(value, 0, 100));
        }

        public int GetSkillValue(SkillName skill) => _skills[(int)skill];
        public int GetSpecialValue(SpecialAttribute attr) => _special[(int)attr];

        public int BaseHealth => 90 + _special[(int)SpecialAttribute.Endurance] * 20;
        public int ActionPoints => 65 + _special[(int)SpecialAttribute.Agility] / 2;
        public int CarryWeight => 50 + _special[(int)SpecialAttribute.Strength] * 10;
        public int MeleeDamage => _special[(int)SpecialAttribute.Strength] / 2 + 1;
        public float CritChance => _special[(int)SpecialAttribute.Luck] * 1.0f;
        public float PoisonResist => _special[(int)SpecialAttribute.Endurance] * 5f;
        public float RadResist => _special[(int)SpecialAttribute.Endurance] * 2f;

        public CharacterStats()
        {
            RecalculateSkills();
        }

        private void RecalculateSkills()
        {
            _skills[(int)SkillName.Barter] = _special[(int)SpecialAttribute.Charisma] * 2 + 10;
            _skills[(int)SkillName.BigGuns] = _special[(int)SpecialAttribute.Endurance] * 2 + 6;
            _skills[(int)SkillName.EnergyWeapons] = _special[(int)SpecialAttribute.Intelligence] * 2 + 8;
            _skills[(int)SkillName.Explosives] = _special[(int)SpecialAttribute.Perception] * 2 + 6;
            _skills[(int)SkillName.Lockpick] = _special[(int)SpecialAttribute.Perception] * 2 + 6;
            _skills[(int)SkillName.Medicine] = _special[(int)SpecialAttribute.Intelligence] * 2 + 8;
            _skills[(int)SkillName.MeleeWeapons] = _special[(int)SpecialAttribute.Strength] * 2 + 6;
            _skills[(int)SkillName.Repair] = _special[(int)SpecialAttribute.Intelligence] * 2 + 8;
            _skills[(int)SkillName.Science] = _special[(int)SpecialAttribute.Intelligence] * 2 + 10;
            _skills[(int)SkillName.SmallGuns] = _special[(int)SpecialAttribute.Agility] * 2 + 8;
            _skills[(int)SkillName.Sneak] = _special[(int)SpecialAttribute.Agility] * 2 + 6;
            _skills[(int)SkillName.Speech] = _special[(int)SpecialAttribute.Charisma] * 2 + 8;
            _skills[(int)SkillName.Unarmed] = _special[(int)SpecialAttribute.Strength] * 2 + 6;
        }

        public void SetSpecial(SpecialAttribute attr, int value)
        {
            _special[(int)attr] = Mathf.Clamp(value, 1, 10);
            RecalculateSkills();
        }

        public void SetSkill(SkillName skill, int value)
        {
            _skills[(int)skill] = Mathf.Clamp(value, 0, 100);
        }

        public void AddXP(int amount)
        {
            _xp += amount;
            if (_xp >= _xpToNext)
                LevelUp();
        }

        private void LevelUp()
        {
            _xp -= _xpToNext;
            _level++;
            _xpToNext = 200 + (_level - 1) * 150;
        }

        public bool TryAddPerk(PerkDef perk)
        {
            if (perk.MinLevel > _level) return false;
            foreach (var cond in perk.Conditions)
            {
                if (cond.RequiredAttribute.HasValue && _special[(int)cond.RequiredAttribute.Value] < cond.MinAttributeValue)
                    return false;
                if (cond.RequiredSkill.HasValue && _skills[(int)cond.RequiredSkill.Value] < cond.MinSkillValue)
                    return false;
                if (cond.RequiredLevel > _level) return false;
            }

            int idx = _perks.FindIndex(p => p.Def == perk);
            if (idx >= 0)
            {
                var (def, rank) = _perks[idx];
                if (!def.CanTakeMultiple && rank >= def.MaxRank) return false;
                if (rank >= def.MaxRank) return false;
                _perks[idx] = (def, rank + 1);
            }
            else
            {
                _perks.Add((perk, 1));
            }

            ApplyPerkEffects(perk);
            return true;
        }

        private void ApplyPerkEffects(PerkDef perk)
        {
            foreach (var effect in perk.Effects)
            {
                switch (effect.Type)
                {
                    case PerkEffectType.AddSkillBonus:
                        if (effect.TargetSkill.HasValue)
                            _skills[(int)effect.TargetSkill.Value] += (int)effect.Value;
                        break;
                    case PerkEffectType.AddAttributeBonus:
                        if (effect.TargetAttribute.HasValue)
                            _special[(int)effect.TargetAttribute.Value] += (int)effect.Value;
                        break;
                }
            }
        }

        public bool HasPerk(PerkDef perk) => _perks.Exists(p => p.Def == perk);

        public int GetPerkRank(PerkDef perk)
        {
            int idx = _perks.FindIndex(p => p.Def == perk);
            return idx >= 0 ? _perks[idx].Rank : 0;
        }
    }
}
