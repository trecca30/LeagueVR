using System.Collections;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Aatrox, the Darkin Blade. Three-stage Q with sweet spots, chains that pull back, a short dash,
    /// and World Ender for more damage and healing. Heals from damage dealt to champions.
    /// </summary>
    public class AatroxKit : MeleeKit
    {
        int qStage;
        float qWindow, busyUntil, passiveReadyAt, worldEnderUntil;

        public bool Transcendent => Time.time < worldEnderUntil;
        public override bool Busy => Time.time < busyUntil;
        public override bool CanRecast(int slot) => slot == 0 && qStage > 0 && Time.time < qWindow;

        public override string SlotStatus(int slot) => slot switch
        {
            0 when qStage > 0 && Time.time < qWindow => "SLASH " + (qStage + 1),
            3 when Transcendent => $"WORLD ENDER {worldEnderUntil - Time.time:0}s",
            _ => null,
        };

        public override string StateText => Transcendent ? "WORLD ENDER" : Time.time >= passiveReadyAt ? "DEATHBRINGER STANCE READY" : "DARKIN BLADE";

        public override void OnEquip() => ResetState();

        public override void OnUnequip() => ResetState();

        public override void OnDeath() => ResetState();

        void ResetState()
        {
            qStage = 0;
            qWindow = busyUntil = passiveReadyAt = worldEnderUntil = 0;
        }

        public override void Tick()
        {
            if (qStage > 0 && Time.time > qWindow)
                qStage = 0;
        }

        protected override float AttackDamage(Combatant target) => AD * (Transcendent ? 1.2f : 1);

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            // Deathbringer Stance: the empowered attack deals max-health damage and heals for it.
            if (Time.time < passiveReadyAt || target.GetComponent<RiftStructure>())
                return;
            float bonus = target.maxHealth * Mathf.Lerp(.04f, .08f, (Player.Level - 1) / 17f);
            if (!target.countsAsChampion)
                bonus = Mathf.Min(bonus, 100);
            Health.Heal(Player.Hit(target, bonus, DamageKind.Magic, "Passive"));
            passiveReadyAt = Time.time + Mathf.Lerp(24, 12, (Player.Level - 1) / 17f);
        }

        public override void OnDamageDealt(Combatant target, float dealt, string ability, bool basic)
        {
            // Umbral Dash passive: heal from damage dealt to champions (stronger during World Ender).
            if (target.countsAsChampion && ability != "Passive")
                Health.Heal(dealt * (Transcendent ? .28f : .16f));
            if (!basic)
                Player.Spark(target.AimPosition, .08f);
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => DarkinBlade(),
            1 => InfernalChains(),
            2 => UmbralDash(),
            _ => WorldEnder(),
        };

        bool DarkinBlade()
        {
            bool recast = qStage > 0 && Time.time < qWindow;
            if (!Player.Commit(0, recast))
                return false;
            Player.StartCoroutine(Slash(recast ? qStage + 1 : 1));
            return true;
        }

        IEnumerator Slash(int stage)
        {
            busyUntil = Time.time + .3f;
            qStage = stage >= 3 ? 0 : stage;
            qWindow = Time.time + 4;
            var aim = Player.PlanarDirection(Player.AttackDirection);
            var from = Player.Feet;
            Player.Ring(from + aim * (stage == 3 ? 2 : 2.8f), stage == 3 ? 1.6f : .9f, .35f);
            yield return new WaitForSeconds(.25f);
            float baseDamage = ByRank(0, 10, 30, 50, 70, 90) + AD * Mathf.Lerp(.6f, .8f, (Rank(0) - 1) / 4f);
            foreach (var t in Player.EnemiesAround(from + aim * 2.4f, 3.4f))
            {
                var delta = Vector3.ProjectOnPlane(t.transform.position - from, Vector3.up);
                float forward = Vector3.Dot(delta, aim), side = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, aim)));
                bool inside = stage == 3
                    ? Vector3.Distance(t.transform.position, from + aim * 2) < 1.8f
                    : forward > .3f && forward < 4.3f && side < (stage == 2 ? 2 : .85f);
                if (!inside)
                    continue;
                bool sweet = stage == 3 ? Vector3.Distance(t.transform.position, from + aim * 2) < .9f : stage == 2 ? side > 1.0f : forward > 3;
                // Each stage hits 25% harder; the edge "sweet spot" deals 70% more and knocks up.
                Player.Hit(t, baseDamage * (1 + .25f * (stage - 1)) * (sweet ? 1.7f : 1), DamageKind.Physical, "Q");
                if (sweet)
                    t.ApplyStun(.5f);
            }
        }

        bool InfernalChains()
        {
            if (!Player.Commit(1))
                return false;
            var chains = Player.Projectile(Player.OffHandOrigin, Player.OffHandDirection, ByRank(1, 30, 40, 50, 60, 70) + AD * .4f, DamageKind.Physical, "W", 14, 10, false);
            chains.hit = (t, d) =>
            {
                t.ApplySlow(.75f, 1.5f);
                if (t.countsAsChampion || t.GetComponent<RiftObjective>())
                    Player.StartCoroutine(PullChain(t, d));
            };
            return true;
        }

        IEnumerator PullChain(Combatant target, float firstHit)
        {
            var centre = target.transform.position;
            Player.Ring(centre, 2.5f, 1.5f);
            yield return new WaitForSeconds(1.5f);
            if (Player.IsEnemy(target) && Geo.FlatDistance(target.transform.position, centre) < 2.5f)
            {
                if (RiftMatch.Instance && RiftMatch.Instance.Ground(centre, out var p))
                    target.transform.position = p;
                Player.Hit(target, firstHit, DamageKind.Physical, "W");
                target.ApplyRoot(.25f);
            }
        }

        bool UmbralDash()
        {
            var aim = Player.PlanarDirection(Player.AttackDirection);
            if (Health.Rooted || !Player.StepPoint(Player.Feet + aim * 2.4f, out var point))
                return false;
            if (!Player.Commit(2))
                return false;
            Player.MoveFeet(point);
            Player.ResetAttackTimer();
            return true;
        }

        bool WorldEnder()
        {
            if (!Player.Commit(3))
                return false;
            worldEnderUntil = Time.time + 10;
            Health.ApplySpeed(ByRank(3, .6f, .8f, 1f) * .5f, 10);
            Player.Ring(Player.Feet, 1.3f, .7f);
            // Nearby minions are feared: they stop briefly.
            foreach (var t in Player.EnemiesAround(Player.Feet, 4))
                if (t.GetComponent<RiftMinion>())
                    t.ApplyStun(ByRank(3, 3, 3.5f, 4));
            return true;
        }

        public override void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            if (hit.source == Health && victim.countsAsChampion && Transcendent)
                worldEnderUntil = Mathf.Max(worldEnderUntil, Time.time + 5);
        }
    }
}
