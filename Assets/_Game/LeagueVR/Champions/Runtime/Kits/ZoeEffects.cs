using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Zoe's Q star. Thrown along the ground plane (it hits whatever stands in its path, like a League skillshot),
    /// it hovers for a second at the end of its range and can be paddled once toward a new point. It grows as it
    /// travels: its size shows how hard it will hit. It always faces the player so it reads as a shining star.
    /// </summary>
    public class PaddleStar : MonoBehaviour
    {
        enum Phase { Thrown, Lingering, Paddled }

        static readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly Collider[] overlaps = new Collider[32];
        static readonly Comparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        ZoeKit kit;
        PlayerChampion owner;
        Phase phase;
        Vector3 direction;
        float speed, lingerUntil;
        Transform visual;

        public float Travelled { get; private set; }
        public float GroundY { get; private set; }
        /// <summary>The star can be paddled while it flies out and for a moment after it stops.</summary>
        public bool CanPaddle => phase == Phase.Thrown || (phase == Phase.Lingering && Time.time < lingerUntil + .25f);
        public bool Paddled => phase == Phase.Paddled;

        public static PaddleStar Launch(ZoeKit kit, PlayerChampion owner, Vector3 start, Vector3 direction)
        {
            var go = new GameObject("Paddle Star");
            go.transform.position = start;
            var star = go.AddComponent<PaddleStar>();
            star.kit = kit;
            star.owner = owner;
            star.direction = Geo.FlatDirection(direction, owner.PlanarDirection(owner.head.transform.forward));
            star.speed = ZoeKit.StarSpeed;
            star.GroundY = owner.Feet.y;
            star.visual = ZoeKit.StarVisual(go.transform, .3f, ZoeKit.Gold).transform;
            AbilityFx.Motes(go.transform, new Color(1f, .9f, .6f, .9f), .05f, .5f, 50, ParticleSystemShapeType.Sphere, .1f);
            owner.Track(go);
            return star;
        }

        /// <summary>Sends the star toward a ground point, bigger and faster, until it is a full range away from Zoe.</summary>
        public void Paddle(Vector3 target)
        {
            direction = Geo.FlatDirection(target - transform.position, direction);
            speed = ZoeKit.PaddleSpeed;
            phase = Phase.Paddled;
            kit.Burst(transform.position, ZoeKit.Gold, .45f, 26);
        }

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!owner || !owner.Health.IsAlive || (match && !match.Running))
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            if (phase == Phase.Lingering)
            {
                if (Overlap(pos))
                    return;
                if (Time.time > lingerUntil + .25f)
                {
                    Fizzle();
                    return;
                }
            }
            else
            {
                float step = speed * dt;
                if (phase == Phase.Thrown)
                    step = Mathf.Min(step, ZoeKit.StarRange - Travelled);
                if (step > 0 && Sweep(pos, step))
                    return;
                pos += direction * step;
                Travelled += step;
                if (phase == Phase.Thrown && Travelled >= ZoeKit.StarRange - .01f)
                {
                    phase = Phase.Lingering;
                    lingerUntil = Time.time + ZoeKit.StarLinger;
                }
                else if (phase == Phase.Paddled)
                {
                    Vector3 fromZoe = Geo.Flat(pos - owner.Feet);
                    if ((fromZoe.magnitude > ZoeKit.StarRange && Vector3.Dot(fromZoe, direction) > 0) || Travelled > 80)
                    {
                        transform.position = pos;
                        Fizzle();
                        return;
                    }
                }
            }
            if (match && Time.frameCount % 4 == 0 && match.Ground(new Vector3(pos.x, GroundY + 1, pos.z), out var ground))
                GroundY = ground.y;
            float bob = phase == Phase.Lingering ? Mathf.Sin(Time.time * 7) * .07f : 0;
            pos.y = Mathf.Lerp(pos.y, GroundY + 1.1f + bob, 1 - Mathf.Exp(-6 * dt));
            transform.position = pos;
            float growth = kit.StarGrowth(Travelled);
            visual.localScale = Vector3.one * (1 + 1.4f * growth) * (phase == Phase.Paddled ? 1.25f : 1);
            Vector3 toEye = pos - owner.head.transform.position;
            if (toEye.sqrMagnitude > 1e-4f)
                visual.rotation = Quaternion.LookRotation(toEye);
        }

        bool Sweep(Vector3 pos, float step)
        {
            float radius = phase == Phase.Paddled ? .5f : .35f;
            var bottom = new Vector3(pos.x, GroundY + .1f, pos.z);
            var top = new Vector3(pos.x, GroundY + 2.2f, pos.z);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits, step, owner.combatMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
                if (Valid(hits[i].collider.GetComponentInParent<Combatant>()))
                {
                    Explode(pos + direction * hits[i].distance);
                    return true;
                }
            return false;
        }

        bool Overlap(Vector3 pos)
        {
            var bottom = new Vector3(pos.x, GroundY + .1f, pos.z);
            var top = new Vector3(pos.x, GroundY + 2.2f, pos.z);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, .45f, overlaps, owner.combatMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (Valid(overlaps[i].GetComponentInParent<Combatant>()))
                {
                    Explode(pos);
                    return true;
                }
            return false;
        }

        bool Valid(Combatant t) => owner.IsEnemy(t) && !t.GetComponent<RiftStructure>() && !t.GetComponent<RiftVisionWard>();

        void Explode(Vector3 at)
        {
            kit.StarHit(this, new Vector3(at.x, transform.position.y, at.z));
            Destroy(gameObject);
        }

        void Fizzle()
        {
            kit.Burst(transform.position, ZoeKit.Gold, .3f, 14);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Zoe's E bubble: lobbed on a real arc (it can sail over walls), it bounces on along the ground to the end of its
    /// range and waits there as a trap. Walls knock it down where it hits them.
    /// </summary>
    public class SleepyBubble : MonoBehaviour
    {
        enum Phase { Flying, Rolling, Trap }

        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly Collider[] overlaps = new Collider[32];
        static readonly Comparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        const float Radius = .22f;

        ZoeKit kit;
        PlayerChampion owner;
        Phase phase;
        Vector3 velocity, rollDirection;
        float travelled, trapUntil, groundY, nextCheck;
        Transform visual;
        GameObject trapRing;

        public static SleepyBubble Throw(ZoeKit kit, PlayerChampion owner, Vector3 start, Vector3 velocity)
        {
            var go = new GameObject("Sleepy Trouble Bubble");
            go.transform.position = start;
            var bubble = go.AddComponent<SleepyBubble>();
            bubble.kit = kit;
            bubble.owner = owner;
            bubble.velocity = velocity;
            bubble.rollDirection = Geo.FlatDirection(velocity, owner.PlanarDirection(owner.head.transform.forward));
            bubble.groundY = owner.Feet.y;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.name = "Bubble";
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * Radius * 2;
            var r = sphere.GetComponent<Renderer>();
            r.sharedMaterial = AbilityFx.Glass(new Color(1f, .55f, .9f, .38f), false, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shine = AbilityFx.MeshObject("Bubble shine", AbilityFx.StarMesh, new Color(1f, .85f, 1f, .5f), true, sphere.transform, .25f);
            shine.transform.localPosition = new Vector3(-.2f, .22f, -.3f);
            AbilityFx.Motes(go.transform, new Color(1f, .7f, .95f, .8f), .03f, .6f, 40, ParticleSystemShapeType.Sphere, Radius * .8f);
            bubble.visual = sphere.transform;
            owner.Track(go);
            return bubble;
        }

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!owner || !owner.Health.IsAlive || (match && !match.Running))
            {
                Destroy(gameObject);
                return;
            }
            if (match && match.Ground(transform.position, out var ground))
                groundY = ground.y;
            float dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.Flying:
                    Fly(dt);
                    break;
                case Phase.Rolling:
                    Roll(dt);
                    break;
                default:
                    WaitAsTrap();
                    break;
            }
            if (visual)
            {
                float wobble = 1 + .07f * Mathf.Sin(Time.time * 13);
                visual.localScale = new Vector3(wobble, 1 / wobble, wobble) * Radius * 2 * (phase == Phase.Trap ? 1.35f : 1);
            }
        }

        bool TerrainHit(Vector3 from, Vector3 move, out RaycastHit hit)
        {
            hit = default;
            float length = move.magnitude;
            if (length < 1e-5f)
                return false;
            int count = Physics.SphereCastNonAlloc(from, Radius * .8f, move / length, hits, length, owner.worldMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
                if (!owner.OwnCollider(hits[i].collider) && hits[i].distance > 0)
                {
                    hit = hits[i];
                    return true;
                }
            return false;
        }

        void Fly(float dt)
        {
            Vector3 pos = transform.position;
            velocity.y -= ZoeKit.BubbleGravity * dt;
            Vector3 move = velocity * dt;
            if (TerrainHit(pos, move, out var hit))
            {
                if (hit.normal.y > .55f)
                {
                    transform.position = hit.point + Vector3.up * Radius;
                    travelled += Geo.Flat(transform.position - pos).magnitude;
                    Land();
                    return;
                }
                // A wall knocks the bubble down.
                velocity = new Vector3(0, Mathf.Min(0, velocity.y), 0);
                move = hit.normal * .05f;
            }
            // At full range the bubble stops and drops straight down into place.
            Vector3 flatMove = Geo.Flat(move);
            float remaining = Mathf.Max(0, ZoeKit.BubbleRange - travelled);
            if (flatMove.magnitude > remaining)
            {
                move = flatMove.normalized * remaining + Vector3.up * move.y;
                velocity = new Vector3(0, Mathf.Min(0, velocity.y), 0);
            }
            travelled += Mathf.Min(flatMove.magnitude, remaining);
            pos += move;
            transform.position = pos;
            // Like the League bubble it hits the first unit on its ground path; only a really high lob sails over.
            if (pos.y - groundY < 3.5f && TouchColumn(pos))
                return;
            if (pos.y < groundY + Radius)
            {
                transform.position = new Vector3(pos.x, groundY + Radius, pos.z);
                Land();
            }
        }

        void Land()
        {
            if (travelled >= ZoeKit.BubbleRange - .5f)
            {
                BeginTrap();
                return;
            }
            phase = Phase.Rolling;
            velocity = rollDirection * 7 + Vector3.up * 2.6f;
        }

        void Roll(float dt)
        {
            // Bouncy hops along the ground until the full range is covered.
            Vector3 pos = transform.position;
            velocity.y -= ZoeKit.BubbleGravity * dt;
            Vector3 flatMove = Geo.Flat(velocity * dt);
            float remaining = ZoeKit.BubbleRange - travelled;
            if (flatMove.magnitude > remaining)
                flatMove = flatMove.normalized * remaining;
            if (TerrainHit(pos, flatMove, out var hit) && hit.normal.y < .55f)
            {
                BeginTrap();
                return;
            }
            pos += flatMove + Vector3.up * velocity.y * dt;
            travelled += flatMove.magnitude;
            if (pos.y <= groundY + Radius)
            {
                pos.y = groundY + Radius;
                velocity.y = Mathf.Abs(velocity.y) * .5f;
                if (velocity.y < .8f)
                    velocity.y = 0;
            }
            transform.position = pos;
            if (TouchColumn(pos))
                return;
            if (travelled >= ZoeKit.BubbleRange - .01f)
                BeginTrap();
        }

        void BeginTrap()
        {
            phase = Phase.Trap;
            trapUntil = Time.time + ZoeKit.TrapLife;
            trapRing = AbilityFx.Ring(new Vector3(transform.position.x, groundY, transform.position.z), ZoeKit.TrapRadius, ZoeKit.TrapLife, new Color(1f, .5f, .9f, .7f), .03f);
            owner.Track(trapRing);
        }

        void WaitAsTrap()
        {
            var pos = transform.position;
            pos.y = Mathf.Lerp(pos.y, groundY + .35f + Mathf.Sin(Time.time * 2.4f) * .06f, 1 - Mathf.Exp(-6 * Time.deltaTime));
            transform.position = pos;
            if (Time.time > trapUntil)
            {
                Pop();
                return;
            }
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + .1f;
                Touch(new Vector3(pos.x, groundY + .6f, pos.z), ZoeKit.TrapRadius);
            }
        }

        /// <summary>Units standing under the bubble, whatever its height.</summary>
        bool TouchColumn(Vector3 at)
        {
            int count = Physics.OverlapCapsuleNonAlloc(new Vector3(at.x, groundY + .1f, at.z), new Vector3(at.x, groundY + 2.2f, at.z), Radius + .25f, overlaps, owner.combatMask, QueryTriggerInteraction.Collide);
            return Struck(count);
        }

        bool Touch(Vector3 at, float radius) => Struck(Physics.OverlapSphereNonAlloc(at, radius, overlaps, owner.combatMask, QueryTriggerInteraction.Collide));

        bool Struck(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var t = overlaps[i].GetComponentInParent<Combatant>();
                if (owner.IsEnemy(t) && !t.GetComponent<RiftStructure>() && !t.GetComponent<RiftVisionWard>())
                {
                    kit.BubbleHit(t, transform.position);
                    Pop();
                    return true;
                }
            }
            return false;
        }

        void Pop()
        {
            kit.Burst(transform.position, ZoeKit.Pink, .4f, 22);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (trapRing)
                Destroy(trapRing);
        }
    }

    public enum ZoeShardKind { Heal, Barrier, Ghost, Cleanse, Ignite, Exhaust }

    /// <summary>A Spell Thief shard on the ground: a floating crystal with its spell's name, for 20 seconds.</summary>
    public class ZoeShard : MonoBehaviour
    {
        public static readonly List<ZoeShard> Active = new();

        public ZoeShardKind kind;
        public PlayerChampion owner;
        float expires, baseY;
        TextMeshPro label;

        public static string Name(ZoeShardKind kind) => kind.ToString().ToUpperInvariant();

        /// <summary>Ignite and Exhaust are thrown at an enemy; the others act on Zoe.</summary>
        public static bool Targeted(ZoeShardKind kind) => kind == ZoeShardKind.Ignite || kind == ZoeShardKind.Exhaust;

        public static Color Tint(ZoeShardKind kind) => kind switch
        {
            ZoeShardKind.Heal => new Color(.45f, 1f, .5f, .9f),
            ZoeShardKind.Barrier => new Color(1f, .92f, .55f, .9f),
            ZoeShardKind.Ghost => new Color(.5f, .9f, 1f, .9f),
            ZoeShardKind.Cleanse => new Color(.85f, .95f, 1f, .9f),
            ZoeShardKind.Ignite => new Color(1f, .5f, .15f, .9f),
            _ => new Color(.95f, .75f, .25f, .9f),
        };

        public static ZoeShard Drop(PlayerChampion owner, ZoeShardKind kind, Vector3 at, float life)
        {
            var go = AbilityFx.MeshObject(Name(kind) + " spell shard", AbilityFx.CrystalMesh, Tint(kind), true, null, .32f);
            go.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 90, 0);
            AbilityFx.Motes(go.transform, Tint(kind), .04f, .8f, 25, ParticleSystemShapeType.Sphere, .15f);
            var shard = go.AddComponent<ZoeShard>();
            shard.kind = kind;
            shard.owner = owner;
            shard.expires = Time.time + life;
            var match = RiftMatch.Instance;
            if (match && match.Ground(at, out var ground))
                at = ground;
            shard.baseY = at.y + .75f;
            go.transform.position = new Vector3(at.x, shard.baseY, at.z);
            var text = new GameObject("Shard label");
            text.transform.SetParent(go.transform, false);
            shard.label = text.AddComponent<TextMeshPro>();
            shard.label.text = Name(kind);
            shard.label.fontSize = 3;
            shard.label.alignment = TextAlignmentOptions.Center;
            shard.label.color = Tint(kind);
            shard.label.rectTransform.sizeDelta = new Vector2(3, 1);
            shard.label.transform.localScale = Vector3.one * .3f;
            owner.Track(go);
            return shard;
        }

        void OnEnable() => Active.Add(this);

        void OnDisable() => Active.Remove(this);

        void Update()
        {
            if (!owner || Time.time > expires)
            {
                Destroy(gameObject);
                return;
            }
            var p = transform.position;
            p.y = baseY + Mathf.Sin(Time.time * 2.2f) * .08f;
            transform.position = p;
            if (label && owner.head)
            {
                label.transform.position = p + Vector3.up * .32f;
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - owner.head.transform.position);
            }
        }
    }

    /// <summary>Drowsy and asleep tells over a unit hit by Zoe's bubble: a pink bubble grows, then sleepy Zs float up.</summary>
    public class ZoeSleepFx : MonoBehaviour
    {
        Combatant unit;
        GameObject bubble;
        readonly List<TextMeshPro> letters = new();
        bool asleep;
        float started;

        void Awake()
        {
            unit = GetComponent<Combatant>();
            started = Time.time;
            bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bubble.GetComponent<Collider>());
            bubble.name = "Drowsy bubble";
            var r = bubble.GetComponent<Renderer>();
            r.sharedMaterial = AbilityFx.Glass(new Color(1f, .55f, .9f, .3f), false, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Sleep()
        {
            asleep = true;
            started = Time.time;
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Sleep Z");
                var text = go.AddComponent<TextMeshPro>();
                text.text = "Z";
                text.fontSize = 4 - i;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(1f, .7f, .95f, .95f);
                text.rectTransform.sizeDelta = new Vector2(1, 1);
                go.transform.localScale = Vector3.one * .25f;
                letters.Add(text);
            }
        }

        void Update()
        {
            if (!unit || !unit.IsAlive || (asleep && !unit.Asleep) || Time.time - started > 3)
            {
                Destroy(this);
                return;
            }
            var match = RiftMatch.Instance;
            var eye = match && match.player ? match.player.head.transform.position : Camera.main ? Camera.main.transform.position : Vector3.zero;
            Vector3 top = unit.AimPosition + Vector3.up * .75f;
            float t = Time.time - started;
            float size = asleep ? .55f + .04f * Mathf.Sin(Time.time * 3) : Mathf.Lerp(.15f, .45f, t / 1.4f);
            bubble.transform.position = top;
            bubble.transform.localScale = Vector3.one * size;
            for (int i = 0; i < letters.Count; i++)
            {
                float phase = (t * .6f + i / 3f) % 1;
                Vector3 p = top + new Vector3(Mathf.Sin(phase * 6 + i) * .15f, .2f + phase * .7f, 0);
                letters[i].transform.position = p;
                letters[i].transform.rotation = Quaternion.LookRotation(p - eye);
                letters[i].alpha = 1 - phase;
            }
        }

        void OnDestroy()
        {
            if (bubble)
                Destroy(bubble);
            foreach (var l in letters)
                if (l)
                    Destroy(l.gameObject);
        }
    }
}
