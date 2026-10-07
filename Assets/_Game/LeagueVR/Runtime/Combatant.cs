using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using LeagueVR.Match;

namespace LeagueVR
{
    public enum DamageKind
    {
        Physical, Magic, True
    }

    public struct DamageHit
    {
        public Combatant source;
        public Vector3 origin;
        public float amount;
        public DamageKind kind;
        public bool isBasicAttack, isItemEffect, isCritical;
        public string ability;

        public DamageHit(Combatant source, Vector3 origin, float amount, DamageKind kind)
        {
            this.source = source;
            this.origin = origin;
            this.amount = amount;
            this.kind = kind;
            isBasicAttack = isItemEffect = isCritical = false;
            ability = null;
        }
    }

    /// <summary>Blocks damage outright or adds temporary resistances (mist, shields of faith, stasis...).</summary>
    public interface IDamageGuard
    {
        bool Blocks(DamageHit hit);
        float BonusResistance { get; }
    }

    /// <summary>Hides its owner from attackers. <c>attacker</c> is null for general targetability checks.</summary>
    public interface ITargetFilter
    {
        bool HiddenFrom(Combatant attacker);
    }

    /// <summary>Health, mitigation and status effects for every unit that can fight: champions, minions, structures, monsters and wards.</summary>
    [DisallowMultipleComponent]
    public class Combatant : MonoBehaviour
    {
        public int team;
        public bool countsAsChampion = true;
        public float maxHealth = 650, armor = 30, magicResistance = 30;
        public Transform aimPoint;
        public UnityEvent onDeath = new(), onDamaged = new();
        public event Action<DamageHit, float> Damaged;
        public event Action HealthChanged;
        public static event Action<Combatant, DamageHit> Attacked;
        public static event Action<Combatant, DamageHit> Defeated;

        public float Health { get; private set; }
        public bool IsAlive => Health > 0;
        public Vector3 AimPosition => aimPoint ? aimPoint.position : transform.position + Vector3.up;

        /// <summary>Last time this unit damaged something, and what it hit. Minions and turrets use it for "call for help" aggro.</summary>
        public float LastHitTime { get; private set; } = -100;
        public Combatant LastHitTarget { get; private set; }

        float stunUntil, rootUntil, sleepUntil, sleepBonus;
        float slowUntil, slowMultiplier = 1, woundsUntil, wounds, speedUntil, speedBonus, attackSlowUntil, attackSlow;
        float attackBuffUntil, attackBuff, spellBuffUntil, spellBuff, onHitUntil, onHitBonus;
        float armorShredUntil, armorShred, magicShredUntil, magicShred;
        bool deathReported;
        IDamageGuard[] guards = Array.Empty<IDamageGuard>();
        ITargetFilter[] filters = Array.Empty<ITargetFilter>();
        RiftEconomy economy;
        bool isWard;

        class ShieldLayer
        {
            public float amount, rate, until;
            public DamageKind? type;
            public string key;
        }

        readonly List<ShieldLayer> shields = new();
        static readonly List<IDamageGuard> guardBuffer = new();
        static readonly List<ITargetFilter> filterBuffer = new();

        public bool Stunned => Time.time < stunUntil || Time.time < sleepUntil;
        public bool Rooted => Stunned || Time.time < rootUntil;
        public float SlowMultiplier => Rooted ? 0 : Time.time < slowUntil ? slowMultiplier : 1;
        public float SpeedMultiplier => Time.time < speedUntil ? 1 + speedBonus : 1;
        public float AttackIntervalMultiplier => (Time.time < attackSlowUntil ? 1 / (1 - attackSlow) : 1) / (1 + (Time.time < attackBuffUntil ? attackBuff : 0));
        public float SpellPowerBonus => Time.time < spellBuffUntil ? spellBuff : 0;
        public float OnHitBonus => Time.time < onHitUntil ? onHitBonus : 0;
        float Tenacity => economy ? economy.Tenacity : 0;

        public float Shield
        {
            get
            {
                float total = 0;
                foreach (var s in shields)
                    if (s.until > Time.time)
                        total += s.amount;
                return total;
            }
        }

        /// <summary>Alive and not hidden from everyone (stealth, stasis, unrevealed wards).</summary>
        public bool IsTargetable => IsAlive && !HiddenFrom(null);

        public bool IsTargetableBy(Combatant attacker) => IsAlive && !HiddenFrom(attacker);

        bool HiddenFrom(Combatant attacker)
        {
            foreach (var filter in filters)
                if (filter.HiddenFrom(attacker))
                    return true;
            return false;
        }

        void Awake()
        {
            RefreshGuards();
            ResetHealth();
        }

        void Update()
        {
            if (shields.Count == 0)
                return;
            float now = Time.time, dt = Time.deltaTime;
            for (int i = shields.Count - 1; i >= 0; i--)
            {
                var s = shields[i];
                s.amount = Mathf.Max(0, s.amount - s.rate * dt);
                if (s.amount <= 0 || now >= s.until)
                    shields.RemoveAt(i);
            }
        }

        /// <summary>Re-collects guards and target filters. Call after adding or removing such components.</summary>
        public void RefreshGuards()
        {
            economy = GetComponent<RiftEconomy>();
            isWard = GetComponent<RiftVisionWard>();
            guardBuffer.Clear();
            filterBuffer.Clear();
            foreach (var c in GetComponents<MonoBehaviour>())
            {
                if (c is IDamageGuard guard)
                    guardBuffer.Add(guard);
                if (c is ITargetFilter filter)
                    filterBuffer.Add(filter);
            }
            guards = guardBuffer.ToArray();
            filters = filterBuffer.ToArray();
        }

        public static float Mitigate(float damage, float resistance) => damage * (resistance >= 0 ? 100 / (100 + resistance) : 2 - 100 / (100 - resistance));

        public float TakeDamage(DamageHit hit)
        {
            if (!IsAlive || hit.amount <= 0 || (hit.source && (hit.source == this || hit.source.team == team)))
                return 0;
            Attacked?.Invoke(this, hit);
            if (Time.time < sleepUntil && hit.ability != "Passive")
            {
                sleepUntil = 0;
                hit.amount += sleepBonus;
                sleepBonus = 0;
            }
            float bonusResistance = 0;
            foreach (var guard in guards)
            {
                if (guard.Blocks(hit))
                    return 0;
                bonusResistance += guard.BonusResistance;
            }
            var sourceItems = hit.source ? hit.source.economy : null;
            float amount = sourceItems && sourceItems.Effects ? sourceItems.Effects.ModifyOutgoing(hit, this) : hit.amount;
            if (economy && economy.Effects)
                amount = economy.Effects.ModifyIncoming(hit, amount);
            if (isWard)
            {
                // Wards take exactly one point of damage per basic attack and ignore everything else.
                if (!hit.isBasicAttack)
                    return 0;
                amount = 1;
                hit.kind = DamageKind.True;
            }
            if (hit.kind != DamageKind.True)
            {
                bool physical = hit.kind == DamageKind.Physical;
                float resistance = physical
                    ? armor * (1 - (Time.time < armorShredUntil ? armorShred : 0))
                    : magicResistance * (1 - (Time.time < magicShredUntil ? magicShred : 0));
                resistance += bonusResistance;
                // Percentage penetration applies before flat penetration; penetration never takes resistance below zero.
                if (sourceItems && resistance > 0)
                    resistance = Mathf.Max(0, resistance * (1 - (physical ? sourceItems.ArmorPen : sourceItems.MagicPenPercent)) - (physical ? sourceItems.Lethality : sourceItems.MagicPen));
                amount = Mitigate(amount, resistance);
            }
            amount = RiftItemEffects.RedirectVowDamage(this, hit, amount);
            if (economy && economy.Effects)
                amount = economy.Effects.AfterMitigation(hit, amount);
            foreach (var shield in shields)
            {
                if (Time.time >= shield.until || shield.type.HasValue && shield.type.Value != hit.kind)
                    continue;
                float blocked = Mathf.Min(amount, shield.amount);
                amount -= blocked;
                shield.amount -= blocked;
                if (amount <= 0)
                    break;
            }
            float dealt = Mathf.Min(Health, amount);
            Health -= dealt;
            if (!IsAlive && economy && economy.Effects)
                economy.Effects.TryPreventDeath();
            if (hit.source && dealt > 0)
            {
                hit.source.LastHitTime = Time.time;
                hit.source.LastHitTarget = this;
            }
            Damaged?.Invoke(hit, dealt);
            HealthChanged?.Invoke();
            onDamaged.Invoke();
            sourceItems?.Effects?.AfterDamage(hit, dealt, this);
            RiftItemEffects.HealVowedDamage(hit, dealt, this);
            if (hit.isBasicAttack && hit.source && hit.source.OnHitBonus > 0 && IsAlive)
                TakeDamage(new DamageHit(hit.source, hit.origin, hit.source.OnHitBonus, DamageKind.Magic) { isItemEffect = true });
            if (!IsAlive && !deathReported)
            {
                deathReported = true;
                Defeated?.Invoke(this, hit);
                onDeath.Invoke();
            }
            return dealt;
        }

        public void SetHealth(float amount)
        {
            Health = Mathf.Clamp(amount, 0, maxHealth);
            if (Health > 0)
                deathReported = false;
            HealthChanged?.Invoke();
        }

        public void SetMaximumHealth(float value, bool grantIncrease = false)
        {
            float old = maxHealth;
            maxHealth = Mathf.Max(1, value);
            Health = Mathf.Clamp(Health + (grantIncrease ? Mathf.Max(0, maxHealth - old) : 0), 0, maxHealth);
            HealthChanged?.Invoke();
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0)
                return;
            float multiplier = economy && economy.Effects ? economy.Effects.HealingModifier : 1;
            Health = Mathf.Min(maxHealth, Health + amount * multiplier * (Time.time < woundsUntil ? 1 - wounds : 1));
            HealthChanged?.Invoke();
        }

        public void ResetHealth()
        {
            Health = maxHealth;
            deathReported = false;
            ClearCrowdControl();
            ClearShield();
            woundsUntil = armorShredUntil = magicShredUntil = speedUntil = attackBuffUntil = spellBuffUntil = onHitUntil = 0;
            LastHitTime = -100;
            LastHitTarget = null;
            HealthChanged?.Invoke();
        }

        public void AddShield(float amount, float seconds, DamageKind? type = null, bool decay = false)
        {
            if (amount <= 0)
                return;
            shields.Add(new ShieldLayer { amount = amount, until = Time.time + seconds, rate = decay ? amount / seconds : 0, type = type });
            HealthChanged?.Invoke();
        }

        public void AddRefreshableShield(string key, float amount, float maximum, float seconds, DamageKind? type = null)
        {
            var layer = shields.Find(s => s.key == key && s.until > Time.time);
            if (layer == null)
            {
                layer = new ShieldLayer { key = key, type = type };
                shields.Add(layer);
            }
            layer.amount = Mathf.Min(maximum, Mathf.Max(0, layer.amount) + Mathf.Max(0, amount));
            layer.until = Time.time + seconds;
            HealthChanged?.Invoke();
        }

        public void ClearShield() => shields.Clear();

        public void ReduceShield(float fraction)
        {
            foreach (var shield in shields)
                shield.amount *= 1 - fraction;
        }

        public void ApplyStun(float seconds) => stunUntil = Mathf.Max(stunUntil, Time.time + seconds * (1 - Tenacity));

        public void ApplyRoot(float seconds) => rootUntil = Mathf.Max(rootUntil, Time.time + seconds * (1 - Tenacity));

        public void ApplySleep(float seconds, float bonus, Combatant source)
        {
            sleepUntil = Time.time + seconds;
            sleepBonus = bonus;
        }

        public void ApplyGrievousWounds(float reduction, float seconds)
        {
            wounds = Mathf.Max(Time.time < woundsUntil ? wounds : 0, reduction);
            woundsUntil = Time.time + seconds;
        }

        public void ClearCrowdControl()
        {
            stunUntil = rootUntil = sleepUntil = 0;
            sleepBonus = 0;
            slowUntil = attackSlowUntil = 0;
            slowMultiplier = 1;
        }

        public void ApplySlow(float multiplier, float seconds)
        {
            slowMultiplier = Time.time < slowUntil ? Mathf.Min(slowMultiplier, multiplier) : multiplier;
            slowUntil = Mathf.Max(slowUntil, Time.time + seconds * (1 - Tenacity));
        }

        public void ApplySpeed(float amount, float seconds)
        {
            speedBonus = Mathf.Max(Time.time < speedUntil ? speedBonus : 0, amount);
            speedUntil = Time.time + seconds;
        }

        public void ApplyAttackSpeedSlow(float amount, float seconds)
        {
            attackSlow = Mathf.Max(Time.time < attackSlowUntil ? attackSlow : 0, amount);
            attackSlowUntil = Time.time + seconds;
        }

        public void ApplyAttackSpeed(float amount, float seconds)
        {
            attackBuff = Mathf.Max(amount, Time.time < attackBuffUntil ? attackBuff : 0);
            attackBuffUntil = Time.time + seconds;
        }

        public void ApplySpellPower(float amount, float seconds)
        {
            spellBuff = amount;
            spellBuffUntil = Time.time + seconds;
            if (economy)
                economy.Recalculate();
        }

        public void ApplyOnHit(float amount, float seconds)
        {
            onHitBonus = amount;
            onHitUntil = Time.time + seconds;
        }

        public void ShredArmor(float amount, int maxStacks, float seconds)
        {
            armorShred = Mathf.Min(amount * maxStacks, (Time.time < armorShredUntil ? armorShred : 0) + amount);
            armorShredUntil = Time.time + seconds;
        }

        public void ShredMagicResistance(float amount, int maxStacks, float seconds)
        {
            magicShred = Mathf.Min(amount * maxStacks, (Time.time < magicShredUntil ? magicShred : 0) + amount);
            magicShredUntil = Time.time + seconds;
        }
    }
}
