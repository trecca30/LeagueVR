using System;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// A champion skillshot or homing attack. Moves with a sphere cast each frame, stops at terrain,
    /// and deals damage through <see cref="PlayerChampion.Hit"/> so items and kit passives apply.
    /// </summary>
    public class AbilityProjectile : MonoBehaviour
    {
        public PlayerChampion owner;
        public string ability;
        public Vector3 direction;
        public float speed = 14, range = 16, radius = .13f, damage, travelled;
        public DamageKind kind = DamageKind.Magic;
        public bool basic, piercing, returning, ignoreStructures = true;
        public Combatant homing;
        public Action<Combatant, float> hit;
        public Func<float, float> damageAtDistance;
        public Func<Combatant, float> slowFor;
        public float slowDuration;

        public bool ReturnPhase { get; private set; }
        readonly HashSet<Combatant> victims = new();
        static readonly RaycastHit[] hits = new RaycastHit[64];
        static readonly Comparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        /// <summary>Spawns a projectile. Uses <paramref name="visual"/> when provided, otherwise a glowing orb with a trail.</summary>
        public static AbilityProjectile Launch(PlayerChampion owner, Vector3 start, Vector3 direction, float damage, DamageKind kind, string ability, float speed, float range, bool piercing, Color color, GameObject visual = null, float size = .14f)
        {
            var go = new GameObject(owner.ChampionName + " " + ability);
            go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(direction.sqrMagnitude > 1e-4f ? direction : Vector3.forward));
            if (visual)
                Instantiate(visual, go.transform, false);
            else
                AbilityFx.Orb(go.transform, color, size);
            var p = go.AddComponent<AbilityProjectile>();
            p.owner = owner;
            p.direction = direction.normalized;
            p.damage = damage;
            p.kind = kind;
            p.ability = ability;
            p.speed = speed;
            p.range = range;
            p.piercing = piercing;
            owner.Track(go);
            return p;
        }

        /// <summary>Sends the projectile off in a new direction with fresh range (Zoe's Paddle Star recast).</summary>
        public void Redirect(Vector3 aim, float extraRange = 18)
        {
            direction = aim.normalized;
            range = travelled + extraRange;
            victims.Clear();
        }

        void Update()
        {
            if (!owner || !owner.Health.IsAlive || !RiftMatchRunning())
            {
                Destroy(gameObject);
                return;
            }
            if (ReturnPhase)
            {
                var back = owner.AttackOrigin - transform.position;
                if (back.magnitude < .25f)
                {
                    Destroy(gameObject);
                    return;
                }
                direction = back.normalized;
            }
            else if (homing && homing.IsTargetable)
                direction = (homing.AimPosition - transform.position).normalized;
            float step = Mathf.Min(speed * Time.deltaTime, Mathf.Max(0, range - travelled));
            int count = Physics.SphereCastNonAlloc(transform.position, radius, direction, hits, step, owner.worldMask | owner.combatMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (owner.OwnCollider(h.collider))
                    continue;
                var target = h.collider.GetComponentInParent<Combatant>();
                if (target)
                {
                    if (!owner.IsEnemy(target) || (ignoreStructures && !basic && (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())) || !victims.Add(target))
                        continue;
                    float dealt = owner.Hit(target, damageAtDistance != null ? damageAtDistance(travelled) : damage, kind, ability, basic, transform.position);
                    if (dealt > 0)
                    {
                        if (slowFor != null)
                            target.ApplySlow(slowFor(target), slowDuration);
                        hit?.Invoke(target, dealt);
                    }
                    if (!piercing)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
                else if ((owner.worldMask.value & (1 << h.collider.gameObject.layer)) != 0)
                {
                    if (returning && !ReturnPhase)
                    {
                        BeginReturn();
                        break;
                    }
                    Destroy(gameObject);
                    return;
                }
            }
            transform.position += direction * step;
            if (direction.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(direction);
            travelled += step;
            if (travelled >= range)
            {
                if (returning && !ReturnPhase)
                    BeginReturn();
                else
                    Destroy(gameObject);
            }
            if (travelled > 120)
                Destroy(gameObject);
        }

        void BeginReturn()
        {
            ReturnPhase = true;
            range = travelled + 60;
            victims.Clear();
        }

        static bool RiftMatchRunning() => !RiftMatch.Instance || RiftMatch.Instance.Running;
    }

    /// <summary>Short-lived visual helper: fades line renderers and grows sparks, then destroys itself.</summary>
    public class AbilityVfx : MonoBehaviour
    {
        public float duration = .4f;
        public bool expand;
        float born;
        Vector3 initial;
        LineRenderer line;
        Color startColor;

        void Awake()
        {
            born = Time.time;
            initial = transform.localScale;
            line = GetComponent<LineRenderer>();
            if (line)
                startColor = line.startColor;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (expand)
                transform.localScale = initial * (1 + t * .6f);
            if (line)
            {
                var c = startColor;
                c.a *= 1 - Mathf.Clamp01((t - .6f) / .4f);
                line.startColor = line.endColor = c;
            }
            if (t >= 1)
                Destroy(gameObject);
        }
    }

    /// <summary>Ground rings, beams, sparks and projectile orbs drawn in the champion's colour.</summary>
    public static class AbilityFx
    {
        static readonly Dictionary<Color, Material> materials = new();

        public static Material Material(Color color)
        {
            if (!materials.TryGetValue(color, out var material) || !material)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                material = new Material(shader) { name = "Ability " + ColorUtility.ToHtmlStringRGB(color), hideFlags = HideFlags.HideAndDontSave };
                material.SetColor("_BaseColor", color);
                materials[color] = material;
            }
            return material;
        }

        public static GameObject Ring(Vector3 position, float radius, float seconds, Color color, float width = .035f)
        {
            var go = new GameObject("Ability ground telegraph");
            go.transform.position = position + Vector3.up * .06f;
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Material(color);
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 48;
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
            go.AddComponent<AbilityVfx>().duration = seconds;
            return go;
        }

        public static GameObject Beam(Vector3 from, Vector3 to, float width, Color color, float seconds = .15f)
        {
            var go = new GameObject("Ability trail");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Material(color);
            lr.startWidth = width;
            lr.endWidth = width * .3f;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<AbilityVfx>().duration = seconds;
            return go;
        }

        public static GameObject Spark(Vector3 position, float radius, Color color, float seconds = .15f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = "Ability hit spark";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * radius;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Material(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fx = go.AddComponent<AbilityVfx>();
            fx.duration = seconds;
            fx.expand = true;
            return go;
        }

        /// <summary>Projectile body: a bright core with a fading trail.</summary>
        public static void Orb(Transform parent, Color color, float size)
        {
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(core.GetComponent<Collider>());
            core.name = "Projectile core";
            core.transform.SetParent(parent, false);
            core.transform.localScale = Vector3.one * size;
            var r = core.GetComponent<Renderer>();
            r.sharedMaterial = Material(Color.Lerp(color, Color.white, .35f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Material(color);
            trail.time = .18f;
            trail.startWidth = size * .9f;
            trail.endWidth = 0;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.minVertexDistance = .05f;
        }
    }
}
