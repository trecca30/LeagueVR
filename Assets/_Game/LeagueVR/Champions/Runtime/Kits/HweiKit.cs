using System;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;
using Object = UnityEngine.Object;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Hwei, the Visionary, with League's kit (Data Dragon 16.20.1 and the wiki at V26.12), made physical for VR. Distances
    /// use the Rift's scale (League units / 103 = metres). His brush is in your right hand and is the aim for everything
    /// but the ultimate: hold a spell's button to see where it goes, let go to paint it.
    /// <list type="bullet">
    /// <item>Subjects: B opens Disaster (fire, the brush turns red-orange), X Serenity (water, cyan), A Torment (magenta).
    /// In a subject, B, X and A cast its three spells (League's QQ, QW, QE...): hold to aim with the brush, release to
    /// cast. Casting one ends the subject and starts its cooldown (mana is paid then); the left trigger is Wash Brush,
    /// which leaves the subject at no cost. A floating panel shows the open subject, its three spells and their buttons.</item>
    /// <item>Disaster: Devastating Fire (a fireball that bursts on the first enemy, + % max health), Severing Bolt (lightning
    /// on a far spot after 1 s; isolated or immobile enemies take more the more health they miss), Molten Fissure (seven
    /// eruptions along a line that leave slowing lava).</item>
    /// <item>Serenity: Fleeting Current (a stream that speeds him up while he stands in it), Pool of Reflection (a pool that
    /// shields him while he is inside), Stirring Lights (his next three attacks or spells deal bonus damage and restore
    /// mana).</item>
    /// <item>Torment: Grim Visage (a face that terrifies the first enemy), Gaze of the Abyss (an eye that strikes and roots
    /// an enemy that comes close), Crushing Maw (jaws that bite and drag enemies in, slowing them).</item>
    /// <item>R Spiraling Despair (left trigger, outside a subject): both hands take the staff and point it where you look;
    /// release to fire. It sticks to the first enemy champion it meets (or spreads where you looked), slows more and more
    /// and bursts after 3 s.</item>
    /// <item>Signature of the Visionary: a spell marks the enemies it hits; a different spell completes the signature, which
    /// bursts under them 0.85 s later.</item>
    /// </list>
    /// </summary>
    public class HweiKit : RangedKit
    {
        const float Units = 1 / 103f;
        public const float QQRange = 800 * Units, QQSpeed = 2000 * Units, QQWidth = 70 * Units, QQRadius = 175 * Units;
        public const float QWRange = 1900 * Units, QWDelay = 1, QWRadius = 225 * Units;
        public const float QERange = 1200 * Units, QEHalfWidth = 112 * Units, QEDelay = .6f, QEStep = .2f, QELava = 2.5f, QETick = .25f, QESlow = .35f;
        public const int QEExplosions = 7;
        public const float WQRange = 1200 * Units, WQHalfWidth = 112 * Units;
        public const float WWRange = 650 * Units, WWRadius = 350 * Units, WWTime = 3, WWTick = .264f;
        public const float WEWindow = 9;
        public const float EQRange = 1100 * Units, EQSpeed = 1300 * Units, EQWidth = 70 * Units;
        public const float EWRange = 900 * Units, EWTime = 3, EWArm = .65f, EWSight = 600 * Units, EWSpeed = 1700 * Units;
        public const float EERange = 800 * Units, EERadius = 170 * Units, EEDelay = .5f, EESlowDecay = 1.25f;
        public const float RRange = 1300 * Units, RSpeed = 1400 * Units, RWidth = 90 * Units, RRadius = 500 * Units, RTime = 3, RTick = .25f;
        public const int RMaxStacks = 12;
        public const float MarkTime = 4, MarkDelay = .85f, MarkRadius = 285 * Units;

        /// <summary>The three subjects' paints (from his icons) and his own gold.</summary>
        public static readonly Color Disaster = new(1f, .42f, .14f, 1f), Serenity = new(.3f, .82f, 1f, 1f), Torment = new(.86f, .22f, .8f, 1f);
        public static readonly Color Despair = new(.56f, .22f, .9f, 1f), Gold = new(1f, .78f, .36f, 1f), Wash = new(.7f, .52f, .7f, 1f);

        public static Color SubjectColor(int subject) => subject switch { 0 => Disaster, 1 => Serenity, 2 => Torment, _ => Gold };

        public static readonly string[] SubjectNames = { "Disaster", "Serenity", "Torment" };
        public static readonly string[] SpellCodes = { "QQ", "QW", "QE", "WQ", "WW", "WE", "EQ", "EW", "EE" };
        public static readonly string[] SpellNames =
        {
            "Devastating Fire", "Severing Bolt", "Molten Fissure",
            "Fleeting Current", "Pool of Reflection", "Stirring Lights",
            "Grim Visage", "Gaze of the Abyss", "Crushing Maw",
        };

        /// <summary>The button that casts each slot in VR: B, X, A and the left trigger.</summary>
        public static readonly string[] Buttons = { "B", "X", "A", "LT" };

        public static string SpellName(int subject, int variant) => SpellNames[subject * 3 + variant];

        HweiMoves moves;
        HweiVisuals visuals;
        HweiPanel panel;
        ChampionVRAvatar boundBody;

        int mood = -1, entering = -1, aiming = -1, painted = -1;
        bool aimingR;
        int castCount;
        // Stirring Lights: lights left, until when, and the last cast that used one (one light per cast).
        int lights, lightCast = -1;
        float lightsUntil;
        // The trigger's attack: the paint leaves the brush at the flick's snap.
        Combatant attackTarget;
        float attackRelease = -1;
        bool backhand;
        // Timed effects (a slow wearing off), and the spells waiting to leave the brush partway through their strokes.
        readonly List<(float at, Action action)> pending = new(), releases = new();
        readonly List<Zone> zones = new();
        readonly Dictionary<Combatant, Mark> marks = new();
        readonly List<Combatant> expired = new();

        /// <summary>A Signature of the Visionary mark: until when, which spell put it there, and its view.</summary>
        class Mark
        {
            public float until;
            public string ability;
            public GameObject view;
        }

        // ---------- What the HUD, the panel and the visuals read ----------

        /// <summary>The open subject (0 Disaster, 1 Serenity, 2 Torment), or -1.</summary>
        public int Mood => mood;
        /// <summary>The spell (0-2) being aimed in the open subject, or -1.</summary>
        public int Aiming => mood >= 0 ? aiming : -1;
        /// <summary>The subject whose paint is on the brush (it stays until the spell leaves the bristles), or -1.</summary>
        public int Painted => painted;
        public bool AimingUltimate => aimingR;
        public int Lights => Time.time < lightsUntil ? lights : 0;
        public float LightsLeft => Lights > 0 ? lightsUntil - Time.time : 0;
        public HweiMoves Moves => moves;
        /// <summary>When the last subject spell was cast (the panel flashes its card).</summary>
        public float LastCast { get; private set; } = -10;
        internal ChampionVRAvatar Avatar => boundBody;

        public override bool Busy => attackRelease >= 0 || (moves != null && moves.Casting);
        protected override float MissileSpeed => 2800 * Units;
        public override bool HoldToCast(int slot) => true;
        /// <summary>In a subject its spells are cast with the buttons (skipping their own cooldowns), and R is Wash Brush.</summary>
        public override bool CanRecast(int slot) => mood >= 0;

        public override string TriggerHint => "Point your brush and hold: he flicks paint at the enemy in front";

        public override string ButtonHint(int slot) => slot switch
        {
            0 => "Q: Subject Disaster (fire). Then B, X or A casts Devastating Fire, Severing Bolt or Molten Fissure",
            1 => "W: Subject Serenity (water). Then B, X or A casts Fleeting Current, Pool of Reflection or Stirring Lights",
            2 => "E: Subject Torment (dread). Then B, X or A casts Grim Visage, Gaze of the Abyss or Crushing Maw",
            3 => "R: hold, both hands point the staff where you look, release to fire. In a subject: Wash Brush",
            _ => null,
        };

        public override string SlotStatus(int slot)
        {
            if (mood >= 0)
                return slot < 3 ? (aiming == slot ? "RELEASE!" : SpellCodes[mood * 3 + slot]) : "WASH";
            if (slot == 3 && aimingR)
                return "RELEASE!";
            return null;
        }

        public override string StateText => mood >= 0 ? SubjectNames[mood].ToUpperInvariant() + ": B / X / A"
            : Lights > 0 ? $"STIRRING LIGHTS x{Lights}" : "";

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            ResetState();
            boundBody = Body as ChampionVRAvatar;
            if (boundBody && boundBody.isActiveAndEnabled && boundBody.Weapon != null)
            {
                moves = new HweiMoves(Player, boundBody);
                boundBody.GripOverride = moves.Grip;
                boundBody.HandOverride = moves.Fingers;
                boundBody.Posed += OnPosed;
            }
            else
                boundBody = null;
            visuals = new HweiVisuals(Player, this);
            panel = HweiPanel.Create(Player, this);
        }

        public override void OnUnequip()
        {
            if (boundBody)
            {
                boundBody.Posed -= OnPosed;
                boundBody.GripOverride = null;
                boundBody.HandOverride = null;
            }
            boundBody = null;
            moves = null;
            foreach (var zone in zones)
                zone.End(this);
            zones.Clear();
            foreach (var mark in marks.Values)
                if (mark.view)
                    Object.Destroy(mark.view);
            marks.Clear();
            visuals?.Destroy();
            visuals = null;
            if (panel)
                Object.Destroy(panel.gameObject);
            panel = null;
            ResetState();
        }

        void ResetState()
        {
            mood = entering = aiming = painted = -1;
            aimingR = false;
            lights = 0;
            lightCast = -1;
            lightsUntil = 0;
            attackTarget = null;
            attackRelease = -1;
            pending.Clear();
            releases.Clear();
        }

        public override void OnDeath()
        {
            // Spells already out (lava, the pool, an eye, the despair zone) play out, as in League; one still on the brush
            // is lost, and the brush is put down.
            releases.Clear();
            mood = entering = aiming = painted = -1;
            aimingR = false;
            attackTarget = null;
            attackRelease = -1;
            lights = 0;
            moves?.Stop();
            visuals?.HideAll();
        }

        void OnPosed() => visuals?.Pose();

        /// <summary>Wash Brush also works while stunned (League: it ignores cast-inhibiting crowd control).</summary>
        public override bool CastsWhileStunned(int slot) => slot == 3 && mood >= 0;

        /// <summary>While he is dead his lava, pool, eye and despair keep running on their own clocks.</summary>
        public override void TickWhileDead()
        {
            if (visuals == null)
                return;
            visuals.Previews(-1, -1, false);
            RunDue(pending);
            RunZones();
        }

        public override void Tick()
        {
            if (visuals == null)
                return;
            // The ultimate's aim: both hands come up to the staff while R is held (the gaze is only traced while needed).
            if (aimingR || (moves != null && moves.UltimateWeight > 0))
                moves?.HoldUltimate(aimingR, GazePoint(RRange));
            visuals.Previews(mood, Aiming, aimingR);
            // Without a first-person body nothing poses the brush effects: pose them here.
            if (!boundBody)
                visuals.Pose();
            if (attackRelease >= 0 && Time.time >= attackRelease)
                FireAttack();
            RunDue(releases);
            RunDue(pending);
            RunZones();
            TickTimers();
        }

        void RunZones()
        {
            for (int i = 0; i < zones.Count; i++)
                if (!zones[i].Tick(this))
                {
                    zones[i].End(this);
                    zones.RemoveAt(i--);
                }
        }

        void TickTimers()
        {
            if (lights > 0 && Time.time >= lightsUntil)
            {
                lights = 0;
                Player.Emit("WEEnd", Player.Feet + Vector3.up, Vector3.up);
            }
            expired.Clear();
            foreach (var pair in marks)
                if (!pair.Key || !pair.Key.IsAlive || Time.time >= pair.Value.until)
                    expired.Add(pair.Key);
            foreach (var target in expired)
            {
                if (marks[target].view)
                    Object.Destroy(marks[target].view);
                marks.Remove(target);
            }
        }

        static void RunDue(List<(float at, Action action)> list)
        {
            for (int i = 0; i < list.Count; i++)
                if (Time.time >= list[i].at)
                {
                    var action = list[i].action;
                    list.RemoveAt(i--);
                    action();
                }
        }

        // ---------- Aim ----------

        /// <summary>The brush's ray as the controller holds it (an animation playing on the brush does not move the aim).</summary>
        void BrushRay(out Vector3 origin, out Vector3 direction)
        {
            if (boundBody && boundBody.Weapon != null)
            {
                var grip = boundBody.RightGrip();
                direction = grip.rotation * Quaternion.Euler(boundBody.PropTilt(boundBody.Weapon)) * Vector3.forward;
                origin = grip.position;
                return;
            }
            origin = Player.AttackOrigin;
            direction = Player.AttackDirection;
        }

        /// <summary>Where the brush points along the ground (flat).</summary>
        public Vector3 AimDirection()
        {
            BrushRay(out _, out var direction);
            return Player.PlanarDirection(direction);
        }

        /// <summary>The floor the brush points at, up to <paramref name="range"/>; aimed at a wall, the wall's foot.</summary>
        public Vector3 AimPoint(float range)
        {
            BrushRay(out var origin, out var direction);
            return GroundAlong(origin, direction, range);
        }

        /// <summary>The floor where Hwei looks, up to <paramref name="range"/> (the ultimate's aim).</summary>
        public Vector3 GazePoint(float range)
        {
            var head = Player.head.transform;
            return GroundAlong(head.position, head.forward, range);
        }

        Vector3 GroundAlong(Vector3 origin, Vector3 direction, float range)
        {
            Vector3 feet = Player.Feet;
            Vector3 flatAim = Player.PlanarDirection(direction);
            Vector3 point;
            if (Player.TerrainRay(origin, direction, range * 1.6f, out var hit))
                point = hit.normal.y > .65f ? hit.point : hit.point - flatAim * .3f;
            else
                point = feet + flatAim * range;
            Vector3 flat = Geo.Flat(point - feet);
            if (flat.magnitude > range)
                point = feet + flat.normalized * range;
            return Floor(point, feet, flatAim);
        }

        /// <summary>Walks back toward Hwei until the point is on the Rift's floor (not a wall top or the void).</summary>
        Vector3 Floor(Vector3 point, Vector3 feet, Vector3 fallback)
        {
            var grid = RiftNavGrid.Active;
            if (grid)
            {
                Vector3 toward = Geo.Flat(point - feet);
                float distance = toward.magnitude;
                Vector3 step = distance > .01f ? toward / distance : fallback;
                for (float d = distance; d >= 0; d -= .25f)
                {
                    Vector3 probe = feet + step * d;
                    if (grid.Floor(probe, out float y))
                        return new Vector3(probe.x, y, probe.z);
                }
                return feet;
            }
            return RiftMatch.Instance && RiftMatch.Instance.Ground(point + Vector3.up * 2, out var ground) ? ground : point;
        }

        /// <summary>The floor under a point (its own height when the grid has none there).</summary>
        public static Vector3 Ground(Vector3 point)
        {
            var grid = RiftNavGrid.Active;
            return grid && grid.Floor(point, out float y) ? new Vector3(point.x, y, point.z) : point;
        }

        /// <summary>Where spells leave the bristles: the brush tip as posed now (the palm without a brush).</summary>
        Vector3 Tip() => visuals != null ? visuals.Tip() : Player.AttackOrigin;

        // ---------- Subjects and casting ----------

        bool CanPay(int slot)
        {
            float cost = Definition ? Definition.spells[slot].Cost(Rank(slot)) : 0;
            if (Player.Economy && Definition && Definition.UsesMana && Player.Economy.Mana < cost)
            {
                RiftMatch.Instance?.Notify("Not enough mana.");
                return false;
            }
            return true;
        }

        public override bool Cast(int slot) => false;

        public override bool BeginHold(int slot)
        {
            if (slot == 3)
            {
                // In a subject the left trigger is Wash Brush; Spiraling Despair is only cast outside one.
                if (mood >= 0)
                {
                    WashBrush();
                    return false;
                }
                if (!CanPay(3))
                    return false;
                aimingR = true;
                return true;
            }
            if (mood < 0)
                return EnterMood(slot);
            // The latest spell button pressed is the one aimed (and cast on its release).
            aiming = slot;
            return true;
        }

        public override void ReleaseHold(int slot)
        {
            if (slot == 3)
            {
                if (aimingR)
                {
                    aimingR = false;
                    FireDespair();
                }
                return;
            }
            // The press that opened the subject only opened it.
            if (entering == slot)
            {
                entering = -1;
                return;
            }
            if (aiming != slot || mood < 0)
                return;
            aiming = -1;
            CastSpell(mood, slot);
        }

        public override void CancelHold(int slot)
        {
            if (slot == 3)
                aimingR = false;
            if (entering == slot)
                entering = -1;
            if (aiming == slot)
                aiming = -1;
        }

        /// <summary>
        /// Opens a subject: the brush flourishes as its paint soaks into the bristles and the panel shows its three spells.
        /// Blocked without the mana to cast it (League); cost and cooldown are paid when one of its spells is cast.
        /// </summary>
        bool EnterMood(int subject)
        {
            if (!CanPay(subject))
                return false;
            mood = subject;
            painted = subject;
            entering = subject;
            aiming = -1;
            // Spiraling Despair is only cast outside a subject.
            Player.CancelSlot(3);
            Player.Emit("Subject" + "QWE"[subject], Player.AttackOrigin, Player.AttackDirection);
            if (moves != null && !moves.Casting)
                moves.Begin(HweiMoves.Move.Inspire, Player.head.transform.forward);
            visuals?.Inspire(subject);
            return true;
        }

        /// <summary>Wash Brush: leaves the subject at no cost; the brush shakes its paint off.</summary>
        void WashBrush()
        {
            int was = mood;
            mood = entering = aiming = -1;
            painted = -1;
            Player.CancelSlot(0);
            Player.CancelSlot(1);
            Player.CancelSlot(2);
            Player.Emit("WashBrush", Player.AttackOrigin, Vector3.down);
            if (moves != null && !moves.Casting)
                moves.Begin(HweiMoves.Move.Wash, Player.head.transform.forward);
            visuals?.WashBrush(was);
        }

        /// <summary>
        /// Casts a subject's spell where the brush points as the button lets go: pays the subject's cost and starts its
        /// cooldown, leaves the subject, and plays the spell's stroke; the spell leaves the bristles partway through it.
        /// </summary>
        void CastSpell(int subject, int variant)
        {
            var spell = HweiMoves.Spell(subject, variant);
            BrushRay(out var origin, out var ray);
            Vector3 flat = Player.PlanarDirection(ray);
            Vector3 point = variant switch
            {
                1 when subject == 0 => AimPoint(QWRange),
                1 when subject == 1 => AimPoint(WWRange),
                1 when subject == 2 => AimPoint(EWRange),
                2 when subject == 2 => AimPoint(EERange),
                _ => Player.Feet + flat,
            };
            if (!Player.Commit(subject, false, origin, flat))
            {
                // The subject stays open: say why nothing happened.
                if (Player.Economy && Definition && Player.Economy.Mana < Definition.spells[subject].Cost(Rank(subject)))
                    RiftMatch.Instance?.Notify("Not enough mana.");
                return;
            }
            mood = entering = aiming = -1;
            LastCast = Time.time;
            int cast = ++castCount;
            string code = SpellCodes[subject * 3 + variant];
            Player.Emit(code, origin, flat);
            // A spell still waiting to leave the brush goes now, so a quick second cast never swallows the first.
            FirePending();
            CancelAttack();
            Vector3 toward = variant == 1 || (subject == 2 && variant == 2) ? Geo.FlatDirection(point - Player.Feet, flat) : flat;
            moves?.Begin(spell, toward);
            visuals?.Cast(subject, variant);
            releases.Add((Time.time + HweiMoves.ReleaseAfter(spell), () => Release(subject, variant, cast, point, toward)));
        }

        void FirePending()
        {
            while (releases.Count > 0)
            {
                var action = releases[0].action;
                releases.RemoveAt(0);
                action();
            }
        }

        void Release(int subject, int variant, int cast, Vector3 point, Vector3 flat)
        {
            // The paint has left the bristles.
            if (painted == subject && mood < 0)
                painted = -1;
            switch (subject * 3 + variant)
            {
                case 0: DevastatingFire(cast, flat); break;
                case 1: SeveringBolt(cast, point); break;
                case 2: MoltenFissure(cast, flat); break;
                case 3: FleetingCurrent(cast, flat); break;
                case 4: PoolOfReflection(cast, point); break;
                case 5: StirringLights(cast); break;
                case 6: GrimVisage(cast, flat); break;
                case 7: GazeOfTheAbyss(cast, point); break;
                case 8: CrushingMaw(cast, point); break;
            }
        }

        // ---------- Damage, Signature of the Visionary and Stirring Lights ----------

        static bool Ignored(Combatant c) => !c || c.GetComponent<RiftStructure>() || c.GetComponent<RiftVisionWard>();

        static bool Minion(Combatant c) => c.GetComponent<RiftMinion>();

        static bool Monster(Combatant c) => c.GetComponent<RiftObjective>();

        static bool Epic(Combatant c)
        {
            var objective = c.GetComponent<RiftObjective>();
            if (!objective || string.IsNullOrEmpty(objective.objective))
                return false;
            string name = objective.objective;
            return name.Contains("Dragon") || name.Contains("Baron") || name.Contains("Herald") || name.Contains("Grub") || name.Contains("Atakhan");
        }

        /// <summary>
        /// A spell's magic damage to one enemy: marks it for Signature of the Visionary (a different spell completes the
        /// mark) and spends a light of Stirring Lights on the first enemy each cast hits.
        /// </summary>
        float SpellHit(Combatant target, float amount, string ability, int cast, Vector3 from, bool area = true)
        {
            float dealt = Player.Hit(target, amount, DamageKind.Magic, ability, false, from);
            if (dealt <= 0)
                return dealt;
            Player.Emit("SpellHit", target.AimPosition, Vector3.up);
            Signature(target, ability);
            ConsumeLight(target, cast, area);
            return dealt;
        }

        void Signature(Combatant target, string ability)
        {
            if (!target || !target.IsAlive)
                return;
            if (marks.TryGetValue(target, out var mark) && Time.time < mark.until)
            {
                // The same spell never completes its own mark (one cast cannot both mark and complete).
                if (mark.ability == ability)
                    return;
                marks.Remove(target);
                if (mark.view)
                    Object.Destroy(mark.view);
                Vector3 at = Ground(target.transform.position);
                zones.Add(new SignatureZone(at, visuals?.SignatureWarning(at)));
                Player.Emit("MarkWarn", at, Vector3.up);
                return;
            }
            marks[target] = new Mark { until = Time.time + MarkTime, ability = ability, view = visuals?.Mark(target) };
            Player.Emit("Mark", target.AimPosition, Vector3.up);
        }

        /// <summary>The signature completes: 40-285 by level + 35% AP to every enemy around where the mark was taken.</summary>
        void SignatureBurst(Vector3 at)
        {
            float damage = Mathf.Lerp(40, 285, (Player.Level - 1) / 17f) + AP * .35f;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, MarkRadius))
                if (!Ignored(enemy))
                    Player.Hit(enemy, damage, DamageKind.Magic, "Signature", false, at);
            Player.Emit("MarkBurst", at, Vector3.up);
            visuals?.SignatureBurst(at);
        }

        /// <summary>
        /// Stirring Lights: a light empowers the first enemy each attack or spell hits (one light per cast): bonus magic
        /// damage (less from area spells against minions and monsters) and mana back.
        /// </summary>
        void ConsumeLight(Combatant target, int cast, bool area)
        {
            if (Lights <= 0 || cast == lightCast || !target || !target.IsAlive)
                return;
            lightCast = cast;
            lights--;
            bool reduced = area && !target.countsAsChampion;
            float damage = reduced ? ByRank(1, 10, 15, 20, 25, 30) + AP * .075f : ByRank(1, 20, 30, 40, 50, 60) + AP * .15f;
            Player.Hit(target, damage, DamageKind.Magic, "WE", false, target.AimPosition);
            var economy = Player.Economy;
            if (economy)
                economy.Mana = Mathf.Min(economy.MaxMana, economy.Mana + ByRank(1, 45, 50, 55, 60, 65));
            Player.Emit("WEHit", target.AimPosition, Vector3.up);
            visuals?.LightStrike(target.AimPosition, lights);
            if (lights == 0)
                Player.Emit("WEEnd", Player.Feet + Vector3.up, Vector3.up);
        }

        /// <summary>A slow that wears off over <paramref name="seconds"/> (Crushing Maw): four steps, each weaker.</summary>
        void DecayingSlow(Combatant target, float slow, float seconds)
        {
            const int Steps = 4;
            float step = seconds / Steps;
            for (int i = 0; i < Steps; i++)
            {
                float strength = slow * (1 - i / (float)Steps);
                if (i == 0)
                    target.ApplySlow(1 - strength, step - .01f);
                else
                    pending.Add((Time.time + i * step, () =>
                    {
                        if (target && target.IsAlive)
                            target.ApplySlow(1 - strength, step - .01f);
                    }));
            }
        }

        // ---------- Attacks ----------

        /// <summary>
        /// The trigger's attack: the brush flicks at the enemy in front (a forehand and a backhand by turn) and the paint
        /// leaves the bristles at the snap.
        /// </summary>
        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var target = AttackTarget(origin, direction);
            if (!target)
                return;
            backhand = !backhand;
            var kind = backhand ? HweiMoves.Move.Backflick : HweiMoves.Move.Flick;
            float seconds = Mathf.Clamp(Player.AttackInterval * .45f, .22f, .36f);
            attackTarget = target;
            attackRelease = Time.time + seconds * HweiMoves.ReleaseFraction(kind);
            if (moves != null && !moves.Casting)
                moves.Begin(kind, target.transform.position - Player.Feet, seconds);
        }

        /// <summary>A spell cuts the flick short; an attack whose paint never left the brush is given back.</summary>
        void CancelAttack()
        {
            if (attackRelease >= 0)
                Player.ResetAttackTimer();
            attackTarget = null;
            attackRelease = -1;
        }

        void FireAttack()
        {
            attackRelease = -1;
            var target = attackTarget;
            attackTarget = null;
            // A target that died or got out of reach during the flick: the attack is spent on nothing and may start again.
            if (!target || !target.IsAlive || !Player.IsEnemy(target) || Geo.FlatDistance(target.transform.position, Player.Feet) > Player.AttackReach + 1.5f)
            {
                Player.ResetAttackTimer();
                return;
            }
            Vector3 tip = Tip();
            var missile = Player.Projectile(tip, (target.AimPosition - tip).normalized, AD, DamageKind.Physical, "Attack1", MissileSpeed, Player.AttackReach + 4, false);
            missile.basic = true;
            missile.homing = target;
            int cast = ++castCount;
            visuals?.DressAttack(missile.gameObject, Lights > 0);
            missile.hit = (t, dealt) =>
            {
                Player.Emit("Hit", t.AimPosition, missile.direction);
                visuals?.AttackHit(t.AimPosition);
                ConsumeLight(t, cast, false);
            };
            Player.Emit("AttackRelease", tip, missile.direction);
        }

        // ---------- Disaster ----------

        void DevastatingFire(int cast, Vector3 flat)
        {
            Vector3 tip = Tip();
            Player.Emit("QQRelease", tip, flat);
            var view = visuals?.Fireball(tip, flat);
            HweiMissile.Launch(Player, this, tip, flat, QQSpeed, QQRange, QQWidth, .95f, view, null,
                (target, at) => FireBurst(cast, at), at => FireBurst(cast, at));
        }

        /// <summary>The fireball bursts on the first enemy (or at the end of its flight): damage plus 3-7% of maximum health.</summary>
        void FireBurst(int cast, Vector3 at)
        {
            float damage = ByRank(0, 50, 80, 110, 140, 170) + AP * .8f;
            float share = ByRank(0, .03f, .04f, .05f, .06f, .07f);
            foreach (var enemy in Player.EnemiesAround(at, QQRadius))
            {
                if (Ignored(enemy))
                    continue;
                float health = enemy.maxHealth * share;
                if (Monster(enemy))
                    health = Mathf.Min(health, 250);
                SpellHit(enemy, damage + health, "QQ", cast, at);
            }
            Player.Emit("QQHit", at, Vector3.up);
            visuals?.FireBurst(at);
        }

        void SeveringBolt(int cast, Vector3 point)
        {
            Player.Emit("QWWarn", point, Vector3.up);
            zones.Add(new BoltZone(point, cast, visuals?.BoltWarning(point)));
        }

        /// <summary>
        /// The bolt strikes: damage to all in its circle; an isolated enemy (alone in it) or an immobile one takes up to
        /// 2-3.5 times as much the more health it is missing (the bonus is capped at 300 against monsters). Minions and
        /// camp monsters take half.
        /// </summary>
        void BoltStrike(Vector3 at, int cast)
        {
            var hit = Player.EnemiesAround(at + Vector3.up * .6f, QWRadius);
            hit.RemoveAll(Ignored);
            float baseDamage = ByRank(0, 60, 85, 110, 135, 160) + AP * .3f;
            float factor = ByRank(0, 2, 2.375f, 2.75f, 3.125f, 3.5f);
            foreach (var enemy in hit)
            {
                float damage = baseDamage;
                if (hit.Count == 1 || enemy.Rooted)
                {
                    float bonus = baseDamage * (factor - 1) * (1 - enemy.Health / Mathf.Max(1, enemy.maxHealth));
                    if (Monster(enemy))
                        bonus = Mathf.Min(bonus, 300);
                    damage += bonus;
                }
                if (Minion(enemy) || (Monster(enemy) && !Epic(enemy)))
                    damage *= .5f;
                SpellHit(enemy, damage, "QW", cast, at);
            }
            Player.Emit("QWHit", at, Vector3.up);
            visuals?.BoltStrike(at);
        }

        void MoltenFissure(int cast, Vector3 flat)
        {
            Vector3 start = Player.Feet;
            Player.Emit("QEStart", start + flat * QERange * .5f, flat);
            zones.Add(new FissureZone(start, flat, cast, visuals?.FissurePath(start, flat, QERange)));
        }

        /// <summary>One of the fissure's seven eruptions: damage around it (minions 60%, monsters 135%), then 2.5 s of lava.</summary>
        void Erupt(Vector3 at, int cast)
        {
            float damage = ByRank(0, 20, 35, 50, 65, 80) + AP * .3f;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, QEHalfWidth + .2f))
                if (!Ignored(enemy))
                    SpellHit(enemy, damage * FissureShare(enemy), "QE", cast, at);
            Player.Emit("QEErupt", at, Vector3.up);
            visuals?.Erupt(at);
        }

        /// <summary>A lava tick: damage and a 35% slow to everyone standing in that eruption's lava.</summary>
        void LavaTick(Vector3 at, int cast)
        {
            float damage = ByRank(0, 5, 8.75f, 12.5f, 16.25f, 20) + AP * .06f;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, QEHalfWidth + .2f))
            {
                if (Ignored(enemy))
                    continue;
                if (SpellHit(enemy, damage * FissureShare(enemy), "QE", cast, at) > 0)
                    enemy.ApplySlow(1 - QESlow, QETick + .05f);
            }
        }

        static float FissureShare(Combatant enemy) => Minion(enemy) ? .6f : Monster(enemy) ? 1.35f : 1;

        // ---------- Serenity ----------

        void FleetingCurrent(int cast, Vector3 flat)
        {
            Vector3 start = Player.Feet;
            float seconds = ByRank(1, 4, 4.5f, 5, 5.5f, 6);
            Player.Emit("WQRelease", start, flat);
            Player.Emit("WQZone", start + flat * WQRange * .5f, flat);
            zones.Add(new CurrentZone(start, flat, seconds, visuals?.Current(start, flat, WQRange, seconds)));
        }

        /// <summary>Standing in the current: 30-40% (+3% per 100 AP) move speed, refreshed for 0.5 s.</summary>
        void Swept(ref bool first)
        {
            float bonus = ByRank(1, .3f, .325f, .35f, .375f, .4f) + AP * .0003f;
            Health.ApplySpeed(bonus, .5f);
            if (first)
                Player.Emit("WQSpeed", Player.Feet, Vector3.up);
            first = false;
        }

        void PoolOfReflection(int cast, Vector3 point)
        {
            Player.Emit("WWZone", point, Vector3.up);
            zones.Add(new PoolZone(point, visuals?.Pool(point, WWRadius, WWTime)));
        }

        /// <summary>
        /// Inside the pool: a shield that starts at 50-130 (+30% AP) and grows every 0.264 s up to 100-260 (+60% AP); it
        /// lasts 0.5 s after he leaves the pool or it dries.
        /// </summary>
        void Shielded(bool first)
        {
            float cap = ByRank(1, 100, 140, 180, 220, 260) + AP * .6f;
            float amount = first ? ByRank(1, 50, 70, 90, 110, 130) + AP * .3f : ByRank(1, 8.33f, 11.67f, 15, 18.33f, 21.67f) + AP * .05f;
            Health.AddRefreshableShield("HweiPool", amount, cap, .5f);
            if (!first)
                return;
            Player.Emit("WWShield", Player.Feet + Vector3.up, Vector3.up);
            visuals?.ShieldUp();
        }

        void StirringLights(int cast)
        {
            lights = 3;
            lightsUntil = Time.time + WEWindow;
            lightCast = cast;
            Player.Emit("WELights", Player.Feet + Vector3.up, Vector3.up);
        }

        // ---------- Torment ----------

        void GrimVisage(int cast, Vector3 flat)
        {
            Vector3 tip = Tip();
            Player.Emit("EQRelease", tip, flat);
            var view = visuals?.Face(tip);
            HweiMissile.Launch(Player, this, tip, flat, EQSpeed, EQRange, EQWidth, 1.15f, view, null,
                (target, at) => Terrify(target, cast, at), at => visuals?.FaceFade(at));
        }

        /// <summary>The face reaches an enemy: damage, and it flees from Hwei for 1-1.5 s (it cannot act meanwhile).</summary>
        void Terrify(Combatant target, int cast, Vector3 at)
        {
            float damage = ByRank(2, 70, 110, 150, 190, 230) + AP * .65f;
            visuals?.FaceHit(at);
            Player.Emit("EQHit", at, Vector3.up);
            if (SpellHit(target, damage, "EQ", cast, at, false) <= 0 || !target.IsAlive)
                return;
            float flee = ByRank(2, 1, 1.125f, 1.25f, 1.375f, 1.5f);
            Vector3 away = Geo.Flat(target.transform.position - Player.Feet);
            UnitDisplacement.Push(target, away.sqrMagnitude > .01f ? away : Player.PlanarDirection(Player.head.transform.forward), 1.4f * flee, flee, 0);
            visuals?.Terrified(target, flee);
        }

        void GazeOfTheAbyss(int cast, Vector3 point)
        {
            Player.Emit("EWZone", point, Vector3.up);
            zones.Add(new EyeZone(point, cast, visuals?.Eye(point, EWTime)));
        }

        /// <summary>
        /// The eye looks for prey: the nearest enemy champion within 5.8 m (League); with none, the nearest enemy unit, so
        /// it also works against the minions and monsters of this practice Rift.
        /// </summary>
        Combatant EyeTarget(Vector3 at)
        {
            Combatant best = null;
            float bestScore = float.MaxValue;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, EWSight))
            {
                if (Ignored(enemy) || !enemy.IsAlive)
                    continue;
                float score = Geo.FlatDistance(enemy.transform.position, at) - (enemy.countsAsChampion ? 100 : 0);
                if (score < bestScore && Player.ClearAttackLine(at + Vector3.up * 1.2f, enemy.AimPosition, enemy))
                {
                    best = enemy;
                    bestScore = score;
                }
            }
            return best;
        }

        void EyeStrike(Vector3 eye, Combatant target, int cast)
        {
            Player.Emit("EWWarn", eye, Vector3.up);
            Player.Emit("EWStrike", eye, (target.AimPosition - eye).normalized);
            var view = visuals?.EyeBolt(eye);
            HweiMissile.Launch(Player, this, eye, (target.AimPosition - eye).normalized, EWSpeed, 14, .35f, 0, view, null,
                (t, at) => Rooted(t, cast, at), at => visuals?.EyeBoltFade(at), target);
        }

        void Rooted(Combatant target, int cast, Vector3 at)
        {
            float damage = ByRank(2, 70, 110, 150, 190, 230) + AP * .65f;
            Player.Emit("EWHit", at, Vector3.up);
            if (SpellHit(target, damage, "EW", cast, at, false) <= 0 || !target.IsAlive)
                return;
            float root = ByRank(2, 1.2f, 1.4f, 1.6f, 1.8f, 2);
            target.ApplyRoot(root);
            visuals?.Root(target, root);
        }

        void CrushingMaw(int cast, Vector3 point)
        {
            Player.Emit("EEZone", point, Vector3.up);
            zones.Add(new MawZone(point, cast, visuals?.Maw(point, EEDelay)));
        }

        /// <summary>The jaws close: damage to every enemy inside, dragged to the middle, slowed 40-70% (wearing off over 1.25 s).</summary>
        void Bite(Vector3 at, int cast)
        {
            float damage = ByRank(2, 70, 110, 150, 190, 230) + AP * .65f;
            float slow = ByRank(2, .4f, .475f, .55f, .625f, .7f);
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, EERadius + .3f))
            {
                if (Ignored(enemy) || SpellHit(enemy, damage, "EE", cast, at) <= 0 || !enemy.IsAlive)
                    continue;
                Vector3 toCentre = Geo.Flat(at - enemy.transform.position);
                if (toCentre.magnitude > .35f)
                    UnitDisplacement.Push(enemy, toCentre, toCentre.magnitude - .25f, .2f, .15f);
                DecayingSlow(enemy, slow, EESlowDecay);
            }
            Player.Emit("EEHit", at, Vector3.up);
            visuals?.Bite(at);
        }

        // ---------- R: Spiraling Despair ----------

        void FireDespair()
        {
            Vector3 target = GazePoint(RRange);
            Vector3 flat = Player.PlanarDirection(target - Player.Feet);
            Vector3 tip = Tip();
            if (!Player.Commit(3, false, tip, flat))
            {
                moves?.HoldUltimate(false, target);
                return;
            }
            int cast = ++castCount;
            CancelAttack();
            moves?.FireUltimate();
            float distance = Mathf.Max(1, Geo.FlatDistance(Player.Feet, target));
            var view = visuals?.DespairOrb(tip);
            HweiMissile.Launch(Player, this, tip, flat, RSpeed, distance, RWidth, 1.2f, view, c => c.countsAsChampion,
                (host, at) => Attach(cast, host, at), at => Attach(cast, null, Ground(at)));
        }

        /// <summary>The despair sticks to the champion it met (or settles where he looked) and starts to spread.</summary>
        void Attach(int cast, Combatant host, Vector3 at)
        {
            Player.Emit(host ? "RAttach" : "RGrow", at, Vector3.up);
            zones.Add(new DespairZone(host, host ? Ground(host.transform.position) : at, cast, visuals?.Despair(at)));
        }

        /// <summary>Every 0.25 s: a little damage and one more stack of slow (10% each, up to 12, multiplying) inside the aura.</summary>
        void DespairTick(Vector3 at, float radius, int cast, Dictionary<Combatant, int> stacks)
        {
            float damage = ByRank(3, 2.5f, 5, 7.5f) + AP * .0125f;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, radius))
            {
                if (Ignored(enemy))
                    continue;
                stacks.TryGetValue(enemy, out int count);
                count = Mathf.Min(RMaxStacks, count + 1);
                stacks[enemy] = count;
                if (SpellHit(enemy, damage, "R", cast, at) > 0)
                    // League multiplies stacked slows; in this project the strongest slow on a unit wins (Combatant.ApplySlow).
                    enemy.ApplySlow(Mathf.Pow(.9f, count), RTick + .05f);
            }
        }

        void DespairBurst(Vector3 at, float radius, int cast)
        {
            float damage = ByRank(3, 200, 325, 450) + AP * .8f;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .6f, radius))
                if (!Ignored(enemy))
                    SpellHit(enemy, damage, "R", cast, at);
            Player.Emit("RBurst", at, Vector3.up);
            visuals?.DespairBurst(at, radius);
        }

        // ---------- Zones: spells that stay on the Rift for a while ----------

        abstract class Zone
        {
            protected readonly float born = Time.time;
            protected float Age => Time.time - born;

            /// <summary>Runs every frame; false when the zone is done.</summary>
            public abstract bool Tick(HweiKit kit);

            public virtual void End(HweiKit kit) { }
        }

        /// <summary>Severing Bolt's warning circle, then the strike 1 s after the cast.</summary>
        sealed class BoltZone : Zone
        {
            readonly Vector3 at;
            readonly int cast;
            readonly GameObject view;

            public BoltZone(Vector3 at, int cast, GameObject view)
            {
                this.at = at;
                this.cast = cast;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                kit.visuals?.BoltCharging(view, Age / QWDelay);
                if (Age < QWDelay)
                    return true;
                kit.BoltStrike(at, cast);
                return false;
            }

            public override void End(HweiKit kit)
            {
                if (view)
                    Object.Destroy(view);
            }
        }

        /// <summary>Molten Fissure: the path glows, then seven eruptions 0.2 s apart outward, each leaving 2.5 s of lava.</summary>
        sealed class FissureZone : Zone
        {
            readonly Vector3 start, direction;
            readonly int cast;
            readonly GameObject view;
            readonly Vector3[] points = new Vector3[QEExplosions];
            readonly bool[] erupted = new bool[QEExplosions];
            readonly float[] nextTick = new float[QEExplosions];
            readonly int count;

            public FissureZone(Vector3 start, Vector3 direction, int cast, GameObject view)
            {
                this.start = start;
                this.direction = direction;
                this.cast = cast;
                this.view = view;
                // The fissure runs along the floor and stops where the floor does (a wall or the river's edge).
                float lastY = start.y;
                for (int i = 0; i < QEExplosions; i++)
                {
                    var p = start + direction * (QERange * (i + 1) / QEExplosions);
                    var grid = RiftNavGrid.Active;
                    if (grid && (!grid.Floor(p, out float y) || Mathf.Abs(y - lastY) > 1.3f))
                        break;
                    if (grid && grid.Floor(p, out float floor))
                        p.y = lastY = floor;
                    points[count++] = p;
                }
            }

            public override bool Tick(HweiKit kit)
            {
                bool alive = Age < QEDelay + QEStep * count + .1f;
                for (int i = 0; i < count; i++)
                {
                    float at = QEDelay + QEStep * i;
                    if (!erupted[i] && Age >= at)
                    {
                        erupted[i] = true;
                        nextTick[i] = born + at + QETick;
                        kit.Erupt(points[i], cast);
                    }
                    if (!erupted[i] || Age >= at + QELava)
                        continue;
                    alive = true;
                    if (Time.time >= nextTick[i])
                    {
                        nextTick[i] += QETick;
                        kit.LavaTick(points[i], cast);
                    }
                }
                return alive;
            }

            public override void End(HweiKit kit)
            {
                if (view)
                    Object.Destroy(view);
            }
        }

        /// <summary>Fleeting Current: the stream lasts 4-6 s; standing in it speeds Hwei up.</summary>
        sealed class CurrentZone : Zone
        {
            readonly Vector3 start, direction;
            readonly float seconds;
            readonly GameObject view;
            float nextCheck;
            bool first = true;

            public CurrentZone(Vector3 start, Vector3 direction, float seconds, GameObject view)
            {
                this.start = start;
                this.direction = direction;
                this.seconds = seconds;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                if (Time.time >= nextCheck)
                {
                    nextCheck = Time.time + .125f;
                    Vector3 offset = Geo.Flat(kit.Player.Feet - start);
                    float along = Vector3.Dot(offset, direction);
                    if (along >= -.3f && along <= WQRange + .3f && (offset - direction * along).magnitude <= WQHalfWidth + .25f && kit.Health.IsAlive)
                        kit.Swept(ref first);
                }
                return Age < seconds;
            }

            public override void End(HweiKit kit)
            {
                if (view)
                    Object.Destroy(view);
            }
        }

        /// <summary>Pool of Reflection: 3 s; while Hwei stands in it his shield grows.</summary>
        sealed class PoolZone : Zone
        {
            readonly Vector3 at;
            readonly GameObject view;
            float nextTick;
            bool first = true, shielded;

            public PoolZone(Vector3 at, GameObject view)
            {
                this.at = at;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                if (Time.time >= nextTick)
                {
                    nextTick = Time.time + WWTick;
                    bool inside = kit.Health.IsAlive && Geo.FlatDistance(kit.Player.Feet, at) <= WWRadius;
                    if (inside)
                    {
                        kit.Shielded(first);
                        first = false;
                    }
                    shielded = inside;
                }
                return Age < WWTime;
            }

            public override void End(HweiKit kit)
            {
                if (!first)
                    kit.Player.Emit("WWShieldEnd", kit.Player.Feet + Vector3.up, Vector3.up);
                if (view)
                    Object.Destroy(view);
            }
        }

        /// <summary>Gaze of the Abyss: the eye opens, looks for prey for up to 3 s and strikes the first it finds.</summary>
        sealed class EyeZone : Zone
        {
            readonly Vector3 at;
            readonly int cast;
            readonly GameObject view;
            bool struck;

            public EyeZone(Vector3 at, int cast, GameObject view)
            {
                this.at = at;
                this.cast = cast;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                if (struck)
                    return false;
                kit.visuals?.EyeLook(view, Age);
                if (Age >= EWArm)
                {
                    var target = kit.EyeTarget(at);
                    if (target)
                    {
                        struck = true;
                        kit.EyeStrike(at + Vector3.up * 1.3f, target, cast);
                        kit.visuals?.EyeGlare(view, target);
                        return false;
                    }
                }
                return Age < EWTime;
            }

            public override void End(HweiKit kit)
            {
                kit.Player.Emit("EWEnd", at, Vector3.up);
                kit.visuals?.EyeClose(view);
            }
        }

        /// <summary>Crushing Maw: the jaws open over the spot and bite after half a second.</summary>
        sealed class MawZone : Zone
        {
            readonly Vector3 at;
            readonly int cast;
            readonly GameObject view;

            public MawZone(Vector3 at, int cast, GameObject view)
            {
                this.at = at;
                this.cast = cast;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                kit.visuals?.MawClosing(view, at, Age / EEDelay);
                if (Age < EEDelay)
                    return true;
                kit.Bite(at, cast);
                return false;
            }

            public override void End(HweiKit kit)
            {
                if (view)
                    Object.Destroy(view, .35f);
            }
        }

        /// <summary>Spiraling Despair: grows around its host (or its spot) for 3 s, then bursts; early if the host dies.</summary>
        sealed class DespairZone : Zone
        {
            readonly Combatant host;
            readonly bool hosted;
            readonly int cast;
            readonly GameObject view;
            readonly Dictionary<Combatant, int> stacks = new();
            Vector3 at;
            float nextTick;
            bool burst;

            public DespairZone(Combatant host, Vector3 at, int cast, GameObject view)
            {
                this.host = host;
                hosted = host;
                this.at = at;
                this.cast = cast;
                this.view = view;
            }

            float Radius => RRadius * Mathf.Lerp(.4f, 1, Mathf.Clamp01(Age / RTime));

            public override bool Tick(HweiKit kit)
            {
                if (hosted && host && host.IsAlive)
                    at = Ground(host.transform.position);
                kit.visuals?.DespairGrow(view, at, Radius, Age / RTime);
                if (Time.time >= nextTick)
                {
                    nextTick = Time.time + RTick;
                    kit.DespairTick(at, Radius, cast, stacks);
                }
                if (Age < RTime && !(hosted && (!host || !host.IsAlive)))
                    return true;
                burst = true;
                kit.DespairBurst(at, Radius, cast);
                return false;
            }

            public override void End(HweiKit kit)
            {
                if (!burst)
                    kit.visuals?.DespairBurst(at, Radius);
                if (view)
                    Object.Destroy(view);
            }
        }

        /// <summary>A completed signature: its circle fills for 0.85 s, then it bursts.</summary>
        sealed class SignatureZone : Zone
        {
            readonly Vector3 at;
            readonly GameObject view;

            public SignatureZone(Vector3 at, GameObject view)
            {
                this.at = at;
                this.view = view;
            }

            public override bool Tick(HweiKit kit)
            {
                kit.visuals?.SignatureFilling(view, Age / MarkDelay);
                if (Age < MarkDelay)
                    return true;
                kit.SignatureBurst(at);
                return false;
            }

            public override void End(HweiKit kit)
            {
                if (view)
                    Object.Destroy(view);
            }
        }
    }

    /// <summary>
    /// One of Hwei's painted projectiles (Devastating Fire, Grim Visage, Spiraling Despair, the eye's strike): it flies
    /// along the ground at a height and hits the first enemy in its path that <c>filter</c> accepts (anything standing in
    /// its ground path counts, as in League); walls and the end of its flight stop it. A homing one chases its target.
    /// The kit and its visuals decide what the hit and the end do. It is removed if the champion changes.
    /// </summary>
    public class HweiMissile : MonoBehaviour
    {
        PlayerChampion owner;
        ChampionKit kit;
        Vector3 direction;
        float speed, range, radius, height, travelled, groundY;
        Func<Combatant, bool> filter;
        Action<Combatant, Vector3> onHit;
        Action<Vector3> onEnd;
        Combatant homing;
        bool started;
        static readonly RaycastHit[] hits = new RaycastHit[64];
        static readonly Collider[] overlaps = new Collider[64];
        static readonly Comparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        public static HweiMissile Launch(PlayerChampion owner, ChampionKit kit, Vector3 from, Vector3 direction, float speed, float range, float radius, float height,
            GameObject view, Func<Combatant, bool> filter, Action<Combatant, Vector3> onHit, Action<Vector3> onEnd, Combatant homing = null)
        {
            var go = view ? view : new GameObject("Hwei missile");
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction.sqrMagnitude > 1e-4f ? direction : Vector3.forward));
            var missile = go.AddComponent<HweiMissile>();
            missile.owner = owner;
            missile.kit = kit;
            missile.direction = homing ? direction.normalized : Geo.FlatDirection(direction, Vector3.forward);
            missile.speed = speed;
            missile.range = range;
            missile.radius = radius;
            missile.height = height;
            missile.filter = filter;
            missile.onHit = onHit;
            missile.onEnd = onEnd;
            missile.homing = homing;
            missile.groundY = HweiKit.Ground(owner.Feet).y;
            if (!view)
                owner.Track(go);
            return missile;
        }

        bool Accepts(Combatant target) => target && owner.IsEnemy(target) && !target.GetComponent<RiftStructure>() && !target.GetComponent<RiftVisionWard>() && (filter == null || filter(target));

        void Finish(Combatant target, Vector3 at)
        {
            enabled = false;
            if (target)
                onHit?.Invoke(target, at);
            else
                onEnd?.Invoke(at);
            Destroy(gameObject);
        }

        void Update()
        {
            if (!owner || owner.Kit != kit)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 pos = transform.position;
            if (homing)
            {
                HomingStep(pos);
                return;
            }
            // An enemy already inside the missile where it starts (point blank) is hit at once.
            if (!started)
            {
                started = true;
                int inside = Physics.OverlapCapsuleNonAlloc(new Vector3(pos.x, groundY + .1f, pos.z), new Vector3(pos.x, groundY + 2.2f, pos.z), radius, overlaps, owner.combatMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < inside; i++)
                {
                    var target = overlaps[i].GetComponentInParent<Combatant>();
                    if (Accepts(target))
                    {
                        Finish(target, pos);
                        return;
                    }
                }
            }
            float step = Mathf.Min(speed * Time.deltaTime, Mathf.Max(0, range - travelled));
            var bottom = new Vector3(pos.x, groundY + .1f, pos.z);
            var top = new Vector3(pos.x, groundY + 2.2f, pos.z);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits, step, owner.combatMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
            {
                var target = hits[i].collider.GetComponentInParent<Combatant>();
                if (Accepts(target))
                {
                    Finish(target, pos + direction * hits[i].distance);
                    return;
                }
            }
            // Walls stop it (floors never do: it cruises above them).
            count = Physics.SphereCastNonAlloc(pos, radius * .5f, direction, hits, step, owner.worldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].normal.y < .55f && !owner.OwnCollider(hits[i].collider))
                {
                    Finish(null, hits[i].point - direction * .1f);
                    return;
                }
            travelled += step;
            if (Time.frameCount % 5 == 0)
                groundY = HweiKit.Ground(pos).y;
            var next = pos + direction * step;
            next.y = Mathf.Lerp(pos.y, groundY + height, 1 - Mathf.Exp(-6 * Time.deltaTime));
            transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
            if (travelled >= range - 1e-3f)
                Finish(null, next);
        }

        /// <summary>Chases the target in 3D; the first enemy it touches on the way takes the hit.</summary>
        void HomingStep(Vector3 pos)
        {
            if (!homing)
            {
                Finish(null, pos);
                return;
            }
            if (homing.IsAlive)
                direction = (homing.AimPosition - pos).normalized;
            float step = speed * Time.deltaTime;
            var next = pos + direction * step;
            int count = Physics.OverlapSphereNonAlloc(next, radius, overlaps, owner.combatMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlaps[i].GetComponentInParent<Combatant>();
                if (Accepts(target))
                {
                    Finish(target, next);
                    return;
                }
            }
            travelled += step;
            transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
            if (travelled >= range || (!homing.IsAlive && Vector3.Distance(next, homing.AimPosition) < .3f))
                Finish(null, next);
        }
    }
}
