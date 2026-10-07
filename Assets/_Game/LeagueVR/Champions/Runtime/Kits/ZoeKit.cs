using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Zoe, the Aspect of Twilight. Paddle Star can be redirected while in flight, Spell Thief casts
    /// shards dropped by minions, the bubble puts targets to sleep and Portal Jump blinks out and back.
    /// </summary>
    public class ZoeKit : RangedKit
    {
        static readonly string[] ShardNames = { "HEAL", "BLINK", "BOLT" };

        AbilityProjectile star;
        float qWindow, passiveUntil;
        int shard = -1, minionKills;
        Vector3 portalFrom;
        bool portalActive;

        public override bool CanRecast(int slot) => slot == 0 && star && Time.time < qWindow;

        public override string SlotStatus(int slot) => slot switch
        {
            0 when star && Time.time < qWindow => "REDIRECT",
            1 => shard >= 0 ? "SHARD: " + ShardNames[shard] : "NO SHARD",
            _ => null,
        };

        public override string StateText => shard >= 0 ? "W: SHARD READY" : star ? "Q: REDIRECT" : "W: FIND A SHARD";

        public override void OnEquip() => ResetState();

        public override void OnUnequip()
        {
            ReturnFromPortal();
            ResetState();
        }

        void ResetState()
        {
            star = null;
            qWindow = passiveUntil = 0;
            shard = -1;
            minionKills = 0;
            portalActive = false;
        }

        public override void OnDeath()
        {
            ReturnFromPortal();
            star = null;
        }

        public override void Tick()
        {
            if (shard >= 0)
                return;
            // Touching a shard with the left hand picks it up.
            foreach (var s in SpellShard.Active)
                if (s && s.owner == Player && Vector3.Distance(s.transform.position, Player.leftHand.position) < .3f)
                {
                    TakeShard(s);
                    Player.Emit("Shard", Player.leftHand.position, Vector3.up);
                    break;
                }
        }

        void TakeShard(SpellShard s)
        {
            shard = s.kind;
            Object.Destroy(s.gameObject);
        }

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            // More Sparkles: the next attack after a spell deals bonus magic damage.
            if (Time.time < passiveUntil)
            {
                Player.Hit(target, Mathf.Lerp(16, 130, (Player.Level - 1) / 17f) + AP * .2f, DamageKind.Magic, "Passive");
                passiveUntil = 0;
            }
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => PaddleStar(),
            1 => SpellThief(),
            2 => SleepyTroubleBubble(),
            _ => PortalJump(),
        };

        bool PaddleStar()
        {
            if (star && Time.time < qWindow)
            {
                if (!Player.Commit(0, true))
                    return false;
                star.Redirect(Player.AttackDirection);
                star = null;
                passiveUntil = Time.time + 5;
                return true;
            }
            if (!Player.Commit(0))
                return false;
            passiveUntil = Time.time + 5;
            float baseDamage = ByRank(0, 50, 80, 110, 140, 170) + AP * .6f;
            star = Player.Projectile(Player.AttackOrigin, Player.AttackDirection, baseDamage, DamageKind.Magic, "Q", 9, 7, false);
            // Damage grows with distance travelled, up to 2.5x.
            star.damageAtDistance = d => baseDamage * (1 + Mathf.Min(1.5f, d * .08f));
            qWindow = Time.time + 1.25f;
            star.hit = (t, d) =>
            {
                foreach (var n in Player.EnemiesAround(t.AimPosition, 1.2f))
                    if (n != t)
                        Player.Hit(n, baseDamage * .6f, DamageKind.Magic, "Q");
            };
            return true;
        }

        bool SpellThief()
        {
            if (shard < 0)
            {
                SpellShard near = null;
                foreach (var s in SpellShard.Active)
                    if (s && s.owner == Player && Geo.FlatDistance(s.transform.position, Player.Feet) < 1.7f)
                    {
                        near = s;
                        break;
                    }
                if (!near)
                {
                    RiftMatch.Instance?.Notify("Collect a glowing spell shard from fallen minions.");
                    return false;
                }
                TakeShard(near);
            }
            if (!Player.Commit(1))
                return false;
            int stolen = shard;
            shard = -1;
            passiveUntil = Time.time + 5;
            if (stolen == 0)
                Health.Heal(90 + 10 * Player.Level);
            else if (stolen == 1)
            {
                if (Player.StepPoint(Player.Feet + Geo.FlatDirection(Player.OffHandDirection, Vector3.forward) * 2.5f, out var at))
                    Player.MoveFeet(at);
            }
            else
            {
                var t = Player.AimTarget(10, Player.OffHandOrigin, Player.OffHandDirection);
                if (t)
                    Player.Hit(t, 90, DamageKind.True, "W");
            }
            Player.StartCoroutine(ShardMissiles());
            Health.ApplySpeed(.25f, 2);
            return true;
        }

        IEnumerator ShardMissiles()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(.18f);
                Combatant best = null;
                float bestDistance = float.MaxValue;
                foreach (var t in Player.EnemiesAround(Player.Feet, 10))
                {
                    float d = Vector3.Distance(t.AimPosition, Player.Feet);
                    if (d < bestDistance)
                    {
                        best = t;
                        bestDistance = d;
                    }
                }
                if (!best)
                    yield break;
                var p = Player.Projectile(Player.OffHandOrigin, (best.AimPosition - Player.OffHandOrigin).normalized, ByRank(1, 25, 35, 45, 55, 65) + AP * .13f, DamageKind.Magic, "W", 16, 15, false);
                p.homing = best;
            }
        }

        bool SleepyTroubleBubble()
        {
            if (!Player.Commit(2))
                return false;
            passiveUntil = Time.time + 5;
            float damage = ByRank(2, 70, 110, 150, 190, 230) + AP * .45f;
            var bubble = Player.Projectile(Player.AttackOrigin, Player.AttackDirection, damage * .1f, DamageKind.Magic, "E", 12, 12, false);
            bubble.radius = .25f;
            bubble.hit = (t, d) => Player.StartCoroutine(Sleep(t, damage));
            return true;
        }

        IEnumerator Sleep(Combatant target, float damage)
        {
            target.ApplySlow(.5f, 1.2f);
            Player.Ring(target.transform.position, .65f, 1.2f);
            yield return new WaitForSeconds(1.2f);
            if (Player.IsEnemy(target))
                target.ApplySleep(2, damage, Health);
        }

        bool PortalJump()
        {
            if (Health.Rooted || !Player.GroundAim(Player.OffHandOrigin, Player.OffHandDirection, 6, out var ground) || !Player.ClearDestination(ground))
                return false;
            if (!Player.Commit(3))
                return false;
            passiveUntil = Time.time + 5;
            Player.StartCoroutine(Portal(ground));
            return true;
        }

        IEnumerator Portal(Vector3 at)
        {
            portalFrom = Player.Feet;
            portalActive = true;
            Player.Ring(portalFrom, .65f, 1);
            Player.MoveFeet(at);
            Player.Ring(at, .65f, 1);
            yield return new WaitForSeconds(1);
            ReturnFromPortal();
        }

        void ReturnFromPortal()
        {
            if (!portalActive || !Player)
                return;
            Player.MoveFeet(portalFrom);
            portalActive = false;
        }

        public override void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            // Every fourth enemy minion that dies near Zoe drops a spell shard (heal, blink, bolt in rotation).
            if (!victim.GetComponent<RiftMinion>() || victim.team == Health.team || Geo.FlatDistance(victim.transform.position, Player.Feet) > 12)
                return;
            if (++minionKills % 4 != 0)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Spell Thief shard";
            go.transform.position = victim.transform.position + Vector3.up * .65f;
            go.transform.localScale = Vector3.one * .22f;
            go.GetComponent<Renderer>().sharedMaterial = AbilityFx.Material(Tint);
            var s = go.AddComponent<SpellShard>();
            s.owner = Player;
            s.kind = minionKills / 4 % 3;
            s.expires = Time.time + 30;
            Player.Track(go);
        }
    }

    /// <summary>A Spell Thief pickup left by a fallen minion.</summary>
    public class SpellShard : MonoBehaviour
    {
        public static readonly List<SpellShard> Active = new();
        public int kind;
        public float expires;
        public PlayerChampion owner;

        void OnEnable() => Active.Add(this);

        void OnDisable() => Active.Remove(this);

        void Update()
        {
            transform.Rotate(0, 55 * Time.deltaTime, 0);
            if (Time.time > expires || !owner)
                Destroy(gameObject);
        }
    }
}
