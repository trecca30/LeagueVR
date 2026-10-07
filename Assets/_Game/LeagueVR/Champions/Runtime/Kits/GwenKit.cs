using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen, the Hallowed Seamstress. Melee scissor swings, a channelled Q, a protective mist,
    /// a short dash that empowers attacks and a three-cast needle volley thrown from the left hand.
    /// </summary>
    public class GwenKit : ChampionKit
    {
        GwenTuning t;
        GameObject needle;
        Material threadMaterial;

        public int QStacks { get; private set; }
        public int RStage { get; private set; }
        public Vector3 MistCenter { get; private set; }
        public bool MistActive => Time.time < mistUntil && Health.IsAlive;
        public float MistRemaining => Mathf.Max(0, mistUntil - Time.time);
        public bool Empowered => Time.time < empoweredUntil;

        float stackUntil, mistUntil, wRecastAt, empoweredUntil, rWindowUntil, rNext;
        bool qCasting, mistMoved, eRefunded;
        readonly HashSet<Combatant> needleVictims = new();
        LineRenderer mistRing;
        readonly List<LineRenderer> mistThreads = new();

        public override bool Busy => qCasting;
        public override float BonusAttackSpeed => Empowered ? ByRank(2, .2f, .35f, .5f, .65f, .8f) : 0;
        public override float BonusAttackRange => Empowered ? .35f : 0;
        public override float BonusResistance => MistActive ? t.wResistance + AP * .07f : 0;
        public override bool CanRecast(int slot) => (slot == 1 && MistActive && !mistMoved) || (slot == 3 && RStage > 0 && Time.time < rWindowUntil);

        public override string SlotStatus(int slot) => slot switch
        {
            0 when QStacks > 0 && Player.Cooldown(0) <= 0 => $"READY {QStacks}/4",
            1 when MistActive => $"MIST {MistRemaining:0.0}s",
            3 when RStage > 0 => "RECAST " + (RStage + 1),
            _ => null,
        };

        public override string StateText => Empowered ? "SKIP 'N SLASH: EMPOWERED" : QStacks > 0 ? $"SNIP STACKS {QStacks}/4" : "A THOUSAND CUTS";

        public override void OnEquip()
        {
            t = Definition.kitTuning as GwenTuning;
            if (!t)
                t = ScriptableObject.CreateInstance<GwenTuning>();
            needle = Definition.kitPrefab;
            threadMaterial = Definition.kitMaterial ? Definition.kitMaterial : AbilityFx.Material(Tint);
            ResetState();
            mistRing = Line("Hallowed Mist boundary", .035f, 81);
            for (int i = 0; i < 12; i++)
                mistThreads.Add(Line("Hallowed Mist thread", .015f, 3));
            SetMistVisible(false);
        }

        public override void OnUnequip()
        {
            ResetState();
            if (mistRing)
                Object.Destroy(mistRing.gameObject);
            foreach (var thread in mistThreads)
                if (thread)
                    Object.Destroy(thread.gameObject);
            mistThreads.Clear();
        }

        void ResetState()
        {
            QStacks = RStage = 0;
            mistUntil = empoweredUntil = stackUntil = rWindowUntil = rNext = 0;
            qCasting = mistMoved = eRefunded = false;
            needleVictims.Clear();
        }

        public override void OnDeath()
        {
            ResetState();
            SetMistVisible(false);
        }

        public override void Tick()
        {
            if (Time.time > stackUntil)
                QStacks = 0;
            if (RStage > 0 && Time.time > rWindowUntil)
                RStage = 0;
            if (MistActive && Geo.FlatDistance(Player.Feet, MistCenter) > t.wRadius)
            {
                // The first exit carries the mist with Gwen once, a second exit ends it.
                if (!mistMoved)
                {
                    MistCenter = Player.Feet;
                    mistMoved = true;
                }
                else
                    mistUntil = 0;
            }
            UpdateMistVisual();
        }

        // ---------- Passive: A Thousand Cuts ----------

        float ApplyPassive(Combatant target)
        {
            if (!target || !target.IsAlive || target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                return 0;
            float amount = target.maxHealth * (t.passiveMaxHealthFraction + AP * .00006f);
            if (target.GetComponent<RiftObjective>())
                amount = Mathf.Min(amount, 10 + .25f * AP);
            float dealt = Player.Hit(target, amount, DamageKind.Magic, "Passive");
            if (target.countsAsChampion && dealt > 0)
                Health.Heal(Mathf.Min(dealt * t.passiveChampionHealFraction, Mathf.Lerp(12, 40, (Player.Level - 1) / 17f) + .07f * AP));
            return dealt;
        }

        // ---------- Basic attack ----------

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var targets = Player.SweepTargets(origin, direction, Player.AttackReach, .45f);
            if (targets.Count == 0)
                return;
            var target = targets[0];
            float dealt = Player.Hit(target, AD, DamageKind.Physical, "Attack1", true, origin);
            if (dealt <= 0)
                return;
            ApplyPassive(target);
            if (Empowered)
            {
                Player.Hit(target, t.eBonusDamage + AP * .2f, DamageKind.Magic, "E");
                if (!eRefunded)
                {
                    Player.RefundCooldown(2, t.eCooldownRefundFraction);
                    eRefunded = true;
                }
            }
            QStacks = Mathf.Min(4, QStacks + 1);
            stackUntil = Time.time + t.qStackLifetime;
            Player.Emit("Hit", target.AimPosition, direction);
        }

        // ---------- Abilities ----------

        public override bool Cast(int slot) => slot switch
        {
            0 => SnipSnip(),
            1 => HallowedMist(),
            2 => SkipNSlash(),
            _ => Needlework(),
        };

        bool SnipSnip()
        {
            Vector3 direction = Player.PlanarDirection(Player.AttackDirection);
            if (!Player.Commit(0, false, Player.AttackOrigin, direction))
                return false;
            int snips = 2 + QStacks;
            QStacks = 0;
            Player.StartCoroutine(Snip(snips, direction));
            return true;
        }

        IEnumerator Snip(int snips, Vector3 forward)
        {
            qCasting = true;
            var audio = Player.GetComponent<ChampionAudio>();
            for (int i = 0; i < snips && Health.IsAlive; i++)
            {
                // Heading is locked at cast; E may still reposition the snip origin.
                Vector3 start = Player.AttackOrigin;
                Player.Emit("Snip", start, forward);
                audio?.Snip(i, snips);
                bool final = i == snips - 1;
                foreach (var target in Player.SweepTargets(start, forward, t.qRange, t.qHalfWidth, true))
                {
                    if (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                        continue;
                    Vector3 delta = Vector3.ProjectOnPlane(target.AimPosition - start, Vector3.up);
                    bool centre = Vector3.Cross(delta, forward).magnitude <= t.qCenterHalfWidth + .15f;
                    float amount = final ? t.qFinalDamage + AP * .35f : t.qSnipDamage + AP * .05f;
                    if (target.GetComponent<RiftMinion>())
                        amount *= target.Health / target.maxHealth < t.qExecuteThreshold ? 1.5f : t.qMinionModifier;
                    float dealt = Player.Hit(target, amount * (centre ? .5f : 1), DamageKind.Magic, "Q", false, start);
                    if (centre)
                    {
                        dealt += Player.Hit(target, amount * .5f, DamageKind.True, "Q", false, start);
                        if (dealt > 0)
                            ApplyPassive(target);
                    }
                    if (dealt > 0)
                    {
                        Player.Emit("Hit", target.AimPosition, forward);
                        audio?.SnipHit(target.AimPosition, final);
                    }
                }
                if (i < snips - 1)
                    yield return new WaitForSeconds(t.qDuration / (snips - 1));
            }
            qCasting = false;
        }

        bool HallowedMist()
        {
            if (MistActive && !mistMoved)
            {
                if (Time.time < wRecastAt || !Player.Commit(1, true, Player.Feet, Vector3.up))
                    return false;
                MistCenter = Player.Feet;
                mistMoved = true;
                return true;
            }
            if (!Player.Commit(1, false, Player.Feet, Vector3.up))
                return false;
            MistCenter = Player.Feet;
            mistMoved = false;
            mistUntil = Time.time + t.wDuration;
            wRecastAt = Time.time + .5f;
            return true;
        }

        bool SkipNSlash()
        {
            Vector3 direction = Player.PlanarDirection(Player.AttackDirection);
            Vector3 from = Player.Feet;
            if (Health.Rooted || !Player.StepPoint(from + direction * t.eDistance, out _))
                return false;
            if (!Player.Commit(2, false, from, direction))
                return false;
            // An instantaneous, collision-checked short step avoids a forced camera animation in VR.
            Player.Dash(direction, t.eDistance);
            empoweredUntil = Time.time + t.eDuration;
            eRefunded = false;
            Player.ResetAttackTimer();
            return true;
        }

        bool Needlework()
        {
            if (Time.time < rNext)
                return false;
            bool recast = RStage > 0 && Time.time < rWindowUntil;
            Vector3 start = Player.OffHandOrigin, aim = Player.OffHandDirection.normalized;
            if (!Player.Commit(3, recast, start, aim))
                return false;
            if (!recast)
                needleVictims.Clear();
            rWindowUntil = Time.time + t.rWindow;
            int stage = RStage + 1;
            RStage = stage >= 3 ? 0 : stage;
            rNext = Time.time + t.rRecastDelay;
            Player.StartCoroutine(Volley(2 * stage - 1, start, aim));
            return true;
        }

        IEnumerator Volley(int count, Vector3 start, Vector3 aim)
        {
            // Needles fan out like League's 1 / 3 / 5 needle casts; the centre needle flies first.
            float spread = 7.5f;
            for (int i = 0; i < count && Health.IsAlive; i++)
            {
                int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                var direction = Quaternion.AngleAxis(side * spread, Vector3.up) * aim;
                var p = AbilityProjectile.Launch(Player, start, direction, t.rDamage + AP * .1f, DamageKind.Magic, "R", t.rSpeed, t.rRange, true, Tint, needle);
                p.ignoreStructures = true;
                p.slowFor = target => needleVictims.Add(target) ? t.rSlowMultiplier : .85f;
                p.slowDuration = t.rSlowDuration;
                p.hit = (target, dealt) =>
                {
                    ApplyPassive(target);
                    Player.Emit("Hit", target.AimPosition, direction);
                    Player.GetComponent<ChampionAudio>()?.NeedleHit(target);
                };
                yield return new WaitForSeconds(.06f);
            }
        }

        // ---------- Mist: untargetable from outside, except by towers ----------

        public override bool HiddenFrom(Combatant attacker)
        {
            return MistActive && attacker && !attacker.GetComponent<RiftStructure>() && Geo.FlatDistance(attacker.AimPosition, MistCenter) > t.wRadius;
        }

        public override bool Blocks(DamageHit hit)
        {
            return MistActive && hit.source && !hit.source.GetComponent<RiftStructure>() && Geo.FlatDistance(hit.source.AimPosition, MistCenter) > t.wRadius;
        }

        LineRenderer Line(string name, float width, int count)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = threadMaterial;
            line.startWidth = line.endWidth = width;
            line.positionCount = count;
            line.useWorldSpace = true;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        void SetMistVisible(bool visible)
        {
            if (mistRing)
                mistRing.gameObject.SetActive(visible);
            foreach (var thread in mistThreads)
                if (thread)
                    thread.gameObject.SetActive(visible);
        }

        void UpdateMistVisual()
        {
            bool active = MistActive;
            SetMistVisible(active);
            if (!active || !mistRing)
                return;
            Vector3 centre = MistCenter + Vector3.up * .06f;
            for (int i = 0; i < 81; i++)
            {
                float a = i * 2 * Mathf.PI / 80;
                mistRing.SetPosition(i, centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * t.wRadius);
            }
            for (int i = 0; i < mistThreads.Count; i++)
            {
                float a = i * 2 * Mathf.PI / mistThreads.Count + Time.time * .1f;
                Vector3 pos = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * t.wRadius;
                mistThreads[i].SetPosition(0, pos);
                mistThreads[i].SetPosition(1, pos + Vector3.up * .6f);
                mistThreads[i].SetPosition(2, pos + Vector3.up * (1.1f + .25f * Mathf.Sin(Time.time + i)));
            }
        }
    }
}
