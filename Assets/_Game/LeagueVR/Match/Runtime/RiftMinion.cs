using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Match
{
    /// <summary>
    /// A lane minion. Marches in one of three formation columns along a precomputed lane path, picks targets with
    /// League's aggro priorities (sticky targets, call for help) and fights with the original attack timings.
    /// </summary>
    public class RiftMinion : RiftActor
    {
        public MinionKind kind;
        public Transform visual;
        public Vector3[] route;
        public MinionStats stats;
        public int lane;

        /// <summary>All living minions per lane; neighbour checks only look at the same lane.</summary>
        public static readonly List<RiftMinion>[] ByLane = { new(), new(), new() };

        const float AcquisitionRange = 7f, CorridorLimit = 3.8f, CallForHelpRange = 10f, ThinkInterval = .25f;
        static int thinkSlot;

        public int FormationIndex { get; private set; }
        public int Waypoint => waypoint;
        public float MarchAt { get; private set; }
        public RiftActor Target => target;
        public bool Moving { get; private set; }

        Vector3[] path;
        int waypoint = 1, groundPhase;
        float nextThink, nextAttack, born, targetTier = 99;
        RiftActor target;
        DamageHit lastHit;
        RiftMinionMotion motion;
        LeagueUnitAudio audio;

        protected override void Awake()
        {
            base.Awake();
            born = Time.time;
            motion = GetComponentInChildren<RiftMinionMotion>();
            audio = GetComponent<LeagueUnitAudio>();
            health.onDeath.AddListener(Die);
            health.Damaged += (hit, amount) => lastHit = hit;
            groundPhase = thinkSlot++ % 4;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (lane >= 0 && lane < ByLane.Length && !ByLane[lane].Contains(this))
                ByLane[lane].Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            foreach (var list in ByLane)
                list.Remove(this);
        }

        public void Initialize(RiftMatch match, int team, int lane, MinionKind kind, Vector3[] route, int member = 1)
        {
            foreach (var list in ByLane)
                list.Remove(this);
            this.kind = kind;
            this.lane = lane;
            this.route = route;
            FormationIndex = Mathf.Clamp(member, 0, 2);
            path = match.LanePaths != null ? match.LanePaths.Path(lane, team, FormationIndex) : route;
            waypoint = 1;
            // Melee lead, casters follow: the back of the column waits a beat before marching.
            MarchAt = Time.time + ((kind == MinionKind.Melee || kind == MinionKind.Caster) ? (2 - FormationIndex) * match.rules.minionSpawnSpacing : 0);
            nextThink = Time.time + (thinkSlot++ % 8) * ThinkInterval / 8;
            stats = match.rules.Stats(kind, match.Seconds);
            health.team = team;
            health.countsAsChampion = false;
            health.maxHealth = stats.health;
            health.armor = stats.armor;
            health.magicResistance = stats.magicResistance;
            health.ResetHealth();
            if (lane >= 0 && lane < ByLane.Length)
                ByLane[lane].Add(this);
        }

        // ---------- Target selection ----------

        /// <summary>
        /// League's minion priority (lower is more important): 1 champion hitting an allied champion, 2 minion hitting an
        /// allied champion, 3 minion hitting an allied minion, 4 turret hitting an allied minion, 5 champion hitting an
        /// allied minion, 6 nearest minion, 7 nearest champion, 8 structures.
        /// </summary>
        float Tier(RiftActor actor)
        {
            int team = health.team;
            if (actor.health.countsAsChampion)
            {
                var victim = actor.health.LastHitTarget;
                bool recent = Time.time - actor.health.LastHitTime < 2f && victim && victim.team == team;
                if (recent && victim.countsAsChampion)
                    return 1;
                if (recent && victim.GetComponent<RiftMinion>() && Geo.FlatDistance(victim.transform.position, transform.position) < CallForHelpRange)
                    return 5;
                return 7;
            }
            if (actor is RiftMinion minion)
            {
                var victim = minion.target ? minion.target.health : null;
                if (victim && victim.team == team)
                    return victim.countsAsChampion ? 2 : 3;
                return 6;
            }
            if (actor is RiftStructure turret && turret.Target is RiftMinion attacked && attacked.health.team == team)
                return 4;
            return 8;
        }

        bool Eligible(RiftActor actor, out float distance)
        {
            distance = 0;
            if (!actor || actor == this || actor.neutral || actor.health.team == health.team || !actor.Targetable || !actor.health.IsTargetableBy(health))
                return false;
            if (actor is RiftMinion minion && minion.lane != lane)
                return false;
            if (actor is RiftStructure structure && structure.lane >= 0 && structure.lane != lane)
                return false;
            distance = Geo.FlatDistance(transform.position, actor.transform.position) - actor.radius;
            if (distance > AcquisitionRange)
                return false;
            // Minions never chase into the jungle: targets must stay close to the lane.
            return actor.structure || LocalCorridorDistance(actor.transform.position) <= CorridorLimit;
        }

        float LocalCorridorDistance(Vector3 point)
        {
            float best = float.PositiveInfinity;
            int from = Mathf.Max(1, waypoint - 10), to = Mathf.Min(path.Length - 1, waypoint + 14);
            for (int i = from; i <= to; i++)
                best = Mathf.Min(best, Geo.FlatDistanceToSegment(point, path[i - 1], path[i]));
            return best;
        }

        void Think()
        {
            bool keep = target && Eligible(target, out _);
            if (keep)
                targetTier = Tier(target);
            RiftActor best = null;
            float bestTier = 99, bestDistance = float.MaxValue;
            foreach (var actor in All)
            {
                if (!Eligible(actor, out float distance))
                    continue;
                float tier = Tier(actor);
                if (tier < bestTier || tier == bestTier && distance < bestDistance)
                {
                    best = actor;
                    bestTier = tier;
                    bestDistance = distance;
                }
            }
            // Targets are sticky: only a strictly higher priority (a call for help) pulls a minion off its target.
            if (!keep || best && bestTier < targetTier)
            {
                target = best;
                targetTier = best ? bestTier : 99;
            }
        }

        // ---------- Movement and combat ----------

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!match || !match.Running || !health.IsAlive)
                return;
            if (health.Stunned || Time.time < MarchAt)
            {
                SetMoving(false);
                return;
            }
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + ThinkInterval;
                Think();
            }
            float speed = match.rules.MinionSpeed(match.Seconds) * match.rules.metresPerLeagueUnit * health.SlowMultiplier * health.SpeedMultiplier;
            Vector3 goal;
            bool chasing = false;
            if (target && target.Targetable)
            {
                float range = Range(target);
                Vector3 delta = Geo.Flat(target.transform.position - transform.position);
                Face(delta);
                if (delta.magnitude <= range)
                {
                    SetMoving(false);
                    if (Time.time >= nextAttack)
                        BeginAttack(target);
                    return;
                }
                // Spread around the target by formation column instead of stacking on one point.
                Vector3 toward = delta.normalized;
                goal = target.transform.position - toward * (range * .82f) + Vector3.Cross(Vector3.up, toward) * (FormationIndex - 1) * .85f;
                chasing = true;
            }
            else
            {
                AdvanceWaypoint();
                goal = path[waypoint];
            }
            Vector3 step = Vector3.ClampMagnitude(Geo.Flat(goal - transform.position), speed * Time.deltaTime);
            step += Separation(speed);
            step = Vector3.ClampMagnitude(step, speed * Time.deltaTime * 1.2f);
            if (step.sqrMagnitude < 1e-8f)
            {
                SetMoving(false);
                return;
            }
            if (chasing)
                step = AvoidWalls(match, step);
            var next = transform.position + step;
            next.y = GroundHeight(match, next, chasing);
            transform.position = next;
            if (!chasing)
                Face(step);
            SetMoving(true);
            if (Time.time - born > 600)
                Destroy(gameObject);
        }

        void AdvanceWaypoint()
        {
            // Rejoin the path ahead after a fight rather than walking back to a waypoint already passed.
            if (target == null && waypoint < path.Length - 1)
                waypoint = Mathf.Max(waypoint, RiftLanePaths.NearestAhead(path, transform.position, waypoint, 6));
            while (waypoint < path.Length - 1 && Geo.FlatDistanceSqr(transform.position, path[waypoint]) < .65f * .65f)
                waypoint++;
            // Also advance once the minion has walked past the waypoint along the lane direction.
            if (waypoint < path.Length - 1)
            {
                Vector3 segment = Geo.Flat(path[waypoint] - path[waypoint - 1]);
                if (Vector3.Dot(Geo.Flat(transform.position - path[waypoint]), segment) > 0)
                    waypoint++;
            }
        }

        Vector3 Separation(float speed)
        {
            Vector3 push = Vector3.zero;
            if (lane < 0 || lane >= ByLane.Length)
                return push;
            float maxPush = speed * Time.deltaTime * .7f;
            foreach (var other in ByLane[lane])
            {
                if (other == this || !other.health.IsAlive)
                    continue;
                Vector3 away = Geo.Flat(transform.position - other.transform.position);
                float min = radius + other.radius + .15f;
                float d2 = away.sqrMagnitude;
                if (d2 >= min * min)
                    continue;
                float d = Mathf.Sqrt(d2);
                if (d < .001f)
                {
                    away = transform.right * (FormationIndex == 0 ? -1 : 1);
                    d = .001f;
                }
                push += away / d * Mathf.Min(maxPush, (min - d) * .3f);
            }
            // Structures are solid: anyone inside a base (pushed by the wave, chasing a target) is moved out of it.
            var match = RiftMatch.Instance;
            if (match)
                foreach (var structure in match.structures)
                {
                    if (!structure || !structure.health.IsAlive)
                        continue;
                    Vector3 away = Geo.Flat(transform.position - structure.transform.position);
                    float min = structure.Footprint + radius;
                    float d2 = away.sqrMagnitude;
                    if (d2 >= min * min)
                        continue;
                    float d = Mathf.Sqrt(d2);
                    push += (d > .001f ? away / d : transform.right) * Mathf.Min(speed * Time.deltaTime * 1.5f, min - d);
                }
            return push;
        }

        /// <summary>Slides along walls when chasing off the precomputed path; stops if the way is fully blocked.</summary>
        Vector3 AvoidWalls(RiftMatch match, Vector3 step)
        {
            float length = step.magnitude;
            var origin = transform.position + Vector3.up * .55f;
            if (!Physics.SphereCast(origin, .22f, step / length, out var hit, length + .05f, match.WorldMask, QueryTriggerInteraction.Ignore))
                return step;
            var slide = Geo.Flat(Vector3.ProjectOnPlane(step, hit.normal));
            if (slide.sqrMagnitude < 1e-8f || Physics.SphereCast(origin, .22f, slide.normalized, out _, slide.magnitude + .05f, match.WorldMask, QueryTriggerInteraction.Ignore))
                return Vector3.zero;
            return slide;
        }

        float GroundHeight(RiftMatch match, Vector3 position, bool chasing)
        {
            // Marching minions read the height from their grounded path; only chasing minions sample the terrain.
            if (!chasing && waypoint > 0)
            {
                Vector3 a = path[waypoint - 1], b = path[waypoint];
                Vector3 d = Geo.Flat(b - a);
                float t = d.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(Geo.Flat(position - a), d) / d.sqrMagnitude) : 1;
                return Mathf.Lerp(a.y, b.y, t);
            }
            if ((Time.frameCount + groundPhase) % 2 != 0)
                return transform.position.y;
            return match.Ground(position + Vector3.up * .3f, out var ground) && Mathf.Abs(ground.y - transform.position.y) < 1.2f ? ground.y : transform.position.y;
        }

        void SetMoving(bool moving)
        {
            Moving = moving;
            motion?.Locomotion(moving);
        }

        float Range(RiftActor victim) => Mathf.Max(.65f, stats.range * RiftMatch.Instance.rules.metresPerLeagueUnit) + victim.radius;

        void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-12 * Time.deltaTime));
        }

        void BeginAttack(RiftActor victim)
        {
            float interval = stats.attackInterval * health.AttackIntervalMultiplier;
            nextAttack = Time.time + interval;
            float delay = motion ? motion.Attack(interval) : stats.attackInterval * .3f;
            StartCoroutine(Strike(victim, delay, motion ? motion.AttackVariant : 0));
        }

        IEnumerator Strike(RiftActor victim, float delay, int variant)
        {
            yield return new WaitForSeconds(delay);
            var match = RiftMatch.Instance;
            if (health.Stunned || !health.IsAlive || !victim || !victim.Targetable || !victim.health.IsTargetableBy(health) || !match || !match.Running || Geo.FlatDistance(transform.position, victim.transform.position) > Range(victim) + .35f)
                yield break;
            float damage = stats.damage;
            var victimStructure = victim as RiftStructure;
            if (victim is RiftMinion)
                damage += victim.health.Health * stats.minionOnHit;
            else if (victimStructure)
            {
                // Minions deal 60% to structures; siege minions 84% to turrets; super minions only 12.5% to inhibitors and the Nexus.
                bool turret = victimStructure.IsTurret;
                damage *= kind == MinionKind.Cannon && turret ? .84f : kind == MinionKind.Super && !turret ? .125f : .6f;
            }
            else if (victim.health.countsAsChampion)
                damage *= .6f;
            audio?.Attack(victim.health.countsAsChampion, variant);
            if (kind == MinionKind.Caster || kind == MinionKind.Cannon)
                RiftMissile.Launch(health, victim.health, health.AimPosition, damage, 12, match.TeamMaterial(health.team));
            else
            {
                victim.health.TakeDamage(new DamageHit(health, health.AimPosition, damage, DamageKind.Physical) { isBasicAttack = true });
                audio?.Hit(victim.health.AimPosition);
            }
        }

        protected override void LateUpdate()
        {
            if (!health.IsAlive)
            {
                if (label)
                    label.gameObject.SetActive(false);
                return;
            }
            base.LateUpdate();
        }

        void Die()
        {
            StopAllCoroutines();
            foreach (var list in ByLane)
                list.Remove(this);
            var match = RiftMatch.Instance;
            if (match)
                match.AwardUnit(health, lastHit, stats.gold, stats.xp);
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;
            if (label)
                label.gameObject.SetActive(false);
            if (motion)
                motion.Die(lastHit.kind == DamageKind.Magic);
            else
                Destroy(gameObject, 4);
        }
    }
}
