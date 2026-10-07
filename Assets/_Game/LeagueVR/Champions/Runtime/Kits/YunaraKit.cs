using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Yunara. Attacks build Spirit; Cultivation of Spirit unleashes it for attack speed and splashing hits.
    /// Arc of Judgment fires a slowing bead, Kanmei's Steps speeds her up, and Transcend One's Self upgrades every spell.
    /// </summary>
    public class YunaraKit : RangedKit
    {
        const int SpiritMax = 8;
        int spirit;
        float unleashedUntil, transcendentUntil;

        public bool Transcendent => Time.time < transcendentUntil;
        bool Unleashed => Time.time < unleashedUntil;

        public override string StateText => Transcendent ? "TRANSCENDENT" : Unleashed ? "UNLEASHED" : $"SPIRIT {spirit}/{SpiritMax}";

        public override string SlotStatus(int slot) => slot switch
        {
            0 when spirit < SpiritMax && !Transcendent => $"SPIRIT {spirit}/{SpiritMax}",
            3 when Transcendent => $"TRANSCEND {transcendentUntil - Time.time:0}s",
            _ => null,
        };

        public override void OnEquip() => ResetState();

        public override void OnUnequip() => ResetState();

        public override void OnDeath() => ResetState();

        void ResetState()
        {
            spirit = 0;
            unleashedUntil = transcendentUntil = 0;
        }

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            spirit = Mathf.Min(SpiritMax, spirit + (target.countsAsChampion ? 2 : 1));
            // Cultivation of Spirit passive: on-hit magic damage.
            Player.Hit(target, 5 + AP * .2f, DamageKind.Magic, "Passive");
            if (Player.Economy && Player.Economy.LastAttackCritical)
                Player.Hit(target, AD * .3f + AP * .1f, DamageKind.Magic, "Passive");
            if (Unleashed || Transcendent)
                foreach (var near in Player.EnemiesAround(target.AimPosition, 1.8f))
                    if (near != target)
                        Player.Hit(near, AD * .25f, DamageKind.Magic, "Q");
        }

        public override bool Cast(int slot) => slot switch
        {
            0 => Cultivation(),
            1 => ArcOfJudgment(),
            2 => KanmeisSteps(),
            _ => Transcend(),
        };

        bool Cultivation()
        {
            if (spirit < SpiritMax && !Transcendent)
            {
                RiftMatch.Instance?.Notify("Build 8 Spirit with basic attacks first.");
                return false;
            }
            if (!Player.Commit(0))
                return false;
            spirit = 0;
            unleashedUntil = Time.time + 5;
            Health.ApplyAttackSpeed(ByRank(0, .4f, .55f, .7f, .85f, 1f), 5);
            Player.Spark(Player.AttackOrigin, .25f);
            return true;
        }

        bool ArcOfJudgment()
        {
            if (!Player.Commit(1))
                return false;
            bool upgraded = Transcendent;
            var bead = Player.Projectile(Player.OffHandOrigin, Player.OffHandDirection, ByRank(1, 50, 95, 140, 185, 230) + BonusAD * .5f + AP * .5f, DamageKind.Magic, "W", upgraded ? 30 : 13, 16, upgraded);
            bead.radius = upgraded ? .35f : .2f;
            bead.hit = (t, d) => t.ApplySlow(.55f, 1.3f);
            if (upgraded)
                Player.Beam(Player.OffHandOrigin, Player.OffHandOrigin + Player.OffHandDirection * 16, .14f);
            return true;
        }

        bool KanmeisSteps()
        {
            if (Transcendent)
            {
                // Untouchable Shadow: a short dash instead of the speed boost.
                var aim = Player.PlanarDirection(Player.AttackDirection);
                if (Health.Rooted || !Player.StepPoint(Player.Feet + aim * 2.4f, out var point) || !Player.Commit(2))
                    return false;
                Player.MoveFeet(point);
                return true;
            }
            if (!Player.Commit(2))
                return false;
            Health.ApplySpeed(ByRank(2, .2f, .25f, .3f, .35f, .4f), 2);
            return true;
        }

        bool Transcend()
        {
            if (!Player.Commit(3))
                return false;
            transcendentUntil = Time.time + 12;
            spirit = SpiritMax;
            Health.ApplyAttackSpeed(.4f, 12);
            Player.ReduceCooldown(1, 999);
            Player.ReduceCooldown(2, 999);
            Player.Ring(Player.Feet, 1, .5f);
            return true;
        }
    }
}
