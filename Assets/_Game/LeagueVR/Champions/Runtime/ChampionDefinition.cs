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
        public bool UsesMana => mana > 0;

        public int Rank(int slot, int level) => slot == 3 ? Mathf.Clamp(1 + (level - 1) / 6, 1, 3) : Mathf.Clamp(1 + (level - 1) / 4, 1, 5);
    }
}
