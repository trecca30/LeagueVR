using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen, the Hallowed Seamstress, built for VR. Numbers follow League (wiki, 26.x); the interactions are physical:
    /// <list type="bullet">
    /// <item>Attacks: swing the real scissors through enemies, or hold the trigger and she attacks the enemy in front with
    /// League's own moves in turn (a thrust, a sweep, a backhand, a lunge on a critical strike); the hit lands as the
    /// blades snap shut.</item>
    /// <item>A Thousand Cuts: every hit stitches a max-health cut and heals Gwen against champions.</item>
    /// <item>Q Snip Snip!: her left hand takes the second handle and both hands snip the scissors in front of her, once per
    /// stored stack plus two, while giant spectral scissors snip ahead; the centre line deals true damage.</item>
    /// <item>W Hallowed Mist: the scissors twirl and the mist blooms from her open left palm into a dome you stand inside;
    /// attackers outside cannot target you. Recast pulls it to you.</item>
    /// <item>E Skip 'n Slash: she dashes where you walk (or where the blades point) with both hands on the scissors and
    /// slashes the enemy in front at the end; for a while her attacks are two-handed snips, faster and longer.</item>
    /// <item>R Needlework: hold the left trigger to ready 1 / 3 / 5 needles between your fingers, threaded from the
    /// scissors' tips; release (or throw) to hurl them, and the scissors snip the thread.</item>
    /// </list>
    /// </summary>
    public class GwenKit : MeleeKit
    {
        static readonly Color Cyan = new(.45f, .95f, 1f, 1f);

        /// <summary>
        /// One of Gwen's own particle textures, decoded from the base skin in the player's League install
        /// (Champions/Art/Gwen/Resources/GwenFx); drawn with the shared additive sprite and beam helpers.
        /// </summary>
        static string Fx(string name) => "GwenFx/" + name;

        GwenTuning t;
        GameObject needle;
        Material threadMaterial;
        GwenAvatar avatar;

        public int QStacks { get; private set; }
        public int RStage { get; private set; }
        public int NeedlesReady { get; private set; }
        /// <summary>The needles the hand shows: a plain release keeps them in the fingers until the flick.</summary>
        public int NeedlesShown => volleyAt >= 0 ? volleyCount : NeedlesReady;
        public Vector3 MistCenter { get; private set; }
        public bool MistActive => Time.time < mistUntil && Health.IsAlive;
        public float MistRemaining => Mathf.Max(0, mistUntil - Time.time);
        public bool Empowered => Time.time < empoweredUntil;

        float stackUntil, mistUntil, wRecastAt, empoweredUntil, rWindowUntil, rNext;
        bool qCasting, mistMoved, eRefunded;
        float mistBloom = -1;

        GwenMoves moves;
        // The trigger's attack under way and its target; how many basic attacks so far (they cycle League's three).
        Combatant swingTarget;
        bool swingPending;
        float swingStrikeAt;
        int swingCount;
        // A plain Needlework release: the needles leave the fingers partway through the flick.
        float volleyAt = -1;
        int volleyCount;
        Vector3 volleyAim;

        /// <summary>True while Skip 'n Slash carries her along the ground.</summary>
        public bool Dashing { get; private set; }
        bool slashFromDash;

        /// <summary>Skip 'n Slash's dash: League's dash animation lasts 0.27 s.</summary>
        const float DashTime = .25f;
        readonly HashSet<Combatant> needleVictims = new();
        GameObject dome;
        Material domeMaterial, ringMaterial;
        Transform mistGround;
        LineRenderer mistRing;
        ParticleSystem mistMotes;
        readonly List<LineRenderer> mistThreads = new();

        // A move with the scissors (an attack, the snips, the dash, the flourish) holds the next attack until it ends.
        public override bool Busy => qCasting || Dashing || (moves != null && moves.Current is GwenMoves.Move.Thrust or GwenMoves.Move.Sweep
            or GwenMoves.Move.Backhand or GwenMoves.Move.Lunge or GwenMoves.Move.TwinSnip or GwenMoves.Move.Mist);
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

        public override string TriggerHint => "Swing the scissors through enemies, or hold and she attacks the one in front";

        public override string ButtonHint(int slot) => slot switch
        {
            0 => "Q: Snip Snip! with both hands",
            1 => "W: Hallowed Mist (recast pulls it to you)",
            2 => "E: Skip 'n Slash where you walk or where the blades point",
            3 => "R: hold, then release (or throw) to hurl the needles",
            _ => null,
        };

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
            if (avatar)
            {
                moves = new GwenMoves(Player, avatar);
                avatar.ScissorsOverride = moves.Scissors;
                avatar.BladeOpenOverride = moves.BladeOpen;
                avatar.TrailOverride = () => moves.Trailing;
                avatar.GripOverride = moves.Grip;
                avatar.HandOverride = moves.Fingers;
            }
        }

        public override void OnUnequip()
        {
            EndDash();
            if (avatar)
            {
                avatar.ScissorsOverride = null;
                avatar.BladeOpenOverride = null;
                avatar.TrailOverride = null;
                avatar.GripOverride = null;
                avatar.HandOverride = null;
            }
            moves = null;
            ResetState();
            if (dome)
                Object.Destroy(dome);
            if (domeMaterial)
                Object.Destroy(domeMaterial);
            if (ringMaterial)
                Object.Destroy(ringMaterial);
            dome = null;
            mistGround = null;
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
            swingPending = false;
            swingTarget = null;
            volleyAt = -1;
            mistBloom = -1;
            moves?.Stop();
        }

        public override void OnDeath()
        {
            EndDash();
            ResetState();
            UpdateMist();
        }

        public override void Tick()
        {
            if (swingPending && Time.time >= swingStrikeAt)
                LandSwing();
            if (volleyAt >= 0 && Time.time >= volleyAt)
            {
                volleyAt = -1;
                if (Health.IsAlive)
                    Volley(volleyCount, moves != null ? moves.ThrowPoint() : Player.OffHandOrigin, volleyAim);
            }
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

        /// <summary>A Thousand Cuts tell: a glowing stitch where the thread cut lands, with Gwen's own star flare on it.</summary>
        void Stitch(Vector3 at)
        {
            var toHead = (Player.head.transform.position - at).normalized;
            var side = Vector3.Cross(toHead, Vector3.up).normalized * .12f;
            var up = Vector3.up * .12f;
            Player.Beam(at - side - up, at + side + up, .02f, .22f);
            Player.Beam(at - side + up, at + side - up, .02f, .22f);
            ZeriSprite.Create(Player, at, Fx("GwenFx_Flare"), new Color(.6f, 1f, 1f, 1f), .35f, .7f, .25f);
        }

        // ---------- Attacks ----------

        /// <summary>
        /// The trigger's attack: she attacks the enemy in front herself with League's moves in turn (Attack1's thrust,
        /// Attack2's sweep, Attack3's backhand; the lunge when the hit will be a critical strike, rolled now so the lunge
        /// shows from the start), or a two-handed snip while Skip 'n Slash empowers her. The hit lands as the blades snap.
        /// </summary>
        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            bool fromDash = slashFromDash;
            slashFromDash = false;
            var targets = Player.SweepTargets(origin, direction, Player.AttackReach, .45f);
            if (targets.Count == 0)
                return;
            var target = targets[0];
            Vector3 toward = Geo.FlatDirection(target.transform.position - Player.Feet, Player.PlanarDirection(Player.head.transform.forward));
            float seconds;
            if (moves == null)
                seconds = 0;
            else if (Empowered)
            {
                seconds = Mathf.Clamp(Player.AttackInterval * .55f, .26f, .4f);
                moves.StartTwinSnip(seconds, toward, fromDash);
                seconds *= GwenMoves.TwinStrike;
            }
            else
            {
                seconds = Mathf.Clamp(Player.AttackInterval * .6f, .3f, .5f);
                bool crit = Player.Economy && Player.Economy.RollNextCritical();
                var kind = crit ? GwenMoves.Move.Lunge : (swingCount++ % 3) switch { 0 => GwenMoves.Move.Thrust, 1 => GwenMoves.Move.Sweep, _ => GwenMoves.Move.Backhand };
                moves.StartSwing(kind, seconds, toward);
                seconds *= GwenMoves.Strike;
            }
            swingTarget = target;
            swingPending = true;
            swingStrikeAt = Time.time + seconds;
            if (seconds <= 0)
                LandSwing();
        }

        /// <summary>
        /// True when the blade's edge passes within reach of the target, in three dimensions: the blade stops about 1.1 m
        /// from the eyes, so a target further off than that is not hit by a blade that cannot reach it.
        /// </summary>
        bool BladeReaches(Combatant target)
        {
            if (!WeaponEdge(out var from, out var to))
                return Geo.FlatDistance(target.transform.position, Player.Feet) <= Player.AttackReach;
            Vector3 p = target.AimPosition, ab = to - from;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - from, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(p, from + ab * t) <= .6f;
        }

        /// <summary>
        /// The blades snap shut: the attack hits its target if it is still within reach (+1 m: it may have stepped back).
        /// A miss spends nothing, so the next attack may start at once.
        /// </summary>
        void LandSwing()
        {
            swingPending = false;
            var target = swingTarget;
            swingTarget = null;
            if (!target || !target.IsAlive || !BladeReaches(target))
            {
                Player.ResetAttackTimer();
                return;
            }
            float dealt = Player.Hit(target, AttackDamage(target), DamageKind.Physical, "Attack1", true, target.AimPosition);
            if (dealt <= 0)
                return;
            Player.Emit("Hit", target.AimPosition, Player.AttackDirection);
            OnAttackHit(target, dealt);
        }

        /// <summary>An ability cuts a basic attack short (a hit that already landed stays).</summary>
        void CancelSwing()
        {
            swingPending = false;
            swingTarget = null;
            moves?.Stop();
        }

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
            // League chimes when Snip Snip! reaches its fourth stack.
            if (QStacks == 3)
                Player.GetComponent<ChampionAudio>()?.Cue("QMaxStacks", .5f);
            QStacks = Mathf.Min(4, QStacks + 1);
            stackUntil = Time.time + t.qStackLifetime;
        }

        /// <summary>The cutting edge of the real scissors: from just outside the fist to the blade tips.</summary>
        public override bool WeaponEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (!avatar || !avatar.isActiveAndEnabled || Player.WeaponPose == null || !avatar.WeaponReady)
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
            CancelSwing();
            moves?.StartSnip(snips, t.qDuration, direction);
            Player.StartCoroutine(Snip(snips, direction));
            return true;
        }

        IEnumerator Snip(int snips, Vector3 forward)
        {
            qCasting = true;
            var audio = Player.GetComponent<ChampionAudio>();
            // Her left hand takes the second handle first; then both hands snip.
            if (moves != null)
                yield return new WaitForSeconds(GwenMoves.SnipSetup);
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
                SnipMarks(forward, final);
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
                        audio?.SnipHit(target.AimPosition, final, centre);
                    }
                }
                if (i < snips - 1)
                    yield return new WaitForSeconds(t.qDuration / (snips - 1));
            }
            qCasting = false;
        }

        /// <summary>
        /// League's cut marks: every snip scratches curved slashes across the ground in front, lined up with the cut;
        /// the final snip adds the bright star-and-line along the centre, where the true damage lands.
        /// </summary>
        void SnipMarks(Vector3 forward, bool final)
        {
            Vector3 middle = Player.Feet + forward * (t.qRange * .5f);
            float heading = Quaternion.LookRotation(forward).eulerAngles.y;
            var scratch = ZeriSprite.Create(Player, middle + Vector3.up * .05f, Fx("GwenFx_Scratch"), new Color(.55f, 1f, 1f, .9f), t.qRange * .5f, t.qRange * .6f, .45f, flat: true);
            scratch.Roll = heading;
            if (final)
                ZeriSprite.Create(Player, middle + Vector3.up * .9f, Fx("GwenFx_CentreCut"), new Color(.85f, 1f, 1f, 1f), t.qRange * .35f, t.qRange * .45f, .35f, axis: forward);
        }

        SpectralScissorsFx SpectralScissors(Vector3 feet, Vector3 forward, int snips)
        {
            if (!avatar || !avatar.bladeA || !avatar.bladeB)
                return null;
            var go = new GameObject("Spectral scissors");
            var weapon = Player.WeaponPose != null ? Player.WeaponPose() : new Pose(Player.AttackOrigin, Quaternion.LookRotation(Player.AttackDirection));
            float roll = Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(weapon.rotation * Vector3.up, forward), forward);
            // Far enough ahead (and a little smaller) that the closing blades do not fill the view in the headset.
            go.transform.SetPositionAndRotation(feet + Vector3.up * .9f + forward * (t.qRange * .75f), Quaternion.LookRotation(forward) * Quaternion.Euler(0, 0, Mathf.Clamp(roll, -40, 40)));
            go.transform.localScale = Vector3.one * avatar.WeaponScale * 1.1f;
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
                mistBloom = Time.time;
                CancelSwing();
                moves?.StartMist(Player.PlanarDirection(Player.head.transform.forward));
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
            // The scissors twirl and the mist blooms from her open left palm.
            CancelSwing();
            mistBloom = Time.time;
            moves?.StartMist(Player.PlanarDirection(Player.head.transform.forward));
            Vector3 palm = moves != null ? moves.MistSource() : Player.OffHandOrigin;
            ZeriSprite.Create(Player, palm, Fx("GwenFx_Flare"), new Color(.6f, 1f, 1f, .9f), .1f, .5f, .45f);
            ZeriSprite.Create(Player, palm, Fx("GwenFx_StarBlue"), new Color(.7f, 1f, 1f, .8f), .08f, .3f, .6f);
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
            CancelSwing();
            Afterimage();
            empoweredUntil = Time.time + t.eDuration;
            eRefunded = false;
            Player.ResetAttackTimer();
            if (ComfortSettings.RushCharges && Player.StepPoint(from + direction * t.eDistance, out var landing))
            {
                Player.StartCoroutine(Dash(from, landing, direction));
                return true;
            }
            Player.ComfortBlink(.12f);
            moves?.StartDash(.15f, direction);
            Player.Dash(direction, t.eDistance);
            DashDone(from, direction);
            return true;
        }

        /// <summary>
        /// Skip 'n Slash in first person: both hands take the scissors and the view rushes along the dash behind the comfort
        /// vignette; at the end she slashes the enemy in front with both hands (the dash resets her attack).
        /// </summary>
        IEnumerator Dash(Vector3 from, Vector3 landing, Vector3 direction)
        {
            Dashing = true;
            Player.FreezeLocomotion(true);
            Player.RushVignette(true);
            moves?.StartDash(DashTime + .1f, direction);
            float begin = Time.time, height = from.y;
            while (Player.Kit == this && Health.IsAlive)
            {
                float u = Mathf.Clamp01((Time.time - begin) / DashTime);
                Vector3 at = Vector3.Lerp(from, landing, u * u * (3 - 2 * u));
                if (RiftMatch.Instance && RiftMatch.Instance.Ground(new Vector3(at.x, height + .1f, at.z), out var floor) && Mathf.Abs(floor.y - height) < .6f)
                    height = Mathf.MoveTowards(height, floor.y, 3f * Time.deltaTime);
                at.y = height;
                height = at.y;
                Player.MoveFeet(at);
                if (u >= 1)
                    break;
                yield return null;
            }
            bool landed = Dashing && Player.Kit == this && Health.IsAlive;
            EndDash();
            if (landed)
                DashDone(from, direction);
        }

        void EndDash()
        {
            if (!Dashing)
                return;
            Dashing = false;
            Player.RushVignette(false);
            Player.FreezeLocomotion(false);
        }

        /// <summary>The dash's cyan streak, and the two-handed slash on the enemy in front, if there is one.</summary>
        void DashDone(Vector3 from, Vector3 direction)
        {
            ZeriBeam.Create(Player, from + Vector3.up * .9f, Player.Feet + Vector3.up * .9f, new Color(.5f, .95f, 1f, 1f), 1f, .45f, .4f, 0, texture: Fx("GwenFx_DashTrail"));
            Player.ResetAttackTimer();
            slashFromDash = true;
            Player.BasicAttack();
            slashFromDash = false;
        }

        /// <summary>A fading cyan copy of Gwen left where the dash began.</summary>
        void Afterimage()
        {
            if (!avatar || !avatar.Skin)
                return;
            var mesh = new Mesh { name = "Gwen afterimage" };
            avatar.Skin.BakeMesh(mesh, true);
            var t0 = avatar.Skin.transform;
            var ghost = AbilityFx.Ghost(mesh, t0.position, t0.rotation, Vector3.one, new Color(.45f, .95f, 1f, .6f), .6f);
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
            // A window that ran out while needles were held starts a fresh first cast.
            if (!recast)
                RStage = 0;
            moves?.MeasurePalm();
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
            // The window ran out while the needles were held: this is a new first cast (the audio reads RStage too).
            if (!recast)
                RStage = 0;
            Vector3 start = avatar && avatar.Rig != null ? avatar.Rig.Palm(true) : Player.OffHandOrigin;
            // A real throw aims with the hand's motion; a plain release aims along the hand.
            Vector3 throwVelocity = Player.HandVelocity(true, true);
            Vector3 aim = throwVelocity.magnitude > 1.6f ? throwVelocity.normalized : Player.OffHandDirection.normalized;
            aim = FlattenAim(aim);
            if (!Player.Commit(3, recast, start, aim))
                return;
            if (!recast)
                needleVictims.Clear();
            // League: recast within 6 s, at most once a second.
            rWindowUntil = Time.time + t.rWindow;
            int stage = RStage + 1;
            RStage = stage >= 3 ? 0 : stage;
            rNext = Time.time + t.rRecastDelay;
            bool thrown = throwVelocity.magnitude > 1.6f;
            moves?.StartThrow(aim, !thrown);
            if (thrown || moves == null)
            {
                Volley(count, start, aim);
                return;
            }
            // A plain release: the hand flicks the needles out along the aim; they leave the fingers at the snap.
            volleyAt = Time.time + GwenMoves.ThrowRelease;
            volleyCount = count;
            volleyAim = aim;
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
            Player.GetComponent<ChampionAudio>()?.NeedlesThrown(count);
            for (int i = 0; i < count; i++)
            {
                int side = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
                var direction = Quaternion.AngleAxis(side * spread, Vector3.up) * aim;
                var p = AbilityProjectile.Launch(Player, start, direction, ByRank(3, 30, 50, 70) + AP * .1f, DamageKind.Magic, "R", t.rSpeed, t.rRange, true, Tint, needle);
                p.ignoreStructures = true;
                p.slowFor = target => needleVictims.Add(target) ? firstSlow : laterSlow;
                p.slowDuration = t.rSlowDuration;
                p.hit = (target, dealt) =>
                {
                    ApplyPassive(target, target.AimPosition);
                    Player.Emit("Hit", target.AimPosition, direction);
                    Player.GetComponent<ChampionAudio>()?.NeedleHit(target);
                };
                // The needle pulls a glowing thread behind it (Gwen's own thread strands texture).
                var thread = p.gameObject.AddComponent<TrailRenderer>();
                thread.sharedMaterial = ZeriFx.TexGlow(Fx("GwenFx_Thread"), new Color(.55f, 1f, 1f, .85f));
                thread.textureMode = LineTextureMode.Stretch;
                thread.time = .35f;
                thread.startWidth = .03f;
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
            domeMaterial = new Material(AbilityFx.Glass(new Color(.55f, .95f, 1f, .05f), false, true)) { hideFlags = HideFlags.HideAndDontSave };
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
            // League's mist ring on the ground, slowly turning (a flat quad as wide as the mist).
            var groundRing = new GameObject("Hallowed Mist ground ring");
            groundRing.transform.SetParent(dome.transform, false);
            groundRing.AddComponent<MeshFilter>().sharedMesh = ZeriFx.QuadMesh;
            var groundRenderer = groundRing.AddComponent<MeshRenderer>();
            ringMaterial = new Material(ZeriFx.TexGlow(Fx("GwenFx_MistRing"), Color.white)) { hideFlags = HideFlags.HideAndDontSave };
            groundRenderer.sharedMaterial = ringMaterial;
            groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mistGround = groundRing.transform;
            // Glowing threads (League's holy strings) rise and twist around the edge of the mist.
            var strings = ZeriFx.TexGlow(Fx("GwenFx_MistStrings"), new Color(.55f, 1f, 1f, 1f));
            for (int i = 0; i < 14; i++)
            {
                var threadGo = new GameObject("Hallowed Mist thread");
                threadGo.transform.SetParent(dome.transform, false);
                var thread = threadGo.AddComponent<LineRenderer>();
                thread.sharedMaterial = strings;
                thread.textureMode = LineTextureMode.Stretch;
                thread.useWorldSpace = true;
                thread.positionCount = 6;
                thread.widthCurve = new AnimationCurve(new Keyframe(0, .2f), new Keyframe(1, .04f));
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
            // A fresh mist blooms out from her over 0.4 s.
            float bloom = mistBloom < 0 ? 1 : Mathf.Lerp(.08f, 1, Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.time - mistBloom - .12f) / .4f)));
            float radius = t.wRadius * bloom;
            dome.transform.position = MistCenter;
            dome.transform.localScale = Vector3.one * radius * 2;
            // The dome breathes gently and thins out in its last second.
            float fade = Mathf.Clamp01(MistRemaining);
            // Light enough to see enemies through from inside; the ring and threads carry the shape.
            domeMaterial.SetColor("_BaseColor", new Color(.6f, .95f, 1f, (.13f + .03f * Mathf.Sin(Time.time * 3)) * fade));
            if (mistGround)
            {
                // The dome is scaled to the mist's diameter, so the unit quad under it spans the whole mist.
                mistGround.SetPositionAndRotation(MistCenter + Vector3.up * .04f, Quaternion.Euler(90, Time.time * 12, 0));
                ringMaterial.SetColor("_BaseColor", new Color(.55f, 1f, 1f, .9f * fade));
            }
            Vector3 palm = moves != null ? moves.MistSource() : MistCenter;
            Vector3 threadBase = Vector3.Lerp(palm, MistCenter, Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.time - mistBloom - .12f) / .4f)));
            for (int i = 0; i < mistThreads.Count; i++)
            {
                float baseAngle = i * Mathf.PI * 2 / mistThreads.Count + Time.time * .35f;
                float height = 1.4f + .5f * Mathf.Sin(Time.time * 1.7f + i);
                for (int k = 0; k < 6; k++)
                {
                    float u = k / 5f;
                    float a = baseAngle + u * .6f;
                    float r = radius * (1 - .25f * u * u);
                    mistThreads[i].SetPosition(k, threadBase + new Vector3(Mathf.Cos(a) * r, .05f + u * height, Mathf.Sin(a) * r));
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
