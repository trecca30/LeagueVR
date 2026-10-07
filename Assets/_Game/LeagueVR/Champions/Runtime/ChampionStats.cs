using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Live champion statistics. Base values come from the champion definition and level;
    /// bonuses are written by the item economy. Kits only read from here.
    /// </summary>
    public class ChampionStats
    {
        public const float MaxAttackSpeed = 2.5f;

        public float BaseAttackDamage { get; private set; } = 60;
        public float AttackDamage { get; set; } = 60;
        public float AbilityPower { get; set; }
        public float BaseAttackSpeed { get; private set; } = .65f;
        public float BonusAttackSpeed { get; set; }
        public float AttackReach { get; private set; } = 2.2f;
        public float AbilityHaste { get; set; }
        public float CriticalChance { get; set; }
        public float CriticalDamage { get; set; } = 1.75f;
        public float MoveSpeed { get; set; } = 340;

        public float BonusAttackDamage => Mathf.Max(0, AttackDamage - BaseAttackDamage);
        public float CooldownMultiplier => 100f / (100f + Mathf.Max(0, AbilityHaste));

        /// <summary>League's per-level growth curve: base + growth * (n - 1) * (0.7025 + 0.0175 * (n - 1)).</summary>
        public static float Grow(float baseValue, float growth, int level)
        {
            int n = Mathf.Clamp(level, 1, 18) - 1;
            return baseValue + growth * n * (.7025f + .0175f * n);
        }

        /// <summary>Attacks per second including a kit's temporary bonus (for example Gwen's empowered attacks).</summary>
        public float AttacksPerSecond(float extraBonus = 0) => Mathf.Min(MaxAttackSpeed, BaseAttackSpeed * (1 + BonusAttackSpeed + extraBonus));

        public float AttackInterval(float extraBonus = 0) => 1 / Mathf.Max(.1f, AttacksPerSecond(extraBonus));

        /// <summary>Resets to level-1 values with no items.</summary>
        public void Reset(ChampionDefinition definition)
        {
            if (!definition)
                return;
            BaseAttackDamage = AttackDamage = definition.attackDamage;
            BaseAttackSpeed = Mathf.Max(.1f, definition.attackSpeed);
            AttackReach = definition.attackReach;
            MoveSpeed = definition.moveSpeed;
            AbilityPower = BonusAttackSpeed = AbilityHaste = CriticalChance = 0;
            CriticalDamage = 1.75f;
        }

        /// <summary>Applies level growth to the base attack damage (bonus damage is preserved).</summary>
        public void SetLevelBase(ChampionDefinition definition, int level)
        {
            if (!definition)
                return;
            BaseAttackDamage = Grow(definition.attackDamage, definition.attackGrowth, level);
        }
    }
}
