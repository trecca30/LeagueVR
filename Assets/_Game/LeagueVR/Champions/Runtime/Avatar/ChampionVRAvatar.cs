using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// First-person body for champions built from a prefab (Zoe, Pantheon and the other imported champions). The
    /// champion's first-person model (its own mesh without the head) gets the shared full-body rig on its idle pose
    /// and a complete shadow. Weapons are fitted to the controllers: a weapon runs along the right grip, a shield
    /// faces where the left fist punches, and a gun (Zeri's) points where the controller points.
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
            /// <summary>Held like a pistol: the prop's forward follows the controller's pointing ray.</summary>
            public bool aimed;
            /// <summary>Least finger curl while holding it (a gun leaves the index on the trigger).</summary>
            public HandCurl curl;
            /// <summary>Recoil, 0-1: the prop tips up and back after each shot and settles (the camera never moves).</summary>
            public float kick;
            /// <summary>Angle between the controller handle and this prop; null uses the avatar's <see cref="weaponTilt"/>.</summary>
            public Vector3? tilt;

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
            public bool aimed;
            public HandCurl curl;
            /// <summary>The prop's own angle to the controller handle (zero: the avatar's weapon tilt).</summary>
            public Vector3 tilt;
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
            {
                // Zeri's gun: the bone sits in her grip, the muzzle (League's Buffbone_Glb_Weapon_1) is 0.3 m along -Z and
                // the coils run along the top (+Y) behind the hand. Held like a pistol, index finger on the trigger.
                ChampionId.Zeri, new[]
                {
                    new PropSpec { bone = "Weapon1", material = "Gun", forward = Vector3.back, up = Vector3.up, handle = new Vector3(0, .02f, .03f), edge = Vector3.zero, tip = new Vector3(0, .07f, -.3f), scale = .85f, aimed = true, curl = new HandCurl(.2f, .85f, .55f) },
                }
            },
            {
                // Hwei's brush, in his right hand (League's is in his left): the bristles run from 0.55 m to the tip at
                // 0.97 m along the bone's -Z and the ornamented handle end is 0.53 m along +Z. League's palm holds it at
                // the bone; here the fist sits higher on the shaft, under the ornament, so the short end stays clear of
                // the face in every stroke. Held like a wand: the bristles point a little above where the hand points.
                ChampionId.Hwei, new[]
                {
                    new PropSpec { bone = "Weapon1", material = "Brush", forward = Vector3.back, up = Vector3.up, handle = new Vector3(0, .007f, .3f), edge = new Vector3(0, 0, -.3f), tip = new Vector3(0, .003f, -.97f), scale = .75f, tilt = new Vector3(48, 0, 0) },
                }
            },
        };

        /// <summary>Submeshes of the complete model that cast the body's shadow (head, hair, cape and props in hand).</summary>
        static readonly Dictionary<ChampionId, string[]> ShadowParts = new()
        {
            { ChampionId.Zoe, new[] { "Zoe_Base_Mat", "Zoe_Base_Hair_Mat" } },
            { ChampionId.Pantheon, new[] { "Pantheon_Base_Mat", "L_Arm", "Cape", "Spear", "Shield", "Head", "Helmet" } },
            { ChampionId.Zeri, new[] { "Body", "Shoes", "Gun", "Hair", "HairSpike" } },
            // Gragas is one material: the whole model, keg included, casts the shadow.
            { ChampionId.Gragas, new[] { "MAT_gragas" } },
            { ChampionId.Brand, new[] { "riotRig_lambert4" } },
            // Yunara's prayer beads are left out of her first-person body but belong to her shadow.
            { ChampionId.Yunara, new[] { "Body", "Pants", "Dragon_Head", "Tail", "Bead_1", "Bead_2", "Bead_3", "Bead_4", "Bead_5", "Bead_6", "Bead_7", "Bead_8" } },
            { ChampionId.Hwei, new[] { "Brush", "PaletteA", "Hair", "Body" } },
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
            SetupAnimation(Instance.GetComponentInChildren<Animation>(true), definition.idle ? definition.idle.name : null);
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
                    aimed = spec.aimed,
                    curl = spec.curl,
                    tilt = spec.tilt != Vector3.zero ? spec.tilt : null,
                };
                prop.mesh = ExtractPropMesh(Skin, bone, spec.material, out prop.material);
                if (spec.left)
                    Shield = prop;
                else
                    Weapon = prop;
            }
        }

        /// <summary>
        /// Copies the prop's triangles out of the skinned mesh into the prop bone's own space: by material when the prop
        /// has its own (Pantheon's spear, Zeri's gun), otherwise by the vertices that follow the prop's bones (Gragas's
        /// keg shares the body's only material).
        /// </summary>
        internal static Mesh ExtractPropMesh(SkinnedMeshRenderer skin, Transform bone, string materialSuffix, out Material material)
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
            bool byBone = string.IsNullOrEmpty(materialSuffix);
            var propBones = new HashSet<int>();
            for (int b = 0; b < skin.bones.Length; b++)
                if (skin.bones[b] && (skin.bones[b] == bone || skin.bones[b].IsChildOf(bone)))
                    propBones.Add(b);
            var weights = byBone ? source.boneWeights : null;
            bool OnProp(int v)
            {
                var w = weights[v];
                float sum = (propBones.Contains(w.boneIndex0) ? w.weight0 : 0) + (propBones.Contains(w.boneIndex1) ? w.weight1 : 0)
                    + (propBones.Contains(w.boneIndex2) ? w.weight2 : 0) + (propBones.Contains(w.boneIndex3) ? w.weight3 : 0);
                return sum >= .5f;
            }
            for (int s = 0; s < source.subMeshCount; s++)
            {
                if (s >= materials.Length || !materials[s] || (!byBone && !materials[s].name.EndsWith(materialSuffix, StringComparison.Ordinal)))
                    continue;
                var tris = source.GetTriangles(s);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    if (byBone && !(OnProp(tris[t]) && OnProp(tris[t + 1]) && OnProp(tris[t + 2])))
                        continue;
                    material = materials[s];
                    for (int k = 0; k < 3; k++)
                    {
                        int index = tris[t + k];
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

        /// <summary>The angle between the controller handle and a held prop (pitch, yaw, roll).</summary>
        public Vector3 PropTilt(Prop prop) => prop?.tilt ?? weaponTilt;

        /// <summary>
        /// The hand frame a prop is fitted to. Weapons run along the controller handle, lowered by
        /// <see cref="weaponTilt"/>, with the palm normal as up; shields face where the fist points. A kit animation that
        /// moves the hand (<see cref="ChampionBodyBase.GripOverride"/>) turns the prop with it.
        /// </summary>
        Quaternion HandFrame(Prop prop)
        {
            var grip = ReachedGrip(prop.left);
            if (prop.left || prop.aimed)
                return XRPoses.AimFromGrip(grip.rotation);
            var along = grip.rotation * Quaternion.Euler(PropTilt(prop));
            Vector3 forward = along * Vector3.forward;
            Vector3 palmNormal = Vector3.ProjectOnPlane(grip.rotation * Vector3.right, forward);
            return Quaternion.LookRotation(forward, palmNormal.sqrMagnitude > 1e-4f ? palmNormal : along * Vector3.up);
        }

        /// <summary>World pose and scale of a prop this frame.</summary>
        public void PropPose(Prop prop, out Vector3 position, out Quaternion rotation, out float scale)
        {
            var frame = HandFrame(prop);
            // Recoil pivots about the hand: the muzzle tips up and the prop slides back a little.
            if (prop.kick > 0)
                frame *= Quaternion.Euler(-9 * prop.kick, 0, 0);
            rotation = frame * prop.fit;
            scale = prop.scale * BodyScale;
            position = Palm(prop.left) - rotation * (prop.handle * scale) - frame * Vector3.forward * (.03f * prop.kick);
        }

        /// <summary>Kicks the right-hand weapon (a gun's recoil); it settles back within about a tenth of a second.</summary>
        public void Kick(float amount)
        {
            if (Weapon != null)
                Weapon.kick = Mathf.Min(1, Weapon.kick + amount);
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

        /// <summary>A gun's muzzle and the direction it fires (false when no weapon is out).</summary>
        public bool WeaponMuzzle(out Vector3 position, out Vector3 forward)
        {
            position = forward = default;
            if (Weapon == null || !Weapon.Visible || !Ready)
                return false;
            position = PropPoint(Weapon, Weapon.tip);
            forward = HandFrame(Weapon) * Vector3.forward;
            return true;
        }

        /// <summary>Distance from the fist to the weapon tip in world units.</summary>
        public float WeaponReach => Weapon != null ? (Weapon.tip - Weapon.handle).magnitude * Weapon.scale * BodyScale : 0;

        protected override HandCurl HeldCurl(bool left)
        {
            var prop = left ? Shield : Weapon;
            // Closed around a spear shaft or a shield grip: firm, not crushed through the handle.
            if (prop == null || !prop.Visible)
                return default;
            return prop.aimed ? prop.curl : new HandCurl(.62f, .82f, .72f);
        }

        protected override void AfterSolve()
        {
            if (Weapon != null && Weapon.kick > 0)
                Weapon.kick = Mathf.MoveTowards(Weapon.kick, 0, Time.deltaTime * 4.5f); // runs twice a frame (late update and before render)
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
