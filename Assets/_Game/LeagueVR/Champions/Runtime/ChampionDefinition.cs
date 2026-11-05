using System;
using UnityEngine;

namespace LeagueVR.Champions
{
    public enum ChampionId
    {
        // Append only: champion assets store these as numbers.
        Gwen, Zoe, Aatrox, Akshan, Brand, Pantheon, Yunara, Zeri, Gragas, Hwei
    }

    [Serializable]
    public class ChampionSpell
    {
        public string name, vrDescription;
        public Texture2D icon;
        public float[] cooldowns, costs;

        public float Cooldown(int rank) => cooldowns != null && cooldowns.Length > 0 ? cooldowns[Mathf.Clamp(rank - 1, 0, cooldowns.Length - 1)] : 0;

        public float Cost(int rank) => costs != null && costs.Length > 0 ? costs[Mathf.Clamp(rank - 1, 0, costs.Length - 1)] : 0;
    }

    /// <summary>Data for one playable champion: League base stats and growth, spell data, art and the VR first-person rig.</summary>
    [CreateAssetMenu(menuName = "League VR/Champion")]
    public class ChampionDefinition : ScriptableObject
    {
        public ChampionId id;
        public string title, role, passiveName, passiveDescription, patch;
        public Texture2D portrait, passiveIcon;
        public ChampionSpell[] spells;
        public GameObject model, firstPerson;
        public AnimationClip idle;
        public Color color;
        public float health, healthGrowth, armor, armorGrowth, magicResist, magicResistGrowth, mana, manaGrowth, attackDamage, attackGrowth, attackSpeed, attackSpeedGrowth, moveSpeed, healthRegen, manaRegen, healthRegenGrowth, manaRegenGrowth;
        public float attackReach = 9, bodyHeight = 1.75f;

        [Header("Kit assets (optional, kit specific)")]
        [Tooltip("Ability values the kit reads, e.g. GwenTuning.")]
        public ScriptableObject kitTuning;
        [Tooltip("Projectile visual, e.g. Gwen's needle.")]
        public GameObject kitPrefab;
        [Tooltip("Material for kit-specific effects, e.g. Hallowed Mist threads.")]
        public Material kitMaterial;

        public bool UsesMana => mana > 0;

        [Tooltip("League's usual order for maxing the basic abilities, first to last (for example QEW).")]
        public string skillOrder = "QEW";

        // League's standard skill path (Q W E Q Q R Q E Q E R E E W W R W W): the first ability maxed ranks up at
        // 4, 5, 7 and 9, the second at 8, 10, 12 and 13, the last at 14, 15, 17 and 18.
        static readonly int[][] RankUps = { new[] { 4, 5, 7, 9 }, new[] { 8, 10, 12, 13 }, new[] { 14, 15, 17, 18 } };

        /// <summary>
        /// Ability rank from champion level (no skill points in this game), along League's standard skill path for the
        /// champion's <see cref="skillOrder"/>. In VR all three basic abilities start at rank 1. The ultimate unlocks at
        /// level 6 (rank 0 before) and ranks up at 11 and 16.
        /// </summary>
        public int Rank(int slot, int level)
        {
            if (slot == 3)
                return level >= 16 ? 3 : level >= 11 ? 2 : level >= 6 ? 1 : 0;
            int order = string.IsNullOrEmpty(skillOrder) ? -1 : skillOrder.IndexOf("QWE"[Mathf.Clamp(slot, 0, 2)]);
            if (order < 0)
                order = Mathf.Clamp(slot, 0, 2);
            int rank = 1;
            foreach (int at in RankUps[order])
                if (level >= at)
                    rank++;
            return rank;
        }
    }
}
