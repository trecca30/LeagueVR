using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen's first-person body. Her own idle and run clips animate the legs and hips, a <see cref="BodyRig"/> turns,
    /// leans and crouches the body with the headset, the arms and fingers follow the controllers, the giant scissors sit
    /// in the right fist and open on every cut, and a full-body shadow grounds her on the Rift.
    /// Poses are applied in LateUpdate and again right before rendering with the freshest tracking data.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public class GwenAvatar : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        public Transform visualRoot, scissorsRoot, bladeA, bladeB;
        public Animation animationPlayer;
        [Tooltip("Gwen's complete mesh (with head) used only to cast her shadow.")]
        public Mesh shadowSource;
        public string idleClip = "Idle.anm", runClip = "Run.anm";

        [Header("Scissors")]
        [Range(.4f, 1.2f)] public float scissorsScale = .6f;
        [Tooltip("Point on the scissors (model space) that sits in the fist.")]
        public Vector3 scissorsHandle = new(0, -.06f, -.40f);
        [Tooltip("Blade angle relative to the grip (whose forward runs up the handle): pitch, yaw, roll.")]
        public Vector3 scissorsTilt = new(12, 0, 0);

        public BodyRig Rig { get; private set; }

        Quaternion bladeARest, bladeBRest;
        float snipUntil;
        Vector3 lastOrigin, locomotion;
        float lastYaw, rigTurn;
        bool haveOrigin, subscribed;
        TrailRenderer trail;
        Transform tip;
        SkinnedMeshRenderer skin;
        Renderer[] bodyRenderers;

        void Awake()
        {
            skin = visualRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (animationPlayer)
            {
                animationPlayer.enabled = true;
                animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
                animationPlayer.playAutomatically = false;
                foreach (var clip in new[] { idleClip, runClip })
                    if (animationPlayer[clip] != null)
                    {
                        var state = animationPlayer[clip];
                        state.wrapMode = WrapMode.Loop;
                        state.layer = 0;
                        state.enabled = true;
                        state.weight = clip == idleClip ? 1 : 0;
                    }
                animationPlayer.Sample();
            }
            Rig = new BodyRig(visualRoot, skin);
            bladeARest = bladeA ? bladeA.localRotation : Quaternion.identity;
            bladeBRest = bladeB ? bladeB.localRotation : Quaternion.identity;
            bodyRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            CreateShadow();
            CreateTrail();
            foreach (var r in scissorsRoot.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void CreateShadow()
        {
            if (!shadowSource || !skin)
                return;
            // Only the body submesh casts the shadow; the model's own copies of the scissors, needle and doll stay hidden.
            var mesh = Instantiate(shadowSource);
            mesh.name = "Gwen shadow body";
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (i != mesh.subMeshCount - 1)
                    mesh.SetTriangles(System.Array.Empty<int>(), i);
            var go = new GameObject("Gwen body shadow");
            go.transform.SetParent(skin.transform.parent, false);
            go.transform.SetLocalPositionAndRotation(skin.transform.localPosition, skin.transform.localRotation);
            var shadow = go.AddComponent<SkinnedMeshRenderer>();
            shadow.sharedMesh = mesh;
            shadow.bones = skin.bones;
            shadow.rootBone = skin.rootBone;
            shadow.sharedMaterials = skin.sharedMaterials;
            shadow.updateWhenOffscreen = true;
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            shadow.receiveShadows = false;
            // The first-person mesh has no head, so it stops casting and the complete shadow takes over.
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.updateWhenOffscreen = true;
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

        void OnEnable()
        {
            player.Cast += OnCast;
            player.WeaponPose = WeaponPose;
            if (!subscribed)
            {
                InputSystem.onAfterUpdate += OnAfterInputUpdate;
                subscribed = true;
            }
        }

        void OnDisable()
        {
            player.Cast -= OnCast;
            if (player.WeaponPose == WeaponPose)
                player.WeaponPose = null;
            if (subscribed)
            {
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
                subscribed = false;
            }
        }

        /// <summary>Shows or hides Gwen's body and scissors when she is (un)equipped.</summary>
        public void SetVisible(bool visible)
        {
            enabled = visible;
            if (visualRoot)
                visualRoot.gameObject.SetActive(visible);
            if (scissorsRoot)
                scissorsRoot.gameObject.SetActive(visible);
        }

        void OnCast(string signal, Vector3 position, Vector3 direction)
        {
            if (signal == "Snip" || signal == "Attack1")
                snipUntil = Time.time + .14f;
        }

        // ---------- Poses ----------

        /// <summary>The scissors' pose in the right fist; melee sweeps start here.</summary>
        Pose WeaponPose()
        {
            var grip = RightGrip();
            return new Pose(grip.position, grip.rotation * Quaternion.Euler(scissorsTilt));
        }

        Pose Head()
        {
            var camera = player.head.transform;
            if (!player.DesktopMode)
            {
                // Read the headset directly so the body uses the same input update as the controllers.
                var hmd = InputSystem.GetDevice<XRHMD>();
                if (hmd != null && hmd.isTracked.isPressed && camera.parent)
                    return new Pose(camera.parent.TransformPoint(hmd.centerEyePosition.ReadValue()), camera.parent.rotation * hmd.centerEyeRotation.ReadValue());
            }
            return new Pose(camera.position, camera.rotation);
        }

        Pose RightGrip()
        {
            if (player.DesktopMode)
            {
                var head = player.head.transform;
                return new Pose(head.TransformPoint(new Vector3(.22f, -.32f, .38f)), XRPoses.GripFromAim(head.rotation * Quaternion.Euler(10, -8, 0)));
            }
            return XRPoses.Grip(player, false);
        }

        Pose LeftGrip()
        {
            if (player.DesktopMode)
            {
                var head = player.head.transform;
                return new Pose(head.TransformPoint(new Vector3(-.24f, -.36f, .30f)), XRPoses.GripFromAim(head.rotation * Quaternion.Euler(20, 12, 0)));
            }
            return XRPoses.Grip(player, true);
        }

        static HandCurl ReadCurl(XRNode node)
        {
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return new HandCurl(.15f, .2f, .2f);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigger);
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float grip);
            bool thumb = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryTouch, out bool a) && a
                || device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryTouch, out bool b) && b
                || device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisTouch, out bool s) && s;
            return new HandCurl(trigger, grip, thumb ? .85f : .3f);
        }

        BodyFrame Frame()
        {
            bool armed = scissorsRoot && scissorsRoot.gameObject.activeInHierarchy;
            var right = player.DesktopMode ? new HandCurl(.55f, .9f, .7f) : ReadCurl(XRNode.RightHand);
            if (armed)
                right = right.AtLeast(new HandCurl(.55f, .92f, .75f));
            var left = player.DesktopMode ? new HandCurl(.15f, .25f, .2f) : ReadCurl(XRNode.LeftHand);
            return new BodyFrame
            {
                head = Head(),
                leftGrip = LeftGrip(),
                rightGrip = RightGrip(),
                floorY = player.origin.transform.position.y,
                deltaTime = Time.deltaTime,
                locomotion = locomotion,
                rigTurn = rigTurn,
                leftCurl = left,
                rightCurl = right,
            };
        }

        void LateUpdate()
        {
            if (Rig == null || !Rig.Valid)
                return;
            TrackLocomotion();
            DriveAnimation();
            Rig.CaptureAnimatedPose();
            Rig.Solve(Frame(), true);
            PoseScissors();
            bool alive = player.Health && player.Health.IsAlive;
            foreach (var r in bodyRenderers)
                if (r)
                    r.forceRenderingOff = !alive;
        }

        void OnAfterInputUpdate()
        {
            // Re-solve with the poses the frame will actually be rendered with: hands can never lag the head.
            if (InputState.currentUpdateType != InputUpdateType.BeforeRender || !isActiveAndEnabled || player.DesktopMode || Rig == null || !Rig.Valid)
                return;
            Rig.Solve(Frame(), false);
            PoseScissors();
        }

        void TrackLocomotion()
        {
            Vector3 origin = player.origin.transform.position;
            float yaw = player.origin.transform.eulerAngles.y;
            rigTurn = 0;
            if (haveOrigin && Time.deltaTime > 0)
            {
                Vector3 step = Geo.Flat(origin - lastOrigin);
                rigTurn = Mathf.DeltaAngle(lastYaw, yaw);
                // Teleports, blinks, respawns and snap turns (which swing the rig around the head) are not walking.
                Vector3 velocity = step.magnitude > .6f || Mathf.Abs(rigTurn) > 5 ? Vector3.zero : step / Time.deltaTime;
                locomotion = Vector3.Lerp(locomotion, velocity, 1 - Mathf.Exp(-10 * Time.deltaTime));
            }
            lastOrigin = origin;
            lastYaw = yaw;
            haveOrigin = true;
        }

        void DriveAnimation()
        {
            if (!animationPlayer)
                return;
            var idle = animationPlayer[idleClip];
            var run = animationPlayer[runClip];
            if (idle == null || run == null)
                return;
            float speed = locomotion.magnitude;
            float runWeight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1.6f, speed));
            idle.enabled = run.enabled = true;
            idle.weight = 1 - runWeight;
            run.weight = runWeight;
            run.speed = Mathf.Clamp(speed / 3.2f, .55f, 1.6f);
        }

        void PoseScissors()
        {
            if (!scissorsRoot || !scissorsRoot.gameObject.activeInHierarchy)
            {
                if (trail)
                    trail.emitting = false;
                return;
            }
            var weapon = WeaponPose();
            scissorsRoot.localScale = Vector3.one * scissorsScale * Rig.Scale;
            scissorsRoot.SetPositionAndRotation(weapon.position - weapon.rotation * (scissorsHandle * scissorsScale * Rig.Scale), weapon.rotation);
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
