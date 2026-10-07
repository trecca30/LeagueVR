using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LeagueVR.Match
{
    public enum ItemActive { None, Potion, Refillable, Stasis, SingleStasis, Ward, Farsight, Oracle, ElixirIron, ElixirSorcery, ElixirWrath, Shurelya, Actualizer, Hydra, Ravenous, Titanic, Stridebreaker, Profane, Redemption, Vow, Quicksilver, Mercurial, Ghostblade, Randuin, Gunblade, Rocketbelt, Locket, Mikael, SupportWard, BlackSpear, Effigy }
    [Serializable]
    public class LeagueItem
    {
        public int id, price, sell, stackLimit = 1;
        public string name, description, requiredChampion, statText, effectNotes;
        public int[] recipe;
        public string[] tags;
        public float health, attackDamage, abilityPower, armor, magicResistance, attackSpeed, criticalChance, criticalDamage, moveSpeed, movePercent, lifesteal, omnivamp, abilityHaste, mana, healthRegen, manaRegen, flatHealthRegen, flatManaRegen, magicPen, magicPenPercent, armorPen, lethality, tenacity, healShieldPower, goldPer10;
        public bool consumable, showInShop = true, questUpgrade;
        public ItemActive active;
        public float activeCooldown;
        public Texture2D icon;
        public GameObject physicalPrefab;
        public bool HasTag(string tag) => tags != null && tags.Contains(tag);
    }
    [CreateAssetMenu(menuName = "League VR/Item Catalog")]
    public class LeagueCatalog : ScriptableObject
    {
        public string patch, researchedOn, source;
        public LeagueItem[] items;
        Dictionary<int, LeagueItem> index;
        public LeagueItem Find(int id)
        {
            if (index == null || index.Count != items.Length) index = items.ToDictionary(i => i.id);
            return index.TryGetValue(id, out var item) ? item : null;
        }
        public void Refresh() => index = null;
    }
    [Serializable]
    public class CatalogImport
    {
        public string patch, researchedOn, source;
        public LeagueItem[] items;
    }
}