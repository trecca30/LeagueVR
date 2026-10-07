using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LeagueVR.Match
{
    public partial class RiftItemEffects
    {
        static readonly HashSet<RiftItemEffects> ActiveEffects = new();

        public static float RedirectVowDamage(Combatant target, DamageHit hit, float amount)
        {
            foreach (var effect in ActiveEffects)
            {
                if (!effect || effect.transferring || effect.VowedAlly != target || !effect.health.IsAlive || effect.health.Health < effect.health.maxHealth * .3f || Vector3.Distance(effect.player.Feet, target.transform.position) > 10)
                    continue;
                effect.transferring = true;
                effect.health.TakeDamage(new DamageHit(hit.source, hit.origin, amount * .14f, DamageKind.True) { isItemEffect = true });
                effect.transferring = false;
                return amount * .86f;
            }
            return amount;
        }

        public static void HealVowedDamage(DamageHit hit, float dealt, Combatant target)
        {
            if (!hit.source || !target.countsAsChampion)
                return;
            foreach (var effect in ActiveEffects)
                if (effect && effect.VowedAlly == hit.source)
                    effect.health.Heal(dealt * .12f);
        }

        void ResetPassives()
        {
            targetProcs.Clear();
            eclipseHits.Clear();
            burns.Clear();
            rageStacks = shojinStacks = terminusStacks = cullKills = slayStacks = hubrisKills = 0;
            practiceCrit = 0;
            CancelDamageDebt();
            rodBoughtAt = -1;
            rodStacks = 0;
            feastUntil = arcaneAimUntil = 0;
            debtRunning = false;
            permanentHP = spellbladeUntil = lastMagicIncoming = manaChargeAt = 0;
            reviving = transferring = false;
        }
        readonly Dictionary<Combatant, float> targetProcs = new();
        readonly Dictionary<Combatant, float> eclipseHits = new();
        float lastMagicIncoming, spellbladeUntil, manaChargeAt, energizedDistance;
        int rageStacks, shojinStacks, terminusStacks, cullKills, spellbladeToken;
        Vector3 previousFeet;
        bool reviving, transferring;
        readonly HashSet<Combatant> burns = new();

        public float ModifyOutgoing(DamageHit hit, Combatant target)
        {
            if (!economy || !economy.match.Running)
                return hit.amount;
            float value = hit.amount;
            if (economy.Owns(2523) && hit.isBasicAttack)
                value *= 1 + .1f * Mathf.Clamp01(Vector3.Distance(player.AttackOrigin, target.AimPosition) / 5f);
            if (!hit.isItemEffect && (hit.isBasicAttack || !string.IsNullOrEmpty(hit.ability)))
                Combat();
            if (economy.Owns(4645) && target.Health < target.maxHealth * .4f && hit.kind != DamageKind.Physical)
                value *= 1.2f;
            if (economy.Owns(4633) && combatStarted >= 0)
                value *= 1 + Mathf.Min(.08f, (Time.time - combatStarted) * .02f);
            if (economy.Owns(6653) && combatStarted >= 0)
                value *= 1 + Mathf.Min(.06f, (Time.time - combatStarted) * .02f);
            if (EmpoweredMana && !string.IsNullOrEmpty(hit.ability))
                value *= 1 + Mathf.Min(.3f, economy.MaxMana / 10000);
            if (economy.Owns(3161) && !hit.isBasicAttack && !hit.isItemEffect)
                value *= 1 + shojinStacks * .03f;
            if (economy.Owns(3168) && health.Health > health.maxHealth * .5f)
                value *= 1.04f;
            if (economy.Owns(4628) && !hit.isBasicAttack && Vector3.Distance(player.AttackOrigin, target.AimPosition) > 6)
                value *= 1.1f;
            if (economy.Owns(3036) && target.maxHealth > health.maxHealth)
                value *= 1 + Mathf.Clamp01((target.maxHealth - health.maxHealth) / 2000) * .15f;
            if (hit.isBasicAttack && economy.Owns(6610) && !targetProcs.TryGetValue(target, out float at) && target.countsAsChampion)
                value *= 1.75f;
            if (target.GetComponent<RiftStructure>() && economy.Owns(2520))
                value *= 1.2f;
            return value;
        }

        public float ModifyIncoming(DamageHit hit, float amount)
        {
            if (!economy || hit.ability == "DeferredPain")
                return amount;
            if (economy.Owns(6657) && hit.source && hit.source.countsAsChampion)
                economy.RestoreMana(amount * .1f);
            lastIncoming = Time.time;
            Combat();
            if (hit.kind == DamageKind.Magic)
            {
                lastMagicIncoming = Time.time;
                forceStacks = Mathf.Min(8, forceStacks + 1);
                if (forceStacks == 8)
                    economy.Recalculate();
            }
            if (hit.isBasicAttack && (economy.Owns(3047) || economy.Owns(3174)))
                amount *= .9f;
            if (hit.isCritical && economy.Owns(3143))
                amount *= .7f;
            if (economy.Owns(3082) && hit.isBasicAttack)
                amount = Mathf.Max(0, amount - 5);
            if ((economy.Owns(3102) || economy.Owns(3814)) && !hit.isBasicAttack && !hit.isItemEffect && !string.IsNullOrEmpty(hit.ability) && !HasProc("spellshield"))
            {
                SetProc("spellshield", 40);
                Pulse(player.Feet, Color.cyan, 1, .3f);
                return 0;
            }
            if (economy.Owns(3869) && hit.source && hit.source.countsAsChampion && !HasProc("opposition"))
            {
                SetProc("opposition", 20);
                StartCoroutine(Opposition());
            }
            if (HasProc("opposition-window"))
                amount *= .6f;
            if (health.Health - amount < health.maxHealth * .3f && !HasProc("lifeline"))
            {
                float shield = economy.Owns(3053) ? (health.maxHealth - 650) * .8f : economy.Owns(3156) ? 200 + (player.tuning.attackDamage - 65) * 1.5f : economy.Owns(6673) ? Mathf.Lerp(400, 700, (economy.Level - 1) / 17f) : economy.Owns(3155) ? 200 : 0;
                if (shield > 0)
                {
                    SetProc("lifeline", 90);
                    Shield(health, shield, 3);
                }
            }
            if (hit.isBasicAttack && hit.source && (economy.Owns(3075) || economy.Owns(3076)) && !hit.isItemEffect)
            {
                Deal(hit.source, economy.Owns(3075) ? 15 + health.armor * .15f : 10, DamageKind.Magic);
                hit.source.ApplyGrievousWounds(.4f, 3);
            }
            return amount;
        }

        public void AfterDamage(DamageHit hit, float dealt, Combatant target)
        {
            if (!economy || hit.source != health || dealt <= 0)
                return;
            if (!hit.isItemEffect)
                health.Heal(dealt * economy.Omnivamp * (target.countsAsChampion ? 1 : .33f));
            if (Time.time < elixirUntil && elixir == 2139 && (target.countsAsChampion || target.GetComponent<RiftStructure>()) && !hit.isItemEffect && !HasProc("elixir-" + target.GetEntityId()))
            {
                SetProc("elixir-" + target.GetEntityId(), target.GetComponent<RiftStructure>() ? .1f : 5);
                Deal(target, 25, DamageKind.True);
            }
            if (!target.IsAlive)
            {
                if (economy.Owns(6676) && target.countsAsChampion)
                    economy.AddGold(25, true);
                if (target.countsAsChampion)
                {
                    if (economy.Owns(6333))
                    {
                        CancelDamageDebt();
                        StartCoroutine(Defy());
                    }
                    if (economy.Owns(2517))
                    {
                        feastUntil = Time.time + 6;
                        economy.Recalculate();
                    }
                    if (economy.Owns(2523))
                        arcaneAimUntil = Time.time + 8;
                    if (economy.Owns(1082) || economy.Owns(3041))
                    {
                        soulStacks = Mathf.Min(economy.Owns(3041) ? 25 : 10, soulStacks + (economy.Owns(3041) ? 4 : 2));
                        economy.Recalculate();
                    }
                    if (economy.Owns(3008) || economy.Owns(3168))
                    {
                        slayStacks = Mathf.Min(10, slayStacks + 1);
                        economy.Recalculate();
                    }
                    if (economy.Owns(6697))
                    {
                        hubrisKills++;
                        economy.Recalculate();
                    }
                    if (economy.Owns(6696))
                        player.AdvanceUltimateCooldown(player.tuning.rCooldown * .2f);
                    if (economy.Owns(3137) && !HasProc("cryptbloom"))
                    {
                        SetProc("cryptbloom", 60);
                        foreach (var ally in Allies(6))
                            HealAlly(ally, 200 + economy.AbilityPower * .5f);
                        HealAlly(health, 200 + economy.AbilityPower * .5f);
                    }
                }
                if (target.GetComponent<RiftMinion>() && economy.Owns(1083) && cullKills < 100)
                {
                    cullKills++;
                    economy.AddGold(1, true);
                    if (cullKills == 100)
                        economy.AddGold(350, true);
                }
                if (target.GetComponent<RiftObjective>() && economy.OwnedItems.Any(i => i.HasTag("Jungle")))
                {
                    JungleTreats++;
                    health.Heal(health.maxHealth * .06f);
                    economy.RestoreMana(economy.MaxMana * .04f);
                }
            }
            if (hit.isItemEffect)
                return;
            if ((economy.Owns(6676)) && target.IsAlive && target.Health < target.maxHealth * .05f)
                Deal(target, target.Health, DamageKind.True);
            if ((economy.Owns(3165) || economy.Owns(3916)) && hit.kind == DamageKind.Magic ||
                (economy.Owns(3033) || economy.Owns(6609) || economy.Owns(3123)) && hit.kind == DamageKind.Physical)
                target.ApplyGrievousWounds(.4f, 3);
            if (economy.Owns(3071) && hit.kind == DamageKind.Physical)
                target.ShredArmor(.05f, 6, 6);
            if (economy.Owns(8010) && hit.kind == DamageKind.Magic)
                target.ShredMagicResistance(.1f, 3, 6);
            if (economy.Owns(6695))
                target.ReduceShield(.5f);
            if (target.countsAsChampion && economy.Owns(6692) && !HasProc("eclipse"))
            {
                if (eclipseHits.TryGetValue(target, out float at) && Time.time - at < 2)
                {
                    SetProc("eclipse", 6);
                    Shield(health, 160 + (player.tuning.attackDamage - 65) * .4f, 2);
                    Deal(target, target.maxHealth * .06f, DamageKind.Physical);
                    eclipseHits.Remove(target);
                }
                else
                    eclipseHits[target] = Time.time;
            }
            if (!string.IsNullOrEmpty(hit.ability) && hit.ability != "Passive")
            {
                if (economy.Owns(3116))
                    target.ApplySlow(.7f, 1);
                if (economy.Owns(4629))
                    Speed(health, .05f, 4);
                if (economy.Owns(3161))
                    shojinStacks = Mathf.Min(4, shojinStacks + 1);
                if (economy.Owns(3153) && hit.isBasicAttack)
                    target.ApplySlow(.7f, 1);
                if (economy.Owns(6653) || economy.Owns(2503) || economy.Owns(2508))
                    Burn(target);
                if (economy.Owns(6655) && !HasProc("luden"))
                {
                    SetProc("luden", 12);
                    Deal(target, 75 + economy.AbilityPower * .05f, DamageKind.Magic);
                    foreach (var extra in Enemies(8).Where(t => t != target).Take(5))
                        Deal(extra, 75 + economy.AbilityPower * .05f, DamageKind.Magic);
                }
                if (economy.Owns(3145) && !HasProc("alternator"))
                {
                    SetProc("alternator", 40);
                    Deal(target, 50, DamageKind.Magic);
                }
                if (economy.Owns(3871) && target.countsAsChampion && !HasProc("realmspike"))
                {
                    SetProc("realmspike", 10);
                    Deal(target, 10 + economy.AbilityPower * .2f + target.maxHealth * .03f, DamageKind.Magic);
                }
                if (economy.Owns(3118) && hit.ability == "R" && !HasProc("malignance-" + target.GetEntityId()))
                {
                    SetProc("malignance-" + target.GetEntityId(), 3);
                    StartCoroutine(Malignance(target));
                }
                if (economy.Owns(4646) && target.countsAsChampion && dealt > target.maxHealth * .15f && !HasProc("stormsurge"))
                {
                    SetProc("stormsurge", 30);
                    StartCoroutine(Stormsurge(target));
                }
                ChargeMana(target.countsAsChampion ? 6 : 3);
            }
        }
        int slayStacks, hubrisKills;
        public float ExtraOmnivamp => (economy.Owns(2517) && Time.time < feastUntil ? .15f : 0) + slayStacks * .006f + (economy.Owns(4633) && combatStarted >= 0 && Time.time - combatStarted >= 4 ? .1f : 0);
        public float ExtraAttackDamage => economy.Owns(6697) && hubrisKills > 0 ? 10 + hubrisKills * 2 : 0;
        public float HealingModifier => (economy.Owns(3065) ? 1.25f : 1) * (economy.Owns(3168) && health.Health < health.maxHealth * .5f ? 1.12f : 1);

        public void OnAttack(Combatant target, float dealt)
        {
            float life = dealt * economy.Lifesteal;
            if (economy.Owns(3072) && life > health.maxHealth - health.Health)
            {
                float excess = life - (health.maxHealth - health.Health);
                health.AddRefreshableShield("Bloodthirster", excess, Mathf.Lerp(50, 400, (economy.Level - 1) / 17f), 25);
            }
            health.Heal(life);
            if (economy.Owns(3032))
            {
                practiceCrit = Mathf.Min(.25f, practiceCrit + .002f);
                if (target.countsAsChampion && !HasProc("flurry"))
                {
                    attackSpeedBonus = .3f;
                    attackSpeedUntil = Time.time + 6;
                    SetProc("flurry", 30);
                }
                if (procReady.ContainsKey("flurry"))
                    procReady["flurry"] -= economy.LastAttackCritical ? 2 : 1;
                economy.Recalculate();
            }
            if (economy.Owns(1083))
                health.Heal(3);
            if (!target.IsAlive || target.GetComponent<RiftStructure>())
                return;
            attackCount++;
            if (economy.Owns(3115))
                Deal(target, 15 + economy.AbilityPower * .2f, DamageKind.Magic);
            else if (economy.Owns(1043))
                Deal(target, 15, DamageKind.Physical);
            if ((economy.Owns(1056) || economy.Owns(1055) || economy.Owns(1054) || economy.Owns(1086)) && target.GetComponent<RiftMinion>())
                Deal(target, 5, DamageKind.Physical);
            if (economy.Owns(3153))
                Deal(target, Mathf.Min(target.Health * .08f, target.countsAsChampion ? float.MaxValue : 100), DamageKind.Physical);
            if (economy.Owns(3091))
                Deal(target, 45, DamageKind.Magic);
            if (economy.Owns(3124))
            {
                rageStacks = Mathf.Min(4, rageStacks + 1);
                attackSpeedBonus = rageStacks * .08f;
                attackSpeedUntil = Time.time + 3;
                economy.Recalculate();
                Deal(target, 30, DamageKind.Magic);
                if (rageStacks == 4 && attackCount % 3 == 0)
                {
                    if (economy.Owns(3115))
                        Deal(target, 15 + economy.AbilityPower * .2f, DamageKind.Magic);
                    if (economy.Owns(3153))
                        Deal(target, Mathf.Min(target.Health * .08f, 100), DamageKind.Physical);
                }
            }
            if (economy.Owns(6672) && attackCount % 3 == 0)
                Deal(target, Mathf.Lerp(150, 310, (economy.Level - 1) / 17f) * (1 + Mathf.Clamp01(1 - target.Health / target.maxHealth) * .5f), DamageKind.Physical);
            if (economy.Owns(2512) && attackCount % 3 == 0)
                Deal(target, player.tuning.attackDamage * .75f, DamageKind.Physical);
            if (economy.Owns(3302))
            {
                terminusStacks = Mathf.Min(6, terminusStacks + 1);
                if (attackCount % 2 == 0)
                {
                    target.ShredArmor(.1f, 3, 5);
                    target.ShredMagicResistance(.1f, 3, 5);
                }
            }
            if (economy.Owns(3042))
                Deal(target, economy.MaxMana * .015f, DamageKind.Physical);
            if (titanicReady)
            {
                titanicReady = false;
                Deal(target, health.maxHealth * .06f, DamageKind.Physical);
            }
            if (economy.Owns(3748))
            {
                Deal(target, 5 + health.maxHealth * .015f, DamageKind.Physical);
                Cleave(target, 40 + health.maxHealth * .03f);
            }
            else if (economy.Owns(3074) || economy.Owns(3077) || economy.Owns(6631) || economy.Owns(6698))
                Cleave(target, player.tuning.attackDamage * .4f);
            if (spellbladeUntil > Time.time)
            {
                int blade = economy.Owns(3100) ? 3100 : economy.Owns(3078) ? 3078 : economy.Owns(6662) ? 6662 : economy.Owns(2510) ? 2510 : economy.Owns(3877) ? 3877 : economy.Owns(3057) ? 3057 : 0;
                if (blade != 0 && !HasProc("spellblade"))
                {
                    spellbladeUntil = 0;
                    SetProc("spellblade", 1.5f);
                    float baseAD = 65 + 3 * (economy.Level - 1);
                    Deal(target, blade == 3100 ? baseAD * .75f + economy.AbilityPower * .45f : blade == 3078 ? baseAD * 2 : blade == 2510 ? baseAD + economy.AbilityPower * .1f : baseAD, blade == 3100 || blade == 2510 ? DamageKind.Magic : DamageKind.Physical);
                    if (blade == 6662)
                    {
                        target.ApplySlow(.5f, 2);
                        Pulse(target.transform.position, Color.cyan, 3, 2);
                    }
                }
            }
            if (economy.Owns(6610) && target.countsAsChampion && !targetProcs.ContainsKey(target))
            {
                targetProcs[target] = Time.time + 6;
                health.Heal((65 + 3 * (economy.Level - 1)) * 1.5f + (health.maxHealth - health.Health) * .06f);
            }
            if (economy.Owns(3084) && target.countsAsChampion && !HasProc("heartsteel-" + target.GetEntityId()))
            {
                SetProc("heartsteel-" + target.GetEntityId(), 30);
                float amount = 125 + health.maxHealth * .06f;
                Deal(target, amount, DamageKind.Physical);
                permanentHP += amount * .1f;
                economy.Recalculate();
            }
            if ((economy.Owns(3087) || economy.Owns(3094) || economy.Owns(3095) || economy.Owns(6699) || economy.Owns(3144)) && !HasProc("energized"))
            {
                SetProc("energized", 8);
                Deal(target, economy.Owns(3095) ? 100 : 60, DamageKind.Magic);
                if (economy.Owns(6699))
                    target.ApplySlow(.01f, .75f);
                if (economy.Owns(3095))
                    Speed(health, .45f, 1.5f);
                if (economy.Owns(3087))
                    foreach (var extra in Enemies(8).Where(t => t != target).Take(5))
                        Deal(extra, 90, DamageKind.Magic);
            }
            if (economy.Owns(3085))
                foreach (var extra in Enemies(5).Where(t => t != target).Take(2))
                    Deal(extra, player.tuning.attackDamage * .55f, DamageKind.Physical);
            if (economy.Owns(3046))
                Speed(health, .08f, 3);
            if (economy.Owns(3044) || economy.Owns(3071))
                Speed(health, .06f, 2);
            if (economy.Owns(6675))
                player.AdvanceBasicCooldowns(.6f);
            if (economy.Owns(3508))
                economy.RestoreMana(15 + player.tuning.attackDamage * .1f);
            if (economy.Owns(3742))
            {
                Deal(target, 40, DamageKind.Physical);
                target.ApplySlow(.5f, 1);
            }
            if (economy.Owns(3181) && attackCount % 5 == 0)
                Deal(target, player.tuning.attackDamage * 1.2f + health.maxHealth * .05f, DamageKind.Physical);
            ChargeMana(target.countsAsChampion ? 6 : 3);
        }
        float permanentHP;
        public float PermanentHealth => permanentHP;

        void Cleave(Combatant target, float amount)
        {
            foreach (var other in Enemies(6).Where(t => t != target && Vector3.Distance(t.AimPosition, target.AimPosition) < 3))
                Deal(other, amount, DamageKind.Physical);
        }

        void ChargeMana(int charge)
        {
            if (Time.time < manaChargeAt || !economy.OwnedItems.Any(i => new[] { 3070, 3003, 3004, 3119 }.Contains(i.id)))
                return;
            manaChargeAt = Time.time + 8;
            ManaCharge = Mathf.Min(360, ManaCharge + charge);
            if (ManaCharge >= 360)
            {
                if (economy.Owns(3003))
                    economy.TransformItem(3003, 3040);
                else if (economy.Owns(3004))
                    economy.TransformItem(3004, 3042);
                else if (economy.Owns(3119))
                    economy.TransformItem(3119, 3121);
            }
            economy.Recalculate();
        }

        void OnCast(string name, Vector3 at, Vector3 direction)
        {
            if (!economy || !economy.match.Running)
                return;
            if (new[] { "Q", "W", "E", "R" }.Contains(name))
            {
                spellbladeUntil = Time.time + 10;
                if (name == "R" && economy.Owns(3073))
                {
                    Speed(health, .15f, 8);
                    attackSpeedBonus = .3f;
                    attackSpeedUntil = Time.time + 8;
                    economy.Recalculate();
                }
                if (name == "R" && economy.Owns(3050))
                {
                    Pulse(player.Feet, Color.cyan, 4, 5);
                    foreach (var target in Enemies(4))
                        target.ApplySlow(.7f, 5);
                }
            }
        }
        public int TimelessStacks => economy.Owns(6657) ? rodStacks : 0;
        public float FamineHaste => economy.Owns(2517) ? economy.OwnedItems.Sum(i => i.attackDamage) * .30f : 0;
        public float PracticeCrit => economy.Owns(3032) ? practiceCrit : 0;
        public float BonusAttackRange => economy.Owns(2523) && Time.time < arcaneAimUntil ? 1 : 0;
        float practiceCrit, rodBoughtAt = -1, feastUntil, arcaneAimUntil;
        int rodStacks;
        bool debtRunning;

        class DeferredDebt
        {
            public float remaining, rate, until;
        }
        readonly List<DeferredDebt> damageDebts = new();
        Coroutine debtCoroutine;

        public void CancelDamageDebt()
        {
            damageDebts.Clear();
            if (debtCoroutine != null)
                StopCoroutine(debtCoroutine);
            debtCoroutine = null;
            debtRunning = false;
        }

        public float AfterMitigation(DamageHit hit, float amount)
        {
            if (!economy.Owns(6333) || hit.kind == DamageKind.True || amount <= 0)
                return amount;
            float deferred = amount * .3f;
            damageDebts.Add(new DeferredDebt { remaining = deferred, rate = deferred / 3, until = Time.time + 3 });
            if (!debtRunning)
                debtCoroutine = StartCoroutine(DeferredPain());
            return amount - deferred;
        }

        IEnumerator DeferredPain()
        {
            debtRunning = true;
            while (damageDebts.Count > 0 && health.IsAlive)
            {
                float tick = 0;
                foreach (var debt in damageDebts)
                {
                    float part = Time.time >= debt.until ? debt.remaining : Mathf.Min(debt.remaining, debt.rate * Time.deltaTime);
                    tick += part;
                    debt.remaining -= part;
                }
                damageDebts.RemoveAll(d => d.remaining <= .001f);
                if (tick > 0)
                    health.TakeDamage(new DamageHit(null, player.Feet, tick, DamageKind.True) { isItemEffect = true, ability = "DeferredPain" });
                yield return null;
            }
            debtRunning = false;
            debtCoroutine = null;
        }

        IEnumerator Defy()
        {
            float left = (player.tuning.attackDamage - 65) * .5f;
            float end = Time.time + 2;
            while (Time.time < end && health.IsAlive)
            {
                float v = Mathf.Min(left, (player.tuning.attackDamage - 65) * .25f * Time.deltaTime);
                left -= v;
                health.Heal(v);
                yield return null;
            }
        }

        void Combat()
        {
            lastHitTime = Time.time;
            if (combatStarted < 0)
                combatStarted = Time.time;
        }

        void Burn(Combatant target)
        {
            if (burns.Add(target))
                StartCoroutine(Burning(target));
        }

        IEnumerator Burning(Combatant target)
        {
            for (int n = 0; n < 3 && target && target.IsAlive; n++)
            {
                float amount = (economy.Owns(6653) ? target.maxHealth * .02f : 0) + (economy.Owns(2503) ? 20 + economy.AbilityPower * .04f : economy.Owns(2508) ? 10 : 0);
                Deal(target, amount, DamageKind.Magic);
                yield return new WaitForSeconds(1);
            }
            burns.Remove(target);
        }

        IEnumerator Stormsurge(Combatant target)
        {
            yield return new WaitForSeconds(2);
            if (target && target.IsAlive)
                Deal(target, 125 + economy.AbilityPower * .1f, DamageKind.Magic);
        }

        IEnumerator Malignance(Combatant target)
        {
            Vector3 at = target.transform.position;
            Pulse(at, Color.magenta, 3, 3);
            for (int n = 0; n < 3; n++)
            {
                foreach (var enemy in Enemies(15).Where(t => Vector3.Distance(t.transform.position, at) < 3))
                {
                    Deal(enemy, 60 + economy.AbilityPower * .05f, DamageKind.Magic);
                    enemy.ShredMagicResistance(.1f, 1, 1);
                }
                yield return new WaitForSeconds(1);
            }
        }

        IEnumerator Opposition()
        {
            SetProc("opposition-window", 2);
            yield return new WaitForSeconds(2);
            foreach (var enemy in Enemies(4))
                enemy.ApplySlow(.5f, 1.5f);
        }

        void AuraPassives()
        {
            if (!health.IsAlive)
                return;
            if (economy.Owns(6657))
            {
                if (rodBoughtAt < 0)
                    rodBoughtAt = Time.time;
                int stacks = Mathf.Min(10, (int)((Time.time - rodBoughtAt) / 60));
                if (stacks != rodStacks)
                {
                    rodStacks = stacks;
                    if (stacks == 10 && economy.Level < 18)
                        economy.Level++;
                    economy.Recalculate();
                }
            }
            else
            {
                rodBoughtAt = -1;
                rodStacks = 0;
            }
            foreach (var entry in targetProcs.Where(p => !p.Key || Time.time >= p.Value).ToArray())
                targetProcs.Remove(entry.Key);
            if (economy.Owns(3068) || economy.Owns(6660) || economy.Owns(6664))
                foreach (var target in Enemies(3))
                {
                    float dealt = Deal(target, economy.Owns(6660) ? 15 : 20 + health.maxHealth * .01f, DamageKind.Magic);
                    if (economy.Owns(6664) && !target.IsAlive)
                        Cleave(target, 20 + health.maxHealth * .02f);
                }
            if (economy.Owns(2502) && !HasProc("despair"))
            {
                SetProc("despair", 4);
                foreach (var target in Enemies(3))
                {
                    float dealt = Deal(target, 30 + health.maxHealth * .03f, DamageKind.Magic);
                    health.Heal(dealt * 2.5f);
                }
            }
            if (economy.Owns(8020))
                foreach (var target in Enemies(5))
                    target.ShredMagicResistance(.3f, 1, 1.2f);
            if (economy.Owns(3110))
                foreach (var target in Enemies(7))
                    target.ApplyAttackSpeedSlow(.2f, 1.2f);
            if (economy.Owns(2524) || economy.Owns(2526))
                foreach (var ally in Allies(6))
                    ally.ApplySpeed(.05f, 1.2f);
            if (economy.Owns(1101) || economy.Owns(1102) || economy.Owns(1103))
                foreach (var monster in Enemies(8).Where(t => t.GetComponent<RiftObjective>() && Time.time - lastHitTime < 5))
                    Deal(monster, 20 + economy.AbilityPower * .15f + health.maxHealth * .01f, DamageKind.True);
            if (economy.Owns(3179))
                RiftVisionWard.Reveal(player.Feet, health.team, 4, 1.2f);
            if (economy.Owns(3142) && Time.time - lastHitTime > 5)
                Speed(health, .1f, 1.2f);
            if (economy.Owns(1054) && Time.time - lastIncoming < 8)
                health.Heal(4);
            if (economy.Owns(2525) && health.Health < health.maxHealth * .3f && !HasProc("protoplasm"))
            {
                SetProc("protoplasm", 90);
                health.Heal(health.maxHealth * .15f);
                Speed(health, .2f, 3);
            }
            if (economy.Owns(3040) && health.Health < health.maxHealth * .3f && !HasProc("seraph"))
            {
                SetProc("seraph", 90);
                Shield(health, 200 + economy.Mana * .2f, 3);
            }
            if (economy.Owns(3121) && Enemies(5).Any(t => t.SlowMultiplier < 1) && !HasProc("fimbul"))
            {
                SetProc("fimbul", 8);
                Shield(health, 100 + economy.MaxMana * .05f, 3);
            }
        }

        void SupportHeal(Combatant target)
        {
            if (target == health || !target.countsAsChampion)
                return;
            if (economy.Owns(3504))
            {
                target.ApplyAttackSpeed(.25f, 6);
                target.ApplyOnHit(20, 6);
            }
            if (economy.Owns(6616))
            {
                target.ApplySpeed(.1f, 4);
                target.ApplySpellPower(45, 4);
            }
            if (economy.Owns(3870))
            {
                target.AddShield(Mathf.Lerp(50, 100, (economy.Level - 1) / 17f), 3);
                target.ApplyOnHit(75, 3);
            }
            if (economy.Owns(6620))
            {
                HealAllyWithoutProc(target, 75);
                foreach (var enemy in Enemies(6).Take(1))
                    Deal(enemy, 50, DamageKind.Magic);
            }
            if (economy.Owns(6617))
                foreach (var ally in Allies(6).Where(t => t != target).Take(1))
                    HealAllyWithoutProc(ally, 50);
        }

        void HealAllyWithoutProc(Combatant target, float value) => target.Heal(value * (1 + economy.HealShieldPower));

        public bool TryPreventDeath()
        {
            if (reviving || !economy.Owns(3026) || HasProc("guardian"))
                return false;
            SetProc("guardian", 300);
            health.SetHealth(1);
            stasisUntil = Time.time + 4;
            reviving = true;
            StartCoroutine(Revive());
            return true;
        }

        IEnumerator Revive()
        {
            yield return new WaitForSeconds(4);
            health.SetHealth(health.maxHealth * .5f);
            economy.RestoreMana(economy.MaxMana * .3f);
            reviving = false;
        }
    }
}
