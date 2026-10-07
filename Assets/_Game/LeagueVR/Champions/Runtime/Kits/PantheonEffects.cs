using UnityEngine;
using UnityEngine.Rendering;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// The Grand Starfall hologram: a small map of the Rift floating over the shield hand, aligned with the world
    /// (a spot ahead of you on the map is ahead of you on the Rift). It is a top-down render of the real map taken
    /// once. Point the spear at it to choose where to land; a gold star marks the spot, a ring shows the range.
    /// </summary>
    public class StarfallMap
    {
        const float Width = .46f;

        readonly PlayerChampion player;
        GameObject root;
        Transform quad, playerDot, targetMarker;
        LineRenderer rangeLine, frame, pointer;
        RenderTexture texture;
        Material material;
        Vector3 mapMin;
        float mapSize;

        public Vector3 Target { get; private set; }

        public StarfallMap(PlayerChampion player)
        {
            this.player = player;
            Build();
        }

        void Build()
        {
            var bounds = new Bounds(player.Feet, Vector3.zero);
            var match = RiftMatch.Instance;
            if (match)
            {
                foreach (var f in match.fountains)
                    if (f)
                        bounds.Encapsulate(f.transform.position);
                foreach (var s in match.structures)
                    if (s)
                        bounds.Encapsulate(s.transform.position);
                foreach (var lane in match.lanes)
                    foreach (var p in lane.points)
                        bounds.Encapsulate(p);
            }
            mapSize = Mathf.Max(bounds.size.x, bounds.size.z) + 16;
            var centre = new Vector3(bounds.center.x, 0, bounds.center.z);
            mapMin = centre - new Vector3(mapSize, 0, mapSize) * .5f;
            texture = new RenderTexture(512, 512, 16) { name = "Grand Starfall map", hideFlags = HideFlags.HideAndDontSave };
            RenderTopDown(centre);

            root = new GameObject("Grand Starfall hologram");
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "Rift map";
            quad = q.transform;
            quad.SetParent(root.transform, false);
            // A Quad's local X/Y map to texture U/V; lying flat, they line up with world X/Z like the top-down render.
            quad.localRotation = Quaternion.Euler(90, 0, 0);
            quad.localScale = new Vector3(Width, Width, 1);
            material = new Material(AbilityFx.Glass(new Color(1f, .96f, .85f, .9f), false, true)) { hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", texture);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;

            frame = LocalLine("Map frame", new Color(1f, .78f, .35f, .9f), .012f, 5);
            frame.loop = true;
            for (int i = 0; i < 4; i++)
            {
                float x = i == 0 || i == 3 ? -.5f : .5f, y = i < 2 ? -.5f : .5f;
                frame.SetPosition(i, new Vector3(x, y, -.005f));
            }
            frame.positionCount = 4;
            rangeLine = LocalLine("Starfall range", new Color(1f, .8f, .4f, .7f), .006f, 48);
            rangeLine.loop = true;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(dot.GetComponent<Collider>());
            dot.name = "You";
            playerDot = dot.transform;
            playerDot.SetParent(quad, false);
            playerDot.localScale = Vector3.one * (.022f / Width);
            var dotRenderer = dot.GetComponent<Renderer>();
            dotRenderer.sharedMaterial = AbilityFx.Glass(new Color(.4f, .85f, 1f, 1f), true, true);
            dotRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var star = AbilityFx.MeshObject("Landing star", AbilityFx.StarMesh, new Color(1f, .8f, .3f, 1f), true, quad, .06f / Width);
            targetMarker = star.transform;
            targetMarker.localRotation = Quaternion.identity;
            star.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 0, 90);

            pointer = AbilityFx.Line("Spear pointer", new Color(1f, .85f, .45f, .6f), .006f, 2);
            pointer.transform.SetParent(root.transform, false);
            root.SetActive(false);
        }

        LineRenderer LocalLine(string name, Color color, float width, int points)
        {
            var line = AbilityFx.Line(name, color, width, points);
            line.useWorldSpace = false;
            line.transform.SetParent(quad, false);
            // Line widths stay in world units even under the scaled map.
            line.startWidth = line.endWidth = width;
            return line;
        }

        /// <summary>Renders the Rift once from straight above into the hologram's texture.</summary>
        void RenderTopDown(Vector3 centre)
        {
            var go = new GameObject("Grand Starfall map camera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = mapSize * .5f;
            cam.transform.SetPositionAndRotation(centre + Vector3.up * 120, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            cam.nearClipPlane = 1;
            cam.farClipPlane = 260;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.03f, .05f, .09f, 1);
            cam.cullingMask = ~(1 << 5);
            cam.targetTexture = texture;
            var request = new RenderPipeline.StandardRequest { destination = texture };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();
            cam.targetTexture = null;
            Object.Destroy(go);
        }

        public void Show(bool visible)
        {
            if (root)
                root.SetActive(visible);
        }

        /// <summary>Floats the map at a point, world-aligned, tilted a little toward the eyes for readability.</summary>
        public void Place(Vector3 position, Vector3 eye)
        {
            if (!root)
                return;
            Vector3 toEye = Geo.FlatDirection(eye - position, Vector3.back);
            root.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, toEye, .35f)));
        }

        Vector3 Local(Vector3 world) => new((world.x - mapMin.x) / mapSize - .5f, (world.z - mapMin.z) / mapSize - .5f, 0);

        /// <summary>Where a ray (the spear) points on the map, as a point on the Rift.</summary>
        public bool Point(Ray ray, out Vector3 world)
        {
            world = default;
            pointer.enabled = false;
            if (!root || !root.activeSelf)
                return false;
            var plane = new Plane(quad.forward, quad.position);
            if (!plane.Raycast(ray, out float distance) || distance > 2.5f)
                return false;
            Vector3 hit = ray.GetPoint(distance);
            Vector3 local = quad.InverseTransformPoint(hit);
            if (Mathf.Abs(local.x) > .5f || Mathf.Abs(local.y) > .5f)
                return false;
            pointer.enabled = true;
            pointer.SetPosition(0, ray.origin);
            pointer.SetPosition(1, hit);
            world = new Vector3(mapMin.x + (local.x + .5f) * mapSize, player.Feet.y, mapMin.z + (local.y + .5f) * mapSize);
            var match = RiftMatch.Instance;
            if (match && match.Ground(world, out var ground))
                world = ground;
            return true;
        }

        public void SetTarget(Vector3 world)
        {
            Target = world;
            if (targetMarker)
                targetMarker.localPosition = Local(world) + new Vector3(0, 0, -.03f);
        }

        /// <summary>Marks the player and the reach of the leap around them.</summary>
        public void ShowRange(Vector3 feet, float range, float landingRadius)
        {
            if (!root)
                return;
            Vector3 me = Local(feet);
            playerDot.localPosition = me + new Vector3(0, 0, -.02f);
            float r = range / mapSize;
            for (int i = 0; i < rangeLine.positionCount; i++)
            {
                float a = i * Mathf.PI * 2 / rangeLine.positionCount;
                rangeLine.SetPosition(i, me + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -.01f));
            }
            if (targetMarker)
                targetMarker.localScale = Vector3.one * Mathf.Max(.06f / Width, landingRadius * 2 / mapSize);
        }

        public void Destroy()
        {
            if (root)
                Object.Destroy(root);
            if (material)
                Object.Destroy(material);
            if (texture)
            {
                texture.Release();
                Object.Destroy(texture);
            }
        }
    }

    /// <summary>Moves an effect at a constant velocity (spectral spear lunges, the falling comet).</summary>
    public class FxLunge : MonoBehaviour
    {
        public Vector3 velocity;

        void Update() => transform.position += velocity * Time.deltaTime;
    }

    /// <summary>An expanding ground arc or ring drawn by a line renderer; its own material fades as it grows.</summary>
    public class FxShockwave : MonoBehaviour
    {
        LineRenderer line;
        Material material;
        Vector3 centre, front;
        float radius, halfAngle, duration, born;
        Color color;

        public void Begin(Vector3 at, Vector3 forward, float maxRadius, float half, float seconds)
        {
            line = GetComponent<LineRenderer>();
            material = new Material(line.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            line.sharedMaterial = material;
            color = material.GetColor("_BaseColor");
            centre = at;
            front = Geo.FlatDirection(forward, Vector3.forward);
            radius = maxRadius;
            halfAngle = half;
            duration = seconds;
            born = Time.time;
            line.loop = halfAngle >= 180;
            Update();
        }

        void Update()
        {
            if (!line)
                return;
            float t = Mathf.Clamp01((Time.time - born) / duration);
            float r = Mathf.Lerp(.3f, radius, 1 - (1 - t) * (1 - t));
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = halfAngle >= 180 ? i * 360f / n : Mathf.Lerp(-halfAngle, halfAngle, i / (n - 1f));
                line.SetPosition(i, centre + Quaternion.Euler(0, a, 0) * front * r + Vector3.up * .08f);
            }
            var c = color;
            c.a *= 1 - t;
            material.SetColor("_BaseColor", c);
            line.widthMultiplier = Mathf.Lerp(1.4f, .5f, t);
            if (t >= 1)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material)
                Destroy(material);
        }
    }
}
