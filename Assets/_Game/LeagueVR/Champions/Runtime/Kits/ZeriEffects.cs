using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Zeri's visuals. Everything glows additively and fades by thinning out, so effects never pop: the gun's coils glow
    /// with the Sparkpack charge, a laser sight runs from the muzzle, the enemy the battery shot would hit is ringed (dim,
    /// bright when charged, red when the shot would execute), and bursts, bolts, chains, the wall laser and the nova all
    /// crackle in her yellow-green and blue. Only the wall laser and its warning draw through walls.
    /// </summary>
    public class ZeriVisuals
    {
        readonly ZeriKit kit;
        readonly PlayerChampion player;
        readonly List<GameObject> owned = new();

        Transform leftCoil, rightCoil;
        GameObject leftGlow, rightGlow, palmOrb, palmHalo, sightDot;
        ZeriSprite leftSprite, rightSprite;
        ParticleSystem coilSparks, palmSparks, crashSparks, impactSparks;
        LineRenderer sight, coilArc, palmArc, palmGuide, targetRing, crashRing;
        Combatant shownTarget;
        float nextTargetScan;
        int targetLook = -1;

        public ZeriVisuals(ZeriKit kit, PlayerChampion player)
        {
            this.kit = kit;
            this.player = player;
            sight = Keep(AbilityFx.Line("Zeri laser sight", new Color(.62f, 1f, .38f, .6f), .006f, 2));
            sight.endWidth = .002f;
            sightDot = Keep(ZeriFx.Sphere("Zeri sight dot", new Color(.75f, 1f, .5f, .9f), .04f));
            coilArc = Keep(AbilityFx.Line("Zeri coil arc", ZeriKit.Volt, .008f, 7));
            palmGuide = Keep(AbilityFx.Line("Zeri pulse guide", new Color(1f, .95f, .55f, .45f), .006f, 2));
            palmGuide.endWidth = .002f;
            palmArc = Keep(AbilityFx.Line("Zeri palm crackle", ZeriKit.Volt, .006f, 5));
            targetRing = Keep(AbilityFx.Line("Zeri battery target", ZeriKit.Spark, .035f, 40));
            targetRing.loop = true;
            crashRing = Keep(AbilityFx.Line("Zeri crash charge", ZeriKit.Volt, .05f, 48));
            crashRing.loop = true;
            palmOrb = Keep(ZeriFx.Sphere("Zeri pulse orb", new Color(1f, .95f, .6f, .9f), 1));
            palmHalo = ZeriFx.Sphere("Zeri pulse halo", new Color(.45f, .85f, 1f, .35f), 1.8f);
            palmHalo.transform.SetParent(palmOrb.transform, false);
            palmSparks = AbilityFx.Motes(palmOrb.transform, ZeriKit.Volt, .02f, .25f, 0, ParticleSystemShapeType.Sphere, .5f);
            var crashHost = Keep(new GameObject("Zeri crash sparks"));
            crashSparks = AbilityFx.Motes(crashHost.transform, ZeriKit.Volt, .05f, .45f, 0, ParticleSystemShapeType.Circle, .6f);
            var impactHost = Keep(new GameObject("Zeri impact sparks"));
            impactSparks = AbilityFx.Motes(impactHost.transform, ZeriKit.Volt, .035f, .3f, 0, ParticleSystemShapeType.Sphere, .05f);
            var main = impactSparks.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.gravityModifier = .6f;
            HideAll();
        }

        T Keep<T>(T component) where T : Component
        {
            Keep(component.gameObject);
            return component;
        }

        GameObject Keep(GameObject go)
        {
            owned.Add(go);
            player.Track(go);
            return go;
        }

        public void Destroy()
        {
            foreach (var go in owned)
                if (go)
                    Object.Destroy(go);
            owned.Clear();
            DestroyCoilGlows();
        }

        /// <summary>Hides every persistent piece (death, kit swap) and stops anything still emitting.</summary>
        public void HideAll()
        {
            sight.enabled = coilArc.enabled = palmGuide.enabled = palmArc.enabled = targetRing.enabled = crashRing.enabled = false;
            sightDot.SetActive(false);
            palmOrb.SetActive(false);
            crashSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            crashSparks.Play();
            var emission = crashSparks.emission;
            emission.rateOverTime = 0;
            if (leftGlow)
                leftGlow.SetActive(false);
            if (rightGlow)
                rightGlow.SetActive(false);
            StopCoilSparks();
        }

        void StopCoilSparks()
        {
            if (coilSparks && coilSparks.isEmitting)
                coilSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // ---------- Gun coils and sight ----------

        ChampionVRAvatar Avatar => player.Body as ChampionVRAvatar;

        /// <summary>Middle of each coil mesh in its bone's space (the bones sit where the coils meet the gun).</summary>
        static readonly Vector3 CoilCentre = new(-.05f, 0, 0);

        bool GunOut => !player.DesktopMode && Avatar && Avatar.Weapon != null && Avatar.Weapon.Visible;

        /// <summary>Finds the coils on the current first-person body; the body is rebuilt when champions change.</summary>
        bool FindCoils()
        {
            if (leftCoil && rightCoil)
                return true;
            DestroyCoilGlows();
            var avatar = Avatar;
            if (!avatar || !avatar.Instance)
                return false;
            foreach (var t in avatar.Instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "L_Weapon_Coil")
                    leftCoil = t;
                else if (t.name == "R_Weapon_Coil")
                    rightCoil = t;
            }
            if (!leftCoil || !rightCoil)
                return false;
            // The glows sit on the coil bodies, lifted toward the eyes so the coil meshes do not hide them.
            leftSprite = ZeriSprite.Create(player, leftCoil.TransformPoint(CoilCentre), "ZeriFx_Glow", ZeriKit.Spark, 1, 1, 0, parent: leftCoil);
            rightSprite = ZeriSprite.Create(player, rightCoil.TransformPoint(CoilCentre), "ZeriFx_Glow", ZeriKit.Spark, 1, 1, 0, parent: rightCoil);
            leftSprite.lift = rightSprite.lift = .055f;
            leftGlow = leftSprite.gameObject;
            rightGlow = rightSprite.gameObject;
            var sparkHost = new GameObject("Zeri coil sparks");
            sparkHost.transform.SetParent(rightCoil, false);
            sparkHost.transform.localPosition = CoilCentre;
            coilSparks = AbilityFx.Motes(sparkHost.transform, ZeriKit.Volt, .012f, .25f, 0, ParticleSystemShapeType.Sphere, .02f);
            return true;
        }

        void DestroyCoilGlows()
        {
            if (leftGlow)
                Object.Destroy(leftGlow);
            if (rightGlow)
                Object.Destroy(rightGlow);
            if (coilSparks)
                Object.Destroy(coilSparks.transform.parent.gameObject);
            leftGlow = rightGlow = null;
            leftCoil = rightCoil = null;
            coilSparks = null;
        }

        /// <summary>After each body pose: the coils, the sight and the arcs follow the freshly posed gun.</summary>
        public void Pose()
        {
            // The gun's lights go with the body: not while the view is outside it (vaults, the brush view).
            bool gun = GunOut && player.Health && player.Health.IsAlive && !(player.Body && player.Body.Hidden) && FindCoils();
            if (leftGlow)
                leftGlow.SetActive(gun);
            if (rightGlow)
                rightGlow.SetActive(gun);
            if (!gun)
            {
                sight.enabled = coilArc.enabled = false;
                sightDot.SetActive(false);
                StopCoilSparks();
                return;
            }
            // Zeri's glow swells on each coil as the Sparkpack fills: green when charging, blue-white when Overcharged.
            float fill = kit.Charge / 100f;
            // A full Sparkpack breathes so the ready state reads at a glance without a constant bright blob.
            float size = Mathf.Lerp(.07f, .16f, fill) * (kit.Overcharged ? 1.3f : 1);
            float breathe = kit.FullyCharged || kit.Overcharged ? .85f + .15f * Mathf.Sin(Time.time * 6) : 1;
            var glow = kit.Overcharged ? new Color(.55f, .88f, 1f, .7f * breathe) : new Color(.62f, 1f, .38f, (.25f + .4f * fill) * breathe);
            leftSprite.size = rightSprite.size = size;
            leftSprite.Tint(glow);
            rightSprite.Tint(glow);
            if (coilSparks)
            {
                if (!coilSparks.isPlaying)
                    coilSparks.Play();
                var emission = coilSparks.emission;
                emission.rateOverTime = kit.Overcharged ? 80 : kit.FullyCharged ? 35 : 8 * fill;
            }
            bool arc = kit.FullyCharged || kit.Overcharged;
            coilArc.enabled = arc;
            if (arc)
                Jagged(coilArc, leftCoil.TransformPoint(CoilCentre), rightCoil.TransformPoint(CoilCentre), .015f);
            // Laser sight: from the muzzle to the first thing the gun points at.
            Vector3 muzzle = kit.Muzzle(out var aim);
            float range = ZeriKit.BurstRange;
            if (player.TerrainRay(muzzle, aim, range, out var wall))
                range = wall.distance;
            bool show = !RiftUI.BlocksCombat;
            sight.enabled = show;
            sightDot.SetActive(show && range < ZeriKit.BurstRange);
            if (show)
            {
                sight.SetPosition(0, muzzle);
                sight.SetPosition(1, muzzle + aim * range);
                sightDot.transform.position = muzzle + aim * range;
            }
        }

        /// <summary>Per frame: the battery-shot marker, the charging pulse in the left palm and the Lightning Crash charge.</summary>
        public void Tick(float pulseCharge, float crashCharge)
        {
            // Battery target (looked up 20 times a second): dim ring, bright when charged, red when the shot executes.
            if (Time.time >= nextTargetScan)
            {
                nextTargetScan = Time.time + .05f;
                shownTarget = (player.DesktopMode || GunOut) && !RiftUI.BlocksCombat ? kit.BatteryTarget() : null;
            }
            bool showTarget = shownTarget && shownTarget.IsAlive;
            targetRing.enabled = showTarget;
            if (showTarget)
            {
                int look = kit.WouldExecute(shownTarget) ? 2 : kit.FullyCharged ? 1 : 0;
                if (look != targetLook)
                {
                    targetLook = look;
                    targetRing.sharedMaterial = AbilityFx.Glass(look == 2 ? ZeriKit.Danger : look == 1 ? ZeriKit.Spark : new Color(.62f, 1f, .38f, .35f), true);
                    targetRing.startWidth = targetRing.endWidth = look == 2 ? .045f : .035f;
                }
                float pulse = 1 + Mathf.Sin(Time.time * (look == 2 ? 10 : 6)) * .06f;
                AbilityFx.SetCircle(targetRing, shownTarget.transform.position, (shownTarget.GetComponent<RiftStructure>() ? 1.6f : .55f) * pulse);
            }
            // Ultrashock Laser charging in the left palm: a yellow-white orb in a blue halo, crackling to the fingers.
            bool pulsing = pulseCharge >= 0;
            palmOrb.SetActive(pulsing);
            palmGuide.enabled = pulsing;
            palmArc.enabled = pulsing;
            if (pulsing)
            {
                var aim = kit.PalmAim();
                Vector3 forward = aim.rotation * Vector3.forward;
                Vector3 orb = aim.position + forward * .07f;
                palmOrb.transform.position = orb;
                palmOrb.transform.localScale = Vector3.one * Mathf.Lerp(.025f, .06f, pulseCharge);
                var emission = palmSparks.emission;
                emission.rateOverTime = Mathf.Lerp(10, pulseCharge >= 1 ? 90 : 50, pulseCharge);
                Jagged(palmArc, aim.position, orb, .015f);
                float range = ZeriKit.PulseRange;
                if (player.TerrainRay(aim.position, forward, range, out var wall))
                    range = wall.distance;
                palmGuide.SetPosition(0, orb);
                palmGuide.SetPosition(1, aim.position + forward * range);
            }
            // Lightning Crash charging: a ring tightening at the feet and sparks rising.
            bool crashing = crashCharge >= 0;
            crashRing.enabled = crashing;
            var sparks = crashSparks.emission;
            sparks.rateOverTime = crashing ? 60 : 0;
            if (crashing)
            {
                Vector3 feet = player.Feet;
                crashSparks.transform.parent.position = feet + Vector3.up * .05f;
                crashSparks.transform.parent.rotation = Quaternion.Euler(90, 0, 0);
                AbilityFx.SetCircle(crashRing, feet, Mathf.Lerp(2.2f, .8f, crashCharge));
            }
        }

        // ---------- One-shot effects ----------

        /// <summary>A Burst Fire round: a muzzle flash and a short glowing streak (thicker and blue when Overcharged).</summary>
        public void Tracer(Vector3 from, Vector3 to, bool overcharged)
        {
            var color = overcharged ? ZeriKit.Plasma : ZeriKit.Volt;
            ZeriBeam.Create(player, from, to, color, overcharged ? .09f : .06f, overcharged ? .06f : .04f, .08f, 0, texture: "ZeriFx_Tracer");
            ZeriBeam.Create(player, from, to, Color.white, .015f, .005f, .05f, 0);
            Vector3 along = to - from;
            ZeriSprite.Create(player, from + along.normalized * .06f, "ZeriFx_Muzzle", new Color(1f, .95f, .6f, 1f), .2f, .28f, .06f, along.sqrMagnitude > 1e-6f ? along : Vector3.up);
        }

        /// <summary>Sparks and a small flash where something is hit.</summary>
        public void Impact(Vector3 at, Color color, int sparks = 5)
        {
            ZeriSprite.Create(player, at, "ZeriFx_Hit", new Color(color.r, color.g, color.b, .9f), .3f, .55f, .14f);
            impactSparks.Emit(new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true, startColor = color }, sparks);
        }

        public void BatteryBolt(Vector3 from, Vector3 to, bool big)
        {
            ZeriArc.Create(player, from, to, ZeriKit.Volt, big ? .45f : .25f, big ? .3f : .16f, textured: true);
            ZeriSprite.Create(player, from, "ZeriFx_Muzzle", new Color(1f, .95f, .6f, 1f), big ? .3f : .2f, big ? .4f : .28f, .07f, to - from);
            Impact(to, big ? ZeriKit.Volt : ZeriKit.Spark, big ? 12 : 5);
        }

        /// <summary>An executed enemy: a red ring scorched into the ground and a burst of red sparks.</summary>
        public void ExecuteMark(Vector3 ground)
        {
            ZeriRing.Create(player, ground + Vector3.up * .05f, Vector3.up, .5f, ZeriKit.Danger, .05f, 1.5f);
            impactSparks.Emit(new ParticleSystem.EmitParams { position = ground + Vector3.up * .6f, applyShapeToPosition = true, startColor = ZeriKit.Danger }, 12);
        }

        public void Arc(Vector3 from, Vector3 to, Color color, float seconds, float width = .03f) => ZeriArc.Create(player, from, to, color, width, seconds);

        /// <summary>Overcharged chain lightning: a fat blue arc with a yellow core and a ring on the enemy it jumps to.</summary>
        public void Chain(Vector3 from, Vector3 to)
        {
            ZeriArc.Create(player, from, to, ZeriKit.Plasma, .5f, .3f, textured: true);
            Arc(from, to, ZeriKit.Volt, .25f, .045f);
            ZeriRing.Create(player, to, Vector3.up, .35f, ZeriKit.Plasma, .04f, .3f);
            Impact(to, ZeriKit.Plasma, 4);
        }

        public void SurgeTrail(Vector3 from, Vector3 to)
        {
            ZeriArc.Create(player, from, to, ZeriKit.Spark, .6f, .5f, textured: true);
            Arc(from, to, ZeriKit.Volt, .4f, .025f);
            Impact(from, ZeriKit.Spark, 8);
            Impact(to, ZeriKit.Spark, 10);
        }

        /// <summary>A bolt from the sky onto an enemy hit by Lightning Crash, leaving a scorch ring.</summary>
        public void Strike(Vector3 at, Vector3 ground)
        {
            ZeriArc.Create(player, at + Vector3.up * 14 + Random.insideUnitSphere, at, ZeriKit.Volt, 1.2f, .5f, textured: true);
            Arc(at + Vector3.up * 14, at, Color.white, .35f, .03f);
            ZeriRing.Create(player, ground + Vector3.up * .05f, Vector3.up, .8f, ZeriKit.Volt, .05f, 2);
            Impact(at, ZeriKit.Volt, 8);
        }

        public void Nova(Vector3 feet)
        {
            ZeriNova.Create(player, feet, ZeriKit.NovaRadius);
            ZeriSprite.Create(player, feet + Vector3.up * .1f, "ZeriFx_Ring", new Color(1f, .93f, .4f, .55f), 1, ZeriKit.NovaRadius * 2.2f, .5f, flat: true);
            ZeriFlash.Create(player, feet + Vector3.up * .05f, new Color(1f, .95f, .6f, .25f), 2.5f, .2f, flat: true);
        }

        /// <summary>The pulse hit an enemy: sparks, and a blue slow ring at its feet for the slow's two seconds.</summary>
        public void PulseImpact(Vector3 at, Vector3 ground)
        {
            Impact(at, ZeriKit.Plasma, 8);
            ZeriRing.Create(player, ground + Vector3.up * .05f, Vector3.up, .6f, ZeriKit.Plasma, .05f, 2);
        }

        /// <summary>The wall the pulse hit glows and a warning line shows where the laser will fire (visible through walls).</summary>
        public void LaserTelegraph(Vector3 wall, Vector3 normal, Vector3 end, float seconds)
        {
            ZeriBeam.Create(player, wall, end, new Color(1f, .95f, .45f, .55f), .1f, .1f, seconds, 0, xray: true);
            ZeriRing.Create(player, wall + normal * .03f, normal, .5f, ZeriKit.Volt, .06f, seconds, pulse: true);
            Impact(wall, ZeriKit.Volt, 10);
        }

        /// <summary>The laser itself: a glowing fringe, a yellow core and a white-hot centre, drawn through walls.</summary>
        public void Laser(Vector3 from, Vector3 to)
        {
            ZeriBeam.Create(player, from, to, new Color(1f, .93f, .4f, .3f), ZeriKit.LaserHalfWidth * 1.2f, ZeriKit.LaserHalfWidth, .38f, 0, xray: true, texture: "ZeriFx_BeamFringe");
            ZeriBeam.Create(player, from, to, new Color(1f, .95f, .5f, .9f), 1f, .7f, .35f, 0, xray: true, texture: "ZeriFx_BeamCore");
            ZeriBeam.Create(player, from, to, new Color(1f, 1f, 1f, .9f), .35f, .2f, .3f, 0, xray: true, texture: "ZeriFx_BeamCore");
            ZeriArc.Create(player, from, to, ZeriKit.Volt, .8f, .4f, textured: true);
        }

        /// <summary>Points a line along a jittered lightning path between two points.</summary>
        public static void Jagged(LineRenderer line, Vector3 a, Vector3 b, float amplitude)
        {
            int n = line.positionCount;
            Vector3 delta = b - a;
            Vector3 side = Vector3.Cross(delta, Vector3.up);
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.Cross(delta, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, delta).normalized;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float edge = Mathf.Sin(t * Mathf.PI);
                Vector3 offset = (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * amplitude * edge;
                line.SetPosition(i, a + delta * t + offset);
            }
        }
    }

    /// <summary>
    /// Shared materials and meshes for Zeri's effects. Textures are Zeri's original League particle textures
    /// (Resources/ZeriFx, turned into white glow masks so they can be tinted): her glow trail, chain-lightning bolts,
    /// beam gradient, ring, hit star, glow and muzzle flash.
    /// </summary>
    public static class ZeriFx
    {
        static Mesh sphere, quad;
        static Shader overlay;
        static readonly Dictionary<Color, Material> xray = new();
        static readonly Dictionary<(string, Color), Material> textured = new();
        static readonly Dictionary<string, Material> texturedXRay = new();
        static readonly Dictionary<string, Texture2D> textures = new();

        public static Texture2D Tex(string name)
        {
            if (!textures.TryGetValue(name, out var texture) || !texture)
                textures[name] = texture = Resources.Load<Texture2D>("ZeriFx/" + name);
            return texture;
        }

        /// <summary>One of Zeri's four chain-lightning bolts, picked at random.</summary>
        public static string Bolt() => "ZeriFx_Bolt" + Random.Range(0, 4);

        /// <summary>An additive, two-sided glow material showing one of Zeri's textures in a colour (cached).</summary>
        public static Material TexGlow(string texture, Color color)
        {
            if (textured.TryGetValue((texture, color), out var material) && material)
                return material;
            material = new Material(AbilityFx.Glass(color, true, true)) { name = "Zeri " + texture, hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", Tex(texture));
            textured[(texture, color)] = material;
            return material;
        }

        /// <summary>Like <see cref="XRay"/>, with one of Zeri's textures (the through-wall laser).</summary>
        public static Material TexXRay(string texture)
        {
            if (texturedXRay.TryGetValue(texture, out var material) && material)
                return material;
            material = new Material(XRay(Color.white)) { name = "Zeri x-ray " + texture, hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(material.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap", Tex(texture));
            texturedXRay[texture] = material;
            return material;
        }

        /// <summary>Unity's built-in quad, taken from a primitive once (sprites).</summary>
        public static Mesh QuadMesh
        {
            get
            {
                if (quad)
                    return quad;
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = go.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(go);
                return quad;
            }
        }

        /// <summary>Unity's built-in sphere, taken from a primitive once.</summary>
        public static Mesh SphereMesh
        {
            get
            {
                if (sphere)
                    return sphere;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere = go.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(go);
                return sphere;
            }
        }

        /// <summary>An additive glow material (cached per colour).</summary>
        public static Material Glow(Color color) => AbilityFx.Glass(color, true);

        /// <summary>A glowing sphere, optionally parented.</summary>
        public static GameObject Sphere(string name, Color color, float scale, Transform parent = null)
        {
            var go = AbilityFx.MeshObject(name, SphereMesh, color, true, parent, scale);
            if (parent)
                go.transform.localPosition = Vector3.zero;
            return go;
        }

        /// <summary>
        /// A line material that ignores depth (the menus' overlay shader): the wall laser and its warning must be seen
        /// through the wall that caused them. Line colours fade it.
        /// </summary>
        public static Material XRay(Color color)
        {
            if (xray.TryGetValue(color, out var material) && material)
                return material;
            if (!overlay)
                overlay = Resources.Load<Shader>("RiftPanelOverlay");
            if (!overlay || !overlay.isSupported)
                return Glow(color);
            material = new Material(overlay) { name = "Zeri x-ray " + ColorUtility.ToHtmlStringRGBA(color), hideFlags = HideFlags.HideAndDontSave, renderQueue = 3100 };
            material.SetColor("_Color", Color.white);
            xray[color] = material;
            return material;
        }
    }

    /// <summary>A straight glowing streak that thins out (and optionally crackles) over its life.</summary>
    public class ZeriBeam : MonoBehaviour
    {
        LineRenderer line;
        Vector3 from, to;
        float born, duration, startWidth, endWidth, jitter;
        Color color;
        bool fadeColor;
        Gradient gradient;
        GradientColorKey[] colorKeys;
        GradientAlphaKey[] alphaKeys;

        public static ZeriBeam Create(PlayerChampion owner, Vector3 from, Vector3 to, Color color, float startWidth, float endWidth, float seconds, float jitter, bool xray = false, string texture = null)
        {
            // A straight x-ray beam gets two extra points where its soft ends meet the middle: line colours are only
            // set at points, so with two points both ends' zero alpha would hide the whole beam.
            int points = jitter > 0 ? Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / .5f) + 2, 3, 30) : xray ? 4 : 2;
            var line = AbilityFx.Line(xray ? "Zeri laser" : "Zeri streak", color, startWidth, points);
            if (xray)
            {
                line.sharedMaterial = texture != null ? ZeriFx.TexXRay(texture) : ZeriFx.XRay(color);
                line.startColor = line.endColor = color;
            }
            else if (texture != null)
                line.sharedMaterial = ZeriFx.TexGlow(texture, color);
            line.textureMode = LineTextureMode.Stretch;
            var beam = line.gameObject.AddComponent<ZeriBeam>();
            beam.line = line;
            beam.from = from;
            beam.to = to;
            beam.color = color;
            beam.fadeColor = xray;
            beam.startWidth = startWidth;
            beam.endWidth = endWidth;
            beam.duration = seconds;
            beam.jitter = jitter;
            beam.born = Time.time;
            beam.Update();
            owner.Track(line.gameObject);
            return beam;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            float shrink = 1 - t * t;
            line.startWidth = startWidth * shrink;
            line.endWidth = endWidth * shrink;
            if (fadeColor)
            {
                // Soft ends: a laser seen along its length must not show hard rectangular caps.
                float a = color.a * (1 - t);
                if (gradient == null)
                {
                    gradient = new Gradient();
                    colorKeys = new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) };
                    alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, .06f), new GradientAlphaKey(a, .75f), new GradientAlphaKey(0, 1) };
                }
                alphaKeys[1].alpha = alphaKeys[2].alpha = a;
                gradient.SetKeys(colorKeys, alphaKeys);
                line.colorGradient = gradient;
            }
            if (jitter > 0)
                ZeriVisuals.Jagged(line, from, to, jitter);
            else if (line.positionCount == 4)
            {
                line.SetPosition(0, from);
                line.SetPosition(1, Vector3.Lerp(from, to, .06f));
                line.SetPosition(2, Vector3.Lerp(from, to, .75f));
                line.SetPosition(3, to);
            }
            else
            {
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }
        }
    }

    /// <summary>A brief glowing flash (muzzle, impact, nova disc) that shrinks and fades away.</summary>
    public class ZeriFlash : MonoBehaviour
    {
        Material material;
        Color color;
        Vector3 size;
        float born, duration;

        public static ZeriFlash Create(PlayerChampion owner, Vector3 at, Color color, float radius, float seconds, bool flat = false)
        {
            var go = new GameObject("Zeri flash");
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = ZeriFx.SphereMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var flash = go.AddComponent<ZeriFlash>();
            flash.material = new Material(ZeriFx.Glow(Color.white)) { hideFlags = HideFlags.HideAndDontSave };
            renderer.sharedMaterial = flash.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flash.color = color;
            flash.size = flat ? new Vector3(radius * 2, .02f, radius * 2) : Vector3.one * radius * 2;
            flash.duration = seconds;
            flash.born = Time.time;
            flash.Update();
            owner.Track(go);
            return flash;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = size * (1 - .4f * t);
            var c = color;
            c.a *= 1 - t;
            material.SetColor("_BaseColor", c);
        }

        void OnDestroy()
        {
            if (material)
                Destroy(material);
        }
    }

    /// <summary>A glowing ring on a surface (slow marker, scorch, wall glow) that thins out; it can pulse while it lasts.</summary>
    public class ZeriRing : MonoBehaviour
    {
        LineRenderer line;
        Vector3 centre, axisA, axisB;
        float radius, width, born, duration;
        bool pulse;

        public static ZeriRing Create(PlayerChampion owner, Vector3 centre, Vector3 normal, float radius, Color color, float width, float seconds, bool pulse = false)
        {
            var line = AbilityFx.Line("Zeri ring", color, width, 40);
            line.loop = true;
            var ring = line.gameObject.AddComponent<ZeriRing>();
            ring.line = line;
            ring.centre = centre;
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            ring.axisA = Vector3.Cross(normal, Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up).normalized;
            ring.axisB = Vector3.Cross(normal, ring.axisA);
            ring.radius = radius;
            ring.width = width;
            ring.duration = seconds;
            ring.pulse = pulse;
            ring.born = Time.time;
            ring.Update();
            owner.Track(line.gameObject);
            return ring;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            float fade = t < .7f ? 1 : 1 - (t - .7f) / .3f;
            line.startWidth = line.endWidth = width * fade;
            float r = radius * (pulse ? 1 + Mathf.Sin(Time.time * 18) * .12f : 1);
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                line.SetPosition(i, centre + (axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a)) * r);
            }
        }
    }

    /// <summary>
    /// A crackling lightning arc: one of Zeri's original chain-lightning bolts stretched between two points, swapped
    /// for another bolt every few frames and bent slightly, thinning out over its life. Plain arcs re-fork a line.
    /// </summary>
    public class ZeriArc : MonoBehaviour
    {
        LineRenderer line;
        Vector3 from, to;
        float born, duration, width, nextBolt;
        Color color;
        bool textured;

        public static ZeriArc Create(PlayerChampion owner, Vector3 from, Vector3 to, Color color, float width, float seconds, bool textured = false)
        {
            textured = textured && ZeriFx.Tex("ZeriFx_Bolt0");
            int points = textured ? Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / 1.2f) + 2, 4, 12) : Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / .35f) + 2, 4, 40);
            var line = AbilityFx.Line("Zeri lightning", color, width, points);
            line.textureMode = LineTextureMode.Stretch;
            var arc = line.gameObject.AddComponent<ZeriArc>();
            arc.line = line;
            arc.from = from;
            arc.to = to;
            arc.width = width;
            arc.color = color;
            arc.textured = textured;
            arc.duration = seconds;
            arc.born = Time.time;
            arc.Update();
            owner.Track(line.gameObject);
            return arc;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, duration);
            if (t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            float w = width * (1 - t * t);
            line.startWidth = w;
            line.endWidth = w * .6f;
            if (!textured)
            {
                ZeriVisuals.Jagged(line, from, to, Mathf.Min(.4f, Vector3.Distance(from, to) * .06f));
                return;
            }
            if (Time.time >= nextBolt)
            {
                nextBolt = Time.time + .045f;
                line.sharedMaterial = ZeriFx.TexGlow(ZeriFx.Bolt(), color);
                ZeriVisuals.Jagged(line, from, to, Mathf.Min(.25f, Vector3.Distance(from, to) * .03f));
            }
        }
    }

    /// <summary>
    /// A camera-facing sprite with one of Zeri's textures (hit star, muzzle flash, glow) that grows and fades. Axial
    /// sprites keep their up along a direction (a muzzle flash along the barrel); flat ones lie on the ground (the nova
    /// shockwave). A lifetime of zero keeps it until destroyed; <see cref="size"/> scales it from outside (coil glows).
    /// </summary>
    public class ZeriSprite : MonoBehaviour
    {
        static readonly MaterialPropertyBlock block = new();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        MeshRenderer spriteRenderer;
        Color color;
        Transform view;
        Vector3 axis, anchor;
        float born, duration, startSize, endSize, roll;
        bool axial, flat;
        public float size = 1;

        /// <summary>For a sprite on a parent: metres it is pulled from its anchor toward the eyes (glows on meshes).</summary>
        public float lift;

        public static ZeriSprite Create(PlayerChampion owner, Vector3 at, string texture, Color color, float startSize, float endSize, float seconds, Vector3? axis = null, bool flat = false, Transform parent = null)
        {
            var go = new GameObject("Zeri sprite " + texture);
            if (parent)
                go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = ZeriFx.QuadMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var sprite = go.AddComponent<ZeriSprite>();
            renderer.sharedMaterial = ZeriFx.TexGlow(texture, Color.white);
            sprite.spriteRenderer = renderer;
            sprite.color = color;
            sprite.view = owner.head ? owner.head.transform : null;
            sprite.axial = axis.HasValue;
            sprite.axis = axis.HasValue ? axis.Value.normalized : Vector3.up;
            sprite.flat = flat;
            sprite.startSize = startSize;
            sprite.endSize = endSize;
            sprite.duration = seconds;
            sprite.roll = axis.HasValue ? 0 : Random.Range(0, 360f);
            sprite.born = Time.time;
            sprite.anchor = go.transform.localPosition;
            sprite.LateUpdate();
            if (!parent)
                owner.Track(go);
            return sprite;
        }

        public void Tint(Color value) => color = value;

        void LateUpdate()
        {
            float t = duration > 0 ? (Time.time - born) / duration : 0;
            if (duration > 0 && t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            float eased = 1 - (1 - t) * (1 - t);
            float scale = Mathf.Lerp(startSize, endSize, eased) * size;
            float parentScale = transform.parent ? Mathf.Max(1e-4f, transform.parent.lossyScale.x) : 1;
            transform.localScale = Vector3.one * (scale / parentScale);
            var c = color;
            c.a *= duration > 0 ? 1 - t * t : 1;
            block.SetColor(BaseColor, c);
            spriteRenderer.SetPropertyBlock(block);
            if (lift > 0 && view && transform.parent)
            {
                Vector3 at = transform.parent.TransformPoint(anchor);
                transform.position = at + (view.position - at).normalized * lift;
            }
            if (flat)
                transform.rotation = Quaternion.Euler(90, roll, 0);
            else if (view)
            {
                Vector3 toView = view.position - transform.position;
                if (axial)
                {
                    Vector3 facing = Vector3.ProjectOnPlane(toView, axis);
                    if (facing.sqrMagnitude > 1e-6f)
                        transform.rotation = Quaternion.LookRotation(facing, axis);
                }
                else if (toView.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(toView) * Quaternion.Euler(0, 0, roll);
            }
        }
    }

    /// <summary>Lightning Crash: a ring of lightning racing out across the floor to the nova's edge.</summary>
    public class ZeriNova : MonoBehaviour
    {
        LineRenderer ring, inner;
        Vector3 centre;
        float radius, born;
        const float Expand = .35f, Linger = .4f;

        public static ZeriNova Create(PlayerChampion owner, Vector3 centre, float radius)
        {
            var go = new GameObject("Zeri Lightning Crash");
            var nova = go.AddComponent<ZeriNova>();
            nova.centre = centre;
            nova.radius = radius;
            nova.born = Time.time;
            nova.ring = AbilityFx.Line("Nova ring", ZeriKit.Volt, .25f, 72);
            nova.ring.loop = true;
            nova.ring.transform.SetParent(go.transform, true);
            nova.inner = AbilityFx.Line("Nova inner ring", ZeriKit.Plasma, .1f, 72);
            nova.inner.loop = true;
            nova.inner.transform.SetParent(go.transform, true);
            owner.Track(go);
            return nova;
        }

        void Update()
        {
            float age = Time.time - born;
            if (age > Expand + Linger)
            {
                Destroy(gameObject);
                return;
            }
            float t = Mathf.Clamp01(age / Expand);
            float eased = 1 - (1 - t) * (1 - t);
            float fade = 1 - Mathf.Clamp01((age - Expand) / Linger);
            Wobble(ring, radius * eased, .25f * fade);
            Wobble(inner, radius * eased * .82f, .1f * fade);
        }

        void Wobble(LineRenderer line, float r, float width)
        {
            line.startWidth = line.endWidth = width;
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                float jitter = 1 + Random.Range(-.03f, .03f);
                line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * r * jitter, .08f + Random.Range(0, .15f), Mathf.Sin(a) * r * jitter));
            }
        }
    }

    /// <summary>
    /// Ultrashock Laser's pulse: flies from the left palm and stops at the first enemy (slow) or the first wall, where it
    /// marks the laser that fires through. Floors and ceilings just fizzle it. It vanishes if Zeri dies.
    /// </summary>
    public class ZeriPulse : MonoBehaviour
    {
        ZeriKit kit;
        PlayerChampion owner;
        Vector3 origin, direction;
        float travelled;
        const float Radius = .45f;

        public static ZeriPulse Launch(ZeriKit kit, PlayerChampion owner, Vector3 origin, Vector3 direction)
        {
            var go = new GameObject("Zeri Ultrashock pulse");
            go.transform.position = origin;
            ZeriFx.Sphere("Pulse core", new Color(1f, .97f, .7f, .95f), .12f, go.transform);
            ZeriFx.Sphere("Pulse halo", new Color(1f, .9f, .4f, .35f), .26f, go.transform);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ZeriFx.Glow(new Color(1f, .9f, .4f, .6f));
            trail.time = .35f;
            trail.startWidth = .09f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AbilityFx.Motes(go.transform, ZeriKit.Volt, .03f, .2f, 60, ParticleSystemShapeType.Sphere, .08f);
            var pulse = go.AddComponent<ZeriPulse>();
            pulse.kit = kit;
            pulse.owner = owner;
            pulse.origin = origin;
            pulse.direction = direction.normalized;
            owner.Track(go);
            return pulse;
        }

        void Update()
        {
            if (!owner || owner.Kit != kit || !owner.Health || !owner.Health.IsAlive)
            {
                Destroy(gameObject);
                return;
            }
            float step = Mathf.Min(ZeriKit.PulseSpeed * Time.deltaTime, ZeriKit.PulseRange - travelled);
            Vector3 from = transform.position;
            Vector3 to = from + direction * step;
            float wallAt = step + 1;
            if (owner.TerrainRay(from, direction, step, out var wall))
                wallAt = wall.distance;
            // The nearest enemy along this step, if it comes before the wall.
            Combatant best = null;
            float bestAlong = Mathf.Min(step, wallAt);
            foreach (var target in owner.EnemiesAround((from + to) * .5f, step * .5f + Radius + .6f))
            {
                Vector3 rel = target.AimPosition - from;
                float along = Vector3.Dot(rel, direction);
                if (along < -Radius || along > bestAlong)
                    continue;
                if ((rel - direction * along).magnitude <= Radius + .45f)
                {
                    best = target;
                    bestAlong = along;
                }
            }
            if (best)
            {
                kit.PulseHit(best, best.AimPosition, direction);
                Destroy(gameObject);
                return;
            }
            if (wallAt <= step)
            {
                transform.position = wall.point;
                // Only walls turn into the laser; floors and ceilings just fizzle the pulse.
                if (Mathf.Abs(wall.normal.y) < .6f)
                    kit.PulseWall(origin, wall.point, wall.normal, direction);
                else
                    ZeriFlash.Create(owner, wall.point, new Color(1f, .9f, .4f, .6f), .25f, .15f);
                Destroy(gameObject);
                return;
            }
            transform.position = to;
            travelled += step;
            if (travelled >= ZeriKit.PulseRange - .01f)
                Destroy(gameObject);
        }
    }
}
