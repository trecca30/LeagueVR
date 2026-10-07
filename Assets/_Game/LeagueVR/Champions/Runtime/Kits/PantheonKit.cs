using System.Collections;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Pantheon, the Unbreakable Spear. Attacks and spells build Mortal Will; at five stacks the next spell is empowered.
    /// Comet Spear, Shield Vault stun, Aegis Assault blocks frontal damage and Grand Starfall leaps across the map.
    /// </summary>
    public class PantheonKit : MeleeKit
    {
        int will;
        float guardUntil, busyUntil;

        public bool Guarding => Time.time < guardUntil;
        public override bool Busy => Time.time < busyUntil;
        public override bool BlocksCasts => Busy;
        public override string StateText => Guarding ? "AEGIS ASSAULT" : will >= 5 ? "MORTAL WILL READY" : $"MORTAL WILL {will}/5";
        public override string SlotStatus(int slot) => slot == 2 && Guarding ? "SHIELDING" : null;

        public override void OnEquip() => ResetState();

        public override void OnUnequip() => ResetState();

        public override void OnDeath() => ResetState();

        void ResetState()
        {
            will = 0;
            guardUntil = busyUntil = 0;
        }

        protected override void OnAttackHit(Combatant target, float dealt) => will = Mathf.Min(5, will + 1);

        /// <summary>Spends five stacks for an empowered spell, otherwise adds one stack.</summary>
        bool SpendWill()
        {
            bool empowered = will >= 5;
            will = empowered ? 0 : Mathf.Min(5, will + 1);
            return empowered;
        }

        /// <summary>Aegis Assault blocks damage from in front of the shield arm (towers always get through).</summary>
        public override bool Blocks(DamageHit hit)
        {
            if (!Guarding || hit.source && hit.source.GetComponent<RiftStructure>())
                return false;
            Vector3 front = Player.DesktopMode ? Player.head.transform.forward : Player.leftHand.forward;
            Vector3 from = hit.source ? hit.source.AimPosition : hit.origin;
            return Vector3.Dot(from - Player.head.transform.position, front) > .01f;
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => CometSpear(),
            1 => ShieldVault(),
            2 => AegisAssault(),
            _ => GrandStarfall(),
        };

        bool CometSpear()
        {
            if (!Player.Commit(0))
                return false;
            bool empowered = SpendWill();
            float damage = ByRank(0, 70, 100, 130, 160, 190) + BonusAD * 1.15f + (empowered ? Mathf.Lerp(20, 240, (Player.Level - 1) / 17f) : 0);
            var spear = Player.Projectile(Player.AttackOrigin, Player.AttackDirection, damage, DamageKind.Physical, "Q", 22, 12, true);
            spear.hit = (t, d) =>
            {
                // Low-health targets take bonus damage.
                if (t.Health / t.maxHealth < .2f)
                    Player.Hit(t, d * .7f, DamageKind.Physical, "Q");
            };
            return true;
        }

        bool ShieldVault()
        {
            var aimed = Player.AimTarget(7, Player.OffHandOrigin, Player.OffHandDirection);
            if (!aimed || !Player.StepPoint(aimed.transform.position - (aimed.transform.position - Player.Feet).normalized * .9f, out var ground))
                return false;
            if (!Player.Commit(1))
                return false;
            bool empowered = SpendWill();
            Player.MoveFeet(ground);
            Player.Hit(aimed, aimed.maxHealth * (ByRank(1, 5, 5.5f, 6, 6.5f, 7) + AP * .01f) / 100 + 60, DamageKind.Physical, "W");
            aimed.ApplyStun(1);
            if (empowered)
                for (int i = 0; i < 3; i++)
                    Player.Hit(aimed, AD * .4f, DamageKind.Physical, "W", true);
            return true;
        }

        bool AegisAssault()
        {
            if (!Player.Commit(2))
                return false;
            bool empowered = SpendWill();
            guardUntil = Time.time + (empowered ? 2.5f : 1.5f);
            Player.StartCoroutine(Aegis());
            return true;
        }

        IEnumerator Aegis()
        {
            float until = guardUntil;
            while (Time.time < until && Health.IsAlive)
            {
                foreach (var t in Player.EnemiesAround(Player.Feet, 3.5f))
                {
                    var d = t.AimPosition - Player.OffHandOrigin;
                    if (Vector3.Dot(d.normalized, Player.OffHandDirection) > .65f)
                        Player.Hit(t, (ByRank(2, 55, 105, 155, 205, 255) + BonusAD * 1.5f) * .25f / 5, DamageKind.Physical, "E");
                }
                yield return new WaitForSeconds(.25f);
            }
            // The shield bash at the end.
            foreach (var t in Player.EnemiesAround(Player.Feet + Player.PlanarDirection(Player.OffHandDirection) * 1.5f, 1.8f))
                Player.Hit(t, ByRank(2, 55, 105, 155, 205, 255) + BonusAD * 1.5f, DamageKind.Physical, "E");
        }

        bool GrandStarfall()
        {
            if (Health.Rooted || !Player.GroundAim(Player.OffHandOrigin, Player.OffHandDirection, 24, out var ground) || !Player.ClearDestination(ground))
                return false;
            if (!Player.Commit(3))
                return false;
            Player.StartCoroutine(Starfall(ground));
            return true;
        }

        IEnumerator Starfall(Vector3 at)
        {
            busyUntil = Time.time + 1;
            Player.Ring(at, 3.4f, 1);
            yield return new WaitForSeconds(1);
            if (!Health.IsAlive || !Player.ClearDestination(at))
                yield break;
            Player.MoveFeet(at);
            will = 5;
            foreach (var t in Player.EnemiesAround(at + Vector3.up, 3.4f))
            {
                Player.Hit(t, ByRank(3, 300, 500, 700) + AP, DamageKind.Magic, "R");
                t.ApplySlow(.5f, 2);
            }
            Player.Ring(at, 3.4f, .5f);
        }
    }
}
