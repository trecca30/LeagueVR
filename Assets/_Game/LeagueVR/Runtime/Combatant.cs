using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using LeagueVR.Match;

namespace LeagueVR
{
    public enum DamageKind { Physical, Magic, True }
    public struct DamageHit
    {
        public Combatant source;
        public Vector3 origin;
        public float amount;
        public DamageKind kind;
        public bool isBasicAttack, isItemEffect, isCritical;
        public string ability;
        public DamageHit(Combatant source, Vector3 origin, float amount, DamageKind kind)
        { this.source = source; this.origin = origin; this.amount = amount; this.kind = kind; isBasicAttack = isItemEffect = isCritical = false; ability = null; }
    }
    public interface IDamageGuard { bool Blocks(DamageHit hit); float BonusResistance { get; } }

    [DisallowMultipleComponent]
    public class Combatant : MonoBehaviour
    {
        public int team;
        public bool countsAsChampion = true;
        public float maxHealth = 650, armor = 30, magicResistance = 30;
        public Transform aimPoint;
        public UnityEvent onDeath = new(), onDamaged = new();
        public event Action<DamageHit, float> Damaged;
        public static event Action<Combatant, DamageHit> Attacked;
        public static event Action<Combatant,DamageHit> Defeated;
        float stunUntil,rootUntil,sleepUntil,sleepBonus; Combatant sleepSource;
        public bool Stunned=>Time.time<stunUntil||Time.time<sleepUntil;
        public bool Rooted=>Stunned||Time.time<rootUntil;
        public void ApplyStun(float seconds){stunUntil=Mathf.Max(stunUntil,Time.time+seconds*(1-(GetComponent<RiftEconomy>()?.Tenacity??0)));}
        public void ApplyRoot(float seconds){rootUntil=Mathf.Max(rootUntil,Time.time+seconds*(1-(GetComponent<RiftEconomy>()?.Tenacity??0)));}
        public void ApplySleep(float seconds,float bonus,Combatant source){sleepUntil=Time.time+seconds;sleepBonus=bonus;sleepSource=source;}
        public event Action HealthChanged;
        public float Health { get; private set; }
        public bool IsAlive => Health > 0;
        public bool IsTargetable => IsAlive && !(GetComponent<LeagueVR.Champions.ChampionAbilities>()?.Camouflaged ?? false) && !(GetComponent<RiftEconomy>()?.Stasis ?? false) && (!GetComponent<RiftVisionWard>() || GetComponent<RiftVisionWard>().Revealed);
        public Vector3 AimPosition => aimPoint ? aimPoint.position : transform.position + Vector3.up;
        public bool IsTargetableBy(Combatant attacker)
        {
            if(!IsTargetable)return false;var gwen=GetComponent<GwenAbilities>();
            return !gwen||!gwen.MistActive||!attacker||attacker.GetComponent<RiftStructure>()||GwenAbilities.FlatDistance(attacker.AimPosition,gwen.MistCenter)<=gwen.tuning.wRadius;
        }
        public float SlowMultiplier => Rooted ? 0 : Time.time < slowUntil ? slowMultiplier : 1;
        public float SpeedMultiplier => Time.time < speedUntil ? 1 + speedBonus : 1;
        public float AttackIntervalMultiplier => (Time.time < attackSlowUntil ? 1 / (1 - attackSlow) : 1) / (1 + (Time.time < attackBuffUntil ? attackBuff : 0));
        public float SpellPowerBonus => Time.time < spellBuffUntil ? spellBuff : 0;
        public float OnHitBonus => Time.time < onHitUntil ? onHitBonus : 0;
        public float Shield => shields.Exists(s => s.until > Time.time) ? shields.FindAll(s => s.until > Time.time).ConvertAll(s => s.amount).ToArraySum() : 0;
        float slowUntil, slowMultiplier = 1, woundsUntil, wounds, speedUntil, speedBonus, attackSlowUntil, attackSlow, attackBuffUntil, attackBuff, spellBuffUntil, spellBuff, onHitUntil, onHitBonus, armorShredUntil, armorShred, magicShredUntil, magicShred;
        bool deathReported;
        IDamageGuard[] guards;
        class ShieldLayer { public float amount, rate, until; public DamageKind? type; public string key; }
        readonly List<ShieldLayer> shields = new();
        void Awake() { RefreshGuards(); ResetHealth(); }
        void Update()
        {
            foreach (var s in shields) s.amount = Mathf.Max(0, s.amount - s.rate * Time.deltaTime);
            shields.RemoveAll(s => s.amount <= 0 || Time.time >= s.until);
        }
        public void RefreshGuards()
        {
            var list = new List<IDamageGuard>();
            foreach (var c in GetComponents<MonoBehaviour>()) if (c is IDamageGuard guard) list.Add(guard);
            guards = list.ToArray();
        }
        public static float Mitigate(float damage, float resistance) => damage * (resistance >= 0 ? 100 / (100 + resistance) : 2 - 100 / (100 - resistance));
        public float TakeDamage(DamageHit hit)
        {
            if (!IsAlive || hit.amount <= 0 || (hit.source && (hit.source == this || hit.source.team == team))) return 0;
            Attacked?.Invoke(this, hit);
            if(Time.time<sleepUntil&&hit.ability!="Passive"){sleepUntil=0;hit.amount+=sleepBonus;sleepBonus=0;}
            float bonus = 0;
            if (guards != null) foreach (var guard in guards) { if (guard.Blocks(hit)) return 0; bonus += guard.BonusResistance; }
            var sourceItems = hit.source ? hit.source.GetComponent<RiftEconomy>() : null;
            var items = GetComponent<RiftEconomy>();
            float amount = sourceItems && sourceItems.Effects ? sourceItems.Effects.ModifyOutgoing(hit, this) : hit.amount;
            if (items && items.Effects) amount = items.Effects.ModifyIncoming(hit, amount);
            if (GetComponent<RiftVisionWard>()) { if (!hit.isBasicAttack) return 0; amount = 1; hit.kind = DamageKind.True; }
            float resistance = hit.kind == DamageKind.Physical ? armor * (1 - (Time.time < armorShredUntil ? armorShred : 0)) : magicResistance * (1 - (Time.time < magicShredUntil ? magicShred : 0));
            resistance += bonus;
            if (sourceItems && resistance > 0) resistance = Mathf.Max(0, resistance * (1 - (hit.kind == DamageKind.Physical ? sourceItems.ArmorPen : sourceItems.MagicPenPercent)) - (hit.kind == DamageKind.Physical ? sourceItems.Lethality : sourceItems.MagicPen));
            amount = hit.kind == DamageKind.True ? amount : Mitigate(amount, resistance);
            amount = RiftItemEffects.RedirectVowDamage(this, hit, amount);
            if(items&&items.Effects)amount=items.Effects.AfterMitigation(hit,amount);
            foreach (var shield in shields)
            {
                if (Time.time >= shield.until || shield.type.HasValue && shield.type.Value != hit.kind) continue;
                float blocked = Mathf.Min(amount, shield.amount); amount -= blocked; shield.amount -= blocked;
                if (amount <= 0) break;
            }
            float dealt = Mathf.Min(Health, amount);
            Health -= dealt;
            if (!IsAlive && items && items.Effects) items.Effects.TryPreventDeath();
            Damaged?.Invoke(hit, dealt); HealthChanged?.Invoke(); onDamaged.Invoke();
            sourceItems?.Effects?.AfterDamage(hit, dealt, this);
            RiftItemEffects.HealVowedDamage(hit, dealt, this);
            if (hit.isBasicAttack && hit.source && hit.source.OnHitBonus > 0 && IsAlive) TakeDamage(new DamageHit(hit.source, hit.origin, hit.source.OnHitBonus, DamageKind.Magic) { isItemEffect = true });
            if (!IsAlive && !deathReported) { deathReported = true; Defeated?.Invoke(this,hit);onDeath.Invoke(); }
            return dealt;
        }
        public void SetHealth(float amount) { Health = Mathf.Clamp(amount, 0, maxHealth); if (Health > 0) deathReported = false; HealthChanged?.Invoke(); }
        public void SetMaximumHealth(float value, bool grantIncrease = false)
        { float old = maxHealth; maxHealth = Mathf.Max(1, value); Health = Mathf.Clamp(Health + (grantIncrease ? Mathf.Max(0, maxHealth - old) : 0), 0, maxHealth); HealthChanged?.Invoke(); }
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0) return;
            float multiplier = GetComponent<RiftEconomy>()?.Effects?.HealingModifier ?? 1;
            Health = Mathf.Min(maxHealth, Health + amount * multiplier * (Time.time < woundsUntil ? 1 - wounds : 1)); HealthChanged?.Invoke();
        }
        public void ResetHealth() { Health = maxHealth; deathReported = false; ClearCrowdControl(); ClearShield(); woundsUntil = armorShredUntil = magicShredUntil = speedUntil = attackBuffUntil = spellBuffUntil = onHitUntil = 0; HealthChanged?.Invoke(); }
        public void AddShield(float amount, float seconds, DamageKind? type = null, bool decay = false)
        { if (amount <= 0) return; shields.Add(new ShieldLayer { amount = amount, until = Time.time + seconds, rate = decay ? amount / seconds : 0, type = type }); HealthChanged?.Invoke(); }
        public void AddRefreshableShield(string key,float amount,float maximum,float seconds,DamageKind? type=null)
        {
            var layer=shields.Find(s=>s.key==key&&s.until>Time.time);
            if(layer==null){layer=new ShieldLayer{key=key,type=type};shields.Add(layer);}
            layer.amount=Mathf.Min(maximum,Mathf.Max(0,layer.amount)+Mathf.Max(0,amount));layer.until=Time.time+seconds;HealthChanged?.Invoke();
        }
        public void ClearShield() => shields.Clear();
        public void ReduceShield(float fraction) { foreach (var shield in shields) shield.amount *= 1 - fraction; }
        public void ApplyGrievousWounds(float reduction, float seconds) { wounds = Mathf.Max(wounds, reduction); woundsUntil = Time.time + seconds; }
        public void ClearCrowdControl() { stunUntil=rootUntil=sleepUntil=0;sleepBonus=0;slowUntil = attackSlowUntil = 0; slowMultiplier = 1; }
        public void ApplySlow(float multiplier, float seconds)
        {
            float tenacity = GetComponent<RiftEconomy>()?.Tenacity ?? 0;
            if (Time.time < slowUntil) slowMultiplier = Mathf.Min(slowMultiplier, multiplier); else slowMultiplier = multiplier;
            slowUntil = Mathf.Max(slowUntil, Time.time + seconds * (1 - tenacity));
        }
        public void ApplySpeed(float amount, float seconds) { speedBonus = Mathf.Max(Time.time < speedUntil ? speedBonus : 0, amount); speedUntil = Time.time + seconds; }
        public void ApplyAttackSpeedSlow(float amount, float seconds) { attackSlow = Mathf.Max(Time.time < attackSlowUntil ? attackSlow : 0, amount); attackSlowUntil = Time.time + seconds; }
        public void ApplyAttackSpeed(float amount, float seconds) { attackBuff = Mathf.Max(amount, Time.time < attackBuffUntil ? attackBuff : 0); attackBuffUntil = Time.time + seconds; }
        public void ApplySpellPower(float amount, float seconds) { spellBuff = amount; spellBuffUntil = Time.time + seconds; GetComponent<RiftEconomy>()?.Recalculate(); }
        public void ApplyOnHit(float amount, float seconds) { onHitBonus = amount; onHitUntil = Time.time + seconds; }
        public void ShredArmor(float amount, int maxStacks, float seconds) { armorShred = Mathf.Min(amount * maxStacks, (Time.time < armorShredUntil ? armorShred : 0) + amount); armorShredUntil = Time.time + seconds; }
        public void ShredMagicResistance(float amount, int maxStacks, float seconds) { magicShred = Mathf.Min(amount * maxStacks, (Time.time < magicShredUntil ? magicShred : 0) + amount); magicShredUntil = Time.time + seconds; }
    }
    static class CombatArrayMath { public static float ToArraySum(this IEnumerable<float> values) { float total = 0; foreach (float v in values) total += v; return total; } }
}
