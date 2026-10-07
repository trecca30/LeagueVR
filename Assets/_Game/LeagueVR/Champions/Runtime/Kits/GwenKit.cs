using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen, the Hallowed Seamstress, built for VR. Numbers follow League (wiki, 26.x); the interactions are physical:
    /// <list type="bullet">
    /// <item>Attacks: swing the real scissors through enemies, or hold the trigger to auto-snip the nearest target.</item>
    /// <item>A Thousand Cuts: every hit stitches a max-health cut and heals Gwen against champions.</item>
    /// <item>Q Snip Snip!: giant spectral scissors snip in front of you, once per stored stack plus two; the centre line deals true damage.</item>
    /// <item>W Hallowed Mist: a dome of mist you stand inside; attackers outside cannot target you. Recast pulls it to you.</item>
    /// <item>E Skip 'n Slash: a short dash where you are walking (or where the blades point), leaving an afterimage; blades glow and attack faster.</item>
    /// <item>R Needlework: hold the left trigger to ready 1 / 3 / 5 needles between your fingers, release while throwing to hurl them.</item>
    /// </list>
    /// </summary>
    public class GwenKit : MeleeKit
    {
        static readonly Color Cyan = new(.45f, .95f, 1f, 1f);

        GwenTuning t;
        GameObject needle;
        Material threadMaterial;
        GwenAvatar avatar;

        public int QStacks { get; private set; }
        public int RStage { get; private set; }
        public int NeedlesReady { get; private set; }
        public Vector3 MistCenter { get; private set; }
        public bool MistActive => Time.time < mistUntil && Health.IsAlive;
        public float MistRemaining => Mathf.Max(0, mistUntil - Time.time);
        public bool Empowered => Time.time < empoweredUntil;

        float stackUntil, mistUntil, wRecastAt, empoweredUntil, rWindowUntil, rNext;
        bool qCasting, mistMoved, eRefunded;
        readonly HashSet<Combatant> needleVictims = new();
        GameObject dome;
        Material domeMaterial;
        LineRenderer mistRing;
        ParticleSystem mistMotes;
        readonly List<LineRenderer> mistThreads = new();

        public override bool Busy => qCasting;
        public override float BonusAttackSpeed => Empowered ? ByRank(2, .3f, .425f, .55f, .675f, .8f) : 0;
        public override float BonusAttackRange => Empowered ? .35f : 0;
        public override float BonusResistance => MistActive ? ByRank(1, 22, 24, 26, 28, 30) + AP * .07f : 0;
        public override bool CanRecast(int slot) => (slot == 1 && MistActive && !mistMoved) || (slot == 3 && RStage > 0 && Time.time < rWindowUntil);
        public override bool HoldToCast(int slot) => slot == 3;

        public override string SlotStatus(int slot) => slot switch
        {
            0 when QStacks > 0 && Player.Cooldown(0) <= 0 => $"READY +{QStacks}",
            1 when MistActive => $"MIST {MistRemaining:0.0}s",
            3 when NeedlesReady > 0 => $"THROW {NeedlesReady}",
            3 when RStage > 0 => $"RECAST {RStage + 1} {rWindowUntil - Time.time:0}s",
            _ => null,
        };

        public override string StateText => Empowered ? "SKIP 'N SLASH: EMPOWERED" : QStacks > 0 ? $"SNIP STACKS {QStacks}/4" : "A THOUSAND CUTS";

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            t = Definition.kitTuning as GwenTuning;
            if (!t)
                t = ScriptableObject.CreateInstance<GwenTuning>();
            needle = Definition.kitPrefab;
            threadMaterial = Definition.kitMaterial ? Definition.kitMaterial : AbilityFx.Material(Cyan);
            avatar = Player.GetComponent<GwenAvatar>();
            ResetState();
            BuildMist();
        }

        public override void OnUnequip()
        {
            ResetState();
            if (dome)
                Object.Destroy(dome);
            if (domeMaterial)
                Object.Destroy(domeMaterial);
            dome = null;
            mistRing = null;
            mistMotes = null;
            mistThreads.Clear();
        }

        void ResetState()
        {
            QStacks = RStage = NeedlesReady = 0;
            mistUntil = empoweredUntil = stackUntil = rWindowUntil = rNext = 0;
            qCasting = mistMoved = eRefunded = false;
            needleVictims.Clear();
        }

        public override void OnDeath()
        {
            ResetState();
            UpdateMist();
        }

        public override void Tick()
        {
            if (Time.time > stackUntil)
                QStacks = 0;
            if (RStage > 0 && Time.time > rWindowUntil && NeedlesReady == 0)
                RStage = 0;
            if (MistActive && Geo.FlatDistance(Player.Feet, MistCenter) > t.wRadius)
            {
                // Leaving the mist once carries it along; leaving it again ends it.
                if (!mistMoved)
                {
                    MistCenter = Player.Feet;
                    mistMoved = true;
                }
                else
                    mistUntil = 0;
            }
            UpdateMist();
        }

        // ---------- Passive: A Thousand Cuts ----------

        float ApplyPassive(Combatant target, Vector3 at)
        {
            if (!target || !target.IsAlive || target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                return 0;
            float amount = target.maxHealth * (t.passiveMaxHealthFraction + AP * .00006f);
            if (target.GetComponent<RiftObjective>())
                amount = Mathf.Min(amount, 3 + .05f * AP);
            else if (target.GetComponent<RiftMinion>() && target.Health < target.maxHealth * .4f)
                amount += Mathf.Lerp(8, 30, (Player.Level - 1) / 17f);
            float dealt = Player.Hit(target, amount, DamageKind.Magic, "Passive", false, at);
            if (target.countsAsChampion && dealt > 0)
                Health.Heal(Mathf.Min(dealt * t.passiveChampionHealFraction, Mathf.Lerp(12, 40, (Player.Level - 1) / 17f) + .07f * AP));
            if (dealt > 0)
                Stitch(target.AimPosition);
            return dealt;
        }

        /// <summary>A Thousand Cuts tell: a small glowing stitch where the thread cut lands.</summary>
        void Stitch(Vector3 at)
        {
            var toHead = (Player.head.transform.position - at).normalized;
            var side = Vector3.Cross(toHead, Vector3.up).normalized * .12f;
            var up = Vector3.up * .12f;
            Player.Beam(at - side - up, at + side + up, .02f, .22f);
            Player.Beam(at - side + up, at + side - up, .02f, .22f);
        }

        // ---------- Attacks ----------

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            ApplyPassive(target, target.AimPosition);
            if (Empowered)
            {
                Player.Hit(target, 15 + AP * .2f, DamageKind.Magic, "E");
                if (!eRefunded)
                {
                    Player.RefundCooldown(2, ByRank(2, .25f, .35f, .45f, .55f, .65f));
                    eRefunded = true;
                }
            }
            QStacks = Mathf.Min(4, QStacks + 1);
            stackUntil = Time.time + t.qStackLifetime;
        }

        /// <summary>The cutting edge of the real scissors: from just outside the fist to the blade tips.</summary>
        public override bool WeaponEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (!avatar || !avatar.isActiveAndEnabled || Player.WeaponPose == null || !avatar.scissorsRoot.gameObject.activeInHierarchy)
                return false;
            var pose = Player.WeaponPose();
            var forward = pose.rotation * Vector3.forward;
            from = pose.position + forward * .12f;
            to = pose.position + forward * avatar.BladeLength;
            return true;
        }

        // ---------- Abilities ----------

        public override bool Cast(int slot) => slot switch
        {
            0 => SnipSnip(),
            1 => HallowedMist(),
            2 => SkipNSlash(),
            _ => false,
        };

        bool SnipSnip()
        {
            Vector3 direction = Player.PlanarDirection(Player.AttackDirection);
            if (!Player.Commit(0, false, Player.AttackOrigin, direction))
                return false;
            int snips = 2 + QStacks;
            QStacks = 0;
            Player.StartCoroutine(Snip(snips, direction));
            return true;
        }

        IEnumerator Snip(int snips, Vector3 forward)
        {
            qCasting = true;
            var audio = Player.GetComponent<ChampionAudio>();
            var feet = Player.Feet;
            // Giant spectral scissors appear in front, tilted like the real ones, and close once per snip.
            var spectral = SpectralScissors(feet, forward, snips);
            for (int i = 0; i < snips && Health.IsAlive; i++)
            {
                Vector3 start = Player.Feet - forward * .5f;
                Player.Emit("Snip", start, forward);
                audio?.Snip(i, snips);
                if (spectral)
                    spectral.Snip(i == snips - 1);
                bool final = i == snips - 1;
                float baseDamage = final ? ByRank(0, 60, 85, 110, 135, 160) + AP * .35f : ByRank(0, 10, 14, 18, 22, 26) + AP * .05f;
                foreach (var target in Player.SweepTargets(start + Vector3.up * .9f, forward, t.qRange + .5f, t.qHalfWidth))
                {
                    if (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                        continue;
                    Vector3 delta = Vector3.ProjectOnPlane(target.AimPosition - start, Vector3.up);
                    bool centre = Vector3.Cross(delta, forward).magnitude <= t.qCenterHalfWidth + .15f;
                    bool minion = target.GetComponent<RiftMinion>();
                    float amount = baseDamage * (minion ? t.qMinionModifier : 1);
                    // The centre of the cut converts half the damage to true damage and applies A Thousand Cuts.
                    float dealt = Player.Hit(target, amount * (centre ? .5f : 1), DamageKind.Magic, "Q", false, start);
                    if (centre)
                    {
                        dealt += Player.Hit(target, amount * .5f, DamageKind.True, "Q", false, start);
                        if (dealt > 0)
                            ApplyPassive(target, target.AimPosition);
                    }
                    // Snip Snip! executes minions left below 20% health.
                    if (minion && target.IsAlive && target.Health < target.maxHealth * t.qExecuteThreshold)
                        dealt += Player.Hit(target, target.Health + 1, DamageKind.True, "Q");
                    if (dealt > 0)
                    {
                        Player.Emit("Hit", target.AimPosition, forward);
                        audio?.SnipHit(target.AimPosition, final);
                    }
                }
                if (i < snips - 1)
                    yield return new WaitForSeconds(t.qDuration / (snips - 1));
            }
            qCasting = false;
        }

        SpectralScissorsFx SpectralScissors(Vector3 feet, Vector3 forward, int snips)
        {
            if (!avatar || !avatar.bladeA || !avatar.bladeB)
                return null;
            var go = new GameObject("Spectral scissors");
            var weapon = Player.WeaponPose();
            float roll = Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(weapon.rotation * Vector3.up, forward), forward);
            go.transform.SetPositionAndRotation(feet + Vector3.up * .9f + forward * (t.qRange * .25f), Quaternion.LookRotation(forward) * Quaternion.Euler(0, 0, Mathf.Clamp(roll, -40, 40)));
            go.transform.localScale = Vector3.one * avatar.WeaponScale * 2.2f;
            var fx = go.AddComponent<SpectralScissorsFx>();
            fx.Build(avatar.bladeA, avatar.bladeB, Cyan, t.qDuration + .35f);
            Player.Track(go);
            return fx;
        }

        bool HallowedMist()
        {
            if (MistActive && !mistMoved)
            {
                if (Time.time < wRecastAt || !Player.Commit(1, true, Player.Feet, Vector3.up))
                    return false;
                MistCenter = Player.Feet;
                mistMoved = true;
                return true;
            }
            if (!Player.Commit(1, false, Player.Feet, Vector3.up))
                return false;
            MistCenter = Player.Feet;
            mistMoved = false;
            mistUntil = Time.time + t.wDuration;
            wRecastAt = Time.time + .5f;
            if (mistMotes)
                mistMotes.Play();
            return true;
        }

        bool SkipNSlash()
        {
            // Dash where the player is walking; standing still, dash where the blades point.
            Vector3 direction = Player.LocomotionDirection(out bool walking);
            if (!walking)
                direction = Player.PlanarDirection(Player.AttackDirection);
            Vector3 from = Player.Feet;
            if (Health.Rooted || !Player.StepPoint(from + direction * t.eDistance, out _))
                return false;
            if (!Player.Commit(2, false, from, direction))
                return false;
            Afterimage();
            Player.ComfortBlink(.12f);
            Player.Dash(direction, t.eDistance);
            empoweredUntil = Time.time + t.eDuration;
            eRefunded = false;
            Player.ResetAttackTimer();
            return true;
        }

        /// <summary>A fading cyan copy of Gwen left where the dash began.</summary>
        void Afterimage()
        {
            if (!avatar || !avatar.Skin)
                return;
            var mesh = new Mesh { name = "Gwen afterimage" };
            avatar.Skin.BakeMesh(mesh, true);
            var t0 = avatar.Skin.transform;
            var ghost = AbilityFx.Ghost(mesh, t0.position, t0.rotation, Vector3.one, new Color(.45f, .95f, 1f, .45f), .5f);
            ghost.GetComponent<GhostFade>().destroyMesh = true;
            Player.Track(ghost);
        }

        // R: hold to ready the volley, release (ideally mid-throw) to hurl it.

        public override bool BeginHold(int slot)
        {
            if (slot != 3 || Time.time < rNext)
                return false;
            bool recast = RStage > 0 && Time.time < rWindowUntil;
            if (!recast && (Player.Cooldown(3) > 0 || (Player.Economy && Definition.UsesMana && Player.Economy.Mana < Definition.spells[3].Cost(Rank(3)))))
                return false;
            NeedlesReady = 2 * (RStage + 1) - 1;
            return true;
        }

        public override void CancelHold(int slot) => NeedlesReady = 0;

        public override void ReleaseHold(int slot)
        {
            int count = NeedlesReady;
            NeedlesReady = 0;
            if (slot != 3 || count == 0)
                return;
            bool recast = RStage > 0 && Time.time < rWindowUntil;
            Vector3 start = avatar && avatar.Rig != null ? avatar.Rig.Palm(true) : Player.OffHandOrigin;
            // A real throw aims with the hand's motion; a plain release aims along the hand.
            Vector3 throwVelocity = Player.HandVelocity(true, true);
            Vector3 aim = throwVelocity.magnitude > 1.6f ? throwVelocity.normalized : Player.OffHandDirection.normalized;
            aim = FlattenAim(aim);
            if (!Player.Commit(3, recast, start, aim))
                return;
            if (!recast)
                needleVictims.Clear();
            rWindowUntil = Time.time + 8;
            int stage = RStage + 1;
            RStage = stage >= 3 ? 0 : stage;
            rNext = Time.time + 1;
            Volley(count, start, aim);
        }

        /// <summary>Keeps throws near the ground plane where units stand, while still honouring the throw's heading.</summary>
        static Vector3 FlattenAim(Vector3 aim)
        {
            var flat = Geo.Flat(aim);
            if (flat.sqrMagnitude < .01f)
                return aim;
            float y = Mathf.Clamp(aim.y, -.12f, .06f);
            return (flat.normalized * Mathf.Sqrt(1 - y * y) + Vector3.up * y).normalized;
        }

        void Volley(int count, Vector3 start, Vector3 aim)
        {
            const float spread = 7.5f;
            float firstSlow = ByRank(3, .7f, .55f, .4f), laterSlow = ByRank(3, .85f, .8f, .75f);
            for (int i = 0; i < count; i++)
            {
                int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                var direction = Quaternion.AngleAxis(side * spread, Vector3.up) * aim;
                var p = AbilityProjectile.Launch(Player, start, direction, ByRank(3, 30, 50, 70) + AP * .1f, DamageKind.Magic, "R", t.rSpeed, t.rRange, true, Tint, needle);
                p.ignoreStructures = true;
                p.slowFor = target => needleVictims.Add(target) ? firstSlow : laterSlow;
                p.slowDuration = 1.5f;
                p.hit = (target, dealt) =>
                {
                    ApplyPassive(target, target.AimPosition);
                    Player.Emit("Hit", target.AimPosition, direction);
                    Player.GetComponent<ChampionAudio>()?.NeedleHit(target);
                };
                // The needle pulls a glowing thread behind it.
                var thread = p.gameObject.AddComponent<TrailRenderer>();
                thread.sharedMaterial = AbilityFx.Glass(new Color(.55f, 1f, 1f, .8f), true);
                thread.time = .35f;
                thread.startWidth = .012f;
                thread.endWidth = 0;
                thread.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---------- Mist: untargetable from outside, except by towers ----------

        public override bool HiddenFrom(Combatant attacker)
        {
            return MistActive && attacker && !attacker.GetComponent<RiftStructure>() && Geo.FlatDistance(attacker.AimPosition, MistCenter) > t.wRadius;
        }

        public override bool Blocks(DamageHit hit)
        {
            return MistActive && hit.source && !hit.source.GetComponent<RiftStructure>() && Geo.FlatDistance(hit.source.AimPosition, MistCenter) > t.wRadius;
        }

        void BuildMist()
        {
            mistThreads.Clear();
            dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(dome.GetComponent<Collider>());
            dome.name = "Hallowed Mist";
            domeMaterial = new Material(AbilityFx.Glass(new Color(.55f, .95f, 1f, .1f), false, true)) { hideFlags = HideFlags.HideAndDontSave };
            var r = dome.GetComponent<Renderer>();
            r.sharedMaterial = domeMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ringGo = new GameObject("Hallowed Mist edge");
            ringGo.transform.SetParent(dome.transform, false);
            mistRing = ringGo.AddComponent<LineRenderer>();
            mistRing.sharedMaterial = threadMaterial;
            mistRing.useWorldSpace = true;
            mistRing.loop = true;
            mistRing.positionCount = 64;
            mistRing.startWidth = mistRing.endWidth = .04f;
            mistRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mistMotes = AbilityFx.Motes(dome.transform, new Color(.6f, 1f, 1f, .8f), .07f, 1.8f, 140, ParticleSystemShapeType.Hemisphere, .5f);
            // Glowing threads rise and twist around the edge of the mist.
            for (int i = 0; i < 14; i++)
            {
                var threadGo = new GameObject("Hallowed Mist thread");
                threadGo.transform.SetParent(dome.transform, false);
                var thread = threadGo.AddComponent<LineRenderer>();
                thread.sharedMaterial = AbilityFx.Glass(new Color(.55f, 1f, 1f, .75f), true);
                thread.useWorldSpace = true;
                thread.positionCount = 6;
                thread.widthCurve = new AnimationCurve(new Keyframe(0, .03f), new Keyframe(1, .002f));
                thread.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mistThreads.Add(thread);
            }
            dome.SetActive(false);
        }

        void UpdateMist()
        {
            if (!dome || !mistRing)
                return;
            bool active = MistActive;
            if (dome.activeSelf != active)
                dome.SetActive(active);
            if (!active)
                return;
            float radius = t.wRadius;
            dome.transform.position = MistCenter;
            dome.transform.localScale = Vector3.one * radius * 2;
            // The dome breathes gently and thins out in its last second.
            float fade = Mathf.Clamp01(MistRemaining);
            domeMaterial.SetColor("_BaseColor", new Color(.6f, .95f, 1f, (.17f + .04f * Mathf.Sin(Time.time * 3)) * fade));
            for (int i = 0; i < mistThreads.Count; i++)
            {
                float baseAngle = i * Mathf.PI * 2 / mistThreads.Count + Time.time * .35f;
                float height = 1.4f + .5f * Mathf.Sin(Time.time * 1.7f + i);
                for (int k = 0; k < 6; k++)
                {
                    float u = k / 5f;
                    float a = baseAngle + u * .6f;
                    float r = radius * (1 - .25f * u * u);
                    mistThreads[i].SetPosition(k, MistCenter + new Vector3(Mathf.Cos(a) * r, .05f + u * height, Mathf.Sin(a) * r));
                }
            }
            for (int i = 0; i < mistRing.positionCount; i++)
            {
                float a = i * Mathf.PI * 2 / mistRing.positionCount;
                mistRing.SetPosition(i, MistCenter + new Vector3(Mathf.Cos(a) * radius, .05f, Mathf.Sin(a) * radius));
            }
        }
    }

    /// <summary>Q visual: a giant translucent pair of Gwen's scissors that snaps shut on every snip, then fades.</summary>
    public class SpectralScissorsFx : MonoBehaviour
    {
        Transform a, b;
        Quaternion aRest, bRest;
        Material material;
        Color color;
        float snapAt = -1, born, life;
        bool final;

        public void Build(Transform bladeA, Transform bladeB, Color tint, float lifetime)
        {
            color = new Color(tint.r, tint.g, tint.b, .45f);
            material = new Material(AbilityFx.Glass(color, true)) { hideFlags = HideFlags.HideAndDontSave };
            a = Copy(bladeA);
            b = Copy(bladeB);
            aRest = a.localRotation;
            bRest = b.localRotation;
            born = Time.time;
            life = lifetime;
        }

        Transform Copy(Transform blade)
        {
            var go = new GameObject(blade.name + " (spectral)");
            go.transform.SetParent(transform, false);
            go.transform.SetLocalPositionAndRotation(blade.localPosition, blade.localRotation);
            go.AddComponent<MeshFilter>().sharedMesh = blade.GetComponent<MeshFilter>().sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public void Snip(bool last)
        {
            snapAt = Time.time;
            final = last;
        }

        void Update()
        {
            // Open wide between snips, slam shut on each snip; the final snip closes hardest.
            float sinceSnap = snapAt < 0 ? 1 : Time.time - snapAt;
            float open = Mathf.Lerp(final ? 0 : 4, 28, Mathf.Clamp01(sinceSnap / .12f));
            a.localRotation = aRest * Quaternion.AngleAxis(open, Vector3.right);
            b.localRotation = bRest * Quaternion.AngleAxis(-open, Vector3.right);
            float age = (Time.time - born) / Mathf.Max(.01f, life);
            var c = color;
            c.a *= age < .15f ? age / .15f : 1 - Mathf.Clamp01((age - .7f) / .3f);
            material.SetColor("_BaseColor", c);
            if (age >= 1)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material)
                Destroy(material);
        }
    }
}
