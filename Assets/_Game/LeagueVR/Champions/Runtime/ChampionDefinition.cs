using System;
using UnityEngine;

namespace LeagueVR.Champions
{
    public enum ChampionId
    {
        Gwen, Zoe, Aatrox, Akshan, Brand, Pantheon, Yunara
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
        public float health, healthGrowth, armor, armorGrowth, magicResist, magicResistGrowth, mana, manaGrowth, attackDamage, attackGrowth, attackSpeed, attackSpeedGrowth, moveSpeed, healthRegen, manaRegen;
        public float attackReach = 9, bodyHeight = 1.75f;

        [Header("Kit assets (optional, kit specific)")]
        [Tooltip("Ability values the kit reads, e.g. GwenTuning.")]
        public ScriptableObject kitTuning;
        [Tooltip("Projectile visual, e.g. Gwen's needle.")]
        public GameObject kitPrefab;
        [Tooltip("Material for kit-specific effects, e.g. Hallowed Mist threads.")]
        public Material kitMaterial;

        public bool UsesMana => mana > 0;

        /// <summary>
        /// Ability rank from champion level (no skill points in this prototype): basic abilities gain a rank every
        /// three levels and max at 13, the ultimate ranks up at 11 and 16 like League.
        /// </summary>
        public int Rank(int slot, int level) => slot == 3 ? (level >= 16 ? 3 : level >= 11 ? 2 : 1) : Mathf.Clamp(1 + (level - 1) / 3, 1, 5);
    }
}
