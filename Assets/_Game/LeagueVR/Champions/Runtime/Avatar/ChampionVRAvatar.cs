using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// First-person body for champions built from a prefab (Zoe, Pantheon and the other imported champions). The
    /// champion's first-person model (its own mesh without the head) gets the shared full-body rig, its idle and run
    /// clips and a complete shadow. Weapons are fitted to the controllers: a weapon runs along the right grip, a shield
    /// faces where the left fist punches.
    /// </summary>
    [DefaultExecutionOrder(225)]
    public class ChampionVRAvatar : ChampionBodyBase
    {
        /// <summary>A prop carried by a hand. Its bone is re-posed from the controller every frame.</summary>
        public class Prop
        {
            public Transform bone;
            public bool left;
            /// <summary>Rotation from the hand frame into the prop's own axes.</summary>
            public Quaternion fit;
            /// <summary>Prop-space points: where the hand holds it, the start of its striking edge and its tip.</summary>
            public Vector3 handle, edge, tip;
            /// <summary>Prop size relative to the body (1 = as modelled).</summary>
            public float scale;
            /// <summary>Standalone copy of the prop's geometry in prop space, for glows, ghosts and thrown copies.</summary>
            public Mesh mesh;
            public Material material;
            /// <summary>Hidden by the kit (for example while the spear is in flight).</summary>
            public bool hidden;

            public bool Visible => bone && !hidden && bone.localScale.x > 0;
        }

        /// <summary>How a champion's props sit in the hands.</summary>
        struct PropSpec
        {
            public string bone, material;
            public bool left;
            /// <summary>Prop-space direction that points along the hand frame's forward, and the one along its up.</summary>
            public Vector3 forward, up;
            public Vector3 handle, edge, tip;
            public float scale;
        }

        /// <summary>
        /// Pantheon's spear: the blade is at the prop's -X end (1.2 m long in the model), the shaft continues 2 m to +X.
        /// The fist holds it a little behind the middle, so about two thirds of the length is in front of the hand.
        /// The shield is held by its central handle, its convex face (-X) toward where the fist punches, crest (+Y) up.
        /// </summary>
        static readonly Dictionary<ChampionId, PropSpec[]> Props = new()
        {
            {
                ChampionId.Pantheon, new[]
                {
                    new PropSpec { bone = "Spear", material = "Spear", forward = Vector3.left, up = Vector3.forward, handle = new Vector3(.9f, 0, 0), edge = new Vector3(.25f, 0, 0), tip = new Vector3(-1.2f, 0, 0), scale = .6f },
                    new PropSpec { bone = "Shield", material = "Shield", left = true, forward = Vector3.left, up = Vector3.up, handle = new Vector3(.02f, 0, 0), scale = .48f },
                }
            },
        };

        /// <summary>Submeshes of the complete model that cast the body's shadow (head, hair, cape and props in hand).</summary>
        static readonly Dictionary<ChampionId, string[]> ShadowParts = new()
        {
            { ChampionId.Zoe, new[] { "Zoe_Base_Mat", "Zoe_Base_Hair_Mat" } },
            { ChampionId.Pantheon, new[] { "Pantheon_Base_Mat", "L_Arm", "Cape", "Spear", "Shield", "Head", "Helmet" } },
        };

        [Tooltip("Angle between the controller handle and a held weapon: pitch, yaw, roll. Positive pitch lowers the weapon toward the pointing direction.")]
        public Vector3 weaponTilt = new(40, 0, 0);

        public GameObject Instance { get; private set; }
        public ChampionDefinition Definition { get; private set; }
        public Prop Weapon { get; private set; }
        public Prop Shield { get; private set; }

        protected override bool Ready => Instance && Instance.activeInHierarchy;

        protected override void OnDisable()
        {
            base.OnDisable();
            if (player.Body == this)
                player.Body = null;
        }

        /// <summary>Builds the body for a champion, or removes it (null) when another body takes over.</summary>
        public void SetChampion(ChampionDefinition definition)
        {
            Clear();
            Definition = definition;
            enabled = definition && definition.firstPerson;
            if (!enabled)
                return;
            Instance = Instantiate(definition.firstPerson, transform, false);
            Instance.name = definition.name + " first-person body";
            var root = Instance.transform;
            root.SetPositionAndRotation(player.Feet, Quaternion.Euler(0, player.head.transform.eulerAngles.y, 0));
            var animator = Instance.GetComponentInChildren<Animator>(true);
            if (animator)
                animator.enabled = false;
            Skin = Instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SetupAnimation(Instance.GetComponentInChildren<Animation>(true), null, null);
            // Props are posed by hand, so their bones must not follow the hand's animated pose; build them from the
            // animated rest pose before the rig takes over.
            SetupProps(definition);
            Rig = new BodyRig(root, Skin);
            bodyRenderers = Instance.GetComponentsInChildren<Renderer>(true);
            foreach (var r in bodyRenderers)
                if (r is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            var full = definition.model ? definition.model.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (full && ShadowParts.TryGetValue(definition.id, out var parts) && SameSkeleton(full, Skin))
                CreateShadow(full.sharedMesh, full.sharedMaterials, (_, name) => Array.Exists(parts, p => name.EndsWith("-" + p, StringComparison.Ordinal)));
            if (!Rig.Valid)
                Debug.LogWarning($"{definition.name}: first-person skeleton not recognised; arms will not follow the controllers.");
        }

        void Clear()
        {
            if (Instance)
            {
                Instance.SetActive(false);
                Destroy(Instance);
            }
            foreach (var prop in new[] { Weapon, Shield })
                if (prop != null && prop.mesh)
                    Destroy(prop.mesh);
            if (shadowObject && shadowObject.TryGetComponent<SkinnedMeshRenderer>(out var shadow) && shadow.sharedMesh)
                Destroy(shadow.sharedMesh);
            Instance = null;
            shadowObject = null;
            Weapon = Shield = null;
            Rig = null;
            Skin = null;
            bodyRenderers = Array.Empty<Renderer>();
        }

        void OnDestroy() => Clear();

        static bool SameSkeleton(SkinnedMeshRenderer a, SkinnedMeshRenderer b)
        {
            if (!a || !b || a.bones.Length != b.bones.Length)
                return false;
            for (int i = 0; i < a.bones.Length; i++)
                if (a.bones[i] && b.bones[i] && a.bones[i].name != b.bones[i].name)
                    return false;
            return true;
        }

        void SetupProps(ChampionDefinition definition)
        {
            if (!Props.TryGetValue(definition.id, out var specs) || !Skin)
                return;
            foreach (var spec in specs)
            {
                Transform bone = null;
                foreach (var t in Instance.GetComponentsInChildren<Transform>(true))
                    if (t.name == spec.bone)
                    {
                        bone = t;
                        break;
                    }
                if (!bone)
                    continue;
                var prop = new Prop
                {
                    bone = bone,
                    left = spec.left,
                    fit = Quaternion.Inverse(Quaternion.LookRotation(spec.forward, spec.up)),
                    handle = spec.handle,
                    edge = spec.edge,
                    tip = spec.tip,
                    scale = spec.scale,
                };
                prop.mesh = ExtractPropMesh(Skin, bone, spec.material, out prop.material);
                if (spec.left)
                    Shield = prop;
                else
                    Weapon = prop;
            }
        }

        /// <summary>Copies the prop's triangles out of the skinned mesh into the prop bone's own space.</summary>
        static Mesh ExtractPropMesh(SkinnedMeshRenderer skin, Transform bone, string materialSuffix, out Material material)
        {
            material = null;
            var source = skin.sharedMesh;
            int boneIndex = Array.IndexOf(skin.bones, bone);
            if (!source || boneIndex < 0)
                return null;
            var bind = source.bindposes[boneIndex];
            var vertices = source.vertices;
            var normals = source.normals;
            var uvs = source.uv;
            var map = new Dictionary<int, int>();
            var outVertices = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var triangles = new List<int>();
            var materials = skin.sharedMaterials;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                if (s >= materials.Length || !materials[s] || !materials[s].name.EndsWith(materialSuffix, StringComparison.Ordinal))
                    continue;
                material = materials[s];
                foreach (int index in source.GetTriangles(s))
                {
                    if (!map.TryGetValue(index, out int mapped))
                    {
                        mapped = outVertices.Count;
                        map[index] = mapped;
                        outVertices.Add(bind.MultiplyPoint3x4(vertices[index]));
                        outNormals.Add(normals.Length > index ? bind.MultiplyVector(normals[index]).normalized : Vector3.up);
                        outUvs.Add(uvs.Length > index ? uvs[index] : Vector2.zero);
                    }
                    triangles.Add(mapped);
                }
            }
            if (triangles.Count == 0)
                return null;
            var mesh = new Mesh { name = bone.name + " prop" };
            mesh.SetVertices(outVertices);
            mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Prop poses ----------

        /// <summary>
        /// The hand frame a prop is fitted to. Weapons run along the controller handle, lowered by
        /// <see cref="weaponTilt"/>, with the palm normal as up; shields face where the fist points.
        /// </summary>
        Quaternion HandFrame(Prop prop)
        {
            var grip = Grip(prop.left);
            if (prop.left)
                return XRPoses.AimFromGrip(grip.rotation);
            var along = grip.rotation * Quaternion.Euler(weaponTilt);
            Vector3 forward = along * Vector3.forward;
            Vector3 palmNormal = Vector3.ProjectOnPlane(grip.rotation * Vector3.right, forward);
            return Quaternion.LookRotation(forward, palmNormal.sqrMagnitude > 1e-4f ? palmNormal : along * Vector3.up);
        }

        /// <summary>World pose and scale of a prop this frame.</summary>
        public void PropPose(Prop prop, out Vector3 position, out Quaternion rotation, out float scale)
        {
            rotation = HandFrame(prop) * prop.fit;
            scale = prop.scale * BodyScale;
            position = Palm(prop.left) - rotation * (prop.handle * scale);
        }

        /// <summary>A point given in prop space, in the world.</summary>
        public Vector3 PropPoint(Prop prop, Vector3 local)
        {
            PropPose(prop, out var position, out var rotation, out float scale);
            return position + rotation * (local * scale);
        }

        /// <summary>The weapon's pose in the right fist (forward along the weapon), or the right palm's aim for empty hands.</summary>
        public override Pose WeaponPose()
        {
            if (Weapon != null)
            {
                var frame = HandFrame(Weapon);
                return new Pose(Palm(false), frame);
            }
            return Aim(false);
        }

        /// <summary>Start and end of the weapon's striking edge in the world (false when the hand has no weapon out).</summary>
        public bool WeaponEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (Weapon == null || !Weapon.Visible || !Ready)
                return false;
            from = PropPoint(Weapon, Weapon.edge);
            to = PropPoint(Weapon, Weapon.tip);
            return true;
        }

        /// <summary>Distance from the fist to the weapon tip in world units.</summary>
        public float WeaponReach => Weapon != null ? (Weapon.tip - Weapon.handle).magnitude * Weapon.scale * BodyScale : 0;

        protected override HandCurl HeldCurl(bool left)
        {
            var prop = left ? Shield : Weapon;
            return prop != null && prop.Visible ? new HandCurl(.6f, .95f, .8f) : default;
        }

        protected override void AfterSolve()
        {
            PoseProp(Weapon);
            PoseProp(Shield);
        }

        void PoseProp(Prop prop)
        {
            if (prop == null || !prop.bone)
                return;
            // A menu, a held item or the kit (thrown spear) puts the prop away: collapsing the bone hides its vertices.
            if (prop.hidden || player.HandStowed(prop.left))
            {
                prop.bone.localScale = Vector3.zero;
                return;
            }
            PropPose(prop, out var position, out var rotation, out float scale);
            var parent = prop.bone.parent;
            float parentScale = parent ? parent.lossyScale.x : 1;
            prop.bone.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, parentScale));
            prop.bone.SetPositionAndRotation(position, rotation);
        }
    }
}
