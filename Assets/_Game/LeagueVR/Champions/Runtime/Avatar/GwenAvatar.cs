using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen's first-person body: the shared full-body rig (see <see cref="ChampionBodyBase"/>) on Gwen's scene model,
    /// with her giant scissors in the right fist that open on every cut, snip-stack beads, empowered blade glows and
    /// Needlework needles readied between the fingers of the left hand.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public class GwenAvatar : ChampionBodyBase
    {
        public Transform visualRoot, scissorsRoot, bladeA, bladeB;
        public Animation animationPlayer;
        [Tooltip("Gwen's complete mesh (with head) used only to cast her shadow.")]
        public Mesh shadowSource;
        public string idleClip = "Idle.anm";

        [Header("Scissors")]
        [Range(.4f, 1.2f)] public float scissorsScale = .6f;
        [Tooltip("Point on the scissors (model space) that sits in the fist.")]
        public Vector3 scissorsHandle = new(0, -.06f, -.40f);
        [Tooltip("Blade angle relative to the grip (whose forward runs up the handle): pitch, yaw, roll.")]
        public Vector3 scissorsTilt = new(12, 0, 0);

        /// <summary>Distance from the fist to the blade tips in world units.</summary>
        public float BladeLength => (.93f - scissorsHandle.z) * scissorsScale * BodyScale;
        public float WeaponScale => scissorsScale * BodyScale;
        /// <summary>The scissors are in the hand (not stowed for a menu or a held item).</summary>
        public bool WeaponReady => scissorsRoot && scissorsRoot.gameObject.activeInHierarchy;

        protected override bool Ready => visualRoot && visualRoot.gameObject.activeInHierarchy;

        Quaternion bladeARest, bladeBRest;
        float snipUntil;
        TrailRenderer trail;
        Transform tip;
        readonly GameObject[] beads = new GameObject[4];
        readonly System.Collections.Generic.List<GameObject> glows = new();
        readonly GameObject[] heldNeedles = new GameObject[5];

        void Awake()
        {
            Skin = visualRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SetupAnimation(animationPlayer, idleClip);
            Rig = new BodyRig(visualRoot, Skin);
            bladeARest = bladeA ? bladeA.localRotation : Quaternion.identity;
            bladeBRest = bladeB ? bladeB.localRotation : Quaternion.identity;
            bodyRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            // Only the body submesh casts the shadow; the model's own copies of the scissors, needle and doll stay hidden.
            int body = shadowSource ? shadowSource.subMeshCount - 1 : 0;
            CreateShadow(shadowSource, Skin ? Skin.sharedMaterials : null, (i, _) => i == body);
            CreateTrail();
            CreateKitVisuals();
            foreach (var r in scissorsRoot.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void CreateTrail()
        {
            tip = new GameObject("Scissors tip").transform;
            tip.SetParent(scissorsRoot, false);
            tip.localPosition = new Vector3(0, 0, .9f);
            trail = tip.gameObject.AddComponent<TrailRenderer>();
            trail.time = .14f;
            trail.minVertexDistance = .03f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, .09f), new Keyframe(1, 0));
            var cyan = new Color(.45f, .95f, 1f, .85f);
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(cyan, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(0, 1) },
            };
            trail.sharedMaterial = AbilityFx.Material(cyan);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
        }

        void CreateKitVisuals()
        {
            var cyan = new Color(.45f, .95f, 1f, .9f);
            // Snip stacks: glowing beads along the blade spine.
            for (int i = 0; i < beads.Length; i++)
            {
                var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(bead.GetComponent<Collider>());
                bead.name = "Snip stack " + (i + 1);
                bead.transform.SetParent(scissorsRoot, false);
                bead.transform.localPosition = new Vector3(0, .1f, .1f + i * .09f);
                bead.transform.localScale = Vector3.one * .05f;
                var r = bead.GetComponent<Renderer>();
                r.sharedMaterial = AbilityFx.Glass(cyan, true);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bead.SetActive(false);
                beads[i] = bead;
            }
            // Skip 'n Slash: a glowing shell around both blades.
            foreach (var blade in new[] { bladeA, bladeB })
            {
                if (!blade || !blade.TryGetComponent<MeshFilter>(out var filter))
                    continue;
                var glow = new GameObject("Empowered glow");
                glow.transform.SetParent(blade, false);
                glow.transform.localScale = Vector3.one * 1.03f;
                glow.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var r = glow.AddComponent<MeshRenderer>();
                r.sharedMaterial = AbilityFx.Glass(new Color(.5f, 1f, 1f, .35f), true);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glow.SetActive(false);
                glows.Add(glow);
            }
        }

        /// <summary>Weapon tells driven by the kit: snip stacks, empowered blades and needles readied in the left hand.</summary>
        void UpdateKitVisuals()
        {
            var kit = player.Kit as GwenKit;
            int stacks = kit != null ? kit.QStacks : 0;
            for (int i = 0; i < beads.Length; i++)
                if (beads[i])
                    beads[i].SetActive(i < stacks);
            bool empowered = kit != null && kit.Empowered;
            foreach (var g in glows)
                if (g)
                    g.SetActive(empowered);
            int needles = kit != null ? kit.NeedlesReady : 0;
            var prefab = player.Definition ? player.Definition.kitPrefab : null;
            var grip = LeftGrip();
            Vector3 palm = Palm(true);
            // Needles fan out from between the fingers, points forward along the hand.
            Quaternion aim = XRPoses.AimFromGrip(grip.rotation);
            for (int i = 0; i < heldNeedles.Length; i++)
            {
                bool show = i < needles;
                if (show && !heldNeedles[i] && prefab)
                {
                    heldNeedles[i] = Instantiate(prefab);
                    heldNeedles[i].name = "Readied needle";
                }
                if (!heldNeedles[i])
                    continue;
                heldNeedles[i].SetActive(show);
                if (!show)
                    continue;
                int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                var rotation = aim * Quaternion.Euler(0, side * 9, 0);
                heldNeedles[i].transform.SetPositionAndRotation(palm + rotation * new Vector3(0, .015f * side, .05f), rotation);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            player.Cast += OnCast;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            player.Cast -= OnCast;
            if (player.Body == this)
                player.Body = null;
        }

        /// <summary>Shows or hides Gwen's body and scissors when she is (un)equipped.</summary>
        public void SetVisible(bool visible)
        {
            enabled = visible;
            if (visualRoot)
                visualRoot.gameObject.SetActive(visible);
            if (scissorsRoot)
                scissorsRoot.gameObject.SetActive(visible);
            foreach (var needle in heldNeedles)
                if (needle)
                    needle.SetActive(false);
        }

        void OnCast(string signal, Vector3 position, Vector3 direction)
        {
            if (signal == "Snip" || signal == "Attack1")
                snipUntil = Time.time + .14f;
        }

        /// <summary>The scissors' pose in the right fist; melee sweeps start here.</summary>
        public override Pose WeaponPose()
        {
            var grip = RightGrip();
            return new Pose(grip.position, grip.rotation * Quaternion.Euler(scissorsTilt));
        }

        protected override HandCurl HeldCurl(bool left) => !left && WeaponReady ? new HandCurl(.55f, .92f, .75f) : default;

        protected override void AfterSolve()
        {
            PoseScissors();
            UpdateKitVisuals();
        }

        void PoseScissors()
        {
            if (!scissorsRoot)
                return;
            // A menu or a held item puts the scissors away.
            bool show = enabled && !player.HandStowed(false);
            if (scissorsRoot.gameObject.activeSelf != show)
                scissorsRoot.gameObject.SetActive(show);
            if (!show)
            {
                if (trail)
                    trail.emitting = false;
                return;
            }
            var weapon = WeaponPose();
            scissorsRoot.localScale = Vector3.one * scissorsScale * BodyScale;
            scissorsRoot.SetPositionAndRotation(weapon.position - weapon.rotation * (scissorsHandle * scissorsScale * BodyScale), weapon.rotation);
            float open = Time.time < snipUntil ? Mathf.Sin((snipUntil - Time.time) / .14f * Mathf.PI) * 14 : 2;
            if (bladeA)
                bladeA.localRotation = bladeARest * Quaternion.AngleAxis(open, Vector3.right);
            if (bladeB)
                bladeB.localRotation = bladeBRest * Quaternion.AngleAxis(-open, Vector3.right);
            if (trail)
                trail.emitting = Time.time < snipUntil + .1f;
        }
    }
}
