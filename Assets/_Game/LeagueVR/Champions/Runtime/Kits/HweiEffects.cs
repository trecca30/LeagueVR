using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Hwei's look, from his own particle textures (Resources/HweiFx): the subject's paint soaking his brush's bristles
    /// (red-orange Disaster, cyan Serenity, magenta Torment; the brush is untouched without one), the stroke each move
    /// paints in the air, the aim previews while a button is held, and every spell's effect on the Rift.
    /// </summary>
    public class HweiVisuals
    {
        readonly PlayerChampion player;
        readonly HweiKit kit;
        readonly List<GameObject> owned = new();

        SkinnedMeshRenderer paint;
        Material paintMaterial;
        Mesh paintMesh;
        Transform tip;
        TrailRenderer stroke;
        ParticleSystem drips;
        ZeriSprite tipGlow;
        readonly Transform[] lights = new Transform[3];
        float paintAlpha, lastPaintFrame = -1;
        Color paintColor = HweiKit.Gold, strokeColor = HweiKit.Gold;
        HweiMoves.Move lastMove;
        int lightsShown;

        // Aim previews: a centre line, two edges, a landing ring and a second ring, drawn through walls.
        LineRenderer centre, edgeLeft, edgeRight, ring, outer;

        static readonly Color Ink = new(.82f, .6f, 1f, 1f), Fire = new(1f, .5f, .16f, 1f), Ember = new(1f, .78f, .38f, 1f);
        static readonly Color Water = new(.42f, .9f, 1f, 1f), Foam = new(.78f, 1f, 1f, 1f), Dread = new(.9f, .3f, .95f, 1f);

        public static string Fx(string name) => "HweiFx/Hwei_Base_" + name;

        static readonly Dictionary<Color, Material> strokes = new();

        /// <summary>
        /// His paint stroke in a colour (cached): his stroke texture blended over the world rather than added to it, so
        /// the paint keeps its colour against a bright sky the way light would not.
        /// </summary>
        public static Material StrokeMaterial(Color color)
        {
            if (strokes.TryGetValue(color, out var material) && material)
                return material;
            material = new Material(AbilityFx.Glass(color, false, true)) { name = "Hwei stroke " + ColorUtility.ToHtmlStringRGBA(color), hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", ZeriFx.Tex(Fx("PaintStrokeDiffuse")));
            strokes[color] = material;
            return material;
        }

        public HweiVisuals(PlayerChampion player, HweiKit kit)
        {
            this.player = player;
            this.kit = kit;
            BuildPaint();
            tip = Keep(new GameObject("Hwei brush tip")).transform;
            stroke = new GameObject("Hwei stroke").AddComponent<TrailRenderer>();
            stroke.transform.SetParent(tip, false);
            stroke.time = .42f;
            stroke.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(.25f, .8f), new Keyframe(1, 0));
            stroke.widthMultiplier = .085f;
            stroke.minVertexDistance = .012f;
            stroke.textureMode = LineTextureMode.Stretch;
            stroke.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stroke.emitting = false;
            drips = AbilityFx.Motes(tip, Ember, .026f, .55f, 0, ParticleSystemShapeType.Sphere, .06f);
            var dripMain = drips.main;
            dripMain.gravityModifier = .3f;
            // The drops keep the paint's colour (the shared motes fade toward white).
            var dripFade = drips.colorOverLifetime;
            dripFade.color = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(0, 1) },
            };
            tipGlow = ZeriSprite.Create(player, tip.position, Fx("QE_AlphaGlowmask"), Ember, .26f, .26f, 0, parent: tip);
            tipGlow.gameObject.SetActive(false);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = new GameObject("Stirring light").transform;
                light.SetParent(tip, false);
                ZeriSprite.Create(player, light.position, Fx("Glow_Ring"), new Color(.45f, .92f, 1f, .75f), .11f, .11f, 0, parent: light);
                ZeriSprite.Create(player, light.position, Fx("DotSmol"), new Color(1f, .92f, .62f, 1f), .07f, .07f, 0, parent: light);
                light.gameObject.SetActive(false);
                lights[i] = light;
            }
            centre = PreviewLine("Hwei aim centre", .045f);
            edgeLeft = PreviewLine("Hwei aim edge", .03f);
            edgeRight = PreviewLine("Hwei aim edge", .03f);
            ring = PreviewLine("Hwei aim ring", .05f, 48, true);
            outer = PreviewLine("Hwei aim outer ring", .025f, 64, true);
            HideAll();
        }

        GameObject Keep(GameObject go)
        {
            if (owned.Count > 96)
                owned.RemoveAll(o => !o);
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
            if (paint)
                Object.Destroy(paint.gameObject);
            if (paintMesh)
                Object.Destroy(paintMesh);
            if (paintMaterial)
                Object.Destroy(paintMaterial);
        }

        public void HideAll()
        {
            paintAlpha = 0;
            if (paint)
                paint.enabled = false;
            stroke.emitting = false;
            stroke.Clear();
            tipGlow.gameObject.SetActive(false);
            foreach (var light in lights)
                light.gameObject.SetActive(false);
            lightsShown = 0;
            Previews(-1, -1, false);
        }

        /// <summary>Flashes shrink when they go off near the eyes, so nothing white fills the view.</summary>
        float NearEyes(Vector3 at) => player.head ? Mathf.Clamp(Vector3.Distance(at, player.head.transform.position) / 4f, .3f, 1f) : 1;

        ZeriSprite Sprite(Vector3 at, string texture, Color color, float from, float to, float seconds, Vector3? axis = null, bool flat = false, Transform parent = null)
            => ZeriSprite.Create(player, at, Fx(texture), color, from, to, seconds, axis, flat, parent);

        /// <summary>A one-off spray of glowing specks (embers, droplets, sparks).</summary>
        void Burst(Vector3 at, Color color, int count, float speed, float size, float life, float gravity = 0)
        {
            var go = Keep(new GameObject("Hwei burst"));
            go.transform.position = at;
            var ps = AbilityFx.Motes(go.transform, color, size, life, 0, ParticleSystemShapeType.Sphere, .12f);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .5f, speed);
            main.gravityModifier = gravity;
            ps.Emit(count);
            Object.Destroy(go, life + .3f);
        }

        // ---------- The brush ----------

        /// <summary>
        /// The paint on the bristles: a copy of the brush's bristle triangles, skinned to the same bones and lifted just
        /// off them, drawn over them with the brush's own texture dyed in the subject's colour (the bristles' strands
        /// still show through), with a soft glow of the same colour at the tip.
        /// </summary>
        void BuildPaint()
        {
            var avatar = kit.Avatar;
            if (!avatar || !avatar.Skin || avatar.Weapon == null)
                return;
            var skin = avatar.Skin;
            var source = skin.sharedMesh;
            int weapon = System.Array.IndexOf(skin.bones, avatar.Weapon.bone);
            int brush = System.Array.FindIndex(skin.sharedMaterials, m => m && m.name.EndsWith("Brush", System.StringComparison.Ordinal));
            if (!source || weapon < 0 || brush < 0)
                return;
            var bind = source.bindposes[weapon];
            var vertices = source.vertices;
            var normals = source.normals;
            var triangles = source.GetTriangles(brush);
            var kept = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
                if (bind.MultiplyPoint3x4(vertices[triangles[t]]).z < -.52f && bind.MultiplyPoint3x4(vertices[triangles[t + 1]]).z < -.52f && bind.MultiplyPoint3x4(vertices[triangles[t + 2]]).z < -.52f)
                    kept.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
            if (kept.Count == 0)
                return;
            paintMesh = Object.Instantiate(source);
            paintMesh.name = "Hwei brush paint";
            for (int s = 0; s < paintMesh.subMeshCount; s++)
                paintMesh.SetTriangles(s == brush ? kept : new List<int>(), s);
            for (int v = 0; v < vertices.Length && v < normals.Length; v++)
                vertices[v] += normals[v] * .007f;
            paintMesh.vertices = vertices;
            paintMesh.RecalculateBounds();
            var go = new GameObject("Hwei brush paint");
            go.transform.SetParent(skin.transform.parent, false);
            go.transform.SetLocalPositionAndRotation(skin.transform.localPosition, skin.transform.localRotation);
            go.transform.localScale = skin.transform.localScale;
            paint = go.AddComponent<SkinnedMeshRenderer>();
            paint.sharedMesh = paintMesh;
            paint.bones = skin.bones;
            paint.rootBone = skin.rootBone;
            paint.updateWhenOffscreen = true;
            paint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            paint.receiveShadows = false;
            paintMaterial = new Material(AbilityFx.Glass(Color.white, false, true)) { name = "Hwei brush paint", hideFlags = HideFlags.HideAndDontSave };
            var brushMaterial = skin.sharedMaterials[brush];
            if (brushMaterial && brushMaterial.HasProperty("_BaseMap"))
                paintMaterial.SetTexture("_BaseMap", brushMaterial.GetTexture("_BaseMap"));
            var materials = new Material[paintMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = paintMaterial;
            paint.sharedMaterials = materials;
            paint.enabled = false;
        }

        /// <summary>The bristle tip as posed now (spells leave the brush here); the palm without a brush.</summary>
        public Vector3 Tip()
        {
            var avatar = kit.Avatar;
            return avatar && avatar.Weapon != null && avatar.Weapon.Visible ? avatar.PropPoint(avatar.Weapon, HweiMoves.TipPoint) : player.AttackOrigin;
        }

        /// <summary>
        /// After the body is posed (twice a frame): the paint follows the subject on the brush, the stroke trails from the
        /// bristles while a move paints, and Stirring Lights' lights circle the tip.
        /// </summary>
        public void Pose()
        {
            var avatar = kit.Avatar;
            bool held = avatar && avatar.Weapon != null && avatar.Weapon.Visible && player.Health.IsAlive;
            if (held)
                tip.position = avatar.PropPoint(avatar.Weapon, HweiMoves.PaintPoint);
            var moves = kit.Moves;
            bool stroking = held && moves != null && moves.Stroking;
            if (moves != null && moves.Current != lastMove)
            {
                lastMove = moves.Current;
                // A new move paints in its own colour: the subject's for spells, his violet ink for attacks and flourishes.
                strokeColor = lastMove >= HweiMoves.Move.QQ && lastMove <= HweiMoves.Move.EE ? HweiKit.SubjectColor(((int)lastMove - (int)HweiMoves.Move.QQ) / 3)
                    : lastMove == HweiMoves.Move.Inspire && kit.Painted >= 0 ? HweiKit.SubjectColor(kit.Painted) : Ink;
                stroke.sharedMaterial = StrokeMaterial(new Color(strokeColor.r, strokeColor.g, strokeColor.b, .9f));
                stroke.widthMultiplier = lastMove is HweiMoves.Move.Flick or HweiMoves.Move.Backflick ? .05f : .085f;
            }
            stroke.emitting = stroking;
            if (Time.frameCount == lastPaintFrame)
                return;
            lastPaintFrame = Time.frameCount;
            // The paint fades in as the subject opens and out as the spell leaves the bristles (or the brush is washed).
            int painted = kit.Painted;
            if (painted >= 0)
                paintColor = HweiKit.SubjectColor(painted);
            paintAlpha = Mathf.MoveTowards(paintAlpha, painted >= 0 && held ? 1 : 0, Time.deltaTime * (painted >= 0 ? 4 : 2.5f));
            if (paint)
            {
                paint.enabled = paintAlpha > 0;
                paint.forceRenderingOff = avatar && avatar.Skin && avatar.Skin.forceRenderingOff;
                // Dyed rather than lit: the beige bristles times a bright subject colour come out deep red-orange, teal
                // or magenta, and the strands of the texture stay visible.
                var c = new Color(Mathf.Min(1, paintColor.r * 1.25f), Mathf.Min(1, paintColor.g * 1.25f), Mathf.Min(1, paintColor.b * 1.25f), paintAlpha * (.86f + .06f * Mathf.Sin(Time.time * 5)));
                paintMaterial.SetColor("_BaseColor", c);
            }
            tipGlow.gameObject.SetActive(paintAlpha > 0);
            if (paintAlpha > 0)
            {
                var g = Color.Lerp(paintColor, Color.white, .15f);
                g.a = .7f * paintAlpha;
                tipGlow.Tint(g);
                tipGlow.size = 1 + .12f * Mathf.Sin(Time.time * 7);
            }
            var emission = drips.emission;
            emission.rateOverTime = paintAlpha > .5f ? 16 : 0;
            var main = drips.main;
            main.startColor = paintColor;
            PoseLights(held, moves);
        }

        /// <summary>
        /// Stirring Lights: during the loops each light appears at the tip as its loop closes; while the empowerment lasts
        /// the lights left circle the tip.
        /// </summary>
        void PoseLights(bool held, HweiMoves moves)
        {
            int shown = kit.Lights;
            if (moves != null && moves.Current == HweiMoves.Move.WE)
            {
                shown = 0;
                foreach (float at in HweiMoves.LightsAt)
                    if (moves.Progress >= at)
                        shown++;
            }
            if (!held)
                shown = 0;
            if (shown > lightsShown)
                ZeriFlash.Create(player, tip.position, new Color(.6f, .95f, 1f, .5f), .07f, .18f);
            lightsShown = shown;
            for (int i = 0; i < lights.Length; i++)
            {
                bool on = i < shown;
                lights[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                float a = Time.time * 3.2f + i * Mathf.PI * 2 / 3;
                var view = player.head.transform;
                Vector3 across = Vector3.Cross(view.forward, Vector3.up).normalized;
                lights[i].position = tip.position + (across * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * .13f + view.forward * .02f * Mathf.Sin(a * 2);
            }
        }

        /// <summary>A subject opens: a flash of its colour at the bristles as the paint soaks in.</summary>
        public void Inspire(int subject)
        {
            var c = HweiKit.SubjectColor(subject);
            ZeriFlash.Create(player, Tip(), new Color(c.r, c.g, c.b, .55f), .12f, .25f);
            Burst(Tip(), c, 10, 1.2f, .03f, .45f, .2f);
        }

        /// <summary>Wash Brush: the paint is shaken off the bristles in a spray of droplets.</summary>
        public void WashBrush(int subject)
        {
            var c = subject >= 0 ? HweiKit.SubjectColor(subject) : HweiKit.Wash;
            paintAlpha = Mathf.Min(paintAlpha, .6f);
            Burst(Tip(), c, 16, 2.4f, .035f, .6f, 1.2f);
            Sprite(Tip(), "PaintStrokeDiffuse03", new Color(c.r, c.g, c.b, .7f), .15f, .4f, .3f);
        }

        /// <summary>A spell's stroke begins: it paints in the subject's colour.</summary>
        public void Cast(int subject, int variant) => strokeColor = HweiKit.SubjectColor(subject);

        // ---------- Aim previews ----------

        LineRenderer PreviewLine(string name, float width, int points = 2, bool loop = false)
        {
            var line = AbilityFx.Line(name, Color.white, width, points);
            Keep(line.gameObject);
            line.loop = loop;
            line.sharedMaterial = ZeriFx.XRay(Color.white);
            line.enabled = false;
            return line;
        }

        static void Tint(LineRenderer line, Color color, float alpha)
        {
            color.a = alpha;
            line.startColor = line.endColor = color;
        }

        /// <summary>A line along the floor from <paramref name="from"/> for <paramref name="length"/> metres, offset sideways.</summary>
        void GroundLine(LineRenderer line, Vector3 from, Vector3 direction, float length, float side, float start = .5f)
        {
            Vector3 across = Vector3.Cross(Vector3.up, direction).normalized * side;
            int n = Mathf.Clamp(Mathf.CeilToInt((length - start) / .75f) + 1, 2, 24);
            line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = from + direction * Mathf.Lerp(start, length, i / (n - 1f)) + across;
                line.SetPosition(i, HweiKit.Ground(p) + Vector3.up * .08f);
            }
            line.enabled = true;
        }

        void GroundRing(LineRenderer line, Vector3 at, float radius)
        {
            int n = line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                Vector3 p = at + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                line.SetPosition(i, new Vector3(p.x, HweiKit.Ground(p).y + .08f, p.z));
            }
            line.enabled = true;
        }

        /// <summary>
        /// Shows where the aimed spell will go while its button is held (and the ultimate while R is held): lines for the
        /// skillshots and the fissure and current, circles for the spells that land on a spot.
        /// </summary>
        public void Previews(int mood, int aiming, bool ultimate)
        {
            centre.enabled = edgeLeft.enabled = edgeRight.enabled = ring.enabled = outer.enabled = false;
            if (!player.Health.IsAlive)
                return;
            Vector3 feet = player.Feet;
            float pulse = .8f + .2f * Mathf.Sin(Time.time * 8);
            if (ultimate)
            {
                Vector3 target = kit.GazePoint(HweiKit.RRange);
                Vector3 dir = player.PlanarDirection(target - feet);
                float length = Mathf.Max(1, Geo.FlatDistance(feet, target));
                Shot(feet, dir, length, HweiKit.RWidth, HweiKit.Despair, pulse);
                Tint(ring, HweiKit.Gold, .9f * pulse);
                GroundRing(ring, target, HweiKit.RRadius * .4f);
                Tint(outer, HweiKit.Despair, .35f);
                GroundRing(outer, target, HweiKit.RRadius);
                return;
            }
            if (mood < 0 || aiming < 0)
                return;
            var c = HweiKit.SubjectColor(mood);
            Vector3 flat = kit.AimDirection();
            switch (mood * 3 + aiming)
            {
                case 0: // Devastating Fire: the path and the burst at its end.
                    Shot(feet, flat, HweiKit.QQRange, HweiKit.QQWidth, c, pulse);
                    Tint(ring, c, .55f);
                    GroundRing(ring, feet + flat * HweiKit.QQRange, HweiKit.QQRadius);
                    break;
                case 1: // Severing Bolt: the strike's circle far away.
                    Spot(kit.AimPoint(HweiKit.QWRange), HweiKit.QWRadius, c, pulse, true);
                    break;
                case 2: // Molten Fissure: the band the lava covers.
                    Band(feet, flat, HweiKit.QERange, HweiKit.QEHalfWidth, c, pulse);
                    break;
                case 3: // Fleeting Current: the stream's band.
                    Band(feet, flat, HweiKit.WQRange, HweiKit.WQHalfWidth, c, pulse);
                    break;
                case 4: // Pool of Reflection.
                    Spot(kit.AimPoint(HweiKit.WWRange), HweiKit.WWRadius, c, pulse, false);
                    break;
                case 5: // Stirring Lights empowers Hwei himself.
                    Tint(ring, c, .7f * pulse);
                    GroundRing(ring, feet, .7f);
                    break;
                case 6: // Grim Visage: the face's path.
                    Shot(feet, flat, HweiKit.EQRange, HweiKit.EQWidth, c, pulse);
                    break;
                case 7: // Gaze of the Abyss: the eye, and how far it looks.
                {
                    Vector3 at = kit.AimPoint(HweiKit.EWRange);
                    Spot(at, .55f, c, pulse, true);
                    Tint(outer, c, .3f);
                    GroundRing(outer, at, HweiKit.EWSight);
                    break;
                }
                case 8: // Crushing Maw.
                    Spot(kit.AimPoint(HweiKit.EERange), HweiKit.EERadius, c, pulse, true);
                    break;
            }
        }

        void Shot(Vector3 feet, Vector3 dir, float length, float halfWidth, Color c, float pulse)
        {
            Tint(centre, c, .85f * pulse);
            GroundLine(centre, feet, dir, length, 0);
            Tint(edgeLeft, c, .45f);
            Tint(edgeRight, c, .45f);
            GroundLine(edgeLeft, feet, dir, length, -halfWidth);
            GroundLine(edgeRight, feet, dir, length, halfWidth);
        }

        void Band(Vector3 feet, Vector3 dir, float length, float halfWidth, Color c, float pulse)
        {
            Tint(edgeLeft, c, .75f * pulse);
            Tint(edgeRight, c, .75f * pulse);
            GroundLine(edgeLeft, feet, dir, length, -halfWidth, .3f);
            GroundLine(edgeRight, feet, dir, length, halfWidth, .3f);
            Tint(centre, c, .3f);
            GroundLine(centre, feet, dir, length, 0, .3f);
        }

        void Spot(Vector3 at, float radius, Color c, float pulse, bool line)
        {
            Tint(ring, c, .9f * pulse);
            GroundRing(ring, at, radius);
            if (!line)
                return;
            Vector3 feet = player.Feet;
            Tint(centre, c, .3f);
            float length = Geo.FlatDistance(feet, at);
            if (length > 1.2f)
                GroundLine(centre, feet, player.PlanarDirection(at - feet), length - radius * .5f, 0);
        }

        // ---------- Attacks ----------

        /// <summary>His attack is a glob of violet paint (cyan and gold while Stirring Lights empowers it).</summary>
        public void DressAttack(GameObject missile, bool light)
        {
            var core = missile.transform.Find("Projectile core");
            if (core)
                Object.Destroy(core.gameObject);
            if (missile.TryGetComponent<TrailRenderer>(out var old))
                old.enabled = false;
            var c = light ? Water : Ink;
            Sprite(missile.transform.position, "Glow_Ring", new Color(c.r, c.g, c.b, .8f), .26f, .26f, 0, parent: missile.transform);
            Sprite(missile.transform.position, "DotSmol", light ? new Color(1f, .92f, .6f, 1f) : new Color(1f, .85f, 1f, 1f), .12f, .12f, 0, parent: missile.transform);
            var trail = new GameObject("Hwei attack trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(missile.transform, false);
            trail.sharedMaterial = ZeriFx.TexGlow(Fx("PaintStrokeDiffuse"), new Color(c.r, c.g, c.b, .7f));
            trail.time = .14f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            trail.widthMultiplier = .12f;
            trail.minVertexDistance = .03f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void AttackHit(Vector3 at)
        {
            var splat = Sprite(at, "PaintStrokeDiffuse03", new Color(Ink.r, Ink.g, Ink.b, .85f), .25f, .6f, .25f);
            splat.Roll = Random.Range(0, 360f);
        }

        // ---------- Signature of the Visionary ----------

        /// <summary>His mark floats over a marked enemy: a gold brush ring.</summary>
        public GameObject Mark(Combatant target)
        {
            var go = Keep(new GameObject("Hwei signature mark"));
            go.transform.SetParent(target.transform, false);
            go.transform.position = target.AimPosition + Vector3.up * 1.05f;
            Sprite(go.transform.position, "P_Icon", new Color(1f, .82f, .4f, .9f), .2f, .62f, 0, parent: go.transform);
            Sprite(go.transform.position, "P_Indicator01", new Color(1f, .75f, .35f, .55f), .5f, .5f, 0, parent: go.transform);
            return go;
        }

        /// <summary>The completed signature: his glyph under the enemy, filling in for 0.85 s.</summary>
        public GameObject SignatureWarning(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei signature"));
            go.transform.position = at;
            Sprite(at + Vector3.up * .06f, "P_Indicator01", new Color(1f, .8f, .4f, .85f), HweiKit.MarkRadius * 2.1f, HweiKit.MarkRadius * 2.1f, 0, flat: true, parent: go.transform);
            var fill = Sprite(at + Vector3.up * .05f, "Glow_Ring", new Color(1f, .72f, .3f, .35f), .1f, .1f, 0, flat: true, parent: go.transform);
            fill.name = "Signature fill";
            return go;
        }

        public void SignatureFilling(GameObject view, float t)
        {
            if (!view)
                return;
            var fill = view.transform.Find("Signature fill");
            if (fill && fill.TryGetComponent<ZeriSprite>(out var sprite))
                sprite.size = Mathf.Max(.1f, HweiKit.MarkRadius * 2 * Mathf.Clamp01(t)) / .1f;
        }

        public void SignatureBurst(Vector3 at)
        {
            float near = NearEyes(at + Vector3.up);
            Sprite(at + Vector3.up * .8f, "Glow_Ring", new Color(1f, .82f, .45f, .9f), .6f * near, 3f * near, .3f);
            Sprite(at + Vector3.up * .07f, "P_Icon", new Color(1f, .78f, .35f, 1f), HweiKit.MarkRadius, HweiKit.MarkRadius * 2.6f, .45f, flat: true);
            ZeriRing.Create(player, at + Vector3.up * .1f, Vector3.up, HweiKit.MarkRadius, new Color(1f, .8f, .4f, .8f), .08f, .3f);
            Burst(at + Vector3.up * .5f, HweiKit.Gold, 14, 3f, .04f, .5f, .4f);
        }

        // ---------- Stirring Lights ----------

        /// <summary>A light leaves the brush for the enemy the attack or spell hit.</summary>
        public void LightStrike(Vector3 at, int left)
        {
            ZeriBeam.Create(player, Tip(), at, new Color(.55f, .95f, 1f, .8f), .06f, .02f, .16f, 0);
            Sprite(at, "DotSmol", new Color(1f, .92f, .6f, 1f), .25f, .7f, .25f);
            Sprite(at, "Glow_Ring", new Color(.45f, .92f, 1f, .7f), .3f, 1f, .3f);
        }

        // ---------- Disaster ----------

        public GameObject Fireball(Vector3 at, Vector3 direction)
        {
            var go = Keep(new GameObject("Hwei Devastating Fire"));
            go.transform.position = at;
            // League's flame head: the bright head leads and its tail streams back along the flight.
            Sprite(at, "Missilehead01", new Color(1f, .55f, .2f, 1f), .95f, .95f, 0, -direction, parent: go.transform);
            Sprite(at, "QE_AlphaGlowmask", new Color(1f, .82f, .45f, .9f), .45f, .45f, 0, parent: go.transform);
            go.AddComponent<HweiGrow>();
            AbilityFx.Motes(go.transform, Fire, .05f, .35f, 70, ParticleSystemShapeType.Sphere, .15f);
            var trail = new GameObject("Fire trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(go.transform, false);
            trail.sharedMaterial = ZeriFx.Glow(new Color(1f, .45f, .15f, .55f));
            trail.time = .22f;
            trail.startWidth = .32f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public void FireBurst(Vector3 at)
        {
            float near = NearEyes(at);
            Vector3 ground = HweiKit.Ground(at);
            Sprite(at, "Glow_Ring", new Color(1f, .72f, .35f, .9f), .6f * near, 2.6f * near, .3f);
            Sprite(ground + Vector3.up * .6f, "QE_FireLick0", Fire, 1f * near, 2.2f * near, .4f, Vector3.up);
            Sprite(ground + Vector3.up * .06f, "GroundCrack02Base", new Color(1f, .4f, .12f, .9f), 1f, HweiKit.QQRadius * 2.1f, .55f, flat: true);
            ZeriRing.Create(player, ground + Vector3.up * .1f, Vector3.up, HweiKit.QQRadius, new Color(1f, .55f, .2f, .8f), .06f, .3f);
            Burst(at, Ember, 18, 3.2f, .05f, .6f, .6f);
        }

        public GameObject BoltWarning(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei Severing Bolt"));
            go.transform.position = at;
            Sprite(at + Vector3.up * .06f, "GroundCrack02Base", new Color(1f, .45f, .15f, .7f), HweiKit.QWRadius * 2.15f, HweiKit.QWRadius * 2.15f, 0, flat: true, parent: go.transform);
            var fill = Sprite(at + Vector3.up * .05f, "Glow_Ring", new Color(1f, .5f, .2f, .4f), .1f, .1f, 0, flat: true, parent: go.transform);
            fill.name = "Bolt fill";
            var spark = Sprite(at + Vector3.up * 3.5f, "QE_AlphaGlowmask", new Color(1f, .78f, .45f, .7f), .3f, .3f, 0, parent: go.transform);
            spark.name = "Bolt gathering";
            return go;
        }

        /// <summary>The bolt's circle fills over its 1 s delay while light gathers above it.</summary>
        public void BoltCharging(GameObject view, float t)
        {
            if (!view)
                return;
            t = Mathf.Clamp01(t);
            var fill = view.transform.Find("Bolt fill");
            if (fill && fill.TryGetComponent<ZeriSprite>(out var sprite))
                sprite.size = Mathf.Max(.1f, HweiKit.QWRadius * 2 * t) / .1f;
            var spark = view.transform.Find("Bolt gathering");
            if (spark && spark.TryGetComponent<ZeriSprite>(out var gathering))
                gathering.size = 1 + t * 3 + Mathf.Sin(Time.time * 30) * .4f * t;
        }

        public void BoltStrike(Vector3 at)
        {
            float near = NearEyes(at + Vector3.up);
            Vector3 sky = at + Vector3.up * 10;
            ZeriBeam.Create(player, sky, at, new Color(1f, .8f, .45f, 1f), 1.6f, 1.2f, .28f, 0, texture: Fx("Innerbolt_Line"));
            for (int i = 0; i < 3; i++)
                ZeriArc.Create(player, sky + Random.insideUnitSphere * 1.2f, at + Random.insideUnitSphere * .3f, i == 0 ? new Color(1f, .85f, .5f, 1f) : new Color(1f, .45f, .2f, .9f), .22f, .3f);
            Sprite(at + Vector3.up * .5f, "Glow_Ring", new Color(1f, .8f, .5f, .9f), .8f * near, 3.6f * near, .3f);
            Sprite(at + Vector3.up * .07f, "GroundCrack01Base", new Color(1f, .45f, .15f, 1f), 1f, HweiKit.QWRadius * 2.4f, .7f, flat: true).Roll = Random.Range(0, 360f);
            Sprite(at + Vector3.up * .06f, "Glow_Ring_Edge", new Color(1f, .7f, .35f, .9f), .5f, 6f, .35f, flat: true);
            Burst(at + Vector3.up * .3f, Ember, 20, 4f, .05f, .55f, .8f);
        }

        /// <summary>The fissure's path: a thin molten crack along the ground that fades as the eruptions run down it.</summary>
        public GameObject FissurePath(Vector3 start, Vector3 direction, float length)
        {
            var go = Keep(new GameObject("Hwei Molten Fissure"));
            var line = AbilityFx.Line("Fissure path", new Color(1f, .4f, .12f, .45f), .09f, 14);
            line.transform.SetParent(go.transform, true);
            for (int i = 0; i < line.positionCount; i++)
                line.SetPosition(i, HweiKit.Ground(start + direction * Mathf.Lerp(.6f, length, i / (line.positionCount - 1f))) + Vector3.up * .04f);
            line.gameObject.AddComponent<HweiFade>().Begin(.09f, HweiKit.QEDelay + HweiKit.QEStep * HweiKit.QEExplosions);
            return go;
        }

        /// <summary>An eruption: a column of flame, then its lava glows and cracks the ground for 2.5 s.</summary>
        public void Erupt(Vector3 at)
        {
            float near = NearEyes(at + Vector3.up);
            Sprite(at + Vector3.up * .95f, "QE_FireLick" + Random.Range(0, 4), Fire, .9f * near, 2.4f * near, .45f, Vector3.up);
            // The lava's glow dims near the eyes: the first eruption is close enough to fill the view.
            Sprite(at + Vector3.up * .05f, "QE_AlphaGlowmask", new Color(1f, .42f, .1f, .45f * near), HweiKit.QEHalfWidth * 1.8f, HweiKit.QEHalfWidth * 2f, HweiKit.QELava, flat: true);
            Sprite(at + Vector3.up * .06f, "GroundCrack01Base", new Color(1f, .35f, .08f, .9f), HweiKit.QEHalfWidth * 2, HweiKit.QEHalfWidth * 2.1f, HweiKit.QELava, flat: true).Roll = Random.Range(0, 360f);
            Sprite(at + Vector3.up * .5f, "Glow_Ring", new Color(1f, .65f, .3f, .7f), .4f * near, 1.8f * near, .25f);
            Burst(at + Vector3.up * .3f, Ember, 10, 2.6f, .045f, .55f, .3f);
        }

        // ---------- Serenity ----------

        /// <summary>
        /// Fleeting Current: a band of water along the ground (soft-edged, one piece end to end) with his wave strokes
        /// flowing down it.
        /// </summary>
        public GameObject Current(Vector3 start, Vector3 direction, float length, float seconds)
        {
            var go = Keep(new GameObject("Hwei Fleeting Current"));
            go.transform.position = start;
            var water = AbilityFx.Line("Current water", Water, HweiKit.WQHalfWidth * 2, 14);
            water.transform.SetParent(go.transform, true);
            var material = new Material(ZeriFx.TexGlow(Fx("WQ_Block"), new Color(.3f, .8f, 1f, .3f))) { hideFlags = HideFlags.HideAndDontSave };
            water.sharedMaterial = material;
            water.textureMode = LineTextureMode.Stretch;
            water.alignment = LineAlignment.TransformZ;
            water.transform.rotation = Quaternion.LookRotation(Vector3.down, direction);
            var waves = AbilityFx.Line("Current waves", Foam, HweiKit.WQHalfWidth * 1.7f, 14);
            waves.transform.SetParent(go.transform, true);
            waves.sharedMaterial = new Material(ZeriFx.TexGlow(Fx("PaintStrokeDiffuse"), new Color(.65f, .97f, 1f, .4f))) { hideFlags = HideFlags.HideAndDontSave };
            waves.textureMode = LineTextureMode.Tile;
            // One wave every 2.4 m, flowing along the current.
            waves.textureScale = new Vector2(1 / 2.4f, 1);
            waves.alignment = LineAlignment.TransformZ;
            waves.transform.rotation = water.transform.rotation;
            for (int i = 0; i < water.positionCount; i++)
            {
                Vector3 p = HweiKit.Ground(start + direction * Mathf.Lerp(.9f, length, i / (water.positionCount - 1f))) + Vector3.up * .07f;
                water.SetPosition(i, p);
                waves.SetPosition(i, p + Vector3.up * .01f);
            }
            var motes = AbilityFx.Motes(go.transform, Foam, .04f, 1.2f, 40, ParticleSystemShapeType.Box, 1);
            var shape = motes.shape;
            shape.position = Vector3.zero;
            motes.transform.SetPositionAndRotation(HweiKit.Ground(start + direction * length * .5f) + Vector3.up * .25f, Quaternion.LookRotation(direction));
            shape.scale = new Vector3(HweiKit.WQHalfWidth * 2, .2f, length);
            var velocity = motes.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(direction.x * 3.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(direction.z * 3.5f);
            go.AddComponent<HweiFlow>().Begin(new[] { material, waves.sharedMaterial }, new[] { false, true }, seconds, 1.4f);
            return go;
        }

        /// <summary>Pool of Reflection: a still disc of water with a bright rim and rising droplets.</summary>
        public GameObject Pool(Vector3 at, float radius, float seconds)
        {
            var go = Keep(new GameObject("Hwei Pool of Reflection"));
            go.transform.position = at;
            Sprite(at + Vector3.up * .05f, "Glow_Ring", new Color(.3f, .78f, 1f, .2f), radius * 1.6f, radius * 2.1f, seconds, flat: true, parent: go.transform);
            Sprite(at + Vector3.up * .06f, "Glow_Ring_Edge", new Color(.7f, 1f, 1f, .9f), radius * 1.8f, radius * 2.12f, seconds, flat: true, parent: go.transform);
            Sprite(at + Vector3.up * .055f, "Projection_Ground01", new Color(.45f, .9f, 1f, .14f), radius * 1.9f, radius * 1.9f, seconds, flat: true, parent: go.transform).Roll = Random.Range(0, 360f);
            var motes = AbilityFx.Motes(go.transform, Foam, .045f, 1.1f, 45, ParticleSystemShapeType.Circle, radius * .95f);
            motes.transform.rotation = Quaternion.Euler(-90, 0, 0);
            motes.transform.position = at + Vector3.up * .1f;
            ZeriRing.Create(player, at + Vector3.up * .1f, Vector3.up, radius, new Color(.6f, 1f, 1f, .9f), .1f, .35f);
            return go;
        }

        /// <summary>The pool's shield takes hold: a ring of water around his feet.</summary>
        public void ShieldUp()
        {
            Vector3 feet = player.Feet;
            ZeriRing.Create(player, feet + Vector3.up * .3f, Vector3.up, .55f, new Color(.6f, 1f, 1f, .8f), .05f, .5f);
            Burst(feet + Vector3.up * .9f, Water, 12, 1f, .04f, .7f, -.2f);
        }

        // ---------- Torment ----------

        /// <summary>Grim Visage: the painted grin flies, wrapped in a dark aura with a magenta trail.</summary>
        public GameObject Face(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei Grim Visage"));
            go.transform.position = at;
            Sprite(at, "Glow_Ring", new Color(.55f, .12f, .6f, .6f), 1.1f, 1.1f, 0, parent: go.transform);
            Sprite(at, "EQ_Mouth", new Color(1f, .62f, 1f, 1f), .95f, .95f, 0, parent: go.transform);
            go.AddComponent<HweiGrow>();
            AbilityFx.Motes(go.transform, Dread, .06f, .5f, 45, ParticleSystemShapeType.Sphere, .3f);
            var trail = new GameObject("Face trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(go.transform, false);
            trail.sharedMaterial = ZeriFx.TexGlow(Fx("PaintStrokeDiffuse"), new Color(.85f, .25f, .9f, .6f));
            trail.time = .3f;
            trail.startWidth = .5f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public void FaceHit(Vector3 at)
        {
            float near = NearEyes(at);
            Sprite(at, "EQ_Mouth", new Color(1f, .6f, 1f, 1f), .9f * near, 2.3f * near, .35f);
            Sprite(at, "Glow_Ring", new Color(.8f, .25f, .9f, .8f), .5f * near, 2f * near, .3f);
            Burst(at, Dread, 14, 2.5f, .05f, .5f);
        }

        public void FaceFade(Vector3 at) => Sprite(at, "EQ_Mouth", new Color(.8f, .4f, .9f, .6f), .95f, .3f, .25f);

        /// <summary>A terrified enemy: a dark swirl spins over its head while it flees.</summary>
        public void Terrified(Combatant target, float seconds)
        {
            var swirl = Sprite(target.AimPosition + Vector3.up * .9f, "P_Icon_Dissolve", new Color(.8f, .3f, 1f, .9f), .55f, .55f, seconds, parent: target.transform);
            swirl.gameObject.AddComponent<HweiSpin>().speed = -360;
        }

        /// <summary>Gaze of the Abyss: a glowing eye opens over the spot, its gaze ring on the ground.</summary>
        public GameObject Eye(Vector3 at, float seconds)
        {
            var go = Keep(new GameObject("Hwei Gaze of the Abyss"));
            go.transform.position = at;
            var orb = Sprite(at + Vector3.up * 1.3f, "EW_Eyeicon_Dissolve", new Color(.9f, .35f, .95f, .9f), .1f, .1f, 0, parent: go.transform);
            orb.name = "Eye orb";
            var star = Sprite(at + Vector3.up * 1.3f, "EW_Eyeicon", new Color(1f, .8f, .45f, .9f), .1f, .1f, 0, parent: go.transform);
            star.name = "Eye star";
            Sprite(at + Vector3.up * .06f, "Glow_Ring_Edge", new Color(.85f, .3f, .9f, .7f), 1.4f, 1.4f, 0, flat: true, parent: go.transform);
            Sprite(at + Vector3.up * .05f, "P_Icon", new Color(1f, .7f, .4f, .45f), 1.1f, 1.1f, 0, flat: true, parent: go.transform);
            return go;
        }

        /// <summary>The eye opens over 0.3 s and keeps a slow pulse while it watches.</summary>
        public void EyeLook(GameObject view, float age)
        {
            if (!view)
                return;
            float open = Mathf.Clamp01(age / .3f);
            float pulse = 1 + .08f * Mathf.Sin(age * 6);
            foreach (var (name, size) in new[] { ("Eye orb", 1.0f), ("Eye star", 1.5f) })
            {
                var t = view.transform.Find(name);
                if (t && t.TryGetComponent<ZeriSprite>(out var sprite))
                    sprite.size = size * open * pulse / .1f;
            }
        }

        public void EyeGlare(GameObject view, Combatant target)
        {
            if (view)
                Sprite(view.transform.position + Vector3.up * 1.3f, "EW_Eyeicon", new Color(1f, .85f, .55f, 1f), 1.2f, 2.6f, .25f);
        }

        public void EyeClose(GameObject view)
        {
            if (!view)
                return;
            Sprite(view.transform.position + Vector3.up * 1.3f, "EW_Eyeicon_Dissolve", new Color(.8f, .3f, .9f, .6f), 1, .1f, .25f);
            Object.Destroy(view);
        }

        public GameObject EyeBolt(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei gaze strike"));
            go.transform.position = at;
            Sprite(at, "Glow_Ring", new Color(.9f, .3f, .95f, .85f), .45f, .45f, 0, parent: go.transform);
            Sprite(at, "DotSmol", new Color(1f, .85f, .55f, 1f), .22f, .22f, 0, parent: go.transform);
            var trail = new GameObject("Gaze trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(go.transform, false);
            trail.sharedMaterial = ZeriFx.Glow(new Color(.9f, .35f, .95f, .6f));
            trail.time = .18f;
            trail.startWidth = .2f;
            trail.endWidth = 0;
            trail.minVertexDistance = .04f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public void EyeBoltFade(Vector3 at) => Sprite(at, "Glow_Ring", new Color(.8f, .3f, .9f, .5f), .45f, .1f, .2f);

        /// <summary>A rooted enemy: a ring of his gaze binds its feet while the root lasts.</summary>
        public void Root(Combatant target, float seconds)
        {
            Vector3 feet = HweiKit.Ground(target.transform.position);
            Sprite(feet + Vector3.up * .07f, "Glow_Ring_Edge", new Color(.9f, .3f, .95f, .9f), 1.6f, 1.2f, seconds, flat: true, parent: target.transform);
            var spin = Sprite(feet + Vector3.up * .08f, "P_Icon", new Color(1f, .78f, .4f, .8f), 1.2f, 1.2f, seconds, flat: true, parent: target.transform);
            spin.gameObject.AddComponent<HweiSpin>().speed = 90;
            Sprite(target.AimPosition, "Glow_Ring", new Color(.9f, .35f, .95f, .8f), .5f, 1.8f, .3f);
            Burst(target.AimPosition, Dread, 12, 2f, .05f, .45f);
        }

        /// <summary>Crushing Maw: a dark pool under the spot and two rows of fangs that open over it and bite shut.</summary>
        public GameObject Maw(Vector3 at, float delay)
        {
            var go = Keep(new GameObject("Hwei Crushing Maw"));
            go.transform.position = at;
            Sprite(at + Vector3.up * .05f, "Glow_Ring", new Color(.6f, .1f, .65f, .45f), HweiKit.EERadius * 1.2f, HweiKit.EERadius * 2.2f, delay + .3f, flat: true, parent: go.transform);
            Sprite(at + Vector3.up * .06f, "Glow_Ring_Edge", new Color(.95f, .35f, .95f, .85f), HweiKit.EERadius * 2.15f, HweiKit.EERadius * 2.15f, 0, flat: true, parent: go.transform);
            var upper = Sprite(at + Vector3.up * 2, "EE_Teeth02", new Color(1f, .78f, 1f, 1f), HweiKit.EERadius * 2, HweiKit.EERadius * 2, 0, parent: go.transform);
            upper.name = "Upper jaw";
            var lower = Sprite(at + Vector3.up * .3f, "EE_Teeth01", new Color(1f, .7f, .95f, 1f), HweiKit.EERadius * 1.8f, HweiKit.EERadius * 1.8f, 0, parent: go.transform);
            lower.name = "Lower jaw";
            lower.Roll = 180;
            return go;
        }

        /// <summary>The jaws open wider for most of the delay, then snap shut over the middle as it ends.</summary>
        public void MawClosing(GameObject view, Vector3 at, float t)
        {
            if (!view)
                return;
            t = Mathf.Clamp01(t);
            float open = t < .7f ? Mathf.Lerp(.8f, 1.05f, t / .7f) : Mathf.Lerp(1.05f, 0, (t - .7f) / .3f * ((t - .7f) / .3f));
            var upper = view.transform.Find("Upper jaw");
            var lower = view.transform.Find("Lower jaw");
            if (upper)
                upper.position = at + Vector3.up * (1.05f + open * .95f);
            if (lower)
                lower.position = at + Vector3.up * (.75f - open * .55f);
        }

        public void Bite(Vector3 at)
        {
            float near = NearEyes(at + Vector3.up);
            Sprite(at + Vector3.up * .9f, "Glow_Ring", new Color(.95f, .35f, .95f, .85f), .6f * near, 2.8f * near, .3f);
            Sprite(at + Vector3.up * .9f, "GoldStrandThin", new Color(1f, .78f, .4f, .9f), 1.2f * near, 2.6f * near, .3f).Roll = Random.Range(0, 360f);
            Sprite(at + Vector3.up * .07f, "GroundCrack02Base", new Color(.9f, .3f, .9f, .9f), HweiKit.EERadius * 2.4f, .5f, .35f, flat: true);
            Burst(at + Vector3.up * .8f, Dread, 16, 3f, .05f, .45f);
        }

        // ---------- Spiraling Despair ----------

        public GameObject DespairOrb(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei Spiraling Despair"));
            go.transform.position = at;
            Sprite(at, "Glow_Ring", new Color(.5f, .18f, .85f, .9f), .55f, .55f, 0, parent: go.transform);
            Sprite(at, "Glow_Ring_Edge", new Color(1f, .78f, .38f, .8f), .7f, .7f, 0, parent: go.transform);
            var spiral = Sprite(at, "P_Icon_Dissolve", new Color(.75f, .35f, 1f, .9f), .9f, .9f, 0, parent: go.transform);
            spiral.gameObject.AddComponent<HweiSpin>().speed = 540;
            go.AddComponent<HweiGrow>();
            AbilityFx.Motes(go.transform, HweiKit.Despair, .07f, .6f, 50, ParticleSystemShapeType.Sphere, .25f);
            var trail = new GameObject("Despair trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(go.transform, false);
            trail.sharedMaterial = ZeriFx.Glow(new Color(.55f, .2f, .9f, .55f));
            trail.time = .3f;
            trail.startWidth = .45f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>The despair spreading on the ground: radial cracks, a turning spiral and its edge, with dark motes rising.</summary>
        public GameObject Despair(Vector3 at)
        {
            var go = Keep(new GameObject("Hwei despair zone"));
            go.transform.position = at;
            var cracks = Sprite(at + Vector3.up * .05f, "R_Cracks_Dissolve", new Color(.72f, .3f, 1f, 1f), .1f, .1f, 0, flat: true, parent: go.transform);
            cracks.name = "Cracks";
            var spiral = Sprite(at + Vector3.up * .06f, "P_Indicator01_Dissolve", new Color(.95f, .65f, 1f, .45f), .1f, .1f, 0, flat: true, parent: go.transform);
            spiral.name = "Spiral";
            var edge = Sprite(at + Vector3.up * .07f, "Glow_Ring_Edge", new Color(1f, .78f, .4f, .8f), .1f, .1f, 0, flat: true, parent: go.transform);
            edge.name = "Edge";
            var motes = AbilityFx.Motes(go.transform, HweiKit.Despair, .07f, 1.2f, 40, ParticleSystemShapeType.Circle, 1);
            motes.name = "Despair motes";
            motes.transform.rotation = Quaternion.Euler(-90, 0, 0);
            ZeriFlash.Create(player, at + Vector3.up * .8f, new Color(.6f, .25f, .95f, .6f), .6f, .25f);
            return go;
        }

        public void DespairGrow(GameObject view, Vector3 at, float radius, float t)
        {
            if (!view)
                return;
            view.transform.position = at;
            float diameter = radius * 2;
            foreach (var (name, scale) in new[] { ("Cracks", 1.05f), ("Spiral", .85f), ("Edge", 1.04f) })
            {
                var child = view.transform.Find(name);
                if (!child || !child.TryGetComponent<ZeriSprite>(out var sprite))
                    continue;
                child.position = at + Vector3.up * .06f;
                sprite.size = diameter * scale / .1f;
                if (name == "Spiral")
                    sprite.Roll = Time.time * 70;
            }
            var motes = view.transform.Find("Despair motes");
            if (motes && motes.TryGetComponent<ParticleSystem>(out var ps))
            {
                var shape = ps.shape;
                shape.radius = radius * .9f;
                motes.position = at + Vector3.up * .1f;
            }
        }

        public void DespairBurst(Vector3 at, float radius)
        {
            float near = NearEyes(at + Vector3.up);
            Sprite(at + Vector3.up, "Glow_Ring", new Color(.7f, .3f, 1f, .9f), 1f * near, radius * 1.6f * near, .35f);
            Sprite(at + Vector3.up * .07f, "R_Cracks", new Color(1f, .75f, .4f, .9f), radius * 2, radius * 2.6f, .55f, flat: true);
            Sprite(at + Vector3.up * .08f, "R_Cracks_Dissolve", new Color(.75f, .3f, 1f, .9f), radius * 2, radius * 2.5f, .5f, flat: true);
            ZeriRing.Create(player, at + Vector3.up * .15f, Vector3.up, radius, new Color(1f, .8f, .45f, .9f), .14f, .4f);
            Burst(at + Vector3.up * .6f, HweiKit.Despair, 26, 4.5f, .07f, .7f, .3f);
        }
    }

    /// <summary>Thins a line to nothing over its life, then removes it (Molten Fissure's path).</summary>
    public class HweiFade : MonoBehaviour
    {
        LineRenderer line;
        float born, seconds, width;

        public void Begin(float startWidth, float life)
        {
            line = GetComponent<LineRenderer>();
            width = startWidth;
            seconds = life;
            born = Time.time;
        }

        void Update()
        {
            float t = (Time.time - born) / Mathf.Max(.01f, seconds);
            if (t >= 1)
            {
                Destroy(gameObject);
                return;
            }
            if (line)
                line.startWidth = line.endWidth = width * (1 - t * t);
        }
    }

    /// <summary>
    /// A projectile's sprites start small at the brush and swell to full size over its first 2.5 m, so nothing fills the
    /// view as it leaves the hand.
    /// </summary>
    public class HweiGrow : MonoBehaviour
    {
        Vector3 start;
        ZeriSprite[] sprites;

        void Start()
        {
            start = transform.position;
            sprites = GetComponentsInChildren<ZeriSprite>();
            LateUpdate();
        }

        void LateUpdate()
        {
            float grow = Mathf.Lerp(.3f, 1, Mathf.Clamp01(Vector3.Distance(transform.position, start) / 2.5f));
            foreach (var sprite in sprites)
                if (sprite)
                    sprite.size = grow;
        }
    }

    /// <summary>Turns a sprite about its facing axis (or a flat one about the vertical).</summary>
    public class HweiSpin : MonoBehaviour
    {
        public float speed = 180;
        ZeriSprite sprite;
        float angle;

        void Start() => sprite = GetComponent<ZeriSprite>();

        void Update()
        {
            angle += speed * Time.deltaTime;
            if (sprite)
                sprite.Roll = angle;
        }
    }

    /// <summary>Scrolls the waves of Fleeting Current along its flow and fades the whole current in and out over its life.</summary>
    public class HweiFlow : MonoBehaviour
    {
        Material[] materials;
        bool[] scrolling;
        Color[] colors;
        float born, seconds, speed;

        public void Begin(Material[] fading, bool[] scroll, float life, float flow)
        {
            materials = fading;
            scrolling = scroll;
            colors = new Color[fading.Length];
            for (int i = 0; i < fading.Length; i++)
                colors[i] = fading[i].GetColor("_BaseColor");
            born = Time.time;
            seconds = life;
            speed = flow;
        }

        void Update()
        {
            float age = Time.time - born;
            float fade = Mathf.Clamp01(age / .25f) * Mathf.Clamp01((seconds - age) / .5f);
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i])
                    continue;
                if (scrolling[i])
                    materials[i].SetTextureOffset("_BaseMap", new Vector2(-age * speed, 0));
                var c = colors[i];
                c.a *= fade;
                materials[i].SetColor("_BaseColor", c);
            }
        }

        void OnDestroy()
        {
            if (materials == null)
                return;
            foreach (var m in materials)
                if (m)
                    Destroy(m);
        }
    }
}
