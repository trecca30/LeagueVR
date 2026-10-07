using System.Collections;
using UnityEngine;
namespace LeagueVR.Match
{
    public class RiftMinion : RiftActor
    {
        public MinionKind kind;
        public Transform visual;
        public Vector3[] route;
        public MinionStats stats;
        public int lane;
        public int FormationIndex { get; private set; }
        public int Waypoint => waypoint;
        public float MarchAt { get; private set; }
        static readonly float[] Widths = { 1.15f, .9f, .65f, .45f, 0f }, Turns = { 0f, -25f, 25f, -50f, 50f, -80f, 80f, -110f, 110f, -140f, 140f, 180f };
        int waypoint = 1;
        float nextThink, nextAttack, born;
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
        }

        public void Initialize(RiftMatch match, int team, int lane, MinionKind kind, Vector3[] route, int member = 1)
        {
            this.kind = kind;
            this.lane = lane;
            this.route = route;
            FormationIndex = Mathf.Clamp(member, 0, 2);
            waypoint = 1;
            MarchAt = Time.time + ((kind == MinionKind.Melee || kind == MinionKind.Caster) ? (2 - FormationIndex) * match.rules.minionSpawnSpacing : 0);
            stats = match.rules.Stats(kind, match.Seconds);
            health.team = team;
            health.countsAsChampion = false;
            health.maxHealth = stats.health;
            health.armor = stats.armor;
            health.magicResistance = stats.magicResistance;
            health.ResetHealth();
        }

        Vector3 FormationPoint(int index)
        {
            Vector3 tangent = route[Mathf.Min(index + 1, route.Length - 1)] - route[Mathf.Max(0, index - 1)];
            tangent.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
            var match = RiftMatch.Instance;
            float width = 1.15f;
            foreach (float candidate in Widths)
            {
                var offset = route[index] + side * (FormationIndex - 1) * candidate;
                if (match.Ground(offset, out var g) && Mathf.Abs(g.y - route[index].y) < .5f && !Physics.CheckSphere(g + Vector3.up * .55f, .22f, match.player.worldMask, QueryTriggerInteraction.Ignore))
                {
                    width = candidate;
                    break;
                }
            }
            return route[index] + side * (FormationIndex - 1) * width;
        }

        public static float CorridorDistance(Vector3 point, Vector3[] path)
        {
            float best = float.PositiveInfinity;
            point.y = 0;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                a.y = b.y = 0;
                Vector3 d = b - a;
                float t = d.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude) : 0;
                best = Mathf.Min(best, Vector3.Distance(point, a + d * t));
            }
            return best;
        }

        RiftActor Acquire()
        {
            RiftActor best = null;
            float score = 1000;
            var match = RiftMatch.Instance;
            foreach (var actor in All)
            {
                if (!actor || actor == this || actor.neutral || actor.health.team == health.team || !actor.Targetable || !actor.health.IsTargetableBy(health))
                    continue;
                var minion = actor as RiftMinion;
                if (minion && minion.lane != lane)
                    continue;
                var structure = actor as RiftStructure;
                if (structure && structure.lane >= 0 && structure.lane != lane)
                    continue;
                float distance = GwenAbilities.FlatDistance(transform.position, actor.transform.position) - actor.radius;
                if (distance > 7)
                    continue;
                if (!actor.structure && CorridorDistance(actor.transform.position, route) > 3.8f)
                    continue;
                Vector3 start = transform.position + Vector3.up * .7f, end = actor.transform.position + Vector3.up * .7f;
                if (Physics.Linecast(start, end, match.player.worldMask, QueryTriggerInteraction.Ignore))
                    continue;
                float s = distance + (actor.structure ? 20 : actor.health.countsAsChampion ? 10 : 0);
                if (s < score)
                {
                    best = actor;
                    score = s;
                }
            }
            return best;
        }

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!match || !match.Running || !health.IsAlive)
                return;
            if (health.Stunned)
            {
                motion?.Locomotion(false);
                return;
            }
            if (Time.time < MarchAt)
            {
                motion?.Locomotion(false);
                return;
            }
            if (Time.time >= nextThink)
            {
                target = Acquire();
                nextThink = Time.time + .2f;
            }
            Vector3 goal = transform.position;
            bool moving = false;
            if (target && target.Targetable)
            {
                float range = Range(target);
                Vector3 delta = target.transform.position - transform.position;
                delta.y = 0;
                Face(delta);
                if (delta.magnitude <= range)
                {
                    if (Time.time >= nextAttack)
                    {
                        nextAttack = Time.time + stats.attackInterval * health.AttackIntervalMultiplier;
                        float delay = motion ? motion.Attack(stats.attackInterval * health.AttackIntervalMultiplier) : stats.attackInterval * .3f;
                        StartCoroutine(Strike(target, delay, motion ? motion.AttackVariant : 0));
                    }
                }
                else
                    goal = target.transform.position - delta.normalized * (range * .82f) + Vector3.Cross(Vector3.up, delta.normalized) * (FormationIndex - 1) * .85f;
            }
            else if (route != null && route.Length > waypoint)
            {
                while (waypoint < route.Length - 1 && (GwenAbilities.FlatDistance(transform.position, FormationPoint(waypoint)) < .65f || GwenAbilities.FlatDistance(transform.position, route[waypoint]) < .65f))
                    waypoint++;
                goal = FormationPoint(waypoint);
            }
            float speed = match.rules.minionSpeed * match.rules.metresPerLeagueUnit * health.SlowMultiplier * health.SpeedMultiplier;
            Vector3 deltaMove = Vector3.ProjectOnPlane(goal - transform.position, Vector3.up), step = Vector3.ClampMagnitude(deltaMove, speed * Time.deltaTime);
            foreach (var other in All)
            {
                if (other is not RiftMinion m || m == this || !m.health.IsAlive)
                    continue;
                Vector3 away = transform.position - m.transform.position;
                away.y = 0;
                float d = away.magnitude, min = radius + m.radius + .15f;
                if (d >= min)
                    continue;
                if (d < .001f)
                {
                    away = transform.right * (FormationIndex == 0 ? -1 : 1);
                    d = .001f;
                }
                step += away / d * Mathf.Min(speed * Time.deltaTime * .7f, (min - d) * .3f);
            }
            step = Vector3.ClampMagnitude(step, speed * Time.deltaTime * 1.2f);
            if (step.sqrMagnitude > 1e-8f)
            {
                Vector3 ground;
                bool clear = WalkStep(match, step, out ground);
                if (!clear)
                {
                    // Pull a blocked outside column into the lane before turning around an obstacle.
                    Vector3 centre = route[Mathf.Min(waypoint, route.Length - 1)] - transform.position;
                    centre.y = 0;
                    Vector3 best = Vector3.zero;
                    float score = float.NegativeInfinity;
                    var direction = (target ? deltaMove : centre).normalized;
                    foreach (float turn in Turns)
                    {
                        Vector3 alternative = Quaternion.AngleAxis(turn, Vector3.up) * direction * speed * Time.deltaTime;
                        if (!WalkStep(match, alternative, out var at))
                            continue;
                        float value = Vector3.Dot(alternative.normalized, direction) - RiftMinion.CorridorDistance(at, route) * .1f;
                        foreach (var other in All)
                            if (other is RiftMinion minion && other != this && minion.health.IsAlive)
                            {
                                float distance = GwenAbilities.FlatDistance(at, other.transform.position);
                                if (distance < radius + minion.radius + .1f)
                                    value -= 3 * (radius + minion.radius + .1f - distance);
                            }
                        if (value > score)
                        {
                            score = value;
                            best = at;
                        }
                    }
                    clear = score > float.NegativeInfinity;
                    if (clear)
                        ground = best;
                }
                if (clear)
                {
                    Vector3 movement = ground - transform.position;
                    moving = true;
                    transform.position = ground;
                    if (!target)
                        Face(movement);
                }
            }
            motion?.Locomotion(moving);
            if (Time.time - born > 600)
                Destroy(gameObject);
        }

        bool WalkStep(RiftMatch match, Vector3 step, out Vector3 ground)
        {
            var next = transform.position + step;
            if (!match.Ground(next + Vector3.up * .3f, out ground) || Mathf.Abs(ground.y - transform.position.y) > .55f || CorridorDistance(ground, route) > 3.0f)
                return false;
            if (Physics.CheckSphere(ground + Vector3.up * .55f, .22f, match.player.worldMask, QueryTriggerInteraction.Ignore))
                return false;
            // Reject moves that create or worsen allied body overlap. Columns queue at narrow gates.
            foreach (var other in All)
            {
                if (other is not RiftMinion minion || other == this || !minion.health.IsAlive || minion.health.team != health.team)
                    continue;
                float current = GwenAbilities.FlatDistance(transform.position, other.transform.position), nextDistance = GwenAbilities.FlatDistance(ground, other.transform.position), minimum = radius + minion.radius + .05f;
                if (nextDistance < minimum - .0001f && nextDistance < current + .00001f)
                    return false;
            }
            return true;
        }

        float Range(RiftActor victim) => Mathf.Max(.65f, stats.range * RiftMatch.Instance.rules.metresPerLeagueUnit) + victim.radius;

        void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 1 - Mathf.Exp(-12 * Time.deltaTime));
        }

        IEnumerator Strike(RiftActor victim, float delay, int variant)
        {
            yield return new WaitForSeconds(delay);
            if (health.Stunned || !health.IsAlive || !victim || !victim.Targetable || !victim.health.IsTargetableBy(health) || !RiftMatch.Instance.Running || GwenAbilities.FlatDistance(transform.position, victim.transform.position) > Range(victim) + .35f)
                yield break;
            float damage = stats.damage;
            if (victim.GetComponent<RiftMinion>())
                damage += victim.health.Health * stats.minionOnHit;
            else if (victim.structure)
                damage *= kind == MinionKind.Cannon ? .84f : .6f;
            else if (victim.health.countsAsChampion)
                damage *= .55f;
            audio?.Attack(victim.health.countsAsChampion, variant);
            if (kind == MinionKind.Caster || kind == MinionKind.Cannon)
                RiftMissile.Launch(health, victim.health, health.AimPosition, damage, 12, RiftMatch.Instance.TeamMaterial(health.team));
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
