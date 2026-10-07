using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Shared first-person body for every champion: a still frame of the champion's idle is the base pose, a
    /// <see cref="BodyRig"/> turns, leans and crouches the body with the headset, steps the feet, makes the arms and
    /// fingers follow the controllers, and a complete shadow (head included) grounds the player. Subclasses add
    /// weapons and kit tells.
    /// Poses are applied in LateUpdate and again in the Input System's before-render update with the freshest tracking.
    /// </summary>
    public abstract class ChampionBodyBase : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;

        public BodyRig Rig { get; protected set; }
        public SkinnedMeshRenderer Skin { get; protected set; }

        /// <summary>Raised after every pose update (LateUpdate and before render) so kits can place held effects.</summary>
        public event Action Posed;

        protected Animation clips;
        protected AnimationState idleState;
        protected Renderer[] bodyRenderers = Array.Empty<Renderer>();
        protected GameObject shadowObject;

        Vector3 lastOrigin, locomotion;
        float lastYaw, rigTurn;
        bool haveOrigin, subscribed;

        /// <summary>True when the body exists and should be posed.</summary>
        protected abstract bool Ready { get; }

        /// <summary>The right-hand weapon's pose (forward along the weapon), or the right palm's aim for empty hands.</summary>
        public abstract Pose WeaponPose();

        Func<Pose> weaponPoseGetter;

        /// <summary>A cached delegate to <see cref="WeaponPose"/> for <see cref="PlayerChampion.WeaponPose"/>.</summary>
        public Func<Pose> WeaponPoseGetter => weaponPoseGetter ??= WeaponPose;

        protected virtual void OnEnable()
        {
            if (!subscribed)
            {
                InputSystem.onAfterUpdate += OnAfterInputUpdate;
                subscribed = true;
            }
            haveOrigin = false;
        }

        protected virtual void OnDisable()
        {
            if (subscribed)
            {
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
                subscribed = false;
            }
        }

        /// <summary>
        /// Holds the champion's idle on its first frame as the body's base pose. Nothing plays on its own: run cycles bob
        /// the hips, which looks like a glitch from inside the body. The rig moves the spine, arms and fingers, and the
        /// legs step procedurally (see <see cref="BodyRig"/>).
        /// </summary>
        protected void SetupAnimation(Animation animation, string idle)
        {
            clips = animation;
            idleState = null;
            if (!clips)
                return;
            clips.enabled = true;
            clips.cullingType = AnimationCullingType.AlwaysAnimate;
            clips.playAutomatically = false;
            clips.Stop();
            idleState = FindClip(idle, "Idle1_Base", "Idle1", "Idle.anm", "Idle");
            if (idleState != null)
            {
                idleState.wrapMode = WrapMode.ClampForever;
                idleState.layer = 0;
                idleState.enabled = true;
                idleState.weight = 1;
                idleState.time = 0;
                idleState.speed = 0;
            }
            clips.Sample();
        }

        AnimationState FindClip(string preferred, params string[] candidates)
        {
            if (!string.IsNullOrEmpty(preferred) && clips[preferred] != null)
                return clips[preferred];
            foreach (var name in candidates)
                if (clips[name] != null)
                    return clips[name];
            // Otherwise the first plain loop whose name starts with the last candidate ("Idle", "Run").
            string stem = candidates[candidates.Length - 1];
            foreach (AnimationState s in clips)
                if (s.name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && s.name.IndexOf("_to", StringComparison.OrdinalIgnoreCase) < 0)
                    return s;
            return null;
        }

        /// <summary>
        /// Adds a shadow-only copy of the champion's complete mesh (head included). Submeshes rejected by
        /// <paramref name="keepSubmesh"/> (props for other skins, VFX parts) are emptied. The visible first-person mesh
        /// stops casting, so the player sees one whole shadow.
        /// </summary>
        protected void CreateShadow(Mesh source, Material[] materials, Func<int, string, bool> keepSubmesh)
        {
            if (!source || !Skin)
                return;
            var mesh = Instantiate(source);
            mesh.name = source.name + " shadow";
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                string materialName = materials != null && i < materials.Length && materials[i] ? materials[i].name : "";
                if (!keepSubmesh(i, materialName))
                    mesh.SetTriangles(Array.Empty<int>(), i);
            }
            shadowObject = new GameObject("Champion body shadow");
            shadowObject.transform.SetParent(Skin.transform.parent, false);
            shadowObject.transform.SetLocalPositionAndRotation(Skin.transform.localPosition, Skin.transform.localRotation);
            var shadow = shadowObject.AddComponent<SkinnedMeshRenderer>();
            shadow.sharedMesh = mesh;
            shadow.bones = Skin.bones;
            shadow.rootBone = Skin.rootBone;
            shadow.sharedMaterials = materials ?? Skin.sharedMaterials;
            shadow.updateWhenOffscreen = true;
            shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            shadow.receiveShadows = false;
            Skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Skin.updateWhenOffscreen = true;
        }

        /// <summary>
        /// For a camera outside the head (the stream's shoulder view): shows the complete champion, head included,
        /// instead of the headless first-person mesh. Call again with false after that camera has rendered.
        /// </summary>
        public void ShowFullBody(bool full)
        {
            if (!Skin || !shadowObject || !shadowObject.TryGetComponent<SkinnedMeshRenderer>(out var shadow))
                return;
            if (full)
            {
                // The first-person mesh is hidden while the champion is dead; so is the full body.
                fullBodyShown = !Skin.forceRenderingOff;
                if (!fullBodyShown)
                    return;
                Skin.forceRenderingOff = true;
                shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else if (fullBodyShown)
            {
                fullBodyShown = false;
                Skin.forceRenderingOff = false;
                shadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        bool fullBodyShown;

        /// <summary>A snapshot of the visible body as a mesh in world space units (afterimages, ghosts).</summary>
        public Mesh BakeBody()
        {
            if (!Skin)
                return null;
            var mesh = new Mesh { name = "Champion body snapshot" };
            Skin.BakeMesh(mesh, true);
            return mesh;
        }

        // ---------- Poses ----------

        protected Pose Head()
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

        public Pose RightGrip()
        {
            if (player.DesktopMode)
            {
                var head = player.head.transform;
                return new Pose(head.TransformPoint(new Vector3(.22f, -.32f, .38f)), XRPoses.GripFromAim(head.rotation * Quaternion.Euler(10, -8, 0)));
            }
            return XRPoses.Grip(player, false);
        }

        public Pose LeftGrip()
        {
            if (player.DesktopMode)
            {
                var head = player.head.transform;
                return new Pose(head.TransformPoint(new Vector3(-.24f, -.36f, .30f)), XRPoses.GripFromAim(head.rotation * Quaternion.Euler(20, 12, 0)));
            }
            return XRPoses.Grip(player, true);
        }

        public Pose Grip(bool left) => left ? LeftGrip() : RightGrip();

        /// <summary>Where a hand points, starting at the palm: the natural ray for aiming spells with an open hand.</summary>
        public Pose Aim(bool left)
        {
            var grip = Grip(left);
            return new Pose(Palm(left), XRPoses.AimFromGrip(grip.rotation));
        }

        /// <summary>World position of the centre of a palm on the posed body (where held effects sit).</summary>
        public Vector3 Palm(bool left) => Rig != null && Rig.Valid ? Rig.Palm(left) : Grip(left).position;

        /// <summary>World size of one body unit (the rig grows or shrinks the champion to the player's height).</summary>
        public float BodyScale => Rig != null ? Rig.Scale : 1;

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

        /// <summary>Minimum finger closure while a hand holds something (weapon handle, shield grip).</summary>
        protected virtual HandCurl HeldCurl(bool left) => default;

        protected BodyFrame Frame()
        {
            var right = player.DesktopMode ? new HandCurl(.3f, .4f, .4f) : ReadCurl(XRNode.RightHand);
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
                leftCurl = left.AtLeast(HeldCurl(true)),
                rightCurl = right.AtLeast(HeldCurl(false)),
            };
        }

        /// <summary>Weapons and kit tells, after the body is posed (called twice per frame).</summary>
        protected virtual void AfterSolve() { }

        protected virtual void LateUpdate()
        {
            if (!Ready || Rig == null || !Rig.Valid)
                return;
            TrackLocomotion();
            Rig.CaptureAnimatedPose();
            Rig.Solve(Frame(), true);
            AfterSolve();
            Posed?.Invoke();
            bool alive = player.Health && player.Health.IsAlive;
            foreach (var r in bodyRenderers)
                if (r)
                    r.forceRenderingOff = !alive;
        }

        void OnAfterInputUpdate()
        {
            // Re-solve with the poses the frame will actually be rendered with: hands can never lag the head.
            if (InputState.currentUpdateType != InputUpdateType.BeforeRender || !isActiveAndEnabled || player.DesktopMode || !Ready || Rig == null || !Rig.Valid)
                return;
            Rig.Solve(Frame(), false);
            AfterSolve();
            Posed?.Invoke();
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

    }
}
