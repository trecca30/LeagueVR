using System.Collections;
using System.Linq;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Brand, the Burning Vengeance. Spells set enemies Ablaze; three stacks on a champion or large monster detonate.
    /// Sear stuns burning targets, Pillar of Flame and Conflagration spread fire, Pyroclasm bounces five times.
    /// </summary>
    public class BrandKit : RangedKit
    {
        public override string StateText => "BLAZE: 3 STACKS DETONATE";

        public override void OnUnequip() => BlazeDebuff.ClearFrom(Player);

        public override void OnDeath() => BlazeDebuff.ClearFrom(Player);

        static bool Ablaze(Combatant t) => t && t.GetComponent<BlazeDebuff>() is BlazeDebuff b && b.Burning;

        void Blaze(Combatant target)
        {
            if (!target || target.GetComponent<RiftStructure>())
                return;
            var b = target.GetComponent<BlazeDebuff>();
            if (!b)
                b = target.gameObject.AddComponent<BlazeDebuff>();
            b.Apply(Player);
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => Sear(),
            1 => PillarOfFlame(),
            2 => Conflagration(),
            _ => Pyroclasm(),
        };

        bool Sear()
        {
            if (!Player.Commit(0))
                return false;
            var fire = Player.Projectile(Player.AttackOrigin, Player.AttackDirection, ByRank(0, 70, 100, 130, 160, 190) + AP * .65f, DamageKind.Magic, "Q", 16, 12, false);
            fire.hit = (t, d) =>
            {
                if (Ablaze(t))
                    t.ApplyStun(1.5f);
                Blaze(t);
            };
            return true;
        }

        bool PillarOfFlame()
        {
            if (!Player.GroundAim(Player.OffHandOrigin, Player.OffHandDirection, 12, out var ground) || !Player.Commit(1))
                return false;
            Player.Ring(ground, 2.3f, .65f);
            Player.StartCoroutine(Pillar(ground));
            return true;
        }

        IEnumerator Pillar(Vector3 centre)
        {
            yield return new WaitForSeconds(.6f);
            float damage = ByRank(1, 75, 120, 165, 210, 255) + AP * .6f;
            foreach (var t in Player.EnemiesAround(centre + Vector3.up, 2.3f))
            {
                Player.Hit(t, damage * (Ablaze(t) ? 1.25f : 1), DamageKind.Magic, "W");
                Blaze(t);
            }
            Player.Spark(centre + Vector3.up, .5f);
        }

        bool Conflagration()
        {
            var aimed = Player.AimTarget(10, Player.AttackOrigin, Player.AttackDirection);
            if (!aimed || !Player.Commit(2))
                return false;
            bool burning = Ablaze(aimed);
            float damage = ByRank(2, 60, 85, 110, 135, 160) + AP * .45f;
            foreach (var victim in Player.EnemiesAround(aimed.AimPosition, burning ? 3.5f : 1.8f))
            {
                Player.Hit(victim, damage, DamageKind.Magic, "E");
                Blaze(victim);
            }
            return true;
        }

        bool Pyroclasm()
        {
            var target = Player.AimTarget(12, Player.OffHandOrigin, Player.OffHandDirection);
            if (!target || !Player.Commit(3))
                return false;
            Player.StartCoroutine(Bounce(target));
            return true;
        }

        IEnumerator Bounce(Combatant first)
        {
            Combatant current = first, previous = null;
            float damage = ByRank(3, 100, 175, 250) + AP * .25f;
            for (int i = 0; i < 5 && Player.IsEnemy(current) && Health.IsAlive; i++)
            {
                var start = previous ? previous.AimPosition : Player.OffHandOrigin;
                Player.Beam(start, current.AimPosition, .15f);
                Player.Hit(current, damage, DamageKind.Magic, "R");
                current.ApplySlow(.6f, .5f);
                Blaze(current);
                previous = current;
                yield return new WaitForSeconds(.25f);
                // Bounces prefer champions, then the nearest other enemy; with nobody else nearby it returns through Brand.
                current = Player.EnemiesAround(previous.AimPosition, 5)
                    .Where(t => t != previous)
                    .OrderBy(t => t.countsAsChampion ? 0 : 1)
                    .ThenBy(t => Vector3.Distance(t.AimPosition, previous.AimPosition))
                    .FirstOrDefault();
                if (!current && Geo.FlatDistance(previous.transform.position, Player.Feet) < 5)
                {
                    Player.Beam(previous.AimPosition, Health.AimPosition, .1f);
                    yield return new WaitForSeconds(.12f);
                    current = previous;
                }
            }
        }

        public override void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            // Blaze: enemies that die while burning restore mana.
            if (hit.source == Health && Ablaze(victim))
                Player.Economy?.RestoreMana(victim.countsAsChampion ? Mathf.Lerp(3, 7, (Player.Level - 1) / 17f) / 100 * Player.Economy.MaxMana : 20);
        }
    }

    /// <summary>Brand's Blaze: burns for a few seconds, stacks to three and detonates on champions and large monsters.</summary>
    public class BlazeDebuff : MonoBehaviour
    {
        public PlayerChampion owner;
        public int stacks;
        public float until, detonateAt, immuneUntil;
        Combatant target;
        float tick;

        public bool Burning => Time.time < until;

        public static void ClearFrom(PlayerChampion owner)
        {
            foreach (var b in FindObjectsByType<BlazeDebuff>())
                if (b.owner == owner)
                    Destroy(b);
        }

        void Awake() => target = GetComponent<Combatant>();

        public void Apply(PlayerChampion who)
        {
            owner = who;
            if (!Burning)
                stacks = 0;
            stacks = Mathf.Min(3, stacks + 1);
            until = Time.time + 4;
            bool large = target.countsAsChampion || target.GetComponent<RiftObjective>();
            if (stacks >= 3 && large && Time.time > immuneUntil && detonateAt == 0)
            {
                detonateAt = Time.time + 2;
                immuneUntil = Time.time + 6;
            }
        }

        void Update()
        {
            if (!target.IsAlive || !owner || !(owner.Kit is BrandKit))
            {
                Destroy(this);
                return;
            }
            if (Burning && Time.time >= tick)
            {
                tick = Time.time + .5f;
                owner.Hit(target, target.maxHealth * .003f * stacks, DamageKind.Magic, "Passive");
            }
            if (detonateAt > 0 && Time.time >= detonateAt)
            {
                detonateAt = 0;
                owner.Ring(target.transform.position, 2.5f, .35f);
                foreach (var t in owner.EnemiesAround(target.AimPosition, 2.5f))
                    owner.Hit(t, t.maxHealth * Mathf.Lerp(.09f, .13f, (owner.Level - 1) / 17f) + owner.Stats.AbilityPower * .0002f * t.maxHealth, DamageKind.Magic, "Passive");
            }
            if (!Burning && detonateAt == 0)
                Destroy(this);
        }
    }
}
