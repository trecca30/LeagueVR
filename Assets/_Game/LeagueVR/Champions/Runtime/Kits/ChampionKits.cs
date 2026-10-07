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
    }

    /// <summary>Champions whose basic attack is a homing missile fired from the right hand.</summary>
    public abstract class RangedKit : ChampionKit
    {
        protected virtual float MissileSpeed => 22;

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var target = Player.AimTarget(Player.AttackReach, origin, direction, true);
            if (!target)
                return;
            var missile = Player.Projectile(origin, (target.AimPosition - origin).normalized, AD, DamageKind.Physical, "Attack1", MissileSpeed, Player.AttackReach + 4, false);
            missile.basic = true;
            missile.homing = target;
            missile.hit = (t, dealt) =>
            {
                Player.Emit("Hit", t.AimPosition, missile.direction);
                OnAttackHit(t, dealt);
            };
        }

        protected virtual void OnAttackHit(Combatant target, float dealt) { }
    }
}
