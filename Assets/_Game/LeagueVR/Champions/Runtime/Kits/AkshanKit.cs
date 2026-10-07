using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Akshan, the Rogue Sentinel. Every third hit procs Dirty Fighting, attacks fire a second shot when standing still,
    /// Going Rogue camouflages him, Heroic Swing grapples terrain and Comeuppance unloads charged bullets.
    /// </summary>
    public class AkshanKit : RangedKit
    {
        readonly Dictionary<Combatant, (int count, float until)> dirty = new();
        float stealthUntil, busyUntil;

        public override bool Busy => Time.time < busyUntil;
        public override bool BlocksCasts => Busy;
        public override string StateText => Camouflaged ? "CAMOUFLAGED" : "DIRTY FIGHTING";
        public override string SlotStatus(int slot) => slot == 1 && Camouflaged ? $"ROGUE {stealthUntil - Time.time:0}s" : null;

        /// <summary>Camouflage breaks near enemy champions and inside enemy turret range.</summary>
        public bool Camouflaged
        {
            get
            {
                if (Time.time >= stealthUntil)
                    return false;
                var feet = Player.Feet;
                foreach (var actor in RiftActor.All)
                {
                    if (!actor || actor.health.team == Health.team || !actor.health.IsAlive || actor.health == Health)
                        continue;
                    if (actor.health.countsAsChampion && Geo.FlatDistance(actor.transform.position, feet) < 4)
                        return false;
                    if (actor is RiftStructure s && s.kind != StructureKind.Inhibitor && s.kind != StructureKind.Nexus && Geo.FlatDistance(s.transform.position, feet) < s.range)
                        return false;
                }
                return true;
            }
        }

        public override bool HiddenFrom(Combatant attacker) => Camouflaged;

        public override void OnEquip() => ResetState();

        public override void OnUnequip() => ResetState();

        public override void OnDeath() => ResetState();

        void ResetState()
        {
            dirty.Clear();
            stealthUntil = busyUntil = 0;
        }

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            stealthUntil = 0;
            base.BasicAttack(origin, direction);
        }

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            DirtyFighting(target);
            Player.StartCoroutine(SecondShot(target));
        }

        IEnumerator SecondShot(Combatant target)
        {
            var standing = Player.Feet;
            yield return new WaitForSeconds(.13f);
            if (!Health.IsAlive || !Player.IsEnemy(target))
                yield break;
            // Moving cancels the second shot and grants a burst of speed instead.
            if (Geo.FlatDistance(standing, Player.Feet) > .12f)
            {
                Health.ApplySpeed(.15f, 1);
                yield break;
            }
            if (Geo.FlatDistance(target.AimPosition, Player.AttackOrigin) > Player.AttackReach)
                yield break;
            Player.Hit(target, AD * (target.countsAsChampion ? .5f : 1), DamageKind.Physical, "Attack1", true);
            DirtyFighting(target);
            Player.Beam(Player.AttackOrigin, target.AimPosition, .035f);
        }

        void DirtyFighting(Combatant target)
        {
            if (target.GetComponent<RiftStructure>())
                return;
            int n = (dirty.TryGetValue(target, out var e) && e.until > Time.time ? e.count : 0) + 1;
            dirty[target] = (n, Time.time + 5);
            if (n < 3)
                return;
            dirty[target] = (0, 0);
            Player.Hit(target, Mathf.Lerp(20, 175, (Player.Level - 1) / 17f) + AP * .6f, DamageKind.Magic, "Passive");
            if (target.countsAsChampion)
                Health.AddRefreshableShield("Dirty Fighting", Mathf.Lerp(40, 300, (Player.Level - 1) / 17f), 300, 2);
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => Avengerang(),
            1 => GoingRogue(),
            2 => HeroicSwing(),
            _ => Comeuppance(),
        };

        bool Avengerang()
        {
            if (!Player.Commit(0))
                return false;
            stealthUntil = 0;
            var b = Player.Projectile(Player.AttackOrigin, Player.AttackDirection, ByRank(0, 5, 25, 45, 65, 85) + AD * .8f, DamageKind.Physical, "Q", 14, 11, true);
            b.returning = true;
            b.hit = (t, d) =>
            {
                DirtyFighting(t);
                b.range = Mathf.Max(b.range, b.travelled + 4);
            };
            return true;
        }

        bool GoingRogue()
        {
            if (!Player.Commit(1))
                return false;
            stealthUntil = Time.time + 6;
            Player.Spark(Player.OffHandOrigin, .25f);
            return true;
        }

        bool HeroicSwing()
        {
            if (Health.Rooted || !Player.TerrainRay(Player.AttackOrigin, Player.AttackDirection, 12, out var anchor))
                return false;
            var hook = anchor.point;
            var toward = Geo.FlatDirection(hook - Player.Feet, Vector3.forward);
            if (!Player.StepPoint(Player.Feet + toward * 2.5f + Vector3.Cross(Vector3.up, toward) * .6f, out var point))
                return false;
            if (!Player.Commit(2))
                return false;
            stealthUntil = 0;
            Player.Beam(Player.AttackOrigin, hook, .025f);
            Player.MoveFeet(point);
            var t = Player.AimTarget(Player.AttackReach, Player.AttackOrigin, Player.AttackDirection);
            if (t)
            {
                Player.Hit(t, ByRank(2, 30, 45, 60, 75, 90) + BonusAD * .175f, DamageKind.Physical, "E", true);
                DirtyFighting(t);
            }
            return true;
        }

        bool Comeuppance()
        {
            var target = Player.AimTarget(25, Player.OffHandOrigin, Player.OffHandDirection);
            if (!target || !Player.Commit(3))
                return false;
            stealthUntil = 0;
            busyUntil = Time.time + .6f;
            Player.StartCoroutine(Bullets(target));
            return true;
        }

        IEnumerator Bullets(Combatant target)
        {
            Player.Ring(target.transform.position, .8f, .6f);
            yield return new WaitForSeconds(.6f);
            int bullets = (int)ByRank(3, 5, 6, 7);
            for (int i = 0; i < bullets && Player.IsEnemy(target) && Health.IsAlive; i++)
            {
                // Each bullet deals more the lower the target's health (up to 300%).
                float missing = 1 - target.Health / target.maxHealth;
                var p = Player.Projectile(Player.OffHandOrigin, (target.AimPosition - Player.OffHandOrigin).normalized, (ByRank(3, 20, 25, 30) + BonusAD * .1f) * (1 + 2 * missing), DamageKind.Physical, "R", 25, 35, false);
                p.homing = target;
                yield return new WaitForSeconds(.12f);
            }
        }

        public override void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            // Takedowns on champions refresh Heroic Swing.
            if (hit.source == Health && victim.countsAsChampion)
                Player.ReduceCooldown(2, 999);
        }
    }
}
