using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>How far each part of a hand is closed, 0 (open) to 1 (fist).</summary>
    public struct HandCurl
    {
        public float index, grip, thumb;

        public HandCurl(float index, float grip, float thumb)
        {
            this.index = index;
            this.grip = grip;
            this.thumb = thumb;
        }

        public HandCurl AtLeast(HandCurl other) => new(Mathf.Max(index, other.index), Mathf.Max(grip, other.grip), Mathf.Max(thumb, other.thumb));
    }

    /// <summary>Everything the body needs for one frame, in world space.</summary>
    public struct BodyFrame
    {
        public Pose head, leftGrip, rightGrip;
        public float floorY, deltaTime;
        /// <summary>Horizontal velocity of the rig from locomotion (stick movement, dashes); used for legs and body yaw.</summary>
        public Vector3 locomotion;
        /// <summary>Degrees the rig itself turned this frame (snap or smooth turn); the body turns with it.</summary>
        public float rigTurn;
        public HandCurl leftCurl, rightCurl;
    }

    /// <summary>
    /// Full-body first-person rig for League champion skeletons. Clips animate the legs and hips; on top of that the
    /// body turns with the player, the spine leans toward the headset, the legs bend to keep the feet on the floor
    /// when crouching, two-bone arms reach the controller grip poses with natural elbows, the wrist twist is shared
    /// with the forearm, and the fingers follow grip and trigger.
    /// </summary>
    public class BodyRig
    {
        public class Limb
        {
            public Transform upper, lower, end;
            public int side;
            public Quaternion handAlign = Quaternion.identity;
            public Vector3 palmLocal;
            public float endHeight;
        }

        class Finger
        {
            public Transform[] joints;
            public Quaternion[] bind;
            public Vector3[] axes;
            public float[] maxAngles;
            public float[] relaxed;
            public int hand; // 0 left, 1 right
            public int kind; // 0 thumb, 1 index, 2 other
        }

        // OpenXR grip pose in Unity space: +Z runs up the handle from little finger to thumb, +X is the palm normal
        // (into the right palm, out of the left), +Y points back toward the wrist. A hand closed on the handle therefore
        // has its knuckles pointing along -Y and its knuckle line (pinky -> index) along +Z.
        static readonly Quaternion GripFrame = Quaternion.LookRotation(new Vector3(0, -1, .12f).normalized, Vector3.forward);

        public readonly Transform root;
        public Limb LeftArm { get; }
        public Limb RightArm { get; }
        public Limb LeftLeg { get; }
        public Limb RightLeg { get; }
        public float Scale { get; private set; } = 1;
        public float BodyYaw => bodyYaw;
        public bool Valid { get; }

        readonly Transform hips, spine1, spine2, chest, neck, leftClavicle, rightClavicle;
        readonly List<Finger> fingers = new();
        readonly Transform[] driven;
        readonly Quaternion[] capturedRot;
        readonly Vector3[] capturedPos;
        readonly Vector3 neckRest;
        readonly float eyeRest;

        float bodyYaw, standingEye = 1.55f;
        Vector3 rootFlat;
        bool initialised;

        public BodyRig(Transform root, SkinnedMeshRenderer skin)
        {
            this.root = root;
            Transform Bone(params string[] names)
            {
                foreach (var n in names)
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase))
                            return t;
                return null;
            }
            hips = Bone("Pelvis", "Root");
            spine1 = Bone("Spine1", "Spine");
            spine2 = Bone("Spine2");
            chest = Bone("Chest", "Spine3");
            neck = Bone("Neck");
            leftClavicle = Bone("L_Clavicle");
            rightClavicle = Bone("R_Clavicle");
            LeftArm = new Limb { upper = Bone("L_Shoulder", "L_Uparm"), lower = Bone("L_Elbow", "L_Forearm"), end = Bone("L_Hand"), side = -1 };
            RightArm = new Limb { upper = Bone("R_Shoulder", "R_Uparm"), lower = Bone("R_Elbow", "R_Forearm"), end = Bone("R_Hand"), side = 1 };
            LeftLeg = new Limb { upper = Bone("L_Hip", "L_Thigh"), lower = Bone("L_KneeUpper", "L_Knee", "L_Calf"), end = Bone("L_Foot", "L_Ankle"), side = -1 };
            RightLeg = new Limb { upper = Bone("R_Hip", "R_Thigh"), lower = Bone("R_KneeUpper", "R_Knee", "R_Calf"), end = Bone("R_Foot", "R_Ankle"), side = 1 };
            Valid = spine1 && neck && LeftArm.upper && LeftArm.lower && LeftArm.end && RightArm.upper && RightArm.lower && RightArm.end;
            if (!Valid)
                return;

            neckRest = root.InverseTransformPoint(neck.position);
            eyeRest = neckRest.y + .15f;
            if (LeftLeg.end)
                LeftLeg.endHeight = root.InverseTransformPoint(LeftLeg.end.position).y;
            if (RightLeg.end)
                RightLeg.endHeight = root.InverseTransformPoint(RightLeg.end.position).y;

            var bindWorld = BindPose(skin);
            SetupHand(LeftArm, Bone, bindWorld, 0, "L_");
            SetupHand(RightArm, Bone, bindWorld, 1, "R_");

            var list = new List<Transform> { hips, spine1, spine2, chest, neck, leftClavicle, rightClavicle };
            foreach (var limb in new[] { LeftArm, RightArm, LeftLeg, RightLeg })
                list.AddRange(new[] { limb.upper, limb.lower, limb.end });
            foreach (var f in fingers)
                list.AddRange(f.joints);
            list.RemoveAll(t => !t);
            driven = list.ToArray();
            capturedRot = new Quaternion[driven.Length];
            capturedPos = new Vector3[driven.Length];
        }

        /// <summary>World-space bind pose of every skinned bone (mesh space mapped through the root).</summary>
        static Dictionary<Transform, Matrix4x4> BindPose(SkinnedMeshRenderer skin)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            if (!skin || !skin.sharedMesh)
                return result;
            var binds = skin.sharedMesh.bindposes;
            var bones = skin.bones;
            for (int i = 0; i < Mathf.Min(binds.Length, bones.Length); i++)
                if (bones[i])
                    result[bones[i]] = binds[i].inverse;
            return result;
        }

        void SetupHand(Limb arm, Func<string[], Transform> bone, Dictionary<Transform, Matrix4x4> bind, int handIndex, string prefix)
        {
            Transform Find(string name) => bone(new[] { prefix + name });
            var hand = arm.end;
            var middle = Find("Middle1");
            var index = Find("Index1");
            var pinky = Find("Pinky1");
            if (!middle || !index || !pinky)
                return;
            // Palm frame from the current pose: metacarpals toward the middle knuckle, knuckle line pinky -> index.
            Vector3 f = (middle.position - hand.position).normalized;
            Vector3 u = Vector3.ProjectOnPlane(index.position - pinky.position, f).normalized;
            Vector3 fLocal = Quaternion.Inverse(hand.rotation) * f, uLocal = Quaternion.Inverse(hand.rotation) * u;
            arm.handAlign = Quaternion.Inverse(Quaternion.LookRotation(fLocal, uLocal));
            Vector3 palmNormal = (handIndex == 1 ? 1 : -1) * Vector3.Cross(f, u).normalized;
            Vector3 palmCentre = Vector3.Lerp(hand.position, middle.position, .55f) + palmNormal * .02f * hand.lossyScale.x;
            arm.palmLocal = hand.InverseTransformPoint(palmCentre);

            // Fingers start from the bind pose (an open hand) and curl about their own knuckles toward the palm.
            // All geometry below is measured in the bind pose so the local curl axes match the bind rotations.
            if (!bind.TryGetValue(hand, out var handBind))
                return;
            Vector3 BindPos(Transform t) => bind.TryGetValue(t, out var m) ? m.MultiplyPoint3x4(Vector3.zero) : t.position;
            Quaternion BindRot(Transform t) => bind.TryGetValue(t, out var m) ? m.rotation : t.rotation;
            Vector3 bf = (BindPos(middle) - BindPos(hand)).normalized;
            Vector3 bu = Vector3.ProjectOnPlane(BindPos(index) - BindPos(pinky), bf).normalized;
            Vector3 bindNormal = (handIndex == 1 ? 1 : -1) * Vector3.Cross(bf, bu).normalized;
            Vector3 bindPalm = Vector3.Lerp(BindPos(hand), BindPos(middle), .55f);

            void AddFinger(string name, int count, int kind)
            {
                var joints = new List<Transform>();
                for (int i = 1; i <= count; i++)
                {
                    var j = Find(name + i);
                    if (j)
                        joints.Add(j);
                }
                if (joints.Count == 0)
                    return;
                var finger = new Finger { joints = joints.ToArray(), bind = new Quaternion[joints.Count], axes = new Vector3[joints.Count], maxAngles = new float[joints.Count], relaxed = new float[joints.Count], hand = handIndex, kind = kind };
                for (int i = 0; i < joints.Count; i++)
                {
                    var j = joints[i];
                    finger.bind[i] = BindLocal(j, bind);
                    Vector3 here = BindPos(j);
                    Vector3 along = i + 1 < joints.Count ? BindPos(joints[i + 1]) - here : here - BindPos(i > 0 ? joints[i - 1] : hand);
                    if (along.sqrMagnitude < 1e-10f)
                        along = bf;
                    along.Normalize();
                    // Long fingers fold toward the palm normal; the thumb folds across the palm.
                    Vector3 toward = kind == 0 ? (bindPalm + bindNormal * .02f - here).normalized : bindNormal;
                    Vector3 axis = Vector3.Cross(along, toward);
                    if (axis.sqrMagnitude < 1e-8f)
                        axis = bu;
                    axis.Normalize();
                    if (Vector3.Dot(Quaternion.AngleAxis(30, axis) * along, toward) < Vector3.Dot(along, toward))
                        axis = -axis;
                    finger.axes[i] = Quaternion.Inverse(BindRot(j)) * axis;
                    finger.maxAngles[i] = kind == 0 ? new[] { 22f, 38f, 48f }[Mathf.Min(i, 2)] : i == 0 ? 80f : 100f;
                    finger.relaxed[i] = kind == 0 ? 6 : i == 0 ? 14 : 20;
                }
                fingers.Add(finger);
            }
            AddFinger("Thumb", 3, 0);
            AddFinger("Index", 3, 1);
            AddFinger("Middle", 3, 2);
            AddFinger("Ring", 3, 2);
            AddFinger("Pinky", 3, 2);
        }

        static Quaternion BindLocal(Transform joint, Dictionary<Transform, Matrix4x4> bind)
        {
            if (joint.parent && bind.TryGetValue(joint, out var world) && bind.TryGetValue(joint.parent, out var parent))
            {
                var local = parent.inverse * world;
                return local.rotation;
            }
            return joint.localRotation;
        }

        /// <summary>World position of the centre of a palm (where held objects sit).</summary>
        public Vector3 Palm(bool left)
        {
            var arm = left ? LeftArm : RightArm;
            return arm.end ? arm.end.TransformPoint(arm.palmLocal) : root.position;
        }

        /// <summary>Stores the animated pose; call once per frame after the clips have been sampled.</summary>
        public void CaptureAnimatedPose()
        {
            for (int i = 0; i < driven.Length; i++)
            {
                capturedRot[i] = driven[i].localRotation;
                capturedPos[i] = driven[i].localPosition;
            }
        }

        void RestoreAnimatedPose()
        {
            for (int i = 0; i < driven.Length; i++)
                driven[i].SetLocalPositionAndRotation(capturedPos[i], capturedRot[i]);
        }

        /// <summary>Poses the whole body for this frame. Safe to call several times per frame (LateUpdate and before render).</summary>
        public void Solve(BodyFrame frame, bool advanceState)
        {
            if (!Valid)
                return;
            RestoreAnimatedPose();
            float dt = Mathf.Clamp(frame.deltaTime, 0, .1f);
            var head = frame.head;

            // Height calibration: the avatar grows to the player's standing eye height (never shrinks mid-match).
            float eye = head.position.y - frame.floorY;
            if (advanceState && eye > standingEye)
                standingEye = Mathf.Lerp(standingEye, eye, 1 - Mathf.Exp(-3 * dt));
            Scale = Mathf.Clamp(standingEye / Mathf.Max(.5f, eyeRest), .85f, 1.35f);
            root.localScale = Vector3.one * Scale;

            Vector3 headForward = head.rotation * Vector3.forward;
            Vector3 flat = Geo.Flat(headForward);
            // Looking straight down: use the top of the head to find where the face points.
            if (flat.sqrMagnitude < .05f)
                flat = Geo.Flat(head.rotation * Vector3.up) * Mathf.Sign(headForward.y < 0 ? 1 : -1);
            float headYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            Vector3 move = Geo.Flat(frame.locomotion);
            bool moving = move.magnitude > .35f;
            if (!initialised)
            {
                bodyYaw = headYaw;
                initialised = true;
            }
            if (advanceState)
            {
                bodyYaw += frame.rigTurn;
                float target = headYaw;
                if (moving)
                {
                    float moveYaw = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(headYaw, moveYaw)) < 110)
                        target = Mathf.LerpAngle(headYaw, moveYaw, .35f);
                }
                // Head turns within a comfortable range do not drag the body; beyond it, or while moving, the body follows.
                float deadZone = moving ? 0 : 32;
                float delta = Mathf.DeltaAngle(bodyYaw, target);
                if (Mathf.Abs(delta) > deadZone)
                {
                    float desired = target - Mathf.Sign(delta) * deadZone;
                    bodyYaw = Mathf.LerpAngle(bodyYaw, desired, 1 - Mathf.Exp(-(moving ? 10 : 7) * dt));
                }
            }
            var yaw = Quaternion.Euler(0, bodyYaw, 0);

            // Neck sits a little below and behind the eyes; the torso hangs from it.
            Vector3 neckTarget = head.position + head.rotation * new Vector3(0, -.09f, -.09f) * Scale + Vector3.down * .05f * Scale;
            Vector3 neckOffset = yaw * Geo.Flat(neckRest) * Scale;
            Vector3 desiredRoot = Geo.Flat(neckTarget) - Geo.Flat(neckOffset);
            if (advanceState)
            {
                // Hips lag a little behind small leans (the spine bends instead) but follow steps and locomotion.
                Vector3 lag = Geo.Flat(rootFlat) - desiredRoot;
                float maxLag = moving ? .06f : .14f;
                if (lag.magnitude > 1.2f)
                    lag = Vector3.zero;
                else if (lag.magnitude > maxLag)
                    lag = lag.normalized * maxLag;
                lag = Vector3.Lerp(lag, Vector3.zero, 1 - Mathf.Exp(-1.5f * dt));
                rootFlat = desiredRoot + lag;
            }
            float standingNeck = neckRest.y * Scale;
            float crouch = Mathf.Clamp(frame.floorY + standingNeck - neckTarget.y, -.08f, .9f);
            root.SetPositionAndRotation(new Vector3(rootFlat.x, frame.floorY - crouch, rootFlat.z), yaw);

            LeanSpine(neckTarget);
            TwistChest(Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, headYaw), -55, 55));
            PlantFeet(frame.floorY);
            SolveArm(LeftArm, leftClavicle, frame.leftGrip);
            SolveArm(RightArm, rightClavicle, frame.rightGrip);
            CurlFingers(frame.leftCurl, frame.rightCurl);
        }

        void LeanSpine(Vector3 neckTarget)
        {
            var chain = new[] { spine1, spine2, chest };
            var weights = new[] { .35f, .45f, .8f };
            for (int i = 0; i < chain.Length; i++)
            {
                var bone = chain[i];
                if (!bone)
                    continue;
                Vector3 from = neck.position - bone.position, to = neckTarget - bone.position;
                if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f)
                    continue;
                var r = Quaternion.FromToRotation(from, to);
                r.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180)
                    angle -= 360;
                angle = Mathf.Clamp(angle * weights[i], -40, 40);
                bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
            }
        }

        void TwistChest(float headTwist)
        {
            if (spine2)
                spine2.rotation = Quaternion.AngleAxis(headTwist * .25f, Vector3.up) * spine2.rotation;
            if (chest)
                chest.rotation = Quaternion.AngleAxis(headTwist * .45f, Vector3.up) * chest.rotation;
        }

        void PlantFeet(float floorY)
        {
            foreach (var leg in new[] { LeftLeg, RightLeg })
            {
                if (!leg.upper || !leg.lower || !leg.end)
                    continue;
                Vector3 foot = leg.end.position;
                float minY = floorY + leg.endHeight * Scale;
                if (foot.y >= minY - .005f)
                    continue;
                var footRotation = leg.end.rotation;
                Vector3 target = new(foot.x, minY, foot.z);
                Vector3 pole = root.forward + Vector3.up * .1f + root.right * leg.side * .1f;
                TwoBone(leg.upper, leg.lower, leg.end, target, pole, 1f);
                leg.end.rotation = footRotation;
            }
        }

        void SolveArm(Limb arm, Transform clavicle, Pose grip)
        {
            Quaternion handRotation = grip.rotation * GripFrame * arm.handAlign;
            Vector3 wrist = grip.position - handRotation * (arm.palmLocal * Scale);

            // Shoulders shrug and reach forward a little when the hand is high or far away.
            if (clavicle)
            {
                Vector3 rest = arm.upper.position - clavicle.position, toward = wrist - clavicle.position;
                float raise = Mathf.Clamp01((wrist.y - arm.upper.position.y + .15f) / .45f);
                var full = Quaternion.FromToRotation(rest, toward);
                full.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180)
                    angle -= 360;
                angle = Mathf.Clamp(angle * (.1f + .2f * raise), -22, 22);
                clavicle.rotation = Quaternion.AngleAxis(angle, axis) * clavicle.rotation;
            }

            // Elbows hang down, slightly out and back; they flare out when the hand is raised.
            Vector3 right = root.right * arm.side, back = -root.forward;
            float high = Mathf.Clamp01((wrist.y - arm.upper.position.y) / (.3f * Scale));
            Vector3 pole = Vector3.down * (1 - high) + right * (.55f + high * 1.1f) + back * .35f;
            TwoBone(arm.upper, arm.lower, arm.end, wrist, pole, 1.12f);

            // Share the wrist roll with the forearm so the wrist does not candy-wrap.
            Vector3 forearmAxis = (arm.end.position - arm.lower.position).normalized;
            var delta = handRotation * Quaternion.Inverse(arm.end.rotation);
            var twist = Twist(delta, forearmAxis);
            arm.lower.rotation = Quaternion.Slerp(Quaternion.identity, twist, .5f) * arm.lower.rotation;
            arm.end.rotation = handRotation;
        }

        static Quaternion Twist(Quaternion q, Vector3 axis)
        {
            var v = new Vector3(q.x, q.y, q.z);
            var p = Vector3.Project(v, axis);
            var twist = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (m < 1e-6f)
                return Quaternion.identity;
            return new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m);
        }

        /// <summary>Analytic two-bone IK with a pole hint and limited stretch.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float maxStretch)
        {
            Vector3 aPos = a.position;
            float lenAB = Vector3.Distance(aPos, b.position), lenBC = Vector3.Distance(b.position, c.position);
            if (lenAB < 1e-4f || lenBC < 1e-4f)
                return;
            Vector3 toTarget = target - aPos;
            float dist = toTarget.magnitude;
            if (dist < 1e-4f)
                return;
            float stretch = Mathf.Clamp(dist / (lenAB + lenBC) * 1.002f, 1, maxStretch);
            if (stretch > 1)
            {
                b.localPosition *= stretch;
                c.localPosition *= stretch;
                lenAB *= stretch;
                lenBC *= stretch;
            }
            float d = Mathf.Clamp(dist, Mathf.Abs(lenAB - lenBC) + 1e-3f, lenAB + lenBC - 1e-3f);
            Vector3 dir = toTarget / dist;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.Cross(dir, Vector3.right);
            bend.Normalize();
            float along = (lenAB * lenAB - lenBC * lenBC + d * d) / (2 * d);
            float across = Mathf.Sqrt(Mathf.Max(0, lenAB * lenAB - along * along));
            Vector3 elbow = aPos + dir * along + bend * across;
            a.rotation = Quaternion.FromToRotation(b.position - aPos, elbow - aPos) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, aPos + dir * d - b.position) * b.rotation;
        }

        void CurlFingers(HandCurl left, HandCurl right)
        {
            foreach (var f in fingers)
            {
                var curl = f.hand == 0 ? left : right;
                float amount = f.kind == 0 ? curl.thumb : f.kind == 1 ? curl.index : curl.grip;
                for (int i = 0; i < f.joints.Length; i++)
                {
                    float angle = Mathf.Lerp(f.relaxed[i], f.maxAngles[i], Mathf.Clamp01(amount));
                    f.joints[i].localRotation = f.bind[i] * Quaternion.AngleAxis(angle, f.axes[i]);
                }
            }
        }
    }
}
