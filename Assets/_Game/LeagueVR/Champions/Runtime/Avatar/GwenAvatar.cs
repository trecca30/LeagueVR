using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
namespace LeagueVR.Champions
{
    /// <summary>Gwen's first-person body: tracked arms, finger curl and the animated scissors.</summary>
    [DefaultExecutionOrder(220)]
    public class GwenAvatar : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        public Transform visualRoot, scissorsRoot, bladeA, bladeB, leftUpper, leftLower, leftPalm, rightUpper, rightLower, rightPalm;
        public Animation animationPlayer;
        public GwenHandPoses handPoses;
        [Range(1.2f, 2.1f)] public float avatarHeight = 1.65f;
        public Vector3 leftGripOffset = new(0, -.025f, -.015f), rightGripOffset = new(0, -.025f, -.015f);
        ArmPose leftPose, rightPose;
        Quaternion leftAlignment, rightAlignment, bladeARest, bladeBRest;
        float scissorUntil;
        bool ready;
        Renderer[] bodyRenderers, weaponRenderers;
        float leftCurl, rightCurl;
        Vector3 lastBodyForward;

        struct ArmPose
        {
            Quaternion upper, lower, palm;
            Vector3 a, b, c;

            public ArmPose(Transform u, Transform l, Transform p)
            {
                upper = u.localRotation;
                lower = l.localRotation;
                palm = p.localRotation;
                a = u.localPosition;
                b = l.localPosition;
                c = p.localPosition;
            }

            public void Restore(Transform u, Transform l, Transform p)
            {
                u.SetLocalPositionAndRotation(a, upper);
                l.SetLocalPositionAndRotation(b, lower);
                p.SetLocalPositionAndRotation(c, palm);
            }
        }
        readonly List<(Transform bone, Quaternion rest, Quaternion holding, bool left)> fingers = new();
        public Quaternion WeaponRotation => player.DesktopMode ? player.head.transform.rotation : XRPoses.Grip(player, false).rotation * (handPoses ? handPoses.weaponGripAlignment : Quaternion.identity);

        void Awake()
        {
            if (animationPlayer && animationPlayer["Idle.anm"] != null)
            {
                animationPlayer.Play("Idle.anm");
                animationPlayer["Idle.anm"].time = 0;
                animationPlayer.Sample();
            }
            // The first-person skeleton has one writer; animations cannot overwrite tracked arms.
            if (animationPlayer)
            {
                animationPlayer.Stop();
                animationPlayer.enabled = false;
            }
            visualRoot.localScale = Vector3.one * (avatarHeight / 1.53f);
            leftPose = new ArmPose(leftUpper, leftLower, leftPalm);
            rightPose = new ArmPose(rightUpper, rightLower, rightPalm);
            leftAlignment = handPoses ? handPoses.leftGripAlignment : GripAlignment(leftPalm);
            rightAlignment = handPoses ? handPoses.rightGripAlignment : GripAlignment(rightPalm);
            bladeARest = bladeA ? bladeA.localRotation : Quaternion.identity;
            bladeBRest = bladeB ? bladeB.localRotation : Quaternion.identity;
            bodyRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            weaponRenderers = scissorsRoot.GetComponentsInChildren<Renderer>(true);
            CacheFingers(leftPalm, true);
            CacheFingers(rightPalm, false);
            ready = true;
        }

        static Quaternion GripAlignment(Transform palm)
        {
            Transform middle = null, index = null, pinky = null;
            foreach (Transform t in palm)
            {
                if (t.name.Contains("Middle1"))
                    middle = t;
                if (t.name.Contains("Index1"))
                    index = t;
                if (t.name.Contains("Pinky1"))
                    pinky = t;
            }
            if (!middle || !index || !pinky)
                return Quaternion.identity;
            Vector3 forward = (middle.position - palm.position).normalized, normal = Vector3.Cross(index.position - pinky.position, forward).normalized;
            if (Vector3.Dot(normal, Vector3.up) < 0)
                normal = -normal;
            return Quaternion.Inverse(Quaternion.LookRotation(forward, normal)) * palm.rotation;
        }

        void CacheFingers(Transform palm, bool left)
        {
            foreach (var t in palm.GetComponentsInChildren<Transform>())
                if (t.name.Contains("Index") || t.name.Contains("Middle") || t.name.Contains("Ring") || t.name.Contains("Pinky") || t.name.Contains("Thumb"))
                {
                    var pose = handPoses && handPoses.joints != null ? System.Array.Find(handPoses.joints, j => j.name == t.name) : default;
                    fingers.Add((t, pose.name != null ? pose.relaxed : t.localRotation, pose.name != null ? pose.holding : t.localRotation, left));
                }
        }

        void OnEnable()
        {
            player.Cast += OnCast;
            player.WeaponPose = WeaponPose;
            Application.onBeforeRender += BeforeRender;
        }

        void OnDisable()
        {
            player.Cast -= OnCast;
            if (player.WeaponPose == WeaponPose)
                player.WeaponPose = null;
            Application.onBeforeRender -= BeforeRender;
        }

        /// <summary>The scissors' grip pose; melee sweeps start at the visible blade.</summary>
        Pose WeaponPose()
        {
            var grip = XRPoses.Grip(player, false);
            return new Pose(grip.position + grip.rotation * rightGripOffset, WeaponRotation);
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

        void OnCast(string kind, Vector3 p, Vector3 d)
        {
            if (kind == "Snip" || kind == "Attack1")
                scissorUntil = Time.time + .12f;
        }

        void LateUpdate()
        {
            UpdateTrackedVisuals();
        }

        [BeforeRenderOrder(150)]
        void BeforeRender()
        {
            if (!player.DesktopMode)
                UpdateTrackedVisuals();
        }

        public void UpdateTrackedVisuals()
        {
            if (!ready)
                return;
            var head = player.head.transform;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            // Avoid a 180 degree body flip when the wearer looks straight up/down.
            Vector3 forward = flat.sqrMagnitude > .04f ? flat.normalized : (lastBodyForward.sqrMagnitude > .01f ? lastBodyForward : visualRoot.forward);
            lastBodyForward = forward;
            visualRoot.rotation = Quaternion.LookRotation(forward);
            // Crouching moves the shoulders with eye height; head tracking itself is never changed.
            visualRoot.position = new Vector3(head.position.x, head.position.y - (avatarHeight - .1f), head.position.z) - forward * .12f;
            Pose rightGripPose = XRPoses.Grip(player, false), leftGripPose = XRPoses.Grip(player, true);
            Quaternion rightRotation = player.DesktopMode ? head.rotation : rightGripPose.rotation, leftRotation = player.DesktopMode ? head.rotation : leftGripPose.rotation;
            Vector3 rightTarget = player.DesktopMode ? head.TransformPoint(new Vector3(.23f, -.30f, .40f)) : rightGripPose.position + rightRotation * rightGripOffset;
            Vector3 leftTarget = player.DesktopMode ? head.TransformPoint(new Vector3(-.23f, -.30f, .36f)) : leftGripPose.position + leftRotation * leftGripOffset;
            leftPose.Restore(leftUpper, leftLower, leftPalm);
            rightPose.Restore(rightUpper, rightLower, rightPalm);
            SolveArm(rightUpper, rightLower, rightPalm, rightTarget, visualRoot.right * .65f - Vector3.up);
            SolveArm(leftUpper, leftLower, leftPalm, leftTarget, -visualRoot.right * .65f - Vector3.up);
            rightPalm.rotation = rightRotation * rightAlignment;
            leftPalm.rotation = leftRotation * leftAlignment;
            float leftGrip = 0, rightGrip = scissorsRoot && scissorsRoot.gameObject.activeSelf ? .55f : 0;
            InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.grip, out leftGrip);
            if (InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(CommonUsages.grip, out float grip))
                rightGrip = Mathf.Max(rightGrip, grip);
            // Only curl is smoothed. Tracking remains immediate and is updated again before rendering.
            float smoothing = 1 - Mathf.Exp(-18 * Time.deltaTime);
            leftCurl = Mathf.Lerp(leftCurl, leftGrip, smoothing);
            rightCurl = Mathf.Lerp(rightCurl, rightGrip, smoothing);
            foreach (var f in fingers)
                f.bone.localRotation = Quaternion.Slerp(f.rest, f.holding, f.left ? leftCurl : rightCurl);
            if (scissorsRoot)
            {
                Quaternion rotation = WeaponRotation;
                scissorsRoot.SetPositionAndRotation(rightTarget, rotation);
                float open = Time.time < scissorUntil ? Mathf.Sin((scissorUntil - Time.time) / .12f * Mathf.PI) * 12 : 2;
                if (bladeA)
                    bladeA.localRotation = bladeARest * Quaternion.AngleAxis(open, Vector3.right);
                if (bladeB)
                    bladeB.localRotation = bladeBRest * Quaternion.AngleAxis(-open, Vector3.right);
            }
            bool tracked = player.DesktopMode || (XRPoses.Tracked(true) && XRPoses.Tracked(false));
            foreach (var r in bodyRenderers)
                if (r)
                    r.forceRenderingOff = !tracked;
            foreach (var r in weaponRenderers)
                if (r)
                    r.forceRenderingOff = !player.DesktopMode && !XRPoses.Tracked(false);
        }

        static void SolveArm(Transform upper, Transform lower, Transform palm, Vector3 target, Vector3 pole)
        {
            if (!upper || !lower || !palm)
                return;
            Vector3 root = upper.position, delta = target - root;
            float a = Vector3.Distance(root, lower.position), b = Vector3.Distance(lower.position, palm.position);
            if (a < .001f || b < .001f || delta.sqrMagnitude < .000001f)
                return;
            float stretch = Mathf.Clamp(delta.magnitude / (a + b) * 1.003f, 1, 1.45f);
            lower.localPosition *= stretch;
            palm.localPosition *= stretch;
            a *= stretch;
            b *= stretch;
            float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 dir = delta.normalized, bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < .0001f)
                bend = Vector3.ProjectOnPlane(Vector3.back, dir);
            if (bend.sqrMagnitude < .0001f)
                bend = Vector3.Cross(dir, Vector3.right);
            float along = (a * a - b * b + d * d) / (2 * d), across = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 elbow = root + dir * along + bend.normalized * across;
            upper.rotation = Quaternion.FromToRotation(lower.position - root, elbow - root) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(palm.position - lower.position, root + dir * d - lower.position) * lower.rotation;
            palm.position = target;
        }
    }
}
