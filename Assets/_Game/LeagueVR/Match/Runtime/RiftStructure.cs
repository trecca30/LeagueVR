using UnityEngine;
using LeagueVR.Champions;
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
        public bool IsTurret => kind != StructureKind.Inhibitor && kind != StructureKind.Nexus;

        /// <summary>
        /// Ground radius of the visible base (turret plinth, inhibitor crystal ring, Nexus platform). Units path around
        /// it and attackers stand at its edge, so nothing walks through the model.
        /// </summary>
        public float Footprint => kind == StructureKind.Nexus ? 3.6f : kind == StructureKind.Inhibitor ? 1.8f : 1.4f;
        /// <summary>What this turret is currently shooting (minions use it for "turret attacking an ally" aggro).</summary>
        public RiftActor Target => target;

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
            // Attack ranges are measured to the edge of the base, like League's structure radius, and the collider
            // covers the whole base so weapons and the player's body meet the model where it is drawn.
            radius = Footprint;
            var body = GetComponentInChildren<CapsuleCollider>(true);
            if (body)
                body.radius = Footprint / Mathf.Max(.01f, Mathf.Max(body.transform.lossyScale.x, body.transform.lossyScale.z));
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
            if (!target || !target.Targetable || Geo.FlatDistance(transform.position, target.transform.position) > range + target.radius)
                target = Target(this, range, true);
            if (!target)
                return;
            nextAttack = Time.time + 1.2f;
            if (Time.time > heatUntil)
                heat = 0;
            float damage = match.rules.TurretDamage(kind, match.Seconds);
            if (target.health.countsAsChampion)
            {
                // Heating up: each consecutive shot on a champion hits harder, up to +150%.
                damage *= 1 + Mathf.Min(3, heat) * .5f;
                heat++;
                heatUntil = Time.time + 5;
            }
            else if (target is RiftMinion minion)
                damage = target.health.maxHealth * RiftRules.TurretMinionFraction(minion.kind, kind);
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
