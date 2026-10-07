using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// One champion's passive and QWER abilities. Kits are plain objects owned by <see cref="PlayerChampion"/>:
    /// the player component handles input gating, cooldowns, mana and attack timing, the kit decides what happens.
    /// Add a champion by subclassing this and registering it in <see cref="ChampionKits"/>.
    /// </summary>
    public abstract class ChampionKit
    {
        public PlayerChampion Player { get; private set; }
        public ChampionDefinition Definition { get; private set; }

        protected Combatant Health => Player.Health;
        protected ChampionStats Stats => Player.Stats;
        protected float AD => Stats.AttackDamage;
        protected float BonusAD => Stats.BonusAttackDamage;
        protected float AP => Stats.AbilityPower;
        protected Color Tint => Definition ? Definition.color : Color.white;

        internal void Bind(PlayerChampion player, ChampionDefinition definition)
        {
            Player = player;
            Definition = definition;
        }

        /// <summary>Current rank (1-5, ultimate 1-3) of an ability; ranks follow champion level in this prototype.</summary>
        protected int Rank(int slot) => Definition ? Definition.Rank(slot, Player.Level) : 1;

        protected float ByRank(int slot, params float[] values) => values[Mathf.Clamp(Rank(slot) - 1, 0, values.Length - 1)];

        public virtual void OnEquip() { }
        public virtual void OnUnequip() { }
        /// <summary>Called every frame while the champion is alive.</summary>
        public virtual void Tick() { }
        public virtual void OnDeath() { }
        public virtual void OnRespawn() { }
        public virtual void OnUnitDefeated(Combatant victim, DamageHit hit) { }
        /// <summary>Called after any damage the player deals through <see cref="PlayerChampion.Hit"/>.</summary>
        public virtual void OnDamageDealt(Combatant target, float dealt, string ability, bool basic) { }

        /// <summary>Resolves a basic attack. The attack timer has already been started.</summary>
        public abstract void BasicAttack(Vector3 origin, Vector3 direction);

        /// <summary>Casts Q, W, E or R (slot 0-3). Call <see cref="PlayerChampion.Commit"/> once the cast is valid.</summary>
        public abstract bool Cast(int slot);

        /// <summary>True while a recast window lets the slot be used again without waiting for its cooldown.</summary>
        public virtual bool CanRecast(int slot) => false;

        /// <summary>True while an action occupies the champion: basic attacks wait until it ends.</summary>
        public virtual bool Busy => false;
        /// <summary>True while every ability is locked (channels, leaps). Defaults to false so abilities can be woven together.</summary>
        public virtual bool BlocksCasts => false;
        public virtual float BonusAttackSpeed => 0;
        public virtual float BonusAttackRange => 0;
        public virtual bool Blocks(DamageHit hit) => false;
        public virtual float BonusResistance => 0;
        public virtual bool HiddenFrom(Combatant attacker) => false;
        /// <summary>Optional HUD text replacing the cooldown of a slot, e.g. "RECAST 2".</summary>
        public virtual string SlotStatus(int slot) => null;
        /// <summary>One short line describing the champion's current state for the wrist display.</summary>
        public virtual string StateText => "";
    }
}
