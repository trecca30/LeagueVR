using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using LeagueVR.Champions;

namespace LeagueVR.Match
{
    public partial class RiftItemEffects : MonoBehaviour
    {
        public RiftEconomy economy { get; private set; }
        PlayerChampion player => economy.match.player;
        Combatant health => player.Health;
        public bool Stasis => Time.time < stasisUntil;
        public bool EmpoweredMana => Time.time < actualizerUntil;
        public float BonusAP => (Time.time < elixirUntil && elixir == 2139 ? 50 : 0) + soulStacks * (economy.Owns(3041) ? 5 : 4);
        public float BonusAD => Time.time < elixirUntil && elixir == 2140 ? 30 : 0;
        public float BonusHP => Time.time < elixirUntil && elixir == 2138 ? 300 : 0;
        public float BonusVamp => Time.time < elixirUntil && elixir == 2140 ? .12f : 0;
        public float BonusMove => Time.time < moveUntil ? moveBonus : 0;
        public float BonusAS => Time.time < attackSpeedUntil ? attackSpeedBonus : 0;
        public float BonusArmor => jakshoActive && economy.Owns(6665) ? economy.OwnedItems.Sum(i => i.armor) * .3f : 0;
        public float BonusMR => (jakshoActive && economy.Owns(6665) ? economy.OwnedItems.Sum(i => i.magicResistance) * .3f : 0) + (economy.Owns(4401) && forceStacks >= 8 ? 30 : 0);
        public int WardCharges { get; private set; } = 2;
        public Combatant VowedAlly { get; private set; }
        public int ManaCharge { get; private set; }
        public int JungleTreats { get; private set; }

        public float Cooldown(int id) => ready.TryGetValue(Group(id), out float at) ? Mathf.Max(0, at - Time.time) : 0;

        public bool HasProc(string key) => procReady.TryGetValue(key, out float at) && Time.time < at;
        readonly Dictionary<int, float> ready = new();
        readonly Dictionary<string, float> procReady = new();
        readonly Dictionary<LocomotionProvider, bool> suspended = new();
        readonly Dictionary<ContinuousMoveProvider, float> moveSpeeds = new();
        float stasisUntil, actualizerUntil, elixirUntil, moveUntil, moveBonus, attackSpeedUntil, attackSpeedBonus, potionUntil, trinketNext, lastHitTime, combatStarted = -1, lastIncoming, nextAura, nextRecalculate;
        int elixir, attackCount, soulStacks, forceStacks;
        bool jakshoActive, wasStasis, titanicReady;
        Coroutine potion;

        static int Group(int id) => id == 2420 || id == 3157 ? 3157 : id == 3139 || id == 3140 ? 3140 : new[] { 3077, 3074, 3748, 6631, 6698 }.Contains(id) ? 3077 : id;

        public void Initialize(RiftEconomy owner)
        {
            economy = owner;
            ActiveEffects.Add(this);
            foreach (var move in player.origin.GetComponentsInChildren<ContinuousMoveProvider>(true))
                moveSpeeds[move] = move.moveSpeed;
            player.Cast += OnCast;
            health.onDeath.AddListener(PlayerDied);
        }

        void OnDestroy()
        {
            if (economy && player)
            {
                player.Cast -= OnCast;
                health.onDeath.RemoveListener(PlayerDied);
            }
            RestoreLocomotion();
            ActiveEffects.Remove(this);
        }

        public void ResetEffects()
        {
            StopAllCoroutines();
            ready.Clear();
            procReady.Clear();
            potion = null;
            stasisUntil = actualizerUntil = elixirUntil = moveUntil = attackSpeedUntil = potionUntil = 0;
            elixir = soulStacks = attackCount = forceStacks = ManaCharge = JungleTreats = 0;
            lastHitTime = lastIncoming = nextAura = nextRecalculate = 0;
            WardCharges = 2;
            trinketNext = 0;
            titanicReady = jakshoActive = false;
            combatStarted = -1;
            VowedAlly = null;
            supportCharges = 4;
            ResetPassives();
            health.ClearShield();
            RestoreLocomotion();
        }

        void PlayerDied()
        {
            soulStacks = Mathf.Max(0, soulStacks - (economy.Owns(3041) ? 10 : 5));
            StopAllCoroutines();
            CancelDamageDebt();
            potion = null;
            potionUntil = 0;
            stasisUntil = 0;
            VowedAlly = null;
            economy.Recalculate();
            RestoreLocomotion();
        }

        public void ResetTrinket(int id)
        {
            WardCharges = id == 3363 ? 1 : 2;
            trinketNext = Time.time + (id == 3340 ? 0 : 30);
            if (id != 3340)
                ready[id] = Time.time + 30;
        }

        public void RefillWards()
        {
            if (economy.match.AtShop && economy.OwnedItems.Any(i => i.active == ItemActive.SupportWard) && supportCharges < 4)
            {
                supportCharges = 4;
                economy.Touch();
            }
        }
        int supportCharges = 4;

        public int Charges(int id) => id == 3363 ? (Cooldown(id) <= 0 ? 1 : 0) : id == 2031 ? economy.inventory.FirstOrDefault(s => s.id == id)?.charges ?? 0 : id == 3340 || id == 3364 ? WardCharges : economy.match.catalog.Find(id)?.active == ItemActive.SupportWard ? supportCharges : economy.inventory.FirstOrDefault(s => s.id == id)?.count ?? 0;

        void Update()
        {
            if (!economy || !economy.match.Running)
                return;
            if (Stasis && !wasStasis)
            {
                foreach (var provider in player.origin.GetComponentsInChildren<LocomotionProvider>(true))
                {
                    suspended[provider] = provider.enabled;
                    provider.enabled = false;
                }
            }
            if (!Stasis && wasStasis)
                RestoreLocomotion();
            wasStasis = Stasis;
            foreach (var pair in moveSpeeds)
                if (pair.Key)
                    pair.Key.moveSpeed = pair.Value * Mathf.Max(.1f, 1 + economy.MoveSpeedBonus) * health.SlowMultiplier * health.SpeedMultiplier;
            if (EmpoweredMana)
                player.AdvanceBasicCooldowns(Time.deltaTime * .3f);
            if (Time.time >= nextRecalculate)
            {
                nextRecalculate = Time.time + .25f;
                bool combat = Time.time - lastHitTime < 5;
                if (!combat)
                {
                    combatStarted = -1;
                    forceStacks = 0;
                }
                bool jaksho = combatStarted >= 0 && Time.time - combatStarted >= 5;
                if (jaksho != jakshoActive)
                {
                    jakshoActive = jaksho;
                    economy.Recalculate();
                }
                if (elixirUntil > 0 && Time.time >= elixirUntil)
                {
                    elixirUntil = 0;
                    economy.Recalculate();
                }
                if (moveUntil > 0 && Time.time >= moveUntil)
                {
                    moveUntil = 0;
                    economy.Recalculate();
                }
                if (attackSpeedUntil > 0 && Time.time >= attackSpeedUntil)
                {
                    attackSpeedUntil = 0;
                    economy.Recalculate();
                }
                if (economy.Owns(3083) && health.maxHealth >= 2650 && Time.time - lastIncoming > 6)
                    health.Heal(health.maxHealth * .05f * .25f);
                if (economy.Owns(1056))
                    economy.RestoreMana(.25f * (Time.time - lastHitTime < 5 ? 2f : 1f));
                if (economy.Owns(2504) && Time.time - lastMagicIncoming > 15 && !HasProc("kaenic"))
                {
                    health.AddRefreshableShield("Kaenic Rookern", health.maxHealth * .15f, health.maxHealth * .15f, 3600, DamageKind.Magic);
                    SetProc("kaenic", 15);
                }
            }
            if (economy.Trinket != 3363 && WardCharges < 2 && Time.time >= trinketNext)
            {
                WardCharges++;
                trinketNext = Time.time + TrinketRecharge(economy.Trinket);
                economy.Touch();
            }
            if (Time.time >= nextAura)
            {
                nextAura = Time.time + 1;
                AuraPassives();
            }
        }

        void RestoreLocomotion()
        {
            foreach (var pair in suspended)
                if (pair.Key)
                    pair.Key.enabled = pair.Value;
            suspended.Clear();
            wasStasis = false;
        }

        float TrinketRecharge(int id) => id == 3340 ? Mathf.Lerp(210, 90, (economy.Level - 1) / 17f) : Mathf.Lerp(160, 100, (economy.Level - 1) / 17f);

        void SetProc(string key, float seconds) => procReady[key] = Time.time + seconds;

        public string CannotActivate(int id)
        {
            var item = economy.match.catalog.Find(id);
            if (item == null || item.active == ItemActive.None)
                return "This item has no active.";
            if (!economy.match.Running || (!health.IsAlive && item.active != ItemActive.Redemption))
                return "You cannot use an item right now.";
            if (Stasis)
                return "Wait for stasis to end.";
            if (!economy.Owns(id) && economy.Trinket != id)
                return "You do not own this item.";
            if (Cooldown(id) > 0)
                return "Ready in " + Mathf.CeilToInt(Cooldown(id)) + "s.";
            if (item.active == ItemActive.Refillable && Charges(id) <= 0)
                return "Refill at the fountain.";
            if ((item.active == ItemActive.Potion || item.active == ItemActive.Refillable) && potionUntil > Time.time)
                return "A potion is already healing you.";
            if ((id == 3340 || id == 3364) && WardCharges <= 0)
                return "No trinket charges.";
            if (item.active == ItemActive.SupportWard && supportCharges <= 0)
                return "Refill your wards at the fountain.";
            if (!string.IsNullOrEmpty(item.requiredChampion))
                return "Requires " + item.requiredChampion + ".";
            return null;
        }

        public bool Activate(int id, Vector3 origin, Vector3 direction)
        {
            string denied = CannotActivate(id);
            if (denied != null)
            {
                economy.match.Notify(denied);
                return false;
            }
            var item = economy.match.catalog.Find(id);
            var allies = Allies(6);
            var enemies = Enemies(6);
            Vector3 aim = AimGround(origin, direction, item.active == ItemActive.Farsight ? 40 : 15);
            bool success = true;
            switch (item.active)
            {
                case ItemActive.Potion:
                    economy.Consume(id);
                    StartPotion(120, 15);
                    break;
                case ItemActive.Refillable:
                    economy.inventory.First(s => s.id == id).charges--;
                    economy.Touch();
                    StartPotion(100, 12);
                    break;
                case ItemActive.Stasis:
                case ItemActive.SingleStasis:
                    stasisUntil = Time.time + 2.5f;
                    if (item.active == ItemActive.SingleStasis)
                        economy.TransformItem(2420, 2421);
                    Pulse(player.Feet, new Color(1, .75f, .15f), 1.4f, 2.5f);
                    break;
                case ItemActive.ElixirIron:
                case ItemActive.ElixirSorcery:
                case ItemActive.ElixirWrath:
                    economy.Consume(id);
                    elixir = id;
                    elixirUntil = Time.time + 180;
                    economy.Recalculate();
                    Pulse(player.Feet, new Color(.8f, .35f, .9f), 1, 1);
                    break;
                case ItemActive.Ward:
                case ItemActive.SupportWard:
                case ItemActive.Farsight:
                    if (id == 2055)
                        economy.Consume(id);
                    else if (id == 3340)
                    {
                        WardCharges--;
                        trinketNext = Time.time + TrinketRecharge(id);
                    }
                    else if (item.active == ItemActive.SupportWard)
                        supportCharges--;
                    float life = id == 2055 || id == 3363 ? 0 : Mathf.Lerp(90, 120, (economy.Level - 1) / 17f);
                    RiftVisionWard.Place(economy, aim, id, life);
                    economy.Touch();
                    break;
                case ItemActive.Oracle:
                    WardCharges--;
                    trinketNext = Time.time + TrinketRecharge(id);
                    StartCoroutine(Scan());
                    economy.Touch();
                    break;
                case ItemActive.Shurelya:
                    foreach (var ally in allies)
                        Speed(ally, .30f, 4);
                    Speed(health, .30f, 4);
                    Pulse(player.Feet, Color.cyan, 6, .5f);
                    break;
                case ItemActive.Ghostblade:
                    Speed(health, .20f, 6);
                    break;
                case ItemActive.Actualizer:
                    actualizerUntil = Time.time + 8;
                    Pulse(player.Feet, Color.cyan, 1.2f, 1);
                    break;
                case ItemActive.Quicksilver:
                case ItemActive.Mercurial:
                    health.ClearCrowdControl();
                    if (item.active == ItemActive.Mercurial)
                        Speed(health, .5f, 1.5f);
                    Pulse(player.Feet, Color.white, 1, .5f);
                    break;
                case ItemActive.Randuin:
                    foreach (var enemy in enemies)
                        enemy.ApplySlow(.3f, 2);
                    Pulse(player.Feet, new Color(.5f, .65f, 1), 5, .7f);
                    break;
                case ItemActive.Hydra:
                case ItemActive.Ravenous:
                case ItemActive.Profane:
                case ItemActive.Stridebreaker:
                    foreach (var target in Enemies(4.5f))
                    {
                        float mult = item.active == ItemActive.Hydra ? 1 : item.active == ItemActive.Stridebreaker ? .8f : item.active == ItemActive.Profane ? (target.Health < target.maxHealth * .5f ? 1.3f : 1) : 1;
                        float dealt = Deal(target, player.Stats.AttackDamage * mult, DamageKind.Physical);
                        if (item.active == ItemActive.Ravenous)
                            health.Heal(dealt * economy.Lifesteal);
                        if (item.active == ItemActive.Stridebreaker)
                        {
                            target.ApplySlow(.65f, 3);
                            if (target.countsAsChampion)
                                Speed(health, .35f, 3);
                        }
                    }
                    Pulse(player.Feet, new Color(1, .35f, .2f), 4.5f, .35f);
                    break;
                case ItemActive.Titanic:
                    titanicReady = true;
                    player.ResetAttackTimer();
                    Pulse(player.Feet, new Color(1, .65f, .2f), 1, .3f);
                    break;
                case ItemActive.Locket:
                    foreach (var ally in allies)
                        Shield(ally, Mathf.Lerp(290, 360, (economy.Level - 1) / 17f), 2.5f, true);
                    Shield(health, Mathf.Lerp(290, 360, (economy.Level - 1) / 17f), 2.5f, true);
                    Pulse(player.Feet, Color.yellow, 6, .5f);
                    break;
                case ItemActive.Redemption:
                    StartCoroutine(Redeem(aim));
                    Pulse(aim, Color.green, 5.5f, 2.5f);
                    break;
                case ItemActive.Mikael:
                    var purified = AimAlly(origin, direction, 10) ?? health;
                    purified.ClearCrowdControl();
                    HealAlly(purified, Mathf.Lerp(100, 250, (economy.Level - 1) / 17f));
                    Pulse(purified.AimPosition, Color.white, 1, .5f);
                    break;
                case ItemActive.Vow:
                    var allyChampion = AimAlly(origin, direction, 10);
                    if (!allyChampion || allyChampion == health || !allyChampion.countsAsChampion)
                    {
                        economy.match.Notify("Aim at an allied champion.");
                        success = false;
                    }
                    else
                    {
                        VowedAlly = allyChampion;
                        economy.match.Notify("Pledged to " + allyChampion.name);
                    }
                    break;
                case ItemActive.Gunblade:
                    var shocked = AimEnemy(origin, direction, 7);
                    if (!shocked || !shocked.countsAsChampion)
                    {
                        economy.match.Notify("Aim at an enemy champion in range.");
                        success = false;
                    }
                    else
                    {
                        Deal(shocked, 175 + economy.AbilityPower * .3f, DamageKind.Magic);
                        shocked.ApplySlow(.75f, 1.5f);
                        Beam(origin, shocked.AimPosition, Color.magenta);
                    }
                    break;
                case ItemActive.Rocketbelt:
                    Dash(direction, 2.75f);
                    foreach (var target in Enemies(8).Where(t => Vector3.Dot((t.AimPosition - player.AttackOrigin).normalized, direction.normalized) > .45f))
                    {
                        Deal(target, 100 + economy.AbilityPower * .1f, DamageKind.Magic);
                        Beam(player.AttackOrigin, target.AimPosition, Color.cyan);
                    }
                    break;
                default:
                    success = false;
                    break;
            }
            if (!success)
                return false;
            float cooldown = item.activeCooldown;
            if (id == 3363)
                cooldown = Mathf.Lerp(198, 99, (economy.Level - 1) / 17f);
            if (cooldown > 0 && id != 3340 && id != 3364)
                ready[Group(id)] = Time.time + cooldown;
            economy.Touch();
            economy.match.Notify(item.name + " activated");
            return true;
        }

        public void StartPotion(float amount, float seconds)
        {
            if (potion != null)
                StopCoroutine(potion);
            potionUntil = Time.time + seconds;
            potion = StartCoroutine(Potion(amount, seconds));
        }

        IEnumerator Potion(float amount, float seconds)
        {
            float left = amount;
            while (left > .001f && health.IsAlive)
            {
                float heal = Mathf.Min(left, amount / seconds * Time.deltaTime);
                left -= heal;
                health.Heal(heal);
                yield return null;
            }
            potionUntil = 0;
            potion = null;
        }

        IEnumerator Scan()
        {
            float until = Time.time + 8;
            while (Time.time < until && health.IsAlive)
            {
                RiftVisionWard.Reveal(player.Feet, health.team, 7.5f, .4f);
                Pulse(player.Feet, Color.red, 7.5f, .3f);
                yield return new WaitForSeconds(.25f);
            }
        }

        IEnumerator Redeem(Vector3 aim)
        {
            yield return new WaitForSeconds(2.5f);
            foreach (var target in RiftActor.All.Where(a => a && a.health.IsAlive && !a.structure && Vector3.Distance(a.health.AimPosition, aim) < 5.5f).Select(a => a.health).ToArray())
            {
                if (target.team == health.team)
                    HealAlly(target, Mathf.Lerp(150, 350, (economy.Level - 1) / 17f));
                else if (target.countsAsChampion)
                    Deal(target, target.maxHealth * .1f, DamageKind.True);
            }
            Pulse(aim, Color.green, 5.5f, .5f);
        }

        public Vector3 AimGround(Vector3 origin, Vector3 direction, float range)
        {
            Vector3 point;
            if (Physics.Raycast(origin, direction, out var hit, range, player.worldMask, QueryTriggerInteraction.Ignore))
                point = hit.point;
            else
                point = origin + Vector3.ProjectOnPlane(direction, Vector3.up).normalized * Mathf.Min(range, 4);
            if (economy.match.Ground(point, out var grounded))
                point = grounded;
            return point + Vector3.up * .08f;
        }

        bool Dash(Vector3 direction, float range)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (direction.sqrMagnitude < .1f)
                return false;
            var cc = player.origin.GetComponent<CharacterController>();
            float distance = range;
            if (Physics.CapsuleCast(player.Feet + Vector3.up * .4f, player.Feet + Vector3.up * 1.4f, .25f, direction, out var block, range, player.worldMask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0, block.distance - .12f);
            Vector3 to = player.Feet + direction * distance;
            if (distance < .1f || !economy.match.Ground(to, out var ground))
                return false;
            Vector3 delta = ground - player.Feet + Vector3.up * .08f;
            if (cc && cc.enabled)
                cc.Move(delta);
            else
                player.origin.transform.position += delta;
            return true;
        }

        Combatant[] Enemies(float range) => RiftActor.All.Where(a => a && a.Targetable && !a.structure && a.health != health && a.health.team != health.team && Geo.FlatDistance(player.Feet, a.transform.position) <= range).Select(a => a.health).ToArray();

        Combatant[] Allies(float range) => RiftActor.All.Where(a => a && a.Targetable && !a.structure && a.health != health && a.health.team == health.team && Geo.FlatDistance(player.Feet, a.transform.position) <= range).Select(a => a.health).ToArray();

        Combatant AimEnemy(Vector3 origin, Vector3 direction, float range) => AimTarget(origin, direction, range, false);

        Combatant AimAlly(Vector3 origin, Vector3 direction, float range) => AimTarget(origin, direction, range, true);

        Combatant AimTarget(Vector3 origin, Vector3 direction, float range, bool ally)
        {
            return RiftActor.All.Where(a => a && a.Targetable && a.health != health && (a.health.team == health.team) == ally && !a.structure && Vector3.Distance(origin, a.health.AimPosition) <= range && Vector3.Dot(direction.normalized, (a.health.AimPosition - origin).normalized) > .85f)
                .OrderBy(a => Vector3.Angle(direction, a.health.AimPosition - origin)).Select(a => a.health).FirstOrDefault();
        }

        public void Speed(Combatant target, float amount, float duration)
        {
            if (target == health)
            {
                moveBonus = Mathf.Max(moveBonus, amount);
                moveUntil = Time.time + duration;
                economy.Recalculate();
            }
            else
                target.ApplySpeed(amount, duration);
        }

        public void Shield(Combatant target, float amount, float duration, bool decay = false)
        {
            target.AddShield(amount * (1 + economy.HealShieldPower), duration, null, decay);
            SupportHeal(target);
        }

        public void HealAlly(Combatant target, float amount)
        {
            target.Heal(amount * (1 + economy.HealShieldPower));
            SupportHeal(target);
        }

        float Deal(Combatant target, float amount, DamageKind kind)
        {
            return target.TakeDamage(new DamageHit(health, player.AttackOrigin, amount, kind) { isItemEffect = true });
        }
        static readonly Dictionary<Color, Material> effectMaterials = new();

        Material EffectMaterial(Color color)
        {
            if (!effectMaterials.TryGetValue(color, out var material) || !material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", color);
                material.hideFlags = HideFlags.HideAndDontSave;
                effectMaterials[color] = material;
            }
            return material;
        }

        public void Pulse(Vector3 at, Color color, float radius, float duration)
        {
            var go = new GameObject("Item effect ring");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = EffectMaterial(color);
            line.useWorldSpace = false;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = .035f;
            line.loop = true;
            line.positionCount = 48;
            go.transform.position = at + Vector3.up * .1f;
            for (int n = 0; n < 48; n++)
            {
                float a = n * Mathf.PI * 2 / 48;
                line.SetPosition(n, new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius));
            }
            Destroy(go, duration);
        }

        void Beam(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("Item bolt");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = EffectMaterial(color);
            line.positionCount = 2;
            line.startColor = line.endColor = color;
            line.startWidth = .04f;
            line.endWidth = .015f;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            Destroy(go, .2f);
        }
    }
}
