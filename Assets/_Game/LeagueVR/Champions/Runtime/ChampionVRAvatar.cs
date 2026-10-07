using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
namespace LeagueVR.Champions
{
    [DefaultExecutionOrder(225)]
    public class ChampionVRAvatar : MonoBehaviour
    {
        public GwenAbilities player;
        public GameObject Instance { get; private set; }
        public Transform LeftPalm => left.palm;
        public Transform RightPalm => right.palm;
        Transform root;
        float shouldersBelowEye;
        ChampionDefinition definition;

        class Arm
        {
            public Transform upper, lower, palm;
            public Vector3 lowerPos, palmPos;
            public Quaternion upperRot, lowerRot, palmRot, align;
            public readonly List<(Transform bone, Vector3 position)> helpers = new();

            public Arm(Transform u, Transform l, Transform p)
            {
                upper = u;
                lower = l;
                palm = p;
                lowerPos = l.localPosition;
                palmPos = p.localPosition;
                upperRot = u.localRotation;
                lowerRot = l.localRotation;
                palmRot = p.localRotation;
                align = Alignment(p);
                foreach (var bone in lower.GetComponentsInChildren<Transform>())
                    if (bone != lower && !bone.IsChildOf(palm))
                        helpers.Add((bone, bone.localPosition));
            }
        }
        Arm left, right;
        readonly List<(Transform t, Quaternion rest, bool left)> fingers = new();
        Transform weapon, shield;
        Quaternion weaponAlign, shieldAlign;
        Vector3 weaponScale, shieldScale;

        void OnEnable()
        {
            Application.onBeforeRender += BeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
        }

        public void SetChampion(ChampionDefinition d)
        {
            if (Instance)
            {
                Instance.SetActive(false);
                Destroy(Instance);
            }
            left = right = null;
            root = weapon = shield = null;
            fingers.Clear();
            definition = d;
            if (!d)
                return;
            Instance = Instantiate(d.firstPerson);
            Instance.name = d.name + " tracked champion arms";
            root = Instance.transform;
            var anim = Instance.GetComponentInChildren<Animation>();
            if (anim)
            {
                anim.Stop();
                anim.enabled = false;
            }
            var animator = Instance.GetComponentInChildren<Animator>();
            if (animator)
                animator.enabled = false;
            Transform Bone(params string[] names) => names.Select(n => Instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(n, t.name, System.StringComparison.OrdinalIgnoreCase))).FirstOrDefault(t => t);
            left = new Arm(Bone("L_Uparm", "L_Shoulder"), Bone("L_Elbow", "L_Forearm"), Bone("L_Hand"));
            right = new Arm(Bone("R_Uparm", "R_Shoulder"), Bone("R_Elbow", "R_Forearm"), Bone("R_Hand"));
            float distance = Mathf.Abs(Bone("Head").position.y - (left.upper.position.y + right.upper.position.y) * .5f);
            root.localScale *= Mathf.Clamp(.23f / Mathf.Max(.12f, distance), .7f, 1.3f);
            shouldersBelowEye = .25f;
            if (d.id == ChampionId.Aatrox || d.id == ChampionId.Akshan || d.id == ChampionId.Pantheon)
            {
                weapon = Bone(d.id == ChampionId.Pantheon ? "Spear" : "Weapon");
                if (weapon)
                {
                    Vector3 tip;
                    if (d.id == ChampionId.Aatrox && Bone("Weapon_Tip"))
                        tip = Bone("Weapon_Tip").position;
                    else
                        tip = WeaponTip(weapon, d.id == ChampionId.Pantheon ? "spear" : "weapon");
                    var forward = (tip - weapon.position).normalized;
                    var up = Vector3.ProjectOnPlane(Vector3.up, forward);
                    if (up.sqrMagnitude < .01f)
                        up = Vector3.ProjectOnPlane(root.forward, forward);
                    weaponAlign = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * weapon.rotation;
                    if (d.id == ChampionId.Aatrox)
                        weapon.localScale *= .72f;
                    if (d.id == ChampionId.Pantheon)
                        weapon.localScale *= .75f;
                    weaponScale = weapon.localScale;
                }
                if (d.id == ChampionId.Pantheon)
                {
                    shield = Bone("Shield");
                    if (shield)
                    {
                        var bounds = PartBounds(shield, "shield");
                        var size = bounds.size;
                        Vector3 normal = size.x < size.y && size.x < size.z ? Vector3.right : size.y < size.z ? Vector3.up : Vector3.forward;
                        normal = shield.TransformDirection(normal);
                        var up = Vector3.ProjectOnPlane(root.up, normal);
                        if (up.sqrMagnitude < .01f)
                            up = Vector3.ProjectOnPlane(root.forward, normal);
                        shieldAlign = Quaternion.Inverse(Quaternion.LookRotation(normal, up)) * shield.rotation;
                        shield.localScale *= .55f;
                        shieldScale = shield.localScale;
                    }
                }
            }
            foreach (var arm in new[] { left, right })
                foreach (var t in arm.palm.GetComponentsInChildren<Transform>())
                    if (t.name.Contains("Index") || t.name.Contains("Middle") || t.name.Contains("Ring") || t.name.Contains("Pinky"))
                        fingers.Add((t, t.localRotation, arm == left));
            foreach (var r in Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * 12);
            }
            UpdatePose();
        }

        public static Quaternion Alignment(Transform palm)
        {
            var children = palm.GetComponentsInChildren<Transform>();
            var middle = children.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("middle"));
            var index = children.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("index"));
            var pinky = children.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("pinky"));
            if (!middle || !index || !pinky)
                return Quaternion.Inverse(Quaternion.LookRotation(palm.right, palm.up)) * palm.rotation;
            var f = (middle.position - palm.position).normalized;
            var normal = Vector3.Cross(index.position - pinky.position, f).normalized;
            if (Vector3.Dot(normal, Vector3.up) < 0)
                normal = -normal;
            return Quaternion.Inverse(Quaternion.LookRotation(f, normal)) * palm.rotation;
        }

        void LateUpdate()
        {
            UpdatePose();
        }

        [BeforeRenderOrder(160)]
        void BeforeRender()
        {
            if (player && !player.DesktopMode)
                UpdatePose();
        }

        public void UpdatePose()
        {
            if (!root || left == null)
                return;
            bool show = player.Health && player.Health.IsAlive && !(LeagueVR.Match.RiftMatch.Instance?.ui.IsOpen ?? false);
            foreach (var r in Instance.GetComponentsInChildren<Renderer>())
                r.forceRenderingOff = !show;
            var head = player.head.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f)
                forward = root.forward;
            root.rotation = Quaternion.LookRotation(forward);
            root.position += head.position - Vector3.up * shouldersBelowEye - forward * .1f - (left.upper.position + right.upper.position) * .5f;
            Solve(left, player.DesktopMode ? head.TransformPoint(new Vector3(-.24f, -.32f, .42f)) : player.leftHand.TransformPoint(new Vector3(0, -.025f, -.015f)), -root.right * .65f - Vector3.up, player.DesktopMode ? head.rotation : player.leftHand.rotation);
            Solve(right, player.DesktopMode ? head.TransformPoint(new Vector3(.24f, -.32f, .45f)) : player.rightHand.TransformPoint(new Vector3(0, -.025f, -.015f)), root.right * .65f - Vector3.up, player.DesktopMode ? head.rotation : player.rightHand.rotation);
            float lg = 0, rg = definition.id == ChampionId.Aatrox || definition.id == ChampionId.Akshan || definition.id == ChampionId.Pantheon ? .55f : 0;
            InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(CommonUsages.grip, out lg);
            if (InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(CommonUsages.grip, out float g))
                rg = Mathf.Max(rg, g);
            foreach (var finger in fingers)
                finger.t.localRotation = finger.rest * Quaternion.AngleAxis((finger.left ? -1 : 1) * 40 * (finger.left ? lg : rg), Vector3.forward);
            Quaternion rightGrip = player.DesktopMode ? head.rotation : player.rightHand.rotation, leftGrip = player.DesktopMode ? head.rotation : player.leftHand.rotation;
            var rack = player.GetComponent<LeagueVR.Match.RiftItemRack>();
            if (weapon)
                weapon.localScale = rack && rack.IsHandHolding(1) ? Vector3.zero : weaponScale;
            if (shield)
                shield.localScale = rack && rack.IsHandHolding(0) ? Vector3.zero : shieldScale;
            if (weapon)
                weapon.SetPositionAndRotation(right.palm.position + rightGrip * new Vector3(0, -.015f, .04f), rightGrip * weaponAlign);
            if (shield)
                shield.SetPositionAndRotation(left.palm.position + leftGrip * new Vector3(-.06f, -.03f, .16f), leftGrip * shieldAlign);
        }

        Vector3 WeaponTip(Transform handle, string materialName)
        {
            Vector3 tip = handle.position + root.forward;
            float furthest = 0;
            foreach (var renderer in Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                renderer.BakeMesh(mesh);
                var vertices = mesh.vertices;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (renderer.sharedMaterials[sub].name.ToLowerInvariant().EndsWith(materialName))
                        foreach (int i in mesh.GetTriangles(sub))
                        {
                            var p = renderer.transform.TransformPoint(vertices[i]);
                            float d = (p - handle.position).sqrMagnitude;
                            if (d > furthest)
                            {
                                furthest = d;
                                tip = p;
                            }
                        }
                Destroy(mesh);
            }
            return tip;
        }

        Bounds PartBounds(Transform bone, string materialName)
        {
            var b = new Bounds();
            bool first = true;
            foreach (var renderer in Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                renderer.BakeMesh(mesh);
                var v = mesh.vertices;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    if (renderer.sharedMaterials[sub].name.ToLowerInvariant().EndsWith(materialName))
                        foreach (int i in mesh.GetTriangles(sub))
                        {
                            var p = bone.InverseTransformPoint(renderer.transform.TransformPoint(v[i]));
                            if (first)
                            {
                                b = new Bounds(p, Vector3.zero);
                                first = false;
                            }
                            else
                                b.Encapsulate(p);
                        }
                Destroy(mesh);
            }
            return b;
        }

        static void Solve(Arm a, Vector3 target, Vector3 pole, Quaternion grip)
        {
            a.upper.localRotation = a.upperRot;
            a.lower.SetLocalPositionAndRotation(a.lowerPos, a.lowerRot);
            a.palm.SetLocalPositionAndRotation(a.palmPos, a.palmRot);
            var p = a.upper.position;
            var delta = target - p;
            float x = Vector3.Distance(p, a.lower.position), y = Vector3.Distance(a.lower.position, a.palm.position);
            if (x < .001f || y < .001f)
                return;
            float stretch = Mathf.Clamp(delta.magnitude / (x + y) * 1.003f, 1, 4f);
            a.lower.localPosition *= stretch;
            a.palm.localPosition *= stretch;
            foreach (var helper in a.helpers)
                helper.bone.localPosition = helper.position * stretch;
            x *= stretch;
            y *= stretch;
            float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(x - y) + .001f, x + y - .001f);
            var dir = delta.sqrMagnitude > .00001f ? delta.normalized : Vector3.forward;
            var bend = Vector3.ProjectOnPlane(pole, dir).normalized;
            if (bend.sqrMagnitude < .01f)
                bend = Vector3.Cross(dir, Vector3.right).normalized;
            float along = (x * x - y * y + d * d) / (2 * d), across = Mathf.Sqrt(Mathf.Max(0, x * x - along * along));
            var elbow = p + dir * along + bend * across;
            a.upper.rotation = Quaternion.FromToRotation(a.lower.position - p, elbow - p) * a.upper.rotation;
            a.lower.rotation = Quaternion.FromToRotation(a.palm.position - a.lower.position, p + dir * d - a.lower.position) * a.lower.rotation;
            a.palm.position = target;
            a.palm.rotation = grip * a.align;
        }

        void OnDestroy()
        {
            if (Instance)
                Destroy(Instance);
        }
    }
}
