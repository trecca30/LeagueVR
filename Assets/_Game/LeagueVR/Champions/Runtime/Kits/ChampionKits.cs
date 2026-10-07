using System;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>Maps champion ids to their kit implementation. Register new champions here.</summary>
    public static class ChampionKits
    {
        public static ChampionKit Create(ChampionId id) => id switch
        {
            ChampionId.Gwen => new GwenKit(),
            ChampionId.Zoe => new ZoeKit(),
            ChampionId.Aatrox => new AatroxKit(),
            ChampionId.Akshan => new AkshanKit(),
            ChampionId.Brand => new BrandKit(),
            ChampionId.Pantheon => new PantheonKit(),
            ChampionId.Yunara => new YunaraKit(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No kit registered for this champion."),
        };
    }

    /// <summary>Champions whose basic attack is a weapon swing in front of the right hand.</summary>
    public abstract class MeleeKit : ChampionKit
    {
        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var targets = Player.SweepTargets(origin, direction, Player.AttackReach, .45f);
            var target = targets.Count > 0 ? targets[0] : null;
            if (!target)
                return;
            float dealt = Player.Hit(target, AttackDamage(target), DamageKind.Physical, "Attack1", true, origin);
            if (dealt > 0)
            {
                Player.Emit("Hit", target.AimPosition, direction);
                OnAttackHit(target, dealt);
            }
        }

        protected virtual float AttackDamage(Combatant target) => AD;

        protected virtual void OnAttackHit(Combatant target, float dealt) { }

        /// <summary>A weapon prop on the first-person body (Pantheon's spear) strikes physically.</summary>
        public override bool WeaponEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            return Player.Body is ChampionVRAvatar avatar && avatar.isActiveAndEnabled && avatar.WeaponEdge(out from, out to);
        }

        public override void WeaponHit(Combatant target, Vector3 point, Vector3 swing)
        {
            float dealt = Player.Hit(target, AttackDamage(target), DamageKind.Physical, "Attack1", true, point);
            if (dealt > 0)
            {
                Player.Emit("Hit", point, swing);
                OnAttackHit(target, dealt);
            }
        }
    }

    /// <summary>Champions whose basic attack is a homing missile fired from the right hand.</summary>
    public abstract class RangedKit : ChampionKit
    {
        protected virtual float MissileSpeed => 22;
        /// <summary>How far off the pointing direction (degrees) a target can be and still be picked.</summary>
        protected virtual float AimCone => 14;

        /// <summary>The enemy a basic attack would fire at right now (pointing soft lock, then a thick ray).</summary>
        public Combatant AttackTarget(Vector3 origin, Vector3 direction)
        {
            var target = Player.ConeTarget(origin, direction, Player.AttackReach, AimCone, true);
            return target ? target : Player.AimTarget(Player.AttackReach, origin, direction, true);
        }

        public override bool HasAttackTarget(Vector3 origin, Vector3 direction) => AttackTarget(origin, direction);

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var target = AttackTarget(origin, direction);
            if (!target)
                return;
            var missile = LaunchAttack(origin, target);
            missile.basic = true;
            missile.homing = target;
            missile.hit = (t, dealt) =>
            {
                Player.Emit("Hit", t.AimPosition, missile.direction);
                OnAttackHit(t, dealt);
            };
        }

        /// <summary>Spawns the attack missile; kits replace the look.</summary>
        protected virtual AbilityProjectile LaunchAttack(Vector3 origin, Combatant target)
            => Player.Projectile(origin, (target.AimPosition - origin).normalized, AD, DamageKind.Physical, "Attack1", MissileSpeed, Player.AttackReach + 4, false);

        protected virtual void OnAttackHit(Combatant target, float dealt) { }
    }
}
