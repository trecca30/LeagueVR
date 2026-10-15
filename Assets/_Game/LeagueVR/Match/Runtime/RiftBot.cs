using UnityEngine;
using LeagueVR.Champions;

namespace LeagueVR.Match
{
    /// <summary>
    /// A champion that walks the Rift with no kit, only basic attacks: its full model walks the walkable floor
    /// of <see cref="RiftNavGrid"/>. Given a lane route it marches down that lane to the middle of the map and holds
    /// there; without one it wanders at random. It counts as an enemy champion, so towers, minions and the player's
    /// abilities can hit it. When it dies it waits, then respawns at its fountain and walks its route again.
    /// </summary>
    public class RiftBot : RiftActor
    {
        public ChampionDefinition definition;
        public float speed = 3.2f, wanderRadius = 22f, pauseMin = 1f, pauseMax = 3f, respawnSeconds = 8f;

        /// <summary>The middle of the Rift, where a lane route ends.</summary>
        static readonly Vector3 MapCentre = new(-2.2f, 0, 7.2f);

        Animation clips;
        string runClip, idleClip, attackClip, playing;
        GameObject model;
        Vector3[] route;
        int waypoint;
        Vector3 goal;
        bool hasGoal, dead;
        float pauseUntil, respawnAt, nextAttack, swingUntil;

        /// <summary>
        /// Creates a bot under <paramref name="parent"/> at <paramref name="at"/>, on <paramref name="team"/>. With a
        /// <paramref name="lane"/> (points from the team's own fountain outward) it walks that lane to the middle.
        /// </summary>
        public static RiftBot Spawn(ChampionDefinition definition, int team, Vector3 at, Transform parent, Vector3[] lane = null)
        {
            var go = new GameObject(definition.name + " (bot)", typeof(RiftBot));
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var bot = go.GetComponent<RiftBot>();
            bot.Initialize(definition, team, lane);
            return bot;
        }

        void Initialize(ChampionDefinition champion, int team, Vector3[] lane)
        {
            definition = champion;
            health.team = team;
            health.maxHealth = champion.health;
            health.ResetHealth();
            health.onDeath.AddListener(OnDeath);
            // Hittable like a minion: aim cones, weapon sweeps and abilities query the combat layer. This capsule is on
            // that layer only, so it never blocks walking (movement collides with the world layer).
            gameObject.layer = LayerMask.NameToLayer("LeagueCombat");
            var hitbox = gameObject.AddComponent<CapsuleCollider>();
            hitbox.center = Vector3.up * .9f;
            hitbox.height = 1.8f;
            hitbox.radius = .45f;
            SetRoute(lane);
            if (champion.model)
                BuildModel();
        }

        /// <summary>Keeps the lane up to the point nearest the middle of the map, so the bot stops there.</summary>
        void SetRoute(Vector3[] lane)
        {
            if (lane == null || lane.Length == 0)
                return;
            int hold = 0;
            for (int i = 1; i < lane.Length; i++)
                if (Geo.FlatDistance(lane[i], MapCentre) < Geo.FlatDistance(lane[hold], MapCentre))
                    hold = i;
            route = new Vector3[hold + 1];
            System.Array.Copy(lane, route, hold + 1);
            waypoint = 0;
        }

        void BuildModel()
        {
            model = Instantiate(definition.model, transform, false);
            model.name = "Model";
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>())
                behaviour.enabled = false;
            foreach (var collider in model.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            clips = model.GetComponentInChildren<Animation>();
            if (!clips)
                return;
            clips.cullingType = AnimationCullingType.AlwaysAnimate;
            runClip = FirstClipNamed("run") ?? FirstClipNamed("walk");
            attackClip = FirstClipNamed("attack");
            if (definition.idle)
            {
                idleClip = definition.idle.name;
                if (!clips.GetClip(idleClip))
                    clips.AddClip(definition.idle, idleClip);
            }
            else
                idleClip = FirstClipNamed("idle");
        }

        string FirstClipNamed(string key)
        {
            foreach (AnimationState state in clips)
                if (state.name.ToLowerInvariant().Contains(key))
                    return state.name;
            return null;
        }

        void Locomote(bool moving)
        {
            // A swing plays out before the walk or idle clip takes over again.
            if (Time.time < swingUntil)
                return;
            string name = moving ? runClip : idleClip;
            if (!clips || string.IsNullOrEmpty(name) || playing == name)
                return;
            playing = name;
            clips[name].wrapMode = WrapMode.Loop;
            clips.CrossFade(name, .15f);
        }

        void Update()
        {
            var match = RiftMatch.Instance;
            var grid = RiftNavGrid.Active;
            if (!match || !match.Running || !grid)
                return;
            if (dead)
            {
                if (Time.time >= respawnAt)
                    Respawn(match);
                return;
            }
            // Stunned, knocked up or knocked back: no walking or swinging until it lands.
            if (health.Stunned)
            {
                Locomote(false);
                return;
            }
            if (TryAttack())
                return;
            if (route != null)
                FollowRoute(grid);
            else
                Wander(grid);
        }

        /// <summary>
        /// Basic attack on the nearest enemy in melee reach (Pantheon's own attack damage, attack speed and reach). While an
        /// enemy is in reach the bot stands and swings; the swing's damage goes through the normal damage path.
        /// </summary>
        bool TryAttack()
        {
            float reach = definition.attackReach;
            // The player is not a RiftActor, so check them first; otherwise the nearest minion, turret or other bot.
            var match = RiftMatch.Instance;
            Combatant victim = null;
            if (match && match.player && match.player.Health.team != health.team && match.player.Health.IsAlive
                && Geo.FlatDistance(transform.position, match.player.Feet) - .45f <= reach && match.player.Health.IsTargetableBy(health))
                victim = match.player.Health;
            else
            {
                var target = RiftActor.Target(this, reach);
                if (target)
                    victim = target.health;
            }
            if (!victim)
                return false;
            FaceTowards(victim.transform.position - transform.position);
            if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + 1 / Mathf.Max(.1f, definition.attackSpeed);
                var hit = new DamageHit(health, transform.position, definition.attackDamage, DamageKind.Physical)
                {
                    isBasicAttack = true,
                    ability = "Attack1",
                };
                victim.TakeDamage(hit);
                PlayAttack();
            }
            Locomote(false);
            return true;
        }

        /// <summary>Plays one attack swing, stretched over the attack interval like the champion's own attacks.</summary>
        void PlayAttack()
        {
            float interval = 1 / Mathf.Max(.1f, definition.attackSpeed);
            swingUntil = Time.time + interval;
            if (!clips || string.IsNullOrEmpty(attackClip))
                return;
            var state = clips[attackClip];
            state.wrapMode = WrapMode.ClampForever;
            state.speed = state.length / interval;
            clips.CrossFade(attackClip, .08f);
            playing = attackClip;
        }

        void FaceTowards(Vector3 direction)
        {
            direction = Geo.Flat(direction);
            if (direction.sqrMagnitude < .0001f)
                return;
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>Walks the lane one waypoint at a time. Past the last waypoint the bot holds still in the middle.</summary>
        void FollowRoute(RiftNavGrid grid)
        {
            if (waypoint >= route.Length)
            {
                Locomote(false);
                return;
            }
            Vector3 target = route[waypoint];
            if (Geo.FlatDistance(transform.position, target) < .3f)
            {
                waypoint++;
                return;
            }
            if (!StepToward(grid, target))
            {
                // A wall or ledge on the lane: skip that waypoint rather than get stuck on it.
                waypoint++;
                return;
            }
            Locomote(true);
        }

        void Wander(RiftNavGrid grid)
        {
            if (Time.time < pauseUntil)
            {
                Locomote(false);
                return;
            }
            if (!hasGoal)
            {
                if (!PickGoal(grid))
                {
                    pauseUntil = Time.time + pauseMin;
                    return;
                }
                hasGoal = true;
            }
            if (Geo.FlatDistance(transform.position, goal) < .2f)
            {
                hasGoal = false;
                pauseUntil = Time.time + Random.Range(pauseMin, pauseMax);
                Locomote(false);
                return;
            }
            if (!StepToward(grid, goal))
            {
                // Blocked by a wall or a ledge: stop and choose another walk.
                hasGoal = false;
                pauseUntil = Time.time + .2f;
                Locomote(false);
                return;
            }
            Locomote(true);
        }

        /// <summary>One frame's step toward a point on the floor. False when a wall or ledge is in the way.</summary>
        bool StepToward(RiftNavGrid grid, Vector3 target)
        {
            Vector3 flat = Geo.Flat(target - transform.position);
            float distance = flat.magnitude;
            if (distance < .001f)
                return true;
            float stride = Mathf.Min(distance, speed * Time.deltaTime);
            Vector3 next = transform.position + flat / distance * stride;
            if (!grid.Floor(next, out float height) || Mathf.Abs(height - transform.position.y) > .55f)
                return false;
            transform.position = new Vector3(next.x, height, next.z);
            var facing = Quaternion.LookRotation(flat / distance, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1 - Mathf.Exp(-10 * Time.deltaTime));
            return true;
        }

        /// <summary>A random walkable point within the wander radius whose straight walk is clear of walls.</summary>
        bool PickGoal(RiftNavGrid grid)
        {
            Vector3 from = transform.position;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = Random.Range(0, Mathf.PI * 2);
                float reach = Mathf.Sqrt(Random.value) * wanderRadius;
                Vector3 candidate = from + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * reach;
                if (!grid.Floor(candidate, out float height) || Mathf.Abs(height - from.y) > .55f)
                    continue;
                if (!WalkClear(grid, from, candidate))
                    continue;
                goal = new Vector3(candidate.x, height, candidate.z);
                return true;
            }
            return false;
        }

        /// <summary>Walks the straight line in short steps: every point must be floor with no step above the climb height.</summary>
        static bool WalkClear(RiftNavGrid grid, Vector3 from, Vector3 to)
        {
            float length = Geo.FlatDistance(from, to);
            float previous = from.y;
            for (float along = .25f; along <= length + .001f; along += .25f)
            {
                Vector3 point = Vector3.Lerp(from, to, Mathf.Min(1, along / length));
                if (!grid.Floor(point, out float height) || Mathf.Abs(height - previous) > .55f)
                    return false;
                previous = height;
            }
            return true;
        }

        void OnDeath()
        {
            dead = true;
            hasGoal = false;
            respawnAt = Time.time + respawnSeconds;
            if (model)
                model.SetActive(false);
        }

        void Respawn(RiftMatch match)
        {
            foreach (var fountain in match.fountains)
                if (fountain.team == health.team)
                    transform.position = fountain.transform.position + Vector3.up * .08f;
            health.ResetHealth();
            dead = false;
            hasGoal = false;
            waypoint = 0;
            nextAttack = 0;
            pauseUntil = Time.time + pauseMin;
            if (model)
                model.SetActive(true);
        }
    }
}
