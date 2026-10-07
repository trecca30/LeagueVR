using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LeagueVR.Champions;

namespace LeagueVR.Match
{
    [Serializable]
    public class InventorySlot
    {
        public int id, count = 1, charges;

        public InventorySlot(int id)
        {
            this.id = id;
            charges = id == 2031 ? 2 : 0;
        }
    }

    /// <summary>
    /// Gold, experience, inventory and every champion stat derived from level and items.
    /// Writes totals into the champion's <see cref="ChampionStats"/> and health component.
    /// </summary>
    public class RiftEconomy : MonoBehaviour, IDamageGuard, ITargetFilter
    {
        public RiftMatch match;
        public List<InventorySlot> inventory = new();
        public int Gold = 500, Level = 1, Trinket = 3340;
        public float Experience, AbilityPower, CriticalChance, CriticalDamage, Lifesteal, Omnivamp, MoveSpeedBonus, Mana, MaxMana, MagicPen, MagicPenPercent, ArmorPen, Lethality, Tenacity, HealShieldPower, AbilityHaste;
        public float CombatGold, SupportGold;
        public int Version { get; private set; }
        public RiftItemEffects Effects { get; private set; }
        public RiftItemRack Rack { get; private set; }
        public ChampionDefinition ChampionBase { get; private set; }
        public bool Stasis => Effects && Effects.Stasis;
        public float BonusResistance => 0;

        public bool Blocks(DamageHit hit) => Stasis;

        /// <summary>Stasis (Zhonya's) makes the champion untargetable by everyone.</summary>
        public bool HiddenFrom(Combatant attacker) => Stasis;

        public event Action Changed;
        float healthRegen, manaRegen, goldPerTen, incomeFraction, baronUntil;
        int dragons;
        bool initialized;
        PlayerChampion Player => match.player;

        void Start()
        {
            Effects = GetComponent<RiftItemEffects>();
            if (!Effects)
                Effects = gameObject.AddComponent<RiftItemEffects>();
            Effects.Initialize(this);
            Rack = GetComponent<RiftItemRack>();
            if (!Rack)
            {
                Rack = gameObject.AddComponent<RiftItemRack>();
                Rack.Initialize(this);
            }
            initialized = true;
            var roster = GetComponent<ChampionRoster>();
            if (roster && roster.Active)
                SetChampionBase(roster.Active);
            Player.Health.RefreshGuards();
            Recalculate();
            Mana = MaxMana;
        }

        public void SetChampionBase(ChampionDefinition d)
        {
            ChampionBase = d;
            if (!initialized)
                return;
            Recalculate();
            Mana = MaxMana;
        }

        public bool Owns(int id) => inventory.Any(s => s.id == id);
        public LeagueItem[] OwnedItems => inventory.Select(s => match.catalog.Find(s.id)).Where(i => i != null).ToArray();

        public void Touch()
        {
            Version++;
            Changed?.Invoke();
        }

        public void ResetMatch()
        {
            Gold = match.rules.startingGold;
            Level = 1;
            Experience = CombatGold = SupportGold = 0;
            Trinket = 3340;
            inventory.Clear();
            baronUntil = 0;
            dragons = 0;
            incomeFraction = 0;
            if (Effects)
                Effects.ResetEffects();
            Recalculate();
            Mana = MaxMana;
            Touch();
        }

        public void AddGold(int amount, bool combat = false)
        {
            Gold += amount;
            if (combat)
            {
                CombatGold += amount;
                if (Owns(3865) || Owns(3866))
                {
                    SupportGold += amount;
                    AdvanceSupport();
                }
            }
            Touch();
        }

        void AdvanceSupport()
        {
            int from = Owns(3866) && SupportGold >= 1000 ? 3866 : Owns(3865) && SupportGold >= 500 ? 3865 : 0;
            if (from != 0)
            {
                TransformItem(from, from == 3865 ? 3866 : 3867);
                AdvanceSupport();
            }
        }

        public void AddExperience(float amount)
        {
            Experience += amount;
            while (Level < 18 && Experience >= 180 + 100 * Level)
            {
                Experience -= 180 + 100 * Level;
                Level++;
                Recalculate();
                match.Notify("Level " + Level);
            }
        }

        public int Cost(LeagueItem item, out List<int> consumed)
        {
            int[] pool = inventory.Select(s => s.count).ToArray();
            var used = new List<int>();
            int credit = 0;
            void ConsumeRecipe(int id, int depth)
            {
                if (depth > 12)
                    return;
                int index = -1;
                for (int n = 0; n < inventory.Count; n++)
                    if (inventory[n].id == id && pool[n] > 0)
                    {
                        index = n;
                        break;
                    }
                if (index >= 0)
                {
                    pool[index]--;
                    used.Add(index);
                    credit += match.catalog.Find(id).price;
                    return;
                }
                var part = match.catalog.Find(id);
                if (part?.recipe != null)
                    foreach (int child in part.recipe)
                        ConsumeRecipe(child, depth + 1);
            }
            if (item.recipe != null)
                foreach (int id in item.recipe)
                    ConsumeRecipe(id, 0);
            if (item.active == ItemActive.SupportWard && Owns(3867))
            {
                int n = inventory.FindIndex(s => s.id == 3867);
                used.Add(n);
                credit += match.catalog.Find(3867).price;
            }
            consumed = used;
            return Mathf.Max(0, item.price - credit);
        }

        public string CannotBuy(LeagueItem item)
        {
            if (item == null)
                return "Item unavailable.";
            if (item.id == 2003 && Owns(2031) || item.id == 2031 && Owns(2003))
                return "Choose Health Potion or Refillable Potion.";
            if (!match.Running || !match.player.Health.IsAlive || Stasis)
                return "You cannot shop right now.";
            if (!match.AtShop)
                return "Return to your own fountain.";
            if (!item.showInShop)
                return "This item transforms automatically.";
            if (!string.IsNullOrEmpty(item.requiredChampion) && !string.Equals(item.requiredChampion, Player.ChampionName, StringComparison.OrdinalIgnoreCase))
                return "Requires " + item.requiredChampion + ".";
            if ((item.id == 3363 || item.id == 3364) && Level < 9 && item.id == 3363)
                return "Unlocks at level 9.";
            if (item.active == ItemActive.SupportWard && !Owns(3867))
                return "Complete the World Atlas quest first.";
            if (item.questUpgrade && item.HasTag("Boots") && CombatGold < 1500)
                return "Complete your boots quest: " + (int)CombatGold + " / 1500 combat gold.";
            if ((item.id == 2138 || item.id == 2139 || item.id == 2140) && Level < 9)
                return "Elixirs unlock at level 9.";
            int price = Cost(item, out var used);
            if (Gold < price)
                return "Need " + (price - Gold) + " more gold.";
            if (item.HasTag("Trinket"))
                return null;
            bool stack = inventory.Any(s => s.id == item.id && s.count < item.stackLimit);
            int removed = used.GroupBy(i => i).Count(g => g.Count() >= inventory[g.Key].count);
            if (!stack && inventory.Count - removed >= 6 && item.active is not (ItemActive.ElixirIron or ItemActive.ElixirSorcery or ItemActive.ElixirWrath))
                return "Inventory full: sell an item or build from components.";
            if (item.stackLimit > 1 && Owns(item.id) && !stack)
                return "Stack limit reached.";
            bool legendary = item.price >= 1800 && !(item.recipe?.Length == 0 && item.tags.Contains("Lane"));
            if (legendary && Owns(item.id))
                return "You already own this item.";
            string group = UniqueGroup(item);
            if (group != null && inventory.Select((s, i) => (s, i)).Any(x => !used.Contains(x.i) && UniqueGroup(match.catalog.Find(x.s.id)) == group))
                return "Only one item from the " + group + " group.";
            return null;
        }

        static string UniqueGroup(LeagueItem item)
        {
            if (item == null)
                return null;
            if (item.HasTag("Boots"))
                return "Boots";
            if (new[] { 3077, 3074, 3748, 6631, 6698 }.Contains(item.id))
                return "Hydra";
            if (new[] { 3155, 3156, 3053, 6673 }.Contains(item.id))
                return "Lifeline";
            if (new[] { 3003, 3004, 3119, 3040, 3042, 3121, 3070 }.Contains(item.id))
                return "Mana Charge";
            if (new[] { 3865, 3866, 3867, 3869, 3870, 3871, 3876, 3877 }.Contains(item.id))
                return "Support Quest";
            if (new[] { 3033, 3036, 6694, 3302 }.Contains(item.id))
                return "Last Whisper / Terminus";
            if (new[] { 1101, 1102, 1103 }.Contains(item.id))
                return "Jungle Companion";
            return null;
        }

        public bool Buy(int id)
        {
            var item = match.catalog.Find(id);
            string reason = CannotBuy(item);
            if (reason != null)
            {
                match.Notify(reason);
                return false;
            }
            int cost = Cost(item, out var used);
            Gold -= cost;
            if (inventory.Count >= 6 && item.active is ItemActive.ElixirIron or ItemActive.ElixirSorcery or ItemActive.ElixirWrath)
            {
                inventory.Add(new InventorySlot(id));
                Effects.Activate(id, match.player.AttackOrigin, match.player.AttackDirection);
                return true;
            }
            foreach (var group in used.GroupBy(i => i).OrderByDescending(g => g.Key))
            {
                var slot = inventory[group.Key];
                slot.count -= group.Count();
                if (slot.count <= 0)
                    inventory.RemoveAt(group.Key);
            }
            if (item.HasTag("Trinket"))
            {
                Trinket = id;
                Effects?.ResetTrinket(id);
            }
            else
            {
                var slot = inventory.FirstOrDefault(s => s.id == id && s.count < item.stackLimit);
                if (slot != null)
                    slot.count++;
                else
                    inventory.Add(new InventorySlot(id));
            }
            Recalculate();
            Touch();
            match.Notify("Purchased " + item.name + " for " + cost + "g");
            return true;
        }

        public bool Sell(int index)
        {
            if (!match.AtShop || Stasis || index < 0 || index >= inventory.Count)
                return false;
            var slot = inventory[index];
            var item = match.catalog.Find(slot.id);
            if (item.sell <= 0)
            {
                match.Notify("This item cannot be sold.");
                return false;
            }
            Gold += item.sell;
            if (--slot.count <= 0)
                inventory.RemoveAt(index);
            Recalculate();
            Touch();
            match.Notify("Sold " + item.name);
            return true;
        }

        public bool Consume(int id)
        {
            int index = inventory.FindIndex(s => s.id == id);
            if (index < 0)
                return false;
            if (--inventory[index].count <= 0)
                inventory.RemoveAt(index);
            Recalculate();
            Touch();
            return true;
        }

        public void TransformItem(int from, int to)
        {
            var slot = inventory.FirstOrDefault(s => s.id == from);
            if (slot == null || match.catalog.Find(to) == null)
                return;
            slot.id = to;
            slot.charges = 0;
            Recalculate();
            Touch();
            match.Notify(match.catalog.Find(to).name + " unlocked");
        }

        public bool TrySpendMana(float amount)
        {
            amount *= Effects && Effects.EmpoweredMana ? 2 : 1;
            if (ChampionBase && !ChampionBase.UsesMana)
                return true;
            if (Mana < amount)
            {
                match.Notify("Not enough mana.");
                return false;
            }
            Mana -= amount;
            if (Owns(6657))
                Player.Health.Heal(amount * .25f);
            return true;
        }

        public void RestoreMana(float amount) => Mana = Mathf.Clamp(Mana + amount, 0, MaxMana);

        public void Recalculate()
        {
            if (!initialized || !ChampionBase)
                return;
            var d = ChampionBase;
            var player = Player;
            var stats = player.Stats;
            var health = player.Health;
            var items = OwnedItems.Where(i => !i.consumable).ToArray();
            float Grow(float baseValue, float growth) => ChampionStats.Grow(baseValue, growth, Level);

            float bonusHealth = (Effects ? Effects.TimelessStacks * 10 + Effects.PermanentHealth + Effects.BonusHP : 0) + items.Sum(i => i.health);
            float oldMana = MaxMana;
            AbilityHaste = items.Sum(i => i.abilityHaste) + (Effects ? Effects.FamineHaste : 0);
            AbilityPower = (Effects ? Effects.TimelessStacks * 3 + Effects.BonusAP : 0) + items.Sum(i => i.abilityPower) + health.SpellPowerBonus;
            if (Owns(4633))
                AbilityPower += bonusHealth * .02f;
            if (Owns(3003) || Owns(3040))
                AbilityPower += (Grow(d.mana, d.manaGrowth) + items.Sum(i => i.mana)) * .01f;
            if (Owns(6621))
                AbilityPower += items.Sum(i => i.manaRegen) * 10;
            if (Owns(3089))
                AbilityPower *= 1.3f;
            if (Time.time < baronUntil)
                AbilityPower += 40;
            CriticalChance = Mathf.Clamp01(items.Sum(i => i.criticalChance) + (Effects ? Effects.PracticeCrit : 0));
            CriticalDamage = 1.75f + items.Sum(i => i.criticalDamage);
            Lifesteal = items.Sum(i => i.lifesteal);
            Omnivamp = items.Sum(i => i.omnivamp) + (Effects ? Effects.BonusVamp + Effects.ExtraOmnivamp : 0);
            Tenacity = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.tenacity));
            HealShieldPower = items.Sum(i => i.healShieldPower) + (Owns(6621) ? items.Sum(i => i.manaRegen) * .02f : 0);
            MagicPen = items.Sum(i => i.magicPen);
            MagicPenPercent = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.magicPenPercent));
            // Kit penetration (Pantheon's Grand Starfall passive) stacks multiplicatively with items, like League.
            ArmorPen = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.armorPen)) * (1 - Mathf.Clamp01(player.Kit?.ArmorPenetration ?? 0));
            Lethality = items.Sum(i => i.lethality);

            // Movement: flat bonuses add to base speed before percentage bonuses; 340 is the reference speed of the XR rig.
            float moveFlat = items.Sum(i => i.moveSpeed), movePercent = items.Sum(i => i.movePercent) + (Effects ? Effects.BonusMove : 0);
            stats.MoveSpeed = (d.moveSpeed + moveFlat) * (1 + movePercent);
            MoveSpeedBonus = stats.MoveSpeed / 340 - 1;

            MaxMana = d.UsesMana ? Grow(d.mana, d.manaGrowth) + (Effects ? Effects.TimelessStacks * 30 + Effects.ManaCharge : 0) + items.Sum(i => i.mana) : 0;
            Mana = Mathf.Clamp(Mana + Mathf.Max(0, MaxMana - oldMana), 0, MaxMana);

            float baseHealth = Grow(d.health, d.healthGrowth);
            health.SetMaximumHealth(baseHealth + bonusHealth, true);
            health.armor = Grow(d.armor, d.armorGrowth) + items.Sum(i => i.armor) + (Effects ? Effects.BonusArmor : 0);
            health.magicResistance = Grow(d.magicResist, d.magicResistGrowth) + items.Sum(i => i.magicResistance) + (Effects ? Effects.BonusMR : 0);
            healthRegen = Grow(d.healthRegen, 0) / 5 * (1 + items.Sum(i => i.healthRegen)) + items.Sum(i => i.flatHealthRegen) / 5;
            manaRegen = Grow(d.manaRegen, 0) / 5 * (1 + items.Sum(i => i.manaRegen)) + items.Sum(i => i.flatManaRegen) / 5;
            goldPerTen = items.Sum(i => i.goldPer10);

            stats.SetLevelBase(d, Level);
            float attackDamage = stats.BaseAttackDamage + items.Sum(i => i.attackDamage) + (Effects ? Effects.BonusAD + Effects.ExtraAttackDamage : 0);
            if (Owns(2501))
                attackDamage += bonusHealth * .02f;
            if (Owns(3004) || Owns(3042))
                attackDamage += MaxMana * .025f;
            stats.AttackDamage = attackDamage;
            stats.AbilityPower = AbilityPower;
            stats.AbilityHaste = AbilityHaste;
            stats.CriticalChance = CriticalChance;
            stats.CriticalDamage = CriticalDamage;
            // Level growth of attack speed follows the same curve as every other stat (growth is a percentage).
            stats.BonusAttackSpeed = ChampionStats.Grow(0, d.attackSpeedGrowth / 100, Level) + items.Sum(i => i.attackSpeed) + (Effects ? Effects.BonusAS : 0);
            Touch();
        }

        void Update()
        {
            if (!match.Running || !Player.Health.IsAlive)
                return;
            Player.Health.Heal(healthRegen * Time.deltaTime);
            RestoreMana((match.AtShop ? MaxMana * .1f : manaRegen) * Time.deltaTime);
            incomeFraction += goldPerTen * Time.deltaTime / 10;
            if (incomeFraction >= 1)
            {
                int gain = (int)incomeFraction;
                incomeFraction -= gain;
                Gold += gain;
                Touch();
            }
            if (match.AtShop)
            {
                foreach (var slot in inventory)
                    if (slot.id == 2031 && slot.charges < 2)
                    {
                        slot.charges = 2;
                        Touch();
                    }
            }
            Effects?.RefillWards();
            if (baronUntil > 0 && Time.time >= baronUntil)
            {
                baronUntil = 0;
                Recalculate();
            }
        }

        public bool LastAttackCritical { get; private set; }

        /// <summary>Basic attack damage before mitigation, rolling a critical strike.</summary>
        public float AttackDamage(float amount, Combatant target)
        {
            LastAttackCritical = UnityEngine.Random.value < CriticalChance;
            return LastAttackCritical ? amount * CriticalDamage : amount;
        }

        public void OnAttack(Combatant target, float dealt) => Effects?.OnAttack(target, dealt);

        public bool Use(int id) => Effects && Effects.Activate(id, match.player.AttackOrigin, match.player.AttackDirection);

        public void ObjectiveReward(string objective)
        {
            if (objective.Contains("Baron"))
            {
                baronUntil = Time.time + 180;
                Recalculate();
            }
            else if (objective.Contains("Dragon"))
            {
                dragons++;
                AddGold(100, true);
                match.Notify("Dragon secured");
            }
            else if (objective.Contains("Herald"))
            {
                AddGold(200, true);
                match.Notify("Herald secured");
            }
        }
    }
}
