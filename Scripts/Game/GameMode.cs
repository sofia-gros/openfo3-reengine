using Godot;
using System;
using OpenFo3.World;

namespace OpenFo3.Game
{
    public partial class GameMode : Node
    {
        private CharacterStats _stats;
        private float _difficultyDamageMult = 1.0f;
        private float _difficultyPlayerDamageMult = 1.0f;
        private Random _rng = new();

        public CharacterStats Stats => _stats;

        public GameMode()
        {
            _stats = new CharacterStats();
            _stats.Name = "PlayerStats";
            AddChild(_stats);
        }

        public int CalculateDamage(int baseDamage, SkillName weaponSkill, float weaponCond, float rangeMult)
        {
            float skillBonus = 1.0f + (_stats[weaponSkill] / 100f) * 0.5f;
            float condPenalty = 0.5f + weaponCond * 0.5f;
            int dmg = (int)(baseDamage * skillBonus * condPenalty * rangeMult * _difficultyDamageMult);
            return Mathf.Max(dmg, 1);
        }

        public int ApplyDamageResistance(int damage, int armorDT, int armorDR)
        {
            int afterDT = Mathf.Max(damage - armorDT, 0);
            int afterDR = (int)(afterDT * (100f - armorDR) / 100f);
            return Mathf.Max(afterDR, 0);
        }

        public void OnKillEnemy(NpcAgent enemy)
        {
            int xpReward = CalculateXpReward(enemy);
            _stats.AddXP(xpReward);
        }

        public void OnHitEnemy(float damage)
        {
            _stats.AddXP((int)(damage * 0.5f));
        }

        private int CalculateXpReward(NpcAgent enemy)
        {
            return 25;
        }

        public float GetHitChance(float weaponSkill, float distance, float weaponCond, bool isVats)
        {
            float baseChance = weaponSkill * 0.5f;
            float distPenalty = Mathf.Max(0, distance - 50f) * 0.15f;
            float condBonus = weaponCond * 15f;
            float chance = baseChance - distPenalty + condBonus;
            if (isVats) chance *= 2f;
            return Mathf.Clamp(chance, 5f, 95f);
        }

        public bool RollCrit(float critChance, float weaponMult)
        {
            return _rng.NextDouble() * 100f < critChance * weaponMult;
        }
    }
}
