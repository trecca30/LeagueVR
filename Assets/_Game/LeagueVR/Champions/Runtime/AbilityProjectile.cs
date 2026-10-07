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
        /// <summary>Per-target damage (for example full damage to the first enemy, half to the rest); overrides the others.</summary>
        public Func<Combatant, float> damageFor;
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
                Instantiate(visual, go.transform, false).SetActive(true);
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

        /// <summary>
        /// Skillshots hit like League's: anything standing in their ground path counts, whatever height the projectile
        /// was thrown from. The visual settles to <see cref="flightHeight"/> above the ground. Homing shots fly in 3D.
        /// </summary>
        public bool skillshot = true;
        public float flightHeight = .85f;
        float groundY;
        bool groundKnown;

        void Start()
        {
            if (homing)
                skillshot = false;
            SampleGround();
        }

        void SampleGround()
        {
            var match = RiftMatch.Instance;
            if (match && match.Ground(transform.position, out var g))
            {
                groundY = g.y;
                groundKnown = true;
            }
            else if (!groundKnown && owner)
            {
                groundY = owner.Feet.y;
                groundKnown = true;
            }
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
                if ((skillshot ? Geo.Flat(back).magnitude : back.magnitude) < .3f)
                {
                    Destroy(gameObject);
                    return;
                }
                direction = back.normalized;
            }
            else if (homing && homing.IsTargetable)
                direction = (homing.AimPosition - transform.position).normalized;
            float step = Mathf.Min(speed * Time.deltaTime, Mathf.Max(0, range - travelled));
            if (skillshot ? SkillshotStep(step) : FreeStep(step))
                return;
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

        /// <summary>Ground-plane move: a tall capsule sweeps for units, a sphere at the visual checks for walls. Returns true if destroyed.</summary>
        bool SkillshotStep(float step)
        {
            if (Time.frameCount % 6 == 0)
                SampleGround();
            Vector3 flat = Geo.FlatDirection(direction, transform.forward);
            Vector3 pos = transform.position;
            var bottom = new Vector3(pos.x, groundY + .1f, pos.z);
            var top = new Vector3(pos.x, groundY + 2.2f, pos.z);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, flat, hits, step, owner.combatMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
                if (TryHit(hits[i].collider, pos + flat * hits[i].distance))
                    return true;
            // Walls stop skillshots (floors never do: the projectile cruises above them).
            count = Physics.SphereCastNonAlloc(pos, radius * .6f, flat, hits, step, owner.worldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].normal.y < .55f && !owner.OwnCollider(hits[i].collider) && HitWall())
                    return true;
            float y = Mathf.Lerp(pos.y, groundY + flightHeight, 1 - Mathf.Exp(-7 * Time.deltaTime));
            var next = pos + flat * step;
            next.y = y;
            transform.SetPositionAndRotation(next, Quaternion.LookRotation(next - pos + flat * 1e-3f));
            return false;
        }

        /// <summary>Free 3D move for homing shots. Returns true if destroyed.</summary>
        bool FreeStep(float step)
        {
            int count = Physics.SphereCastNonAlloc(transform.position, radius, direction, hits, step, owner.worldMask | owner.combatMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                if (owner.OwnCollider(h.collider))
                    continue;
                if (h.collider.GetComponentInParent<Combatant>())
                {
                    if (TryHit(h.collider, transform.position + direction * h.distance))
                        return true;
                }
                else if ((owner.worldMask.value & (1 << h.collider.gameObject.layer)) != 0)
                {
                    if (HitWall())
                        return true;
                    break;
                }
            }
            transform.position += direction * step;
            if (direction.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(direction);
            return false;
        }

        /// <summary>Damages a struck unit. Returns true if the projectile was consumed.</summary>
        bool TryHit(Collider collider, Vector3 at)
        {
            var target = collider.GetComponentInParent<Combatant>();
            if (!target || !owner.IsEnemy(target) || (ignoreStructures && !basic && (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())) || !victims.Add(target))
                return false;
            float amount = damageFor != null ? damageFor(target) : damageAtDistance != null ? damageAtDistance(travelled) : damage;
            float dealt = owner.Hit(target, amount, kind, ability, basic, at);
            if (dealt > 0)
            {
                if (slowFor != null)
                    target.ApplySlow(slowFor(target), slowDuration);
                hit?.Invoke(target, dealt);
            }
            if (piercing)
                return false;
            Destroy(gameObject);
            return true;
        }

        /// <summary>Terrain stops the projectile, or sends a boomerang back. Returns true if destroyed.</summary>
        bool HitWall()
        {
            if (returning && !ReturnPhase)
            {
                BeginReturn();
                return false;
            }
            Destroy(gameObject);
            return true;
        }

        void BeginReturn()
        {
            ReturnPhase = true;
            range = travelled + 60;
            victims.Clear();
        }

        static bool RiftMatchRunning() => !RiftMatch.Instance || RiftMatch.Instance.Running;
    }

    /// <summary>Spins an effect about its own axes.</summary>
    public class FxSpin : MonoBehaviour
    {
        public Vector3 degreesPerSecond = new(0, 0, 360);

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }

    /// <summary>
    /// A non-damaging delivery (a thrown spell shard, a tether pulse): flies to a unit and calls back on arrival,
    /// or fades out if the target is lost.
    /// </summary>
    public class FxSeeker : MonoBehaviour
    {
        public Combatant target;
        public float speed = 18;
        public System.Action<Combatant> arrive;
        float born;

        void Start() => born = Time.time;

        void Update()
        {
            if (!target || !target.IsAlive || Time.time - born > 4)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 to = target.AimPosition - transform.position;
            float step = speed * Time.deltaTime;
            if (to.magnitude <= step + .2f)
            {
                arrive?.Invoke(target);
                Destroy(gameObject);
                return;
            }
            transform.position += to.normalized * step;
        }
    }

    /// <summary>Fades a ghost's own material to transparent, then destroys the object and the material.</summary>
    public class GhostFade : MonoBehaviour
    {
        Material material;
        Color color;
        float duration, born;
        public bool destroyMesh;

        public void Begin(Material m, Color c, float seconds)
        {
            material = m;
            color = c;
            duration = seconds;
            born = Time.time;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (material)
            {
                var c = color;
                c.a *= 1 - Mathf.Clamp01(t);
                material.SetColor("_BaseColor", c);
            }
            if (t >= 1)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material)
                Destroy(material);
            if (destroyMesh && TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
                Destroy(filter.sharedMesh);
        }
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
        static readonly Dictionary<(Color, bool, bool), Material> glassMaterials = new();
        static Texture2D softDot;

        /// <summary>Translucent unlit material: alpha blended, or additive for glows; optionally visible from inside (two sided).</summary>
        public static Material Glass(Color color, bool additive = false, bool twoSided = false)
        {
            var key = (color, additive, twoSided);
            if (glassMaterials.TryGetValue(key, out var material) && material)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Ability glass " + ColorUtility.ToHtmlStringRGBA(color), hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", additive ? 2 : 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", twoSided ? 0 : 2);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            glassMaterials[key] = material;
            return material;
        }

        /// <summary>A soft round particle sprite generated once.</summary>
        public static Texture2D SoftDot
        {
            get
            {
                if (softDot)
                    return softDot;
                const int size = 64;
                softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Ability soft dot", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                        float a = Mathf.Clamp01(1 - d);
                        softDot.SetPixel(x, y, new Color(1, 1, 1, a * a));
                    }
                softDot.Apply();
                return softDot;
            }
        }

        /// <summary>Simple glowing particle system (motes, sparkles, mist).</summary>
        public static ParticleSystem Motes(Transform parent, Color color, float size, float lifetime, float rate, ParticleSystemShapeType shape, float radius)
        {
            var go = new GameObject("Ability motes");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .6f, size * 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .35f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = shape;
            sh.radius = radius;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(color, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(color.a, .2f), new GradientAlphaKey(0, 1) },
            };
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var material = new Material(Glass(Color.white, true)) { name = "Ability mote", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", SoftDot);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// <summary>A translucent copy of a mesh that fades out (afterimages, spectral weapons).</summary>
        public static GameObject Ghost(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Color color, float fade, bool additive = true)
        {
            var go = new GameObject("Ability ghost");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var material = new Material(Glass(color, additive)) { hideFlags = HideFlags.HideAndDontSave };
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (fade > 0)
                go.AddComponent<GhostFade>().Begin(material, color, fade);
            return go;
        }

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

        static Mesh starMesh, crystalMesh;

        /// <summary>A puffy five-pointed star, 1 unit across, facing +Z (Zoe's stars).</summary>
        public static Mesh StarMesh
        {
            get
            {
                if (starMesh)
                    return starMesh;
                const int points = 5;
                var vertices = new List<Vector3> { new(0, 0, .16f), new(0, 0, -.16f) };
                for (int i = 0; i < points * 2; i++)
                {
                    float angle = Mathf.PI / 2 + i * Mathf.PI / points;
                    float radius = i % 2 == 0 ? .5f : .22f;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0));
                }
                var triangles = new List<int>();
                for (int i = 0; i < points * 2; i++)
                {
                    int a = 2 + i, b = 2 + (i + 1) % (points * 2);
                    triangles.AddRange(new[] { 0, b, a, 1, a, b });
                }
                // Flat-shaded copy so each facet catches the light.
                var flat = new List<Vector3>();
                var flatTriangles = new List<int>();
                for (int t = 0; t < triangles.Count; t++)
                {
                    flat.Add(vertices[triangles[t]]);
                    flatTriangles.Add(t);
                }
                starMesh = new Mesh { name = "Ability star", hideFlags = HideFlags.HideAndDontSave };
                starMesh.SetVertices(flat);
                starMesh.SetTriangles(flatTriangles, 0);
                starMesh.RecalculateNormals();
                starMesh.RecalculateBounds();
                return starMesh;
            }
        }

        /// <summary>A long eight-faced crystal, 1 unit tall (spell shards).</summary>
        public static Mesh CrystalMesh
        {
            get
            {
                if (crystalMesh)
                    return crystalMesh;
                var top = new Vector3(0, .5f, 0);
                var bottom = new Vector3(0, -.5f, 0);
                var ring = new Vector3[4];
                for (int i = 0; i < 4; i++)
                    ring[i] = new Vector3(Mathf.Cos(i * Mathf.PI / 2) * .28f, .08f, Mathf.Sin(i * Mathf.PI / 2) * .28f);
                var vertices = new List<Vector3>();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = ring[i], b = ring[(i + 1) % 4];
                    vertices.AddRange(new[] { top, b, a, bottom, a, b });
                }
                var triangles = new int[vertices.Count];
                for (int i = 0; i < triangles.Length; i++)
                    triangles[i] = i;
                crystalMesh = new Mesh { name = "Ability crystal", hideFlags = HideFlags.HideAndDontSave };
                crystalMesh.SetVertices(vertices);
                crystalMesh.SetTriangles(triangles, 0);
                crystalMesh.RecalculateNormals();
                crystalMesh.RecalculateBounds();
                return crystalMesh;
            }
        }

        /// <summary>A mesh object drawn with a shared two-sided unlit glass material.</summary>
        public static GameObject MeshObject(string name, Mesh mesh, Color color, bool additive, Transform parent = null, float scale = 1)
        {
            var go = new GameObject(name);
            if (parent)
                go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Glass(color, additive, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A world-space line renderer for arcs and guides.</summary>
        public static LineRenderer Line(string name, Color color, float width, int points, bool additive = true)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Glass(color, additive);
            lr.useWorldSpace = true;
            lr.positionCount = points;
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        /// <summary>Points a ground ring (from <see cref="Ring"/>-style loops) at a radius around a centre.</summary>
        public static void SetCircle(LineRenderer line, Vector3 centre, float radius)
        {
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, .06f, Mathf.Sin(a) * radius));
            }
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
