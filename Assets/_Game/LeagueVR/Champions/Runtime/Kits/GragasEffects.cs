using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gragas's visuals, from his own League textures (Champions/Art/Gragas/Resources/GragasFx, decoded from the player's
    /// install) and his own keg: he holds it the way League's Gragas does, like a big mug, palm on its side (sized for
    /// VR), and lifts it to his mouth to drink; the rolled barrel and the explosive cask are copies of it. Aim
    /// previews (League's own team ring, and a line drawn through walls) show only while a button is held.
    /// </summary>
    public class GragasVisuals
    {
        /// <summary>The keg's width in VR (League's is 0.8 m across at model scale): a hefty keg that still clears the view.</summary>
        const float KegWidth = .42f;
        /// <summary>Middle and radius of the keg in its own bone space (from the model).</summary>
        static readonly Vector3 KegCentre = new(0, .046f, 0);
        const float KegRadius = .4f;
        // League's barrel is about as tall as a minion: big enough to pick out of a crowd.
        const float BarrelSize = .65f, CaskSize = .45f;
        /// <summary>The cask readied in the left palm is ball-sized, so it never hides the spot the hand aims at; it swells in flight.</summary>
        const float HeldCaskSize = .18f;

        /// <summary>Half the keg's height (it is 0.89 tall for 0.79 across in its own space).</summary>
        const float KegHalfHeight = KegWidth * .56f;

        // The keg in the controller's grip space, held exactly the way League's Gragas holds it in every clip (measured on
        // his model): like a big mug, the palm flat on its side and the fingers around it, its axis along the thumb (the
        // bung end past the thumb). In grip space the thumb side is +Z and the right palm faces -X. League puts the palm
        // near the keg's bottom; here it sits at the middle, so the keg does not rise past the face when the hand comes up.
        static readonly Vector3 HoldOffset = new(-.213f, .027f, .017f);
        static readonly Quaternion HoldRotation = Quaternion.LookRotation(new Vector3(-.988f, .03f, .151f), new Vector3(.154f, .193f, .969f));

        /// <summary>How the fingers close around the keg's band.</summary>
        public static readonly HandCurl KegCurl = new(.62f, .82f, .72f);

        /// <summary>His brew's pink foam and amber ale, mixed in the bubbles.</summary>
        static readonly Color Foam = new(1f, .72f, 1f, .9f), AleBubble = new(1f, .75f, .35f, .9f);

        readonly PlayerChampion player;
        readonly List<GameObject> owned = new();

        Transform keg, hand;
        Mesh kegMesh;
        Material kegMaterial;
        /// <summary>Where each palm sits on its controller grip, measured while the hands follow the controllers.</summary>
        Vector3 palmInGrip, leftPalmInGrip;
        TrailRenderer smashTrail;

        ZeriSprite barrelRing, caskRing, kegGlow;
        LineRenderer barrelCircle, caskCircle, slamPath, slamTarget;
        GameObject heldCask;
        bool caskHeld, pushedAir;
        float nextBubble, nextFizz, nextStridePuff;

        static string Fx(string name) => "GragasFx/" + name;

        /// <summary>True when the body has his keg (the model's "weapon" bone).</summary>
        public bool HasKeg => keg;

        static Color Bubble() => Random.value < .4f ? AleBubble : Foam;

        /// <summary>
        /// Flashes shrink when they go off near the eyes (a barrel burst at your feet), so nothing white fills the view.
        /// </summary>
        float NearEyes(Vector3 at) => player.head ? Mathf.Clamp(Vector3.Distance(at, player.head.transform.position) / 4f, .3f, 1f) : 1;

        public GragasVisuals(PlayerChampion player)
        {
            this.player = player;
            var avatar = player.Body as ChampionVRAvatar;
            if (avatar && avatar.Instance)
                foreach (var t in avatar.Instance.GetComponentsInChildren<Transform>(true))
                    if (t.name == "weapon")
                    {
                        keg = t;
                        break;
                    }
            if (keg)
            {
                hand = keg.parent;
                kegMesh = ChampionVRAvatar.ExtractPropMesh(avatar.Skin, keg, null, out kegMaterial);
            }
            barrelRing = Ring(GragasKit.QRadius, new Color(.55f, .9f, 1f, .9f));
            caskRing = Ring(GragasKit.RRadius, new Color(1f, .55f, .3f, .9f));
            barrelCircle = Circle("Gragas barrel landing", new Color(.55f, .9f, 1f, .6f));
            caskCircle = Circle("Gragas cask landing", new Color(1f, .55f, .3f, .6f));
            slamPath = Keep(AbilityFx.Line("Gragas slam path", GragasKit.Ale, .05f, 2));
            slamPath.sharedMaterial = ZeriFx.XRay(GragasKit.Ale);
            slamPath.startColor = slamPath.endColor = GragasKit.Ale;
            // Pale gold: red minions and champions would swallow a red ring.
            slamTarget = Circle("Gragas slam target", new Color(1f, .9f, .55f, .95f), .06f);
            kegGlow = ZeriSprite.Create(player, player.Feet, Fx("GragasFx_WFlare"), new Color(1f, .6f, 1f, .9f), .55f, .55f, 0);
            kegGlow.gameObject.SetActive(false);
            Keep(kegGlow.gameObject);
            heldCask = MakeKeg("Gragas cask (held)", HeldCaskSize);
            heldCask.SetActive(false);
            // The empowered smash leaves a streak of brew behind the keg as it comes down.
            smashTrail = Keep(new GameObject("Gragas smash trail")).AddComponent<TrailRenderer>();
            smashTrail.sharedMaterial = ZeriFx.Glow(new Color(1f, .62f, .95f, .8f));
            smashTrail.time = .25f;
            smashTrail.startWidth = .2f;
            smashTrail.endWidth = 0;
            smashTrail.minVertexDistance = .04f;
            smashTrail.emitting = false;
            smashTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            HideAll();
        }

        T Keep<T>(T component) where T : Component
        {
            Keep(component.gameObject);
            return component;
        }

        GameObject Keep(GameObject go)
        {
            if (owned.Count > 64)
                owned.RemoveAll(o => !o);
            owned.Add(go);
            player.Track(go);
            return go;
        }

        /// <summary>League's own ground ring (a team indicator texture), flat, kept until hidden.</summary>
        ZeriSprite Ring(float radius, Color color)
        {
            var sprite = ZeriSprite.Create(player, player.Feet, Fx("GragasFx_AimRing"), color, radius * 2.13f, radius * 2.13f, 0, flat: true);
            Keep(sprite.gameObject);
            sprite.gameObject.SetActive(false);
            return sprite;
        }

        /// <summary>A thin ring drawn through walls, so a landing behind one still shows.</summary>
        LineRenderer Circle(string name, Color color, float width = .04f)
        {
            var line = Keep(AbilityFx.Line(name, color, width, 48));
            line.loop = true;
            line.sharedMaterial = ZeriFx.XRay(color);
            line.startColor = line.endColor = color;
            return line;
        }

        public void Destroy()
        {
            foreach (var go in owned)
                if (go)
                    Object.Destroy(go);
            owned.Clear();
            if (kegMesh)
                Object.Destroy(kegMesh);
        }

        public void HideAll()
        {
            barrelRing.gameObject.SetActive(false);
            caskRing.gameObject.SetActive(false);
            barrelCircle.enabled = caskCircle.enabled = slamPath.enabled = slamTarget.enabled = false;
            kegGlow.gameObject.SetActive(false);
            caskHeld = false;
            heldCask.SetActive(false);
            smashTrail.emitting = false;
        }

        // ---------- The keg in the hand ----------

        /// <summary>
        /// After the body is posed each frame: the keg sits in the fist (overriding his idle animation), hides when the
        /// hand is busy with an item or a menu, rises to the mouth while he drinks, goes up and down in both hands for the
        /// empowered smash (<paramref name="smashClock"/> seconds in, or -1), swings with the fist in a basic attack, and
        /// glows while Drunken Rage empowers it.
        /// </summary>
        public void Pose(float drinkProgress, bool empowered, float smashClock, Vector3 smashForward, SwingState swing)
        {
            var body = player.Body;
            bool shown = keg && hand && body && !player.HandStowed(false);
            if (keg)
                keg.localScale = shown ? Vector3.one * (KegWidth / (KegRadius * 2) / Mathf.Max(.01f, hand.lossyScale.x)) : Vector3.zero;
            if (shown)
            {
                var grip = body.Grip(false);
                Vector3 palm = body.Palm(false);
                // While the hand follows its controller the keg is held by the posed fist, and the fist's place on the grip
                // is remembered; during the drink the keg and the arm's reach (DrinkGrip, before the body is solved) both
                // anchor on that remembered place, so the fist stays on the keg all the way up and down.
                bool drinking = DrinkWeight(drinkProgress) > 0, smashing = smashClock >= 0;
                if (swing.Active)
                    grip = SwingGrip(grip, swing);
                else if (!drinking && !smashing)
                {
                    palmInGrip = Quaternion.Inverse(grip.rotation) * (palm - grip.position);
                    var leftGrip = body.Grip(true);
                    leftPalmInGrip = Quaternion.Inverse(leftGrip.rotation) * (body.Palm(true) - leftGrip.position);
                }
                Vector3 fist = drinking || smashing ? grip.position + grip.rotation * palmInGrip : palm;
                KegPose(fist, grip.rotation, drinkProgress, out var centre, out var rotation);
                if (smashing)
                    SmashKeg(centre, rotation, smashClock, smashForward, out centre, out rotation);
                keg.SetPositionAndRotation(centre - rotation * (KegCentre * (KegWidth / (KegRadius * 2))), rotation);
                SmashEffects(smashClock, centre, rotation);
            }
            else
                smashTrail.emitting = false;
            // Drunken Rage's next hit: a pulsing flare where the fist meets the keg, fizzing up past the knuckles, so it
            // shows whenever the fist is in view (the keg itself hangs below it).
            bool glow = empowered && shown;
            kegGlow.gameObject.SetActive(glow);
            if (glow)
            {
                // Pulled toward the eyes so the big fist never hides it.
                Vector3 fist = body.Palm(false);
                Vector3 toEyes = player.head.transform.position - fist;
                Vector3 front = toEyes.sqrMagnitude > 1e-4f ? toEyes.normalized : Vector3.up;
                kegGlow.transform.position = Vector3.Lerp(fist, keg.TransformPoint(KegCentre), .25f) + front * .14f;
                // Smaller as the fist comes up to the face, so it never covers the view.
                kegGlow.size = (1.15f + Mathf.Sin(Time.time * 9) * .15f) * Mathf.Clamp(toEyes.magnitude / .8f, .3f, 1f);
                if (Time.time >= nextFizz)
                {
                    nextFizz = Time.time + .09f;
                    Vector3 at = fist + front * .1f + Vector3.up * Random.Range(0f, .22f) + Random.insideUnitSphere * .06f;
                    ZeriSprite.Create(player, at, Fx("GragasFx_Bubble"), Bubble(), .04f, .09f, .45f);
                }
            }
            if (caskHeld)
            {
                heldCask.transform.position = CaskInHand();
                heldCask.transform.rotation = Quaternion.Euler(0, Time.time * 90, 0);
            }
        }

        /// <summary>The readied cask: in the left palm, on the side the palm faces.</summary>
        public Vector3 CaskInHand()
        {
            var body = player.Body;
            if (!body)
                return player.OffHandOrigin;
            return body.Palm(true) + body.LeftGrip().rotation * Vector3.right * (HeldCaskSize * .5f + .02f) - Vector3.up * .04f;
        }

        /// <summary>Where the keg sits: in the fist by its top rim, or on its way up to (and at) his mouth while he drinks.</summary>
        void KegPose(Vector3 fist, Quaternion grip, float drinkProgress, out Vector3 centre, out Quaternion rotation)
        {
            centre = fist + grip * HoldOffset;
            rotation = grip * HoldRotation;
            float w = DrinkWeight(drinkProgress);
            if (w <= 0)
                return;
            DrinkPose(out var drinkCentre, out var drinkRotation);
            centre = Vector3.Lerp(centre, drinkCentre, w);
            rotation = Quaternion.Slerp(rotation, drinkRotation, w);
        }

        /// <summary>Up over the first third of the drink, held at the mouth, down over the last third.</summary>
        static float DrinkWeight(float progress)
        {
            if (progress < 0 || progress > 1)
                return 0;
            float x = progress < .35f ? progress / .35f : progress > .65f ? (1 - progress) / .35f : 1;
            // Stops a little short of the lips, so the keg never comes closer than about 0.15 m to the eyes.
            return x * x * (3 - 2 * x) * .9f;
        }

        /// <summary>
        /// At the mouth: lifted by its rim, the keg tips its top to his lips from below the chin on the right, about 45
        /// degrees out in the lower right of the view and 0.4 m from the eyes (held in front of the eyes, as League's camera
        /// can afford, it would blind the player).
        /// </summary>
        void DrinkPose(out Vector3 centre, out Quaternion rotation)
        {
            var head = player.head.transform;
            Vector3 mouth = head.position - head.up * .1f + head.forward * .07f;
            Vector3 away = (head.right * .56f - head.up * .46f + head.forward * .69f).normalized;
            centre = mouth + away * (KegHalfHeight + .05f);
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, away), -away);
        }

        /// <summary>
        /// While he drinks, the right arm reaches for the keg on its way to his mouth and back, so the fist stays on it;
        /// otherwise the hand follows its controller.
        /// </summary>
        public Pose DrinkGrip(Pose grip, float drinkProgress)
        {
            if (DrinkWeight(drinkProgress) <= 0 || !keg || player.HandStowed(false))
                return grip;
            KegPose(grip.position + grip.rotation * palmInGrip, grip.rotation, drinkProgress, out var centre, out var rotation);
            var hold = rotation * Quaternion.Inverse(HoldRotation);
            // The grip that puts the fist (palmInGrip from the grip) on the keg's top rim.
            Vector3 fist = centre - hold * HoldOffset;
            return new Pose(fist - hold * palmInGrip, hold);
        }

        /// <summary>The keg as a weapon: from the fist to the keg's far side.</summary>
        public bool KegEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (!keg || !hand || keg.localScale.x <= 0)
                return false;
            from = player.Body ? player.Body.Palm(false) : hand.position;
            Vector3 centre = keg.TransformPoint(KegCentre);
            Vector3 outward = centre - from;
            // A little past the keg: in VR a swing that visibly brushes an enemy should land.
            to = centre + (outward.sqrMagnitude > 1e-6f ? outward.normalized : -keg.up) * (KegWidth * .5f + .25f);
            return true;
        }

        /// <summary>A copy of his keg (or a plain barrel if the body has none), about <paramref name="size"/> metres across.</summary>
        GameObject MakeKeg(string name, float size)
        {
            var go = new GameObject(name);
            var model = new GameObject("Keg");
            model.transform.SetParent(go.transform, false);
            var filter = model.AddComponent<MeshFilter>();
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (kegMesh)
            {
                filter.sharedMesh = kegMesh;
                renderer.sharedMaterial = kegMaterial;
                // Centred on the barrel and scaled to the requested width (the keg is 0.8 m across in its own space).
                float scale = size / (KegRadius * 2);
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = -KegCentre * scale;
            }
            else
            {
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                renderer.sharedMaterial = AbilityFx.Material(GragasKit.Ale);
                model.transform.localScale = new Vector3(size, size * .55f, size);
            }
            Keep(go);
            return go;
        }

        // ---------- Aim previews (only while held) ----------

        public void BarrelAim(bool show, Vector3 at) => ShowRing(barrelRing, barrelCircle, show, at, GragasKit.QRadius);

        public void CaskAim(bool show, Vector3 at)
        {
            ShowRing(caskRing, caskCircle, show, at, GragasKit.RRadius);
            caskHeld = show;
            heldCask.SetActive(show);
        }

        void ShowRing(ZeriSprite ring, LineRenderer circle, bool show, Vector3 at, float radius)
        {
            ring.gameObject.SetActive(show);
            circle.enabled = show;
            if (!show)
                return;
            ring.transform.position = at + Vector3.up * .05f;
            ring.size = 1 + Mathf.Sin(Time.time * 6) * .03f;
            AbilityFx.SetCircle(circle, at, radius);
        }

        public void SlamAim(bool show, GragasKit.Slam plan)
        {
            slamPath.enabled = show;
            slamTarget.enabled = show && plan.first;
            if (!show)
                return;
            Vector3 lift = Vector3.up * .15f;
            Vector3 end = plan.start + plan.direction * plan.distance;
            slamPath.SetPosition(0, plan.start + plan.direction * .5f + lift);
            slamPath.SetPosition(1, end + lift);
            if (plan.first)
                AbilityFx.SetCircle(slamTarget, plan.first.transform.position, GragasKit.ERadius);
        }

        // ---------- Q: Barrel Roll ----------

        public GameObject SpawnBarrel(Vector3 at)
        {
            var barrel = MakeKeg("Gragas barrel", BarrelSize);
            barrel.transform.position = at;
            var glow = ZeriSprite.Create(player, at, Fx("GragasFx_Flash"), new Color(1f, .7f, 1f, .9f), .5f, .5f, 0, parent: barrel.transform);
            glow.name = "Ferment glow";
            glow.gameObject.SetActive(false);
            return barrel;
        }

        /// <summary>A lob from the hand that lands and rolls on to the target, turning over as it goes.</summary>
        public void RollBarrel(GameObject barrel, Vector3 from, Vector3 to, float t)
        {
            if (!barrel)
                return;
            Vector3 rest = to + Vector3.up * BarrelSize * .5f;
            float distance = Geo.FlatDistance(from, to);
            float arc = .4f + distance * .05f;
            Vector3 direction = Geo.FlatDirection(to - from, Vector3.forward);
            barrel.transform.position = Vector3.Lerp(from, rest, t) + Vector3.up * arc * 4 * t * (1 - t);
            float roll = distance * t / (BarrelSize * .5f) * Mathf.Rad2Deg;
            barrel.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(roll, 0, 90);
        }

        /// <summary>Fermenting: his purple flare grows on the barrel and bubbles rise from it.</summary>
        public void Ferment(GameObject barrel, float ferment)
        {
            if (!barrel)
                return;
            var glow = barrel.transform.Find("Ferment glow");
            if (glow)
            {
                glow.gameObject.SetActive(true);
                // Above the barrel, so it reads over the units standing around it.
                glow.position = barrel.transform.position + Vector3.up * (.35f + ferment * .25f);
                if (glow.TryGetComponent<ZeriSprite>(out var sprite))
                    sprite.size = .6f + ferment * 1.4f + Mathf.Sin(Time.time * (6 + ferment * 10)) * .1f;
            }
            if (Time.time >= nextBubble)
            {
                nextBubble = Time.time + Mathf.Lerp(.35f, .1f, ferment);
                ZeriSprite.Create(player, barrel.transform.position + Random.insideUnitSphere * .2f + Vector3.up * Random.Range(.25f, .8f), Fx("GragasFx_Bubble"), Bubble(), .12f, .22f, .5f);
            }
        }

        public void BarrelBurst(GameObject barrel, Vector3 at, float ferment)
        {
            if (barrel)
                Object.Destroy(barrel);
            float size = GragasKit.QRadius * 2.2f;
            ZeriSprite.Create(player, at + Vector3.up * .06f, Fx("GragasFx_BlastRing"), new Color(1f, .75f, 1f, 1f), size * .4f, size, .45f, flat: true);
            float near = NearEyes(at + Vector3.up * .5f);
            ZeriSprite.Create(player, at + Vector3.up * .6f, Fx("GragasFx_Foam"), new Color(1f, .7f, .95f, 1f), .8f * near, (2.2f + ferment) * near, .5f);
            ZeriSprite.Create(player, at + Vector3.up * .5f, Fx("GragasFx_Squirt"), new Color(1f, .6f, .95f, 1f), .6f * near, 1.6f * near, .4f);
            ZeriSprite.Create(player, at + Vector3.up * .5f, Fx("GragasFx_Flash"), new Color(1f, .85f, 1f, .8f), .8f * near, (2f + ferment) * near, .25f);
            for (int i = 0; i < 6; i++)
                ZeriSprite.Create(player, at + Random.insideUnitSphere * GragasKit.QRadius * .6f + Vector3.up * .4f, Fx("GragasFx_Bubble"), Bubble(), .15f, .35f, .6f);
        }

        // ---------- W: Drunken Rage and the empowered hit ----------

        /// <summary>While he drinks: bubbles and foam at the keg's tap.</summary>
        public void Drinking(float progress)
        {
            if (!keg || Time.time < nextBubble)
                return;
            nextBubble = Time.time + .1f;
            Vector3 tap = keg.TransformPoint(KegCentre + Vector3.up * KegRadius);
            ZeriSprite.Create(player, tap + Random.insideUnitSphere * .05f, Fx("GragasFx_Bubble"), Bubble(), .05f, .12f, .4f);
            if (progress > .3f && progress < .8f)
                ZeriSprite.Create(player, tap, Fx("GragasFx_Foam"), new Color(1f, .75f, .95f, .7f), .1f, .25f, .25f);
        }

        public void Splash(Vector3 at)
        {
            float near = NearEyes(at);
            ZeriSprite.Create(player, at, Fx("GragasFx_WFlare"), new Color(1f, .65f, 1f, 1f), .6f * near, 1.6f * near, .3f);
            ZeriSprite.Create(player, at, Fx("GragasFx_Foam"), new Color(1f, .7f, .95f, 1f), .5f * near, 1.4f * near, .45f);
            ZeriSprite.Create(player, at + Vector3.down * .9f + Vector3.up * .06f, Fx("GragasFx_BlastRing"), new Color(1f, .7f, 1f, .8f), .5f, GragasKit.WRadius * 2.2f, .35f, flat: true);
        }

        // ---------- Basic attacks (League's Attack1, Attack2 and Crit) ----------

        /// <summary>The trigger's basic attacks: League's Attack1 (a hook from his right side), Attack2 (an overhand bonk) and Crit.</summary>
        public enum Swing { Hook, Bonk, Crit }

        /// <summary>A basic swing: which one, how far through it (0-1; negative for none) and toward where (flat).</summary>
        public readonly struct SwingState
        {
            /// <summary>The hit lands here, as the keg strikes: after the wind-up (0-0.35) and the strike (0.35-0.5).</summary>
            public const float Strike = .5f;
            public static readonly SwingState None = new(Swing.Hook, -1, Vector3.forward);

            public readonly Swing kind;
            public readonly float progress;
            public readonly Vector3 forward;

            public SwingState(Swing kind, float progress, Vector3 forward)
            {
                this.kind = kind;
                this.progress = progress;
                this.forward = forward;
            }

            public bool Active => progress >= 0 && progress < 1;

            public SwingState At(float progress) => new(kind, progress, forward);
        }

        /// <summary>One pose of a swing: where the fist is (right, forward, up from the eyes, in metres for a 0.71 m arm), which
        /// way the back of the hand faces and where the thumb (the keg's axis) points, in the same frame.</summary>
        readonly struct SwingKey
        {
            public readonly Vector3 fist, back, thumb;

            public SwingKey(Vector3 fist, Vector3 back, Vector3 thumb)
            {
                this.fist = fist;
                this.back = back;
                this.thumb = thumb;
            }
        }

        // Wind-up, strike and follow-through of each swing, read off League's clips and kept within his reach (his
        // shoulders sit 0.3-0.4 m behind his eyes) and at least about 0.35 m between the keg and the eyes. The keg rides on
        // the palm side of the fist, so where the palm faces is where the keg strikes.
        static readonly SwingKey[] Hook =
        {
            new(new(.45f, -.05f, -.3f), new(.7f, -.7f, 0), Vector3.up),
            new(new(.2f, .25f, -.3f), new(.85f, -.5f, 0), new(0, .2f, 1)),
            new(new(.05f, .2f, -.35f), new(.9f, .4f, 0), Vector3.up),
        };
        static readonly SwingKey[] Bonk =
        {
            new(new(.55f, -.15f, 0), Vector3.right, new(0, -.8f, .6f)),
            new(new(.25f, .25f, -.3f), Vector3.right, new(0, .8f, -.6f)),
            new(new(.2f, .2f, -.42f), Vector3.right, new(0, .6f, -.8f)),
        };
        static readonly SwingKey[] CritSwing =
        {
            new(new(.6f, -.25f, -.3f), new(0, -1, 0), Vector3.up),
            new(new(.15f, .28f, -.33f), Vector3.right, new(0, .2f, 1)),
            new(new(0, .15f, -.35f), new(.6f, .8f, 0), Vector3.up),
        };

        /// <summary>
        /// The right grip during a basic swing: from the controller to the wind-up (to 0.35), a strike that speeds up into
        /// the hit (0.35-0.5, curving out in front, or over the top for the bonk), a follow-through (to 0.7) and back to
        /// the controller. The keg stays in the fist: it follows the posed palm.
        /// </summary>
        public Pose SwingGrip(Pose grip, SwingState swing)
        {
            if (!swing.Active || !keg || !player.Body || player.HandStowed(false))
                return grip;
            Vector3 forward = swing.forward, right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 eyes = player.head.transform.position;
            float s = SmashReach(out _) / .71f;
            Vector3 Frame(Vector3 v) => right * v.x + forward * v.y + Vector3.up * v.z;
            Vector3 Fist(SwingKey k) => eyes + Frame(k.fist) * s;
            Quaternion Turn(SwingKey k)
            {
                Vector3 x = Frame(k.back).normalized;
                Vector3 z = Vector3.ProjectOnPlane(Frame(k.thumb), x).normalized;
                return Quaternion.LookRotation(z, Vector3.Cross(z, x));
            }
            var keys = swing.kind == Swing.Bonk ? Bonk : swing.kind == Swing.Crit ? CritSwing : Hook;
            Vector3 hand = grip.position + grip.rotation * palmInGrip;
            float p = swing.progress;
            Vector3 fist;
            Quaternion turn;
            if (p < .35f)
            {
                // The bonk's keg goes up to beside the head: it is raised out at his right side first, not past the face.
                float u = Smooth(p / .35f);
                fist = swing.kind == Swing.Bonk
                    ? Bezier(hand, eyes + Frame(new Vector3(.6f, .15f, -.35f)) * s, Fist(keys[0]), u)
                    : Vector3.Lerp(hand, Fist(keys[0]), u);
                turn = Quaternion.Slerp(grip.rotation, Turn(keys[0]), u);
            }
            else if (p < SwingState.Strike)
            {
                float u = (p - .35f) / (SwingState.Strike - .35f);
                u *= u;
                Vector3 a = Fist(keys[0]), b = Fist(keys[1]);
                // The strike curves out in front (the bonk out to the right and forward): straight across it passes the eyes too close.
                Vector3 bulge = (swing.kind == Swing.Bonk ? right * .15f + forward * .2f - Vector3.up * .05f : forward * (swing.kind == Swing.Crit ? .22f : .14f)) * s;
                fist = Bezier(a, (a + b) * .5f + bulge, b, u);
                turn = Quaternion.Slerp(Turn(keys[0]), Turn(keys[1]), u);
            }
            else if (p < .7f)
            {
                float u = (p - SwingState.Strike) / (.7f - SwingState.Strike);
                u = 1 - (1 - u) * (1 - u);
                fist = Vector3.Lerp(Fist(keys[1]), Fist(keys[2]), u);
                turn = Quaternion.Slerp(Turn(keys[1]), Turn(keys[2]), u);
            }
            else
            {
                float u = Smooth((p - .7f) / .3f);
                fist = Vector3.Lerp(Fist(keys[2]), hand, u);
                turn = Quaternion.Slerp(Turn(keys[2]), grip.rotation, u);
            }
            return new Pose(fist - turn * palmInGrip, turn);
        }

        /// <summary>
        /// A basic swing lands on <paramref name="target"/>: a flash of brew where the keg strikes and a little foam and
        /// bubbles; the bonk and the crit also kick up dust at its feet, and a critical strike bursts bigger with a ring on
        /// the ground. Scaled down near the eyes.
        /// </summary>
        public void SwingImpact(Combatant target, Swing kind, bool crit)
        {
            if (!target)
                return;
            Vector3 at = target.AimPosition;
            float near = NearEyes(at) * (crit ? 1.6f : 1);
            ZeriSprite.Create(player, at, Fx("GragasFx_Flash"), new Color(1f, .85f, .6f, .9f), .25f * near, .9f * near, .2f);
            ZeriSprite.Create(player, at + Vector3.up * .1f, Fx("GragasFx_Foam"), new Color(1f, .75f, .95f, .8f), .2f * near, .7f * near, .35f);
            for (int i = 0; i < (crit ? 6 : 3); i++)
                ZeriSprite.Create(player, at + Random.insideUnitSphere * .25f, Fx("GragasFx_Bubble"), Bubble(), .04f, .12f, .5f);
            Vector3 feet = target.transform.position;
            if (kind != Swing.Hook)
                ZeriSprite.Create(player, feet + Vector3.up * .25f, Fx("GragasFx_Dust"), new Color(.85f, .75f, .6f, .7f), .4f, 1.1f, .5f);
            if (crit)
                ZeriSprite.Create(player, feet + Vector3.up * .06f, Fx("GragasFx_BlastRing"), new Color(1f, .85f, .6f, .9f), .3f, 1.8f, .4f, flat: true);
        }

        // ---------- W: the empowered smash (League's Attack_Passive) ----------

        static float Smooth(float x) => x * x * (3 - 2 * x);

        /// <summary>
        /// How much of an arm the smash takes. The right hand, already on the keg, goes with it at once; the left joins it
        /// once it is over his head (reaching for it on the way up would sweep the hand across the eyes). Both go back to
        /// their controllers while the keg returns.
        /// </summary>
        public static float SmashWeight(float clock, bool left = false)
        {
            if (clock < 0 || clock >= GragasKit.SmashTime)
                return 0;
            float back = GragasKit.SmashTime - GragasKit.SmashReturn;
            float join = left ? GragasKit.SmashLift * .55f : 0, ramp = left ? GragasKit.SmashLift * .45f : .1f;
            float w = clock < join + ramp ? (clock - join) / ramp : clock > back ? 1 - (clock - back) / GragasKit.SmashReturn : 1;
            return Smooth(Mathf.Clamp01(w));
        }

        /// <summary>The middle of his shoulders, his right (square to the smash) and how far his arms reach (from his skeleton).</summary>
        void SmashFrame(Vector3 forward, out Vector3 shoulders, out Vector3 right, out float reach)
        {
            right = Vector3.Cross(Vector3.up, forward).normalized;
            var rig = player.Body ? player.Body.Rig : null;
            if (rig != null && rig.Valid)
            {
                shoulders = (rig.LeftArm.upper.position + rig.RightArm.upper.position) * .5f;
                var arm = rig.RightArm;
                reach = Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.end.position);
                return;
            }
            shoulders = player.head.transform.position - Vector3.up * .25f;
            reach = .6f;
        }

        /// <summary>
        /// The keg along League's Attack_Passive, laid out around the eyes: from the hand up past the right shoulder to lie
        /// across the top of his head, then swung down the right of the view to stand upright in front of him toward the
        /// target, held there with a little bounce, and back to the hand. The whole path keeps the keg's middle at least
        /// 0.36 m from the eyes (his shoulders sit well behind his eyes, so a swing straight down the middle would pass
        /// through the view at a hand's width) and within reach of his arms (offsets scale with his arm length, measured
        /// at 0.71 m).
        /// </summary>
        void SmashKeg(Vector3 handCentre, Quaternion handRotation, float clock, Vector3 forward, out Vector3 centre, out Quaternion rotation)
        {
            SmashFrame(forward, out _, out var right, out float reach);
            Vector3 eyes = player.head.transform.position;
            float s = reach / .71f;
            Vector3 At(float r, float f, float u) => eyes + (right * r + forward * f + Vector3.up * u) * s;
            Vector3 over = At(.12f, -.06f, .34f), down = At(.04f, .26f, -.42f);
            var overRotation = Quaternion.LookRotation(Vector3.up, right);
            var downRotation = Quaternion.LookRotation(forward, Vector3.up);
            float lift = GragasKit.SmashLift, fall = lift + GragasKit.SmashDown, hold = fall + GragasKit.SmashHold;
            if (clock < lift)
            {
                float u = Smooth(clock / lift);
                centre = Bezier(handCentre, At(.55f, .2f, .05f), over, u);
                rotation = Quaternion.Slerp(handRotation, overRotation, u);
            }
            else if (clock < fall)
            {
                // Faster and faster down the right of the view, turning upright as it falls.
                float u = (clock - lift) / GragasKit.SmashDown;
                u *= u;
                centre = Bezier(over, At(.55f, .3f, .05f), down, u);
                rotation = Quaternion.Slerp(overRotation, downRotation, u);
            }
            else if (clock < hold)
            {
                float b = (clock - fall) / GragasKit.SmashHold;
                centre = down + Vector3.up * (Mathf.Sin(b * Mathf.PI) * .04f * (1 - b));
                rotation = downRotation;
            }
            else
            {
                float u = Smooth(Mathf.Clamp01((clock - hold) / GragasKit.SmashReturn));
                centre = Vector3.Lerp(down, handCentre, u);
                rotation = Quaternion.Slerp(downRotation, handRotation, u);
            }
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u) => (1 - u) * (1 - u) * a + 2 * (1 - u) * u * b + u * u * c;

        /// <summary>
        /// The grip each hand reaches for during the smash: the palm on the keg's side (on its ends while it lies across
        /// the shoulders, on its staves once it stands), facing the keg, the forearm toward the shoulder. Blended in and
        /// out by <see cref="SmashWeight"/>; a left hand holding an item stays with its controller.
        /// </summary>
        public Pose SmashGrip(bool left, Pose grip, float clock, Vector3 forward)
        {
            float w = SmashWeight(clock, left);
            var body = player.Body;
            if (w <= 0 || !keg || !body || player.HandStowed(false) || (left && player.HandStowed(true)))
                return grip;
            // Where the keg is this frame, from the controllers (the body is posed after this).
            var rightGrip = body.Grip(false);
            KegPose(rightGrip.position + rightGrip.rotation * palmInGrip, rightGrip.rotation, -1, out var handCentre, out var handRotation);
            SmashKeg(handCentre, handRotation, clock, forward, out var centre, out var rotation);
            SmashFrame(forward, out var shoulders, out var right, out _);
            float across = Mathf.Abs(Vector3.Dot(rotation * Vector3.up, right));
            float side = Mathf.Lerp(KegWidth * .5f + .03f, KegHalfHeight + .02f, across);
            Vector3 palm = centre + right * (left ? -side : side) + Vector3.up * ((1 - across) * .05f);
            Vector3 shoulder = shoulders + right * (left ? -.2f : .2f);
            var rig = body.Rig;
            if (rig != null && rig.Valid)
                shoulder = (left ? rig.LeftArm : rig.RightArm).upper.position;
            Vector3 toWrist = Vector3.ProjectOnPlane(shoulder - palm, right);
            toWrist = toWrist.sqrMagnitude > 1e-6f ? toWrist.normalized : -forward;
            // Grip space: +X is the back of the right hand and the palm side of the left, +Y runs back toward the wrist.
            var turn = Quaternion.LookRotation(Vector3.Cross(right, toWrist), toWrist);
            var turned = Quaternion.Slerp(grip.rotation, turn, w);
            if (!left)
                return new Pose(Vector3.Lerp(grip.position, palm - turn * palmInGrip, w), turned);
            // The left hand comes up its own side of the head and takes the keg from there, so the arm never sweeps
            // across the eyes on the way.
            float s = SmashReach(out _) / .71f;
            Vector3 beside = player.head.transform.position + (right * -.38f + forward * .05f + Vector3.up * .05f) * s;
            Vector3 from = grip.position + grip.rotation * leftPalmInGrip;
            return new Pose(Bezier(from, beside, palm, w) - turned * leftPalmInGrip, turned);
        }

        float SmashReach(out Vector3 right)
        {
            SmashFrame(player.origin.transform.forward, out _, out right, out float reach);
            return reach;
        }

        /// <summary>Both hands take the keg: it fizzes over as it goes up.</summary>
        public void SmashBegin()
        {
            if (!keg)
                return;
            Vector3 tap = keg.TransformPoint(KegCentre + Vector3.up * KegRadius);
            for (int i = 0; i < 5; i++)
                ZeriSprite.Create(player, tap + Random.insideUnitSphere * .08f, Fx("GragasFx_Bubble"), Bubble(), .05f, .14f, .45f);
            ZeriSprite.Create(player, tap, Fx("GragasFx_WFlare"), new Color(1f, .6f, 1f, .8f), .15f, .45f, .25f);
        }

        /// <summary>
        /// The smash's own effects: a streak of brew as the keg comes down (only where it is well clear of the eyes, so the
        /// streak never hangs across the view), and foam welling up while it rests.
        /// </summary>
        void SmashEffects(float clock, Vector3 centre, Quaternion rotation)
        {
            float landed = GragasKit.SmashLift + GragasKit.SmashDown;
            bool falling = clock >= GragasKit.SmashLift - .05f && clock < landed + .03f
                && Vector3.Distance(centre, player.head.transform.position) > .6f;
            if (falling && !smashTrail.emitting)
                smashTrail.Clear();
            smashTrail.transform.position = centre;
            smashTrail.emitting = falling;
            if (clock >= landed && clock < landed + GragasKit.SmashHold && Time.time >= nextBubble)
            {
                nextBubble = Time.time + .05f;
                Vector3 top = centre + rotation * Vector3.up * KegHalfHeight;
                ZeriSprite.Create(player, top + Random.insideUnitSphere * .06f, Fx("GragasFx_Bubble"), Bubble(), .05f, .14f, .5f);
            }
        }

        /// <summary>
        /// The keg comes down: a fountain of brew bursts up out of it and away from him toward the target (high enough to
        /// show over his fists, which hold the keg low in the view), with a pink flare, squirts of ale and bubbles, and a
        /// shockwave of foam runs out over the ground at <paramref name="target"/>, slow enough to be seen spreading from
        /// behind the fists. The burst is short-lived and see-through, so it never blinds the view for long.
        /// </summary>
        public void SmashImpact(Vector3 target, Vector3 forward)
        {
            Vector3 kegAt = keg ? keg.TransformPoint(KegCentre) : target + Vector3.up;
            Vector3 top = kegAt + Vector3.up * KegHalfHeight;
            Vector3 spout = (forward * .3f + Vector3.up * .2f).normalized;
            for (int i = 1; i <= 4; i++)
                ZeriSprite.Create(player, top + spout * (.3f * i), Fx("GragasFx_Foam"), new Color(1f, .72f, .95f, .75f), .12f + .07f * i, .45f + .2f * i, .3f + .05f * i);
            ZeriSprite.Create(player, top + spout * .7f, Fx("GragasFx_WFlare"), new Color(1f, .65f, 1f, .9f), .25f, 1f, .28f);
            for (int i = 0; i < 8; i++)
                ZeriSprite.Create(player, top + spout * .45f + Random.insideUnitSphere * .25f, Fx("GragasFx_Squirt"), new Color(1f, .6f, .95f, .9f), .15f, .6f, .4f);
            for (int i = 0; i < 12; i++)
                ZeriSprite.Create(player, top + spout * .8f + Random.insideUnitSphere * .5f, Fx("GragasFx_Bubble"), Bubble(), .06f, .18f, .7f);
            ZeriSprite.Create(player, target + Vector3.up * .08f, Fx("GragasFx_Shockwave"), new Color(1f, .8f, 1f, .9f), .5f, GragasKit.WRadius * 2.6f, .7f, flat: true);
            ZeriSprite.Create(player, target + Vector3.up * .3f, Fx("GragasFx_EdgeDust"), new Color(.9f, .8f, .7f, .85f), .6f, 2f, .7f);
        }

        // ---------- Passive: Happy Hour ----------

        /// <summary>
        /// A swig: a green healing spark low in the view (about 25 degrees under the eye line), sparkles at the fist, a
        /// healing glow at the feet and foam at the keg.
        /// </summary>
        public void Swig()
        {
            var head = player.head.transform;
            Vector3 front = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (front.sqrMagnitude < .01f)
                front = player.origin.transform.forward;
            ZeriSprite.Create(player, head.position + front * .6f - Vector3.up * .28f, Fx("GragasFx_Heal"), new Color(.6f, 1f, .5f, .9f), .12f, .32f, .55f);
            if (player.Body)
                for (int i = 0; i < 3; i++)
                    ZeriSprite.Create(player, player.Body.Palm(false) + Random.insideUnitSphere * .08f + Vector3.up * (.05f + i * .07f), Fx("GragasFx_Heal"), new Color(.6f, 1f, .5f, .85f), .05f, .12f, .5f + i * .1f);
            ZeriSprite.Create(player, player.Feet + Vector3.up * .05f, Fx("GragasFx_HealGlow"), new Color(.5f, 1f, .4f, .6f), .8f, 1.8f, .6f, flat: true);
            if (keg)
                ZeriSprite.Create(player, keg.TransformPoint(KegCentre + Vector3.up * KegRadius), Fx("GragasFx_Foam"), new Color(1f, .75f, .95f, .8f), .12f, .3f, .35f);
        }

        // ---------- E: Body Slam ----------

        /// <summary>He pushes off: dust bursts from where he stood and a ring of it runs out over the ground.</summary>
        public void ChargeBegin(Vector3 feet, Vector3 direction)
        {
            for (int i = 0; i < 3; i++)
                ZeriSprite.Create(player, feet - direction * .3f + Random.insideUnitSphere * .25f + Vector3.up * .25f, Fx("GragasFx_Dust"), new Color(.85f, .75f, .6f, .8f), .5f, 1.3f, .6f);
            ZeriSprite.Create(player, feet + Vector3.up * .06f, Fx("GragasFx_Shockwave"), new Color(1f, .85f, .6f, .6f), .3f, 1.8f, .35f, flat: true);
        }

        /// <summary>
        /// While he charges: dust kicked up behind his strides, and rings of air pushed ahead of his belly that run with
        /// him low in the view (rings, so they never hide what he is running at).
        /// </summary>
        public void Charging(Vector3 feet, Vector3 direction)
        {
            if (Time.time < nextStridePuff)
                return;
            nextStridePuff = Time.time + .07f;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            ZeriSprite.Create(player, feet - direction * .5f + right * Random.Range(-.3f, .3f) + Vector3.up * .2f, Fx("GragasFx_Dust"), new Color(.85f, .75f, .6f, .7f), .4f, 1.2f, .5f);
            pushedAir = !pushedAir;
            if (pushedAir)
                ZeriSprite.Create(player, feet + direction * 1.1f + Vector3.up * .7f, Fx("GragasFx_Shockwave"), new Color(1f, .9f, .7f, .35f), .5f, 1.4f, .22f, parent: player.origin.transform);
        }

        public void SlamTrail(Vector3 from, Vector3 to, Vector3 direction)
        {
            float distance = Geo.FlatDistance(from, to);
            for (float d = 0; d <= distance; d += .7f)
                ZeriSprite.Create(player, from + direction * d + Vector3.up * .25f, Fx("GragasFx_Dust"), new Color(.85f, .75f, .6f, .7f), .5f, 1.1f, .5f);
        }

        public void SlamImpact(Vector3 at, bool hit)
        {
            ZeriSprite.Create(player, at + Vector3.up * .08f, Fx("GragasFx_BlastRing"), new Color(1f, .85f, .6f, hit ? 1 : .6f), .4f, GragasKit.ERadius * 2.4f, .6f, flat: true);
            ZeriSprite.Create(player, at + Vector3.up * .1f, Fx("GragasFx_Shockwave"), new Color(1f, .8f, .45f, hit ? .9f : .5f), .3f, GragasKit.ERadius * 2.1f, .45f, flat: true);
            ZeriSprite.Create(player, at + Vector3.up * .3f, Fx("GragasFx_EdgeDust"), new Color(.9f, .8f, .65f, .9f), .7f, 2.2f, .8f);
            for (int i = 0; i < 4; i++)
                ZeriSprite.Create(player, at + Random.insideUnitSphere * GragasKit.ERadius * .5f + Vector3.up * .2f, Fx("GragasFx_Dust"), new Color(.85f, .75f, .6f, .75f), .5f, 1.2f, .7f);
            if (hit)
                ZeriSprite.Create(player, at + Vector3.up * .9f, Fx("GragasFx_Flash"), new Color(1f, .8f, .6f, 1f), .5f, 1.2f, .25f);
        }

        // ---------- R: Explosive Cask ----------

        public GameObject SpawnCask(Vector3 at)
        {
            caskHeld = false;
            heldCask.SetActive(false);
            var cask = MakeKeg("Gragas explosive cask", CaskSize);
            cask.transform.position = at;
            var glow = ZeriSprite.Create(player, at, Fx("GragasFx_Flash"), new Color(1f, .6f, .4f, .9f), .7f, .7f, 0, parent: cask.transform);
            glow.lift = .3f;
            // A fizzing trail of brew behind it, so the throw can be followed all the way to the blast.
            var trail = cask.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ZeriFx.Glow(new Color(1f, .6f, .9f, .85f));
            trail.time = .4f;
            trail.startWidth = .42f;
            trail.endWidth = 0;
            trail.minVertexDistance = .08f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return cask;
        }

        public void FlyCask(GameObject cask, Vector3 from, Vector3 to, float t)
        {
            if (!cask)
                return;
            float arc = 1.5f + Geo.FlatDistance(from, to) * .12f;
            cask.transform.position = Vector3.Lerp(from, to + Vector3.up * CaskSize * .5f, t) + Vector3.up * arc * 4 * t * (1 - t);
            cask.transform.rotation = Quaternion.Euler(t * 540, t * 220, 0);
            // Leaves the palm ball-sized and swells to a full cask over the first part of the throw.
            cask.transform.localScale = Vector3.one * Mathf.Lerp(HeldCaskSize / CaskSize, 1, Mathf.Clamp01(t / .3f));
        }

        public void CaskBurst(GameObject cask, Vector3 at)
        {
            if (cask)
                Object.Destroy(cask);
            float size = GragasKit.RRadius * 2.2f;
            ZeriSprite.Create(player, at + Vector3.up * .06f, Fx("GragasFx_FlameRing"), new Color(1f, .7f, .9f, 1f), size * .3f, size, .55f, flat: true);
            ZeriSprite.Create(player, at + Vector3.up * .08f, Fx("GragasFx_Shockwave"), new Color(1f, .8f, 1f, .9f), size * .2f, size * 1.3f, .5f, flat: true);
            float near = NearEyes(at + Vector3.up);
            ZeriSprite.Create(player, at + Vector3.up * 1f, Fx("GragasFx_Flash"), new Color(1f, .85f, .95f, .85f), 1.5f * near, 4.5f * near, .3f);
            ZeriSprite.Create(player, at + Vector3.up * .8f, Fx("GragasFx_Foam"), new Color(1f, .7f, .95f, 1f), 1.5f * near, 4f * near, .6f);
            for (int i = 0; i < 5; i++)
                ZeriSprite.Create(player, at + Random.insideUnitSphere * GragasKit.RRadius * .5f + Vector3.up * .6f, Fx("GragasFx_Squirt"), new Color(1f, .6f, .95f, 1f), .5f * near, 1.4f * near, .5f);
        }
    }
}
