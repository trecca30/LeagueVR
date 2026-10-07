using UnityEngine;
namespace LeagueVR.Match
{
    public class RiftStructure : RiftActor, IDamageGuard
    {
        public StructureKind kind;
        public int lane;
        public RiftStructure prerequisite;
        public Transform visual, muzzle;
        public float range = 7.75f;
        public float RespawnAt { get; private set; }
        public float BonusResistance => 0;

        public bool Vulnerable
        {
            get
            {
                var match = RiftMatch.Instance;
                if (!match || !match.Running)
                    return false;
                if (prerequisite && prerequisite.health.IsAlive)
                    return false;
                if (kind == StructureKind.NexusTurret || kind == StructureKind.Nexus)
                {
                    if (!match.InhibitorDown(health.team))
                        return false;
                    if (kind == StructureKind.Nexus && !match.NexusTurretsDown(health.team))
                        return false;
                }
                return true;
            }
        }

        public bool Blocks(DamageHit hit) => !Vulnerable;
        float nextAttack, heatUntil;
        int heat;
        DamageHit lastHit;
        RiftActor target;

        protected override void Awake()
        {
            base.Awake();
            structure = true;
            health.onDeath.AddListener(Die);
            health.Damaged += (h, a) => lastHit = h;
            health.RefreshGuards();
        }

        public void ResetStructure()
        {
            RespawnAt = 0;
            nextAttack = 0;
            heat = 0;
            health.ResetHealth();
            if (visual)
                visual.gameObject.SetActive(true);
            foreach (var c in GetComponentsInChildren<Collider>(true))
                c.enabled = true;
        }

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!match || !match.Running)
                return;
            if (!health.IsAlive)
            {
                if (RespawnAt > 0 && match.Seconds >= RespawnAt)
                {
                    ResetStructure();
                    GetComponent<LeagueUnitAudio>()?.Respawn();
                    if (kind == StructureKind.NexusTurret)
                        health.SetHealth(health.maxHealth * .4f);
                    match.Notify(name + " has respawned");
                }
                return;
            }
            if (kind == StructureKind.Inhibitor || kind == StructureKind.Nexus)
                return;
            if (kind == StructureKind.OuterTurret)
            {
                health.armor = health.magicResistance = Mathf.Max(0, 60 - Mathf.Floor(Mathf.Max(0, match.Seconds - 660) / 60) * 15);
            }
            if (Time.time < nextAttack)
                return;
            if (!target || !target.Targetable || GwenAbilities.FlatDistance(transform.position, target.transform.position) > range + target.radius)
                target = Target(this, range, true);
            if (!target)
                return;
            nextAttack = Time.time + 1.2f;
            if (Time.time > heatUntil)
                heat = 0;
            float damage = Mathf.Min(168, 152 + Mathf.Floor(match.Seconds / 60));
            if (target.health.countsAsChampion)
            {
                damage *= 1 + Mathf.Min(3, heat) * .5f;
                heat++;
                heatUntil = Time.time + 5;
            }
            else
            {
                var minion = target.GetComponent<RiftMinion>();
                if (minion)
                    damage = target.health.maxHealth * (minion.kind == MinionKind.Melee ? .45f : minion.kind == MinionKind.Caster ? .7f : minion.kind == MinionKind.Cannon ? .14f : .05f);
            }
            GetComponent<LeagueUnitAudio>()?.Attack(target.health.countsAsChampion);
            RiftMissile.Launch(health, target.health, muzzle ? muzzle.position : health.AimPosition, damage, 18, match.TeamMaterial(health.team), DamageKind.Physical, .22f);
        }

        void Die()
        {
            var match = RiftMatch.Instance;
            if (!match)
                return;
            GetComponent<LeagueUnitAudio>()?.Death();
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;
            if (visual)
                visual.gameObject.SetActive(false);
            match.AwardUnit(health, lastHit, kind == StructureKind.Inhibitor ? 50 : kind == StructureKind.Nexus ? 0 : 250, 0);
            match.Notify(name + " destroyed");
            if (kind == StructureKind.Inhibitor)
                RespawnAt = match.Seconds + match.rules.inhibitorRespawn;
            else if (kind == StructureKind.NexusTurret)
                RespawnAt = match.Seconds + match.rules.nexusTurretRespawn;
            else if (kind == StructureKind.Nexus)
                match.Finish(health.team != match.player.Health.team);
        }
    }
}
