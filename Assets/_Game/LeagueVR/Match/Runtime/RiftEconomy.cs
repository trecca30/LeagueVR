using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LeagueVR.Match
{
    [Serializable]
    public class InventorySlot
    {
        public int id, count = 1, charges;
        public InventorySlot(int id) { this.id = id; charges = id == 2031 ? 2 : 0; }
    }

    public class RiftEconomy : MonoBehaviour, IDamageGuard
    {
        public RiftMatch match;
        public List<InventorySlot> inventory = new();
        public int Gold = 500, Level = 1, Trinket = 3340;
        public float Experience, AbilityPower, CriticalChance, CriticalDamage, Lifesteal, Omnivamp, MoveSpeedBonus, Mana, MaxMana, MagicPen, MagicPenPercent, ArmorPen, Lethality, Tenacity, HealShieldPower, AbilityHaste;
        public float CombatGold, SupportGold;
        public int Version { get; private set; }
        public RiftItemEffects Effects { get; private set; }
        public bool Stasis => Effects && Effects.Stasis;
        public float BonusResistance => 0;
        public bool Blocks(DamageHit hit) => Stasis;
        public event Action Changed;
        GwenTuning original, basis;
        float baseHealth, baseArmor, baseMR, healthRegen, manaRegen, incomeFraction, baronUntil;
        int dragons;
        bool initialized;

        void Start()
        {
            original = match.player.tuning;
            basis = Instantiate(original);
            match.player.tuning = Instantiate(original);
            baseHealth = match.player.Health.maxHealth;
            baseArmor = match.player.Health.armor;
            baseMR = match.player.Health.magicResistance;
            Effects = GetComponent<RiftItemEffects>();
            if (!Effects) Effects = gameObject.AddComponent<RiftItemEffects>();
            Effects.Initialize(this);
            if (!GetComponent<RiftItemRack>()) gameObject.AddComponent<RiftItemRack>().Initialize(this);
            initialized = true;
            var roster=GetComponent<LeagueVR.Champions.ChampionRoster>();if(roster&&roster.Active)SetChampionBase(roster.Active);
            match.player.Health.RefreshGuards();
            Recalculate();
            Mana = MaxMana;
        }
        void OnDestroy()
        {
            if (match && match.player && original) { Destroy(match.player.tuning); match.player.tuning = original; }
            if (basis) Destroy(basis);
        }
        public LeagueVR.Champions.ChampionDefinition ChampionBase {get;private set;}
        public void SetChampionBase(LeagueVR.Champions.ChampionDefinition d)
        {
            ChampionBase=d;if(!initialized)return;
            baseHealth=d.health;baseArmor=d.armor;baseMR=d.magicResist;
            if(basis)Destroy(basis);basis=Instantiate(original);
            if(d.id!=LeagueVR.Champions.ChampionId.Gwen){basis.attackDamage=d.attackDamage;basis.attackInterval=1/d.attackSpeed;basis.attackReach=d.attackReach;}
            Recalculate();Mana=MaxMana;
        }
        public bool Owns(int id) => inventory.Any(s => s.id == id);
        public LeagueItem[] OwnedItems => inventory.Select(s => match.catalog.Find(s.id)).Where(i => i != null).ToArray();
        public void Touch() { Version++; Changed?.Invoke(); }
        public void ResetMatch()
        {
            Gold = match.rules.startingGold; Level = 1; Experience = CombatGold = SupportGold = 0; Trinket = 3340;
            inventory.Clear(); baronUntil = 0; dragons = 0; incomeFraction = 0;
            if (Effects) Effects.ResetEffects();
            Recalculate(); Mana = MaxMana; Touch();
        }
        public void AddGold(int amount, bool combat = false)
        {
            Gold += amount;
            if (combat) { CombatGold += amount; if (Owns(3865) || Owns(3866)) { SupportGold += amount; AdvanceSupport(); } }
            Touch();
        }
        void AdvanceSupport()
        {
            int from = Owns(3866) && SupportGold >= 1000 ? 3866 : Owns(3865) && SupportGold >= 500 ? 3865 : 0;
            if (from != 0) { TransformItem(from, from == 3865 ? 3866 : 3867); AdvanceSupport(); }
        }
        public void AddExperience(float amount)
        {
            Experience += amount;
            while (Level < 18 && Experience >= 180 + 100 * Level)
            {
                Experience -= 180 + 100 * Level; Level++; Recalculate(); match.Notify("Level " + Level);
            }
        }
        public int Cost(LeagueItem item, out List<int> consumed)
        {
            int[] pool = inventory.Select(s => s.count).ToArray();
            var used = new List<int>();
            int credit = 0;
            void ConsumeRecipe(int id, int depth)
            {
                if (depth > 12) return;
                int index = -1;
                for (int n = 0; n < inventory.Count; n++) if (inventory[n].id == id && pool[n] > 0) { index = n; break; }
                if (index >= 0) { pool[index]--; used.Add(index); credit += match.catalog.Find(id).price; return; }
                var part = match.catalog.Find(id);
                if (part?.recipe != null) foreach (int child in part.recipe) ConsumeRecipe(child, depth + 1);
            }
            if (item.recipe != null) foreach (int id in item.recipe) ConsumeRecipe(id, 0);
            if (item.active == ItemActive.SupportWard && Owns(3867)) { int n = inventory.FindIndex(s => s.id == 3867); used.Add(n); credit += match.catalog.Find(3867).price; }
            consumed = used;
            return Mathf.Max(0, item.price - credit);
        }
        public string CannotBuy(LeagueItem item)
        {
            if (item == null) return "Item unavailable.";
            if(item.id==2003&&Owns(2031)||item.id==2031&&Owns(2003))return "Choose Health Potion or Refillable Potion.";
            if (!match.Running || !match.player.Health.IsAlive || Stasis) return "You cannot shop right now.";
            if (!match.AtShop) return "Return to your own fountain.";
            if (!item.showInShop) return "This item transforms automatically.";
            if (!string.IsNullOrEmpty(item.requiredChampion) && !string.Equals(item.requiredChampion, "Gwen", StringComparison.OrdinalIgnoreCase)) return "Requires " + item.requiredChampion + ".";
            if ((item.id == 3363 || item.id == 3364) && Level < 9 && item.id == 3363) return "Unlocks at level 9.";
            if (item.active == ItemActive.SupportWard && !Owns(3867)) return "Complete the World Atlas quest first.";
            if (item.questUpgrade && item.HasTag("Boots") && CombatGold < 1500) return "Complete your boots quest: " + (int)CombatGold + " / 1500 combat gold.";
            if ((item.id == 2138 || item.id == 2139 || item.id == 2140) && Level < 9) return "Elixirs unlock at level 9.";
            int price = Cost(item, out var used);
            if (Gold < price) return "Need " + (price - Gold) + " more gold.";
            if (item.HasTag("Trinket")) return null;
            bool stack = inventory.Any(s => s.id == item.id && s.count < item.stackLimit);
            int removed = used.GroupBy(i => i).Count(g => g.Count() >= inventory[g.Key].count);
            if (!stack && inventory.Count - removed >= 6 && item.active is not (ItemActive.ElixirIron or ItemActive.ElixirSorcery or ItemActive.ElixirWrath)) return "Inventory full: sell an item or build from components.";
            if (item.stackLimit > 1 && Owns(item.id) && !stack) return "Stack limit reached.";
            bool legendary = item.price >= 1800 && !(item.recipe?.Length == 0 && item.tags.Contains("Lane"));
            if (legendary && Owns(item.id)) return "You already own this item.";
            string group = UniqueGroup(item);
            if (group != null && inventory.Select((s, i) => (s, i)).Any(x => !used.Contains(x.i) && UniqueGroup(match.catalog.Find(x.s.id)) == group)) return "Only one item from the " + group + " group.";
            return null;
        }
        static string UniqueGroup(LeagueItem item)
        {
            if (item == null) return null;
            if (item.HasTag("Boots")) return "Boots";
            if (new[] { 3077, 3074, 3748, 6631, 6698 }.Contains(item.id)) return "Hydra";
            if (new[] { 3155, 3156, 3053, 6673 }.Contains(item.id)) return "Lifeline";
            if (new[] { 3003, 3004, 3119, 3040, 3042, 3121, 3070 }.Contains(item.id)) return "Mana Charge";
            if (new[] { 3865, 3866, 3867, 3869, 3870, 3871, 3876, 3877 }.Contains(item.id)) return "Support Quest";
            if (new[] { 3033, 3036, 6694, 3302 }.Contains(item.id)) return "Last Whisper / Terminus";
            if (new[] { 1101, 1102, 1103 }.Contains(item.id)) return "Jungle Companion";
            return null;
        }
        public bool Buy(int id)
        {
            var item = match.catalog.Find(id);
            string reason = CannotBuy(item);
            if (reason != null) { match.Notify(reason); return false; }
            int cost = Cost(item, out var used);
            Gold -= cost;
            if(inventory.Count>=6 && item.active is ItemActive.ElixirIron or ItemActive.ElixirSorcery or ItemActive.ElixirWrath){ inventory.Add(new InventorySlot(id)); Effects.Activate(id,match.player.AttackOrigin,match.player.AttackDirection);return true; }
            foreach (var group in used.GroupBy(i => i).OrderByDescending(g => g.Key))
            {
                var slot = inventory[group.Key]; slot.count -= group.Count();
                if (slot.count <= 0) inventory.RemoveAt(group.Key);
            }
            if (item.HasTag("Trinket")) { Trinket = id; Effects?.ResetTrinket(id); }
            else
            {
                var slot = inventory.FirstOrDefault(s => s.id == id && s.count < item.stackLimit);
                if (slot != null) slot.count++; else inventory.Add(new InventorySlot(id));
            }
            Recalculate(); Touch(); match.Notify("Purchased " + item.name + " for " + cost + "g");
            return true;
        }
        public bool Sell(int index)
        {
            if (!match.AtShop || Stasis || index < 0 || index >= inventory.Count) return false;
            var slot = inventory[index]; var item = match.catalog.Find(slot.id);
            if (item.sell <= 0) { match.Notify("This item cannot be sold."); return false; }
            Gold += item.sell;
            if (--slot.count <= 0) inventory.RemoveAt(index);
            Recalculate(); Touch(); match.Notify("Sold " + item.name); return true;
        }
        public bool Consume(int id)
        {
            int index = inventory.FindIndex(s => s.id == id);
            if (index < 0) return false;
            if (--inventory[index].count <= 0) inventory.RemoveAt(index);
            Recalculate(); Touch(); return true;
        }
        public void TransformItem(int from, int to)
        {
            var slot = inventory.FirstOrDefault(s => s.id == from);
            if (slot == null || match.catalog.Find(to) == null) return;
            slot.id = to; slot.charges = 0; Recalculate(); Touch(); match.Notify(match.catalog.Find(to).name + " unlocked");
        }
        public bool TrySpendMana(float amount)
        {
            amount *= Effects && Effects.EmpoweredMana ? 2 : 1;
            if(ChampionBase&&!ChampionBase.UsesMana)return true;
            if (Mana < amount) { match.Notify("Not enough mana."); return false; }
            Mana -= amount;if(Owns(6657))match.player.Health.Heal(amount*.25f); return true;
        }
        public void RestoreMana(float amount) => Mana = Mathf.Clamp(Mana + amount, 0, MaxMana);
        public void Recalculate()
        {
            if (!initialized) return;
            var items = OwnedItems.Where(i => !i.consumable).ToArray();
            var health = match.player.Health; var tuning = match.player.tuning;
            float hp = (Effects ? Effects.TimelessStacks*10 : 0) + items.Sum(i => i.health) + (Effects ? Effects.PermanentHealth : 0), aspeed = items.Sum(i => i.attackSpeed), oldMana = MaxMana;
            AbilityHaste = items.Sum(i => i.abilityHaste) + (Effects ? Effects.FamineHaste : 0);
            AbilityPower = (Effects ? Effects.TimelessStacks*3 : 0) + items.Sum(i => i.abilityPower) + health.SpellPowerBonus + (Effects ? Effects.BonusAP : 0);
            if (Owns(4633)) AbilityPower += hp * .02f;
            if (Owns(3003) || Owns(3040)) AbilityPower += (330 + 40 * (Level - 1) + items.Sum(i => i.mana)) * .01f;
            if (Owns(3089)) AbilityPower *= 1.3f;
            if (Time.time < baronUntil) AbilityPower += 40;
            CriticalChance = Mathf.Clamp01(items.Sum(i => i.criticalChance) + (Effects ? Effects.PracticeCrit : 0));
            CriticalDamage = 1.75f + items.Sum(i => i.criticalDamage);
            Lifesteal = items.Sum(i => i.lifesteal); Omnivamp = items.Sum(i => i.omnivamp) + (Effects ? Effects.BonusVamp + Effects.ExtraOmnivamp : 0);
            Tenacity = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.tenacity));
            HealShieldPower = items.Sum(i => i.healShieldPower);
            if(Owns(6621)){HealShieldPower+=items.Sum(i=>i.manaRegen)*.02f;AbilityPower+=items.Sum(i=>i.manaRegen)*10;}
            MagicPen = items.Sum(i => i.magicPen); MagicPenPercent = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.magicPenPercent));
            ArmorPen = 1 - items.Aggregate(1f, (v, i) => v * (1 - i.armorPen)); Lethality = items.Sum(i => i.lethality);
            float moveFlat = items.Sum(i => i.moveSpeed), movePercent = items.Sum(i => i.movePercent) + (Effects ? Effects.BonusMove : 0);
            float baseMove=ChampionBase?ChampionBase.moveSpeed:340;MoveSpeedBonus = ((baseMove + moveFlat) * (1 + movePercent) / 340) - 1;
            MaxMana = (ChampionBase?ChampionBase.mana:330) + (ChampionBase?ChampionBase.manaGrowth:40) * (Level - 1) + (Effects ? Effects.TimelessStacks*30 : 0) + items.Sum(i => i.mana) + (Effects ? Effects.ManaCharge : 0);
            if(ChampionBase&&!ChampionBase.UsesMana)MaxMana=0;
            Mana = Mathf.Clamp(Mana + Mathf.Max(0, MaxMana - oldMana), 0, MaxMana);
            health.SetMaximumHealth(baseHealth + (ChampionBase?ChampionBase.healthGrowth:109) * (Level - 1) + hp + (Effects ? Effects.BonusHP : 0), true);
            health.armor = baseArmor + (ChampionBase?ChampionBase.armorGrowth:4.7f) * (Level - 1) + items.Sum(i => i.armor) + (Effects ? Effects.BonusArmor : 0);
            health.magicResistance = baseMR + (ChampionBase?ChampionBase.magicResistGrowth:2.05f) * (Level - 1) + items.Sum(i => i.magicResistance) + (Effects ? Effects.BonusMR : 0);
            healthRegen = (ChampionBase?ChampionBase.healthRegen/5:1.7f) * (1 + items.Sum(i => i.healthRegen)) + items.Sum(i => i.flatHealthRegen) / 5;
            manaRegen = (ChampionBase?ChampionBase.manaRegen/5:1.5f) * (1 + items.Sum(i => i.manaRegen)) + items.Sum(i => i.flatManaRegen) / 5;
            tuning.attackDamage = basis.attackDamage + (ChampionBase?ChampionBase.attackGrowth:3) * (Level - 1) + items.Sum(i => i.attackDamage) + (Effects ? Effects.BonusAD + Effects.ExtraAttackDamage : 0);
            if (Owns(2501)) tuning.attackDamage += (health.maxHealth - baseHealth - 109 * (Level - 1)) * .02f;
            if (Owns(3004) || Owns(3042)) tuning.attackDamage += MaxMana * .025f;
            tuning.attackInterval = basis.attackInterval / Mathf.Max(.1f, 1 + aspeed + (ChampionBase?ChampionBase.attackSpeedGrowth/100:.0225f) * (Level - 1) + (Effects ? Effects.BonusAS : 0));
            tuning.passiveMaxHealthFraction = .01f + AbilityPower * .00006f;
            tuning.qSnipDamage = basis.qSnipDamage + AbilityPower * .05f;
            tuning.qFinalDamage = basis.qFinalDamage + AbilityPower * .35f;
            tuning.eBonusDamage = basis.eBonusDamage + AbilityPower * .15f;
            tuning.rDamage = basis.rDamage + AbilityPower * .1f; tuning.wResistance = basis.wResistance + AbilityPower * .05f;
            if(ChampionBase&&ChampionBase.id==LeagueVR.Champions.ChampionId.Gwen)
            {tuning.eBonusDamage=basis.eBonusDamage+AbilityPower*.2f;tuning.wResistance=basis.wResistance+AbilityPower*.07f;}
            float cooldown = 100 / (100 + AbilityHaste);
            tuning.qCooldown = basis.qCooldown * cooldown; tuning.wCooldown = basis.wCooldown * cooldown;
            tuning.eCooldown = basis.eCooldown * cooldown; tuning.rCooldown = basis.rCooldown * cooldown;
            Touch();
        }
        void Update()
        {
            if (!match.Running || !match.player.Health.IsAlive) return;
            match.player.Health.Heal(healthRegen * Time.deltaTime);
            RestoreMana((match.AtShop ? MaxMana * .1f : manaRegen) * Time.deltaTime);
            float gold10 = OwnedItems.Sum(i => i.goldPer10);
            incomeFraction += gold10 * Time.deltaTime / 10;
            if (incomeFraction >= 1) { int gain = (int)incomeFraction; incomeFraction -= gain; Gold += gain; Touch(); }
            var refill = inventory.FirstOrDefault(s => s.id == 2031);
            if (match.AtShop && refill != null && refill.charges < 2) { refill.charges = 2; Touch(); }
            Effects?.RefillWards();
            if (baronUntil > 0 && Time.time >= baronUntil) { baronUntil = 0; Recalculate(); }
        }
        public bool LastAttackCritical {get;private set;}
        public float AttackDamage(float amount, Combatant target)
        {
            LastAttackCritical=false;
            if (target.GetComponent<RiftStructure>()) return (amount + AbilityPower * .6f) * 1.2f;
            LastAttackCritical=UnityEngine.Random.value<CriticalChance;
            return LastAttackCritical?amount*CriticalDamage:amount;
        }
        public void OnAttack(Combatant target, float dealt) => Effects?.OnAttack(target, dealt);
        public bool Use(int id) => Effects && Effects.Activate(id, match.player.AttackOrigin, match.player.AttackDirection);
        public void ObjectiveReward(string objective)
        {
            if (objective.Contains("Baron")) { baronUntil = Time.time + 180; Recalculate(); }
            else if (objective.Contains("Dragon")) { dragons++; AddGold(100, true); match.Notify("Dragon secured"); }
            else if (objective.Contains("Herald")) { AddGold(200, true); match.Notify("Herald secured"); }
        }
    }
}
