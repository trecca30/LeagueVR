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
        LineRenderer sight, coilArc, palmArc, palmGuide, targetRing, crashRing, surgePath, surgeRing, surgeHead;
        readonly LineRenderer[] surgeChevrons = new LineRenderer[4];
        ZeriKit.SurgeLook surgeShown = ZeriKit.SurgeLook.Hidden;
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
            surgePath = Keep(AbilityFx.Line("Zeri surge preview", ZeriKit.Spark, .05f, SurgePathPoints));
            surgeRing = Keep(AbilityFx.Line("Zeri surge landing", ZeriKit.Spark, .05f, 40));
            surgeRing.loop = true;
            surgeHead = Keep(AbilityFx.Line("Zeri surge arrowhead", ZeriKit.Spark, .07f, 3));
            for (int i = 0; i < surgeChevrons.Length; i++)
                surgeChevrons[i] = Keep(AbilityFx.Line("Zeri surge chevron", ZeriKit.Spark, .045f, 3));
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
            surgePath.enabled = surgeRing.enabled = surgeHead.enabled = false;
            foreach (var chevron in surgeChevrons)
                chevron.enabled = false;
            leftMove = LeftMove.None;
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
            // Where the left palm sits on its controller, measured while the hand follows the controller.
            if (LeftClock < 0 && player.Body)
            {
                var leftGrip = player.Body.Grip(true);
                leftPalmInGrip = Quaternion.Inverse(leftGrip.rotation) * (player.Body.Palm(true) - leftGrip.position);
            }
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
            HandEffects();
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

        const int SurgePathPoints = 24;

        /// <summary>
        /// Where Spark Surge would take her if pressed now: a high yellow arc over the wall to a landing ring for a vault,
        /// a flat green line for a plain dash, and red when the dash would stop at the wall (barely moving) or find no room.
        /// Drawn through walls, since a vault lands behind one.
        /// </summary>
        public void SurgePreview(ZeriKit.SurgeLook look, ZeriKit.SurgePlan plan, Vector3 feet)
        {
            bool show = look != ZeriKit.SurgeLook.Hidden;
            surgePath.enabled = surgeRing.enabled = surgeHead.enabled = show;
            foreach (var chevron in surgeChevrons)
                chevron.enabled = show;
            if (!show)
                return;
            if (look != surgeShown)
            {
                surgeShown = look;
                Color color = look == ZeriKit.SurgeLook.Vault ? ZeriKit.Volt : look == ZeriKit.SurgeLook.Dash ? new Color(.62f, 1f, .38f, .75f) : ZeriKit.Danger;
                surgePath.sharedMaterial = surgeRing.sharedMaterial = surgeHead.sharedMaterial = ZeriFx.XRay(color);
                surgePath.startColor = surgePath.endColor = surgeRing.startColor = surgeRing.endColor = surgeHead.startColor = surgeHead.endColor = color;
                foreach (var chevron in surgeChevrons)
                {
                    chevron.sharedMaterial = ZeriFx.XRay(color);
                    chevron.startColor = chevron.endColor = color;
                }
                // A vault's arc starts right before the eyes and lands up to 12 m away: thin near, bold far.
                bool vault = look == ZeriKit.SurgeLook.Vault;
                surgePath.startWidth = vault ? .015f : .04f;
                surgePath.endWidth = vault ? .05f : .04f;
                surgeRing.startWidth = surgeRing.endWidth = vault ? .08f : .05f;
            }
            // From just ahead of the feet (followed every frame) to the planned landing, arcing like the vault itself.
            // A vault's arc starts at chest height and a little to the right, so from the eyes it reads as an arc over
            // the wall rather than a line straight up the middle of the view.
            bool vaulting = look == ZeriKit.SurgeLook.Vault;
            Vector3 right = Vector3.Cross(Vector3.up, plan.direction).normalized;
            Vector3 start = feet + plan.direction * .5f + (vaulting ? Vector3.up * .9f + right * .3f : Vector3.up * .15f);
            Vector3 end = plan.landing + Vector3.up * .15f;
            float span = Geo.FlatDistance(feet, plan.landing);
            float arc = vaulting ? Mathf.Clamp(1.2f + span * .18f, 1.4f, 3.2f) : 0;
            Vector3 Along(float t) => Vector3.Lerp(start, end, t) + Vector3.up * arc * 4 * t * (1 - t);
            surgePath.positionCount = SurgePathPoints;
            for (int i = 0; i < SurgePathPoints; i++)
                surgePath.SetPosition(i, Along(i / (SurgePathPoints - 1f)));
            // Chevrons run along the path toward the landing, so which way it goes reads at a glance; they fade in away
            // from the eyes.
            for (int k = 0; k < surgeChevrons.Length; k++)
            {
                float t = Mathf.Lerp(.12f, .9f, Mathf.Repeat(Time.time * .9f + k / (float)surgeChevrons.Length, 1));
                Vector3 tip = Along(t), tangent = (Along(t + .02f) - tip).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, plan.direction).normalized;
                float size = Mathf.Lerp(.12f, .3f, t) * (vaulting ? 1.2f : 1);
                var chevron = surgeChevrons[k];
                chevron.startWidth = chevron.endWidth = Mathf.Lerp(.02f, .05f, t);
                chevron.SetPosition(0, tip - tangent * size + side * size * .8f);
                chevron.SetPosition(1, tip);
                chevron.SetPosition(2, tip - tangent * size - side * size * .8f);
            }
            // A bold arrowhead on the ground at the landing, pointing on along the dash.
            Vector3 head = plan.landing + Vector3.up * .08f + plan.direction * .55f, across = Vector3.Cross(Vector3.up, plan.direction).normalized;
            surgeHead.SetPosition(0, head - plan.direction * .5f + across * .38f);
            surgeHead.SetPosition(1, head);
            surgeHead.SetPosition(2, head - plan.direction * .5f - across * .38f);
            float radius = look == ZeriKit.SurgeLook.Vault ? 1.05f : look == ZeriKit.SurgeLook.Dash ? .55f : .35f;
            float pulse = 1 + Mathf.Sin(Time.time * 8) * .08f;
            AbilityFx.SetCircle(surgeRing, plan.landing + Vector3.up * .06f, radius * pulse);
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

        float nextRushSpark;

        /// <summary>Spark Surge sets off: a ring of sparks bursts from her feet.</summary>
        public void SurgeRushStart(Vector3 feet, Vector3 direction)
        {
            ZeriRing.Create(player, feet + Vector3.up * .05f, Vector3.up, .7f, ZeriKit.Spark, .06f, .35f);
            ZeriFlash.Create(player, feet + Vector3.up * .08f, new Color(.7f, 1f, .5f, .3f), .8f, .2f, flat: true);
            Impact(feet + Vector3.up * .3f, ZeriKit.Spark, 10);
        }

        /// <summary>
        /// While Spark Surge carries her: arcs crackle off both feet and streak back along the ground, low in the view and
        /// never across it, with sparks spraying behind.
        /// </summary>
        public void SurgeRushing(Vector3 feet, Vector3 direction)
        {
            if (Time.time < nextRushSpark)
                return;
            nextRushSpark = Time.time + .04f;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            for (int side = -1; side <= 1; side += 2)
                Arc(feet + Vector3.up * .15f + right * side * .22f, feet - direction * 1.3f + Vector3.up * .08f + right * side * .45f, ZeriKit.Spark, .14f, .035f);
            impactSparks.Emit(new ParticleSystem.EmitParams { position = feet + Vector3.up * .2f - direction * .3f, applyShapeToPosition = true, startColor = ZeriKit.Spark }, 2);
        }

        /// <summary>Zapping through a thin wall or a tower: a burst of her electricity where she meets it.</summary>
        public void ZapThrough(Vector3 at, Vector3 direction)
        {
            Impact(at + Vector3.up * 1.1f + direction * .6f, ZeriKit.Volt, 14);
            Arc(at + Vector3.up * 1.1f, at + Vector3.up * 1.1f + direction * 2.5f, ZeriKit.Volt, .25f, .08f);
        }

        /// <summary>The vault seen from outside: her model trails sparks and a streak of her green glow over the wall.</summary>
        public void VaultTrail(GameObject puppet)
        {
            if (!puppet)
                return;
            var host = new GameObject("Zeri vault trail");
            host.transform.SetParent(puppet.transform, false);
            host.transform.localPosition = Vector3.up * .9f;
            var trail = host.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ZeriFx.Glow(new Color(.62f, 1f, .38f, .5f));
            trail.time = .3f;
            trail.startWidth = .45f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AbilityFx.Motes(host.transform, ZeriKit.Volt, .04f, .25f, 50, ParticleSystemShapeType.Sphere, .25f);
        }

        // ---------- The left hand: the battery flick and the pulse throw ----------

        enum LeftMove { None, Flick, Throw }

        LeftMove leftMove;
        float leftStart, nextHandSpark;
        bool leftBig;
        Combatant flickTarget;
        Vector3 throwDirection, leftPalmInGrip;

        static float Smooth(float x) => x * x * (3 - 2 * x);

        /// <summary>Seconds into the left hand's animation, or -1 while the hand is with its controller.</summary>
        float LeftClock
        {
            get
            {
                if (leftMove == LeftMove.None)
                    return -1;
                float clock = Time.time - leftStart;
                if (clock < (leftMove == LeftMove.Flick ? ZeriKit.FlickTime : ZeriKit.ThrowTime))
                    return clock;
                leftMove = LeftMove.None;
                return -1;
            }
        }

        /// <summary>League's Attack1a and Attack2: the left hand rises beside the shoulder and snaps out at the target.</summary>
        public void StartFlick(Combatant target, bool big)
        {
            leftMove = LeftMove.Flick;
            leftStart = Time.time;
            flickTarget = target;
            leftBig = big;
        }

        /// <summary>Ultrashock Laser's throw: the arm draws back and thrusts the pulse out along the palm's aim.</summary>
        public void StartThrow(Vector3 direction)
        {
            leftMove = LeftMove.Throw;
            leftStart = Time.time;
            throwDirection = direction.normalized;
        }

        /// <summary>The left shoulder and how far the arm reaches (from the skeleton; 0.52 m for Zeri at her usual size).</summary>
        void LeftArm(out Vector3 shoulder, out Vector3 elbow, out float reach)
        {
            var rig = player.Body ? player.Body.Rig : null;
            if (rig != null && rig.Valid)
            {
                var arm = rig.LeftArm;
                shoulder = arm.upper.position;
                elbow = arm.lower.position;
                reach = Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.end.position);
                return;
            }
            var head = player.head.transform;
            shoulder = head.position - Vector3.up * .2f - head.right * .16f;
            elbow = shoulder - Vector3.up * .2f;
            reach = .52f;
        }

        /// <summary>Where the flick goes: at the target's chest, or straight on if it is gone.</summary>
        Vector3 FlickDirection(Vector3 shoulder) => flickTarget && flickTarget.IsAlive
            ? (flickTarget.AimPosition - shoulder).normalized
            : player.PlanarDirection(player.head.transform.forward);

        /// <summary>A left grip turned so the palm (+X on the left hand) faces <paramref name="palm"/>, thumb toward <paramref name="thumb"/>.</summary>
        static Quaternion LeftTurn(Vector3 palm, Vector3 thumb)
        {
            Vector3 x = palm.normalized;
            Vector3 z = Vector3.ProjectOnPlane(thumb, x);
            if (z.sqrMagnitude < 1e-4f)
                z = Vector3.ProjectOnPlane(Vector3.forward, x);
            z.Normalize();
            return Quaternion.LookRotation(z, Vector3.Cross(z, x));
        }

        /// <summary>
        /// The left grip during a flick or a throw, laid out from her shoulder and arm length: wind up, snap or thrust out
        /// (the spark or pulse leaves at <see cref="ZeriKit.FlickSnap"/> / <see cref="ZeriKit.ThrowRelease"/>), follow
        /// through, and back to the controller.
        /// </summary>
        public Pose LeftGrip(Pose grip)
        {
            float clock = LeftClock;
            if (clock < 0 || !player.Body || player.HandStowed(true))
                return grip;
            LeftArm(out var shoulder, out _, out float reach);
            Vector3 hand = grip.position + grip.rotation * leftPalmInGrip;
            Vector3 up = Vector3.up, wind, snap, follow;
            Quaternion windTurn, snapTurn;
            float tWind, tSnap, tFollow, total;
            if (leftMove == LeftMove.Flick)
            {
                Vector3 dir = FlickDirection(shoulder);
                Vector3 flat = player.PlanarDirection(dir);
                Vector3 left = -Vector3.Cross(up, flat).normalized;
                // Cocked up just under eye height out to the left, in view (beside the shoulder the eyes never saw it),
                // then snapped out at the target.
                wind = player.head.transform.position + (flat * .32f + left * .22f - up * .07f) * (reach / .52f);
                snap = shoulder + dir * (.92f * reach);
                follow = shoulder + (dir * .8f - up * .12f) * reach;
                windTurn = LeftTurn(flat + up * .3f, up);
                snapTurn = LeftTurn(dir, up);
                tWind = .12f;
                tSnap = .16f;
                tFollow = .22f;
                total = ZeriKit.FlickTime;
            }
            else
            {
                Vector3 dir = throwDirection;
                // Drawn back a hand's width from where the player holds it, then thrust out along the palm's own aim line,
                // so the pulse leaves exactly where the player aimed from.
                wind = hand - dir * .25f;
                snap = hand + dir * .3f;
                follow = hand + dir * .2f - up * .03f;
                windTurn = snapTurn = LeftTurn(dir, up);
                tWind = .06f;
                tSnap = .12f;
                tFollow = .2f;
                total = ZeriKit.ThrowTime;
            }
            Vector3 palm;
            Quaternion turn;
            if (clock < tWind)
            {
                float u = Smooth(clock / tWind);
                palm = Vector3.Lerp(hand, wind, u);
                turn = Quaternion.Slerp(grip.rotation, windTurn, u);
            }
            else if (clock < tSnap)
            {
                float u = (clock - tWind) / (tSnap - tWind);
                u = 1 - (1 - u) * (1 - u);
                palm = Vector3.Lerp(wind, snap, u);
                turn = Quaternion.Slerp(windTurn, snapTurn, u);
            }
            else if (clock < tFollow)
            {
                palm = Vector3.Lerp(snap, follow, (clock - tSnap) / (tFollow - tSnap));
                turn = snapTurn;
            }
            else
            {
                float u = Smooth((clock - tFollow) / (total - tFollow));
                palm = Vector3.Lerp(follow, hand, u);
                turn = Quaternion.Slerp(snapTurn, grip.rotation, u);
            }
            return new Pose(palm - turn * leftPalmInGrip, turn);
        }

        /// <summary>The left hand's fingers while it flicks or throws: a loose fist gathering the spark, then open as it lets go.</summary>
        public HandCurl? LeftFingers()
        {
            float clock = LeftClock;
            if (clock < 0)
                return null;
            if (leftMove == LeftMove.Flick && clock < ZeriKit.FlickSnap - .02f)
                return new HandCurl(.5f, .55f, .7f, .75f, .8f);
            return new HandCurl(.1f, 0, .05f, .1f, .15f);
        }

        /// <summary>Where the flicked spark leaves the hand: just past the fingers of the posed left hand.</summary>
        public Vector3 FlickTip()
        {
            LeftArm(out var shoulder, out _, out _);
            return (player.Body ? player.Body.Palm(true) : player.OffHandOrigin) + FlickDirection(shoulder) * .07f;
        }

        /// <summary>Where the thrown pulse leaves the hand: on the palm's aim line, just past where the thrust ends.</summary>
        public Vector3 ThrowPoint() => kit.PalmAim().position + throwDirection * .3f;

        /// <summary>
        /// The hand's own effects: sparks gather between the fingers as the flick winds up (bigger for a charged or
        /// executing shot), and lightning coils around the forearm as it throws the pulse.
        /// </summary>
        void HandEffects()
        {
            float clock = LeftClock;
            if (clock < 0 || Time.time < nextHandSpark || !player.Body)
                return;
            nextHandSpark = Time.time + .03f;
            Vector3 palm = player.Body.Palm(true);
            if (leftMove == LeftMove.Flick && clock < ZeriKit.FlickSnap)
            {
                float size = leftBig ? .14f : .09f;
                ZeriSprite.Create(player, palm, "ZeriFx_Glow", leftBig ? ZeriKit.Volt : ZeriKit.Spark, size * .5f, size, .1f);
                impactSparks.Emit(new ParticleSystem.EmitParams { position = palm, applyShapeToPosition = true, startColor = leftBig ? ZeriKit.Volt : ZeriKit.Spark }, leftBig ? 3 : 1);
            }
            else if (leftMove == LeftMove.Throw && clock < .14f)
            {
                LeftArm(out _, out var elbow, out _);
                Arc(elbow, palm, ZeriKit.Plasma, .07f, .025f);
                Arc(elbow, palm, ZeriKit.Volt, .06f, .018f);
            }
        }

        /// <summary>The pulse leaves the palm: a ring of light bursts around the hand, facing the throw.</summary>
        public void PulseThrown(Vector3 at, Vector3 direction)
        {
            ZeriRing.Create(player, at + direction * .2f, direction, .12f, ZeriKit.Volt, .02f, .18f);
            ZeriSprite.Create(player, at + direction * .15f, "ZeriFx_Glow", new Color(.75f, 1f, .5f, .6f), .1f, .25f, .12f);
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

        /// <summary>
        /// The wall the pulse hit glows, and the laser's whole hit area shows until it fires: a faint band as wide as the
        /// laser hits, outlined at both edges, with a line down the middle (all visible through walls).
        /// </summary>
        public void LaserTelegraph(Vector3 wall, Vector3 normal, Vector3 end, float seconds)
        {
            Vector3 side = Vector3.Cross(Vector3.up, Geo.FlatDirection(end - wall, Vector3.forward)).normalized * ZeriKit.LaserHalfWidth;
            float width = ZeriKit.LaserHalfWidth * 2;
            ZeriBeam.Create(player, wall, end, new Color(1f, .95f, .45f, .1f), width, width, seconds, 0, xray: true);
            ZeriBeam.Create(player, wall + side, end + side, new Color(1f, .95f, .45f, .55f), .07f, .07f, seconds, 0, xray: true);
            ZeriBeam.Create(player, wall - side, end - side, new Color(1f, .95f, .45f, .55f), .07f, .07f, seconds, 0, xray: true);
            ZeriBeam.Create(player, wall, end, new Color(1f, .95f, .45f, .45f), .06f, .06f, seconds, 0, xray: true);
            ZeriRing.Create(player, wall + normal * .03f, normal, .5f, ZeriKit.Volt, .06f, seconds, pulse: true);
            Impact(wall, ZeriKit.Volt, 10);
        }

        /// <summary>
        /// The laser itself, as wide as it hits: a translucent green-yellow band with bright crackling edges, a golden core
        /// and a pale centre. Saturated rather than white, so it reads clearly without blinding (drawn through walls).
        /// </summary>
        public void Laser(Vector3 from, Vector3 to)
        {
            Vector3 side = Vector3.Cross(Vector3.up, Geo.FlatDirection(to - from, Vector3.forward)).normalized * ZeriKit.LaserHalfWidth;
            float width = ZeriKit.LaserHalfWidth * 2;
            ZeriBeam.Create(player, from, to, new Color(.62f, 1f, .38f, .22f), width, width * .9f, .45f, 0, xray: true, texture: "ZeriFx_BeamFringe");
            ZeriBeam.Create(player, from + side, to + side, new Color(.8f, 1f, .45f, .75f), .14f, .1f, .42f, 0, xray: true);
            ZeriBeam.Create(player, from - side, to - side, new Color(.8f, 1f, .45f, .75f), .14f, .1f, .42f, 0, xray: true);
            ZeriBeam.Create(player, from, to, new Color(1f, .82f, .3f, .55f), 1.1f, .8f, .4f, 0, xray: true, texture: "ZeriFx_BeamCore");
            ZeriBeam.Create(player, from, to, new Color(1f, .95f, .65f, .55f), .3f, .2f, .32f, 0, xray: true, texture: "ZeriFx_BeamCore");
            ZeriArc.Create(player, from, to, ZeriKit.Volt, .6f, .4f, textured: true);
            ZeriArc.Create(player, from + side, to + side, ZeriKit.Spark, .3f, .35f, textured: true);
            ZeriArc.Create(player, from - side, to - side, ZeriKit.Spark, .3f, .35f, textured: true);
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

        /// <summary>
        /// One of Zeri's textures by name, or any champion's by resource path ("GwenFx/GwenFx_Flare"): the sprite, beam
        /// and glow helpers here are shared with other kits.
        /// </summary>
        public static Texture2D Tex(string name)
        {
            if (!textures.TryGetValue(name, out var texture) || !texture)
                textures[name] = texture = Resources.Load<Texture2D>(name.Contains('/') ? name : "ZeriFx/" + name);
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

        /// <summary>Turn about its facing axis in degrees; for a flat sprite, the compass heading its top points to.</summary>
        public float Roll { set => roll = value; }

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
            // A ball of her electricity: a green-gold core in a blue halo (not a white blob), wide enough to follow.
            ZeriFx.Sphere("Pulse core", new Color(.85f, 1f, .5f, .7f), .22f, go.transform);
            ZeriFx.Sphere("Pulse halo", new Color(.45f, .85f, 1f, .25f), .55f, go.transform);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = ZeriFx.Glow(new Color(.62f, 1f, .38f, .5f));
            trail.time = .3f;
            trail.startWidth = .35f;
            trail.endWidth = 0;
            trail.minVertexDistance = .05f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AbilityFx.Motes(go.transform, ZeriKit.Volt, .03f, .2f, 60, ParticleSystemShapeType.Sphere, .12f);
            // Two sparks orbit it, leaving a double helix of light along its path.
            for (int i = 0; i < 2; i++)
            {
                var orbiter = ZeriFx.Sphere("Pulse orbiter", ZeriKit.Volt, .06f, go.transform);
                var helix = orbiter.AddComponent<TrailRenderer>();
                helix.sharedMaterial = ZeriFx.Glow(new Color(1f, .93f, .4f, .7f));
                helix.time = .25f;
                helix.startWidth = .05f;
                helix.endWidth = 0;
                helix.minVertexDistance = .03f;
                helix.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var pulse = go.AddComponent<ZeriPulse>();
            pulse.kit = kit;
            pulse.owner = owner;
            pulse.origin = origin;
            pulse.direction = direction.normalized;
            go.transform.localScale = Vector3.one * .3f;
            trail.widthMultiplier = .3f;
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
            foreach (var target in owner.EnemiesAround((from + to) * .5f, step * .5f + Radius + 1.05f))
            {
                Vector3 rel = target.AimPosition - from;
                float along = Vector3.Dot(rel, direction);
                if (along < -Radius || along > bestAlong)
                    continue;
                if ((rel - direction * along).magnitude <= .25f + .3f * transform.localScale.x)
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
            // It leaves the palm small and swells to full size over its first 2.5 m, so it never fills the view.
            float grow = Mathf.Lerp(.3f, 1, Mathf.Clamp01(travelled / 2.5f));
            transform.localScale = Vector3.one * grow;
            if (TryGetComponent<TrailRenderer>(out var trail))
                trail.widthMultiplier = grow;
            // The two orbiting sparks circle the line of flight.
            Vector3 across = Vector3.Cross(direction, Mathf.Abs(direction.y) > .9f ? Vector3.right : Vector3.up).normalized;
            Vector3 over = Vector3.Cross(direction, across);
            int k = 0;
            foreach (Transform child in transform)
            {
                if (child.name != "Pulse orbiter")
                    continue;
                float angle = travelled * 2.2f + k * Mathf.PI;
                child.position = transform.position + (across * Mathf.Cos(angle) + over * Mathf.Sin(angle)) * .22f * grow;
                k++;
            }
            if (travelled >= ZeriKit.PulseRange - .01f)
                Destroy(gameObject);
        }
    }
}
