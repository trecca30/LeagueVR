using System.Collections;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gragas, the Rabble Rouser, with League's kit (Data Dragon 16.20.1 and the wiki), made physical for VR. Distances use
    /// the Rift's own scale (League units / 103 = metres).
    /// <list type="bullet">
    /// <item>Attacks: swing the keg he carries in your right hand into enemies, or hold the trigger and he swings it at
    /// the enemy in front himself, with League's own attacks: a hook from his right side, an overhand bonk, and a big
    /// wound-up swing when the hit is a critical strike. The hit lands when the keg does.</item>
    /// <item>Happy Hour: after an ability he takes a swig, healing 5.5% of his maximum health (12 / 10 / 8 / 6 s by level).</item>
    /// <item>Q Barrel Roll (B): hold to see where the barrel lands (where your right hand points), release to roll it
    /// there. It ferments for 2 s (up to +50% damage and a stronger slow) and bursts 4 s after it stops; press B again to
    /// burst it sooner.</item>
    /// <item>W Drunken Rage (X): he drinks for 0.75 s (his arm lifts the keg to your mouth; you can walk), takes less damage for
    /// 2.5 s, and his next attack within 5 s is League's two-handed smash: both arms lift the keg over his head and slam
    /// it down toward the target, splashing for bonus magic damage plus 7% of the target's maximum health.</item>
    /// <item>E Body Slam (A): tap to charge where you walk or look; hold to see the charge and aim it with your right
    /// hand, release to charge. The view rushes along with him at League's speed (or blinks, in the comfort settings).
    /// The first enemy his belly meets stops him (even one already at his belly gets a short lunge); enemies around it
    /// are knocked up and stunned for 1 s, and a hit takes 40% off the cooldown.</item>
    /// <item>R Explosive Cask (left trigger): hold to ready a cask in your left palm, with its blast ring where your left
    /// hand points; release to hurl it. It bursts, knocking enemies away from its centre.</item>
    /// </list>
    /// Previews show only while a button is held, so nothing appears on its own.
    /// </summary>
    public class GragasKit : MeleeKit
    {
        const float Units = 1 / 103f;
        public const float QRange = 850 * Units, QRadius = 250 * Units, QSpeed = 1000 * Units, QFerment = 2, QFuse = 4;
        public const float WDrink = .75f, WResist = 2.5f, WWindow = 5, WRadius = 250 * Units, WBonusRange = 50 * Units;
        // Body Slam's collision width and its small shove and hop are not published: tuned so the charge meets the
        // unit it visibly runs into and the stun reads as a knock-up.
        public const float ERange = 600 * Units, ERadius = 180 * Units, EHalfWidth = .55f, EStun = 1;
        const float EShove = .6f, EHop = .6f, EHopTime = .5f;
        /// <summary>Body Slam's dash speed, 900 units per second: 0.67 s for the full charge.</summary>
        public const float ESpeed = 900 * Units;
        /// <summary>How far ahead of him an enemy meets his belly (a planned charge stops 0.7 m short of a unit's centre).</summary>
        const float EContact = .85f;
        /// <summary>
        /// A press of E shorter than this is a tap: the charge goes where you walk or look. Held longer, the preview shows
        /// and the right hand aims it.
        /// </summary>
        public const float ETap = .25f;
        /// <summary>Even into an enemy already at his belly the charge lunges this far (League's dash always moves him).</summary>
        const float ELunge = .35f;
        // Drunken Rage's empowered hit, timed after League's Attack_Passive: the keg goes up over his head (0.24 s), comes
        // down (0.14 s, when the hit lands), stays down (0.15 s) and goes back to the hand (0.22 s).
        public const float SmashLift = .24f, SmashDown = .14f, SmashHold = .15f, SmashReturn = .22f;
        public const float SmashTime = SmashLift + SmashDown + SmashHold + SmashReturn;
        public const float RRange = 1000 * Units, RRadius = 400 * Units, RTravel = .5f, RKnockback = 900 * Units, RKnockTime = .6f;
        const float HappyHourHeal = .055f;

        /// <summary>His amber ale: the Body Slam charge line.</summary>
        public static readonly Color Ale = new(1f, .7f, .25f, 1f);

        GragasVisuals visuals;
        ChampionBodyBase boundBody;

        float happyHourReady, drinkUntil, resistUntil, empoweredUntil;
        bool aimingBarrel, aimingSlam, aimingCask;
        float smashStart = -1;
        bool smashLanded;
        Combatant smashTarget;
        Vector3 smashDirection;
        float slamHeldSince;
        // The trigger's attack under way: which of League's swings, when it started and how long it takes, its target.
        float swingStart = -1, swingTime;
        bool swingLanded;
        int swingCount;
        Combatant swingTarget;
        GragasVisuals.SwingState swing = GragasVisuals.SwingState.None;
        Barrel barrel;
        Cask cask;

        /// <summary>
        /// A rolled barrel: where it lands, when it stopped (negative while rolling), its view, and when its coroutine
        /// last ran (a barrel whose coroutine was stopped bursts from <see cref="Tick"/>).
        /// </summary>
        class Barrel
        {
            public GameObject view;
            public Vector3 at;
            public float landedAt = -1;
            public int step;
            public bool burstOnLanding, done;
        }

        /// <summary>A thrown cask in flight, with when its coroutine last ran.</summary>
        class Cask
        {
            public GameObject view;
            public Vector3 from, to;
            public int step;
            public bool done;
        }

        /// <summary>A planned Body Slam: where it starts, which way, how far, and the first enemy it meets.</summary>
        public struct Slam
        {
            public Vector3 start, direction;
            public float distance;
            public Combatant first;
        }

        public bool Drinking => Time.time < drinkUntil;
        public bool Empowered => Time.time < empoweredUntil;
        float DrinkProgress => Drinking ? 1 - (drinkUntil - Time.time) / WDrink : -1;
        /// <summary>True while Body Slam carries him along the ground.</summary>
        public bool Charging { get; private set; }
        /// <summary>True while both hands smash the keg down (the empowered hit).</summary>
        public bool Smashing => smashStart >= 0 && Time.time - smashStart < SmashTime;
        /// <summary>Seconds into the empowered smash, or -1.</summary>
        float SmashClock => Smashing ? Time.time - smashStart : -1;
        /// <summary>True while he swings the keg at the trigger's target.</summary>
        public bool Swinging => swingStart >= 0 && Time.time - swingStart < swingTime;
        /// <summary>The swing under way, with its progress 0-1 (none when not swinging).</summary>
        GragasVisuals.SwingState CurrentSwing => Swinging ? swing.At((Time.time - swingStart) / swingTime) : GragasVisuals.SwingState.None;
        // The cooldown steps (12 / 10 / 8 / 6 s) are from the research notes; the level breakpoints are assumed (the ultimate's 1 / 6 / 11 / 16), not verified.
        float HappyHourCooldown => Player.Level >= 16 ? 6 : Player.Level >= 11 ? 8 : Player.Level >= 6 ? 10 : 12;

        // Drinking and the smash take his hands, the charge takes all of him: no attacks or other abilities meanwhile
        // (he can still walk while he drinks or smashes). A basic swing only holds the next attack; casting cuts it short.
        public override bool Busy => Drinking || Charging || Smashing || Swinging;
        public override bool BlocksCasts => Drinking || Charging || Smashing;
        public override float BonusAttackRange => Empowered ? WBonusRange : 0;
        /// <summary>The keg is a fat weapon: a swing lands anywhere across its width.</summary>
        public override float WeaponRadius => .24f;
        public override bool HoldToCast(int slot) => slot == 0 ? barrel == null : slot == 2 || slot == 3;
        public override bool CanRecast(int slot) => slot == 0 && barrel != null && !barrel.done;

        /// <summary>Drunken Rage: 10-26% less damage (+4% per 100 AP) for 2.5 s from the drink.</summary>
        public override float DamageTaken(DamageHit hit) => Time.time < resistUntil && hit.source ? 1 - Mathf.Min(.9f, ByRank(1, .1f, .14f, .18f, .22f, .26f) + AP * .0004f) : 1;

        public override string TriggerHint => "Swing your keg into enemies, or hold and he swings it at the one in front";

        public override string ButtonHint(int slot) => slot switch
        {
            0 => "Q: hold to aim the barrel with your right hand, release to roll it; press again to burst it",
            1 => "W: Drunken Rage, your arm lifts the keg to drink (less damage, your next hit is a two-handed keg smash)",
            2 => "E: tap to Body Slam where you walk or look; hold to aim it with your right hand, release to charge",
            3 => "R: hold, point your left hand at the landing spot, release to hurl the cask",
            _ => null,
        };

        public override string SlotStatus(int slot) => slot switch
        {
            0 when barrel != null => barrel.landedAt < 0 ? "ROLLING" : $"BURST {Mathf.RoundToInt(Ferment(barrel) * 100)}%",
            0 when aimingBarrel => "RELEASE!",
            1 when Drinking => "GLUG",
            1 when Smashing => "SMASH!",
            1 when Empowered => "NEXT HIT",
            2 when aimingSlam => "RELEASE!",
            3 when aimingCask => "RELEASE!",
            _ => null,
        };

        public override string StateText => Drinking ? "DRUNKEN RAGE"
            : Charging ? "BODY SLAM"
            : Empowered ? "NEXT HIT: KEG SMASH"
            : barrel != null ? "BARREL OUT: B TO BURST"
            : Time.time >= happyHourReady ? "HAPPY HOUR READY" : $"HAPPY HOUR {happyHourReady - Time.time:0}s";

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            ResetState();
            boundBody = Body;
            visuals = new GragasVisuals(Player);
            if (boundBody)
            {
                boundBody.Posed += OnPosed;
                // The fist closes on the keg and the arm lifts it to his mouth while he drinks; for the smash both hands
                // take it over his head and bring it down.
                boundBody.KitCurl = left => visuals == null || !visuals.HasKeg || Player.HandStowed(false) ? default
                    : !left || GragasVisuals.SmashWeight(SmashClock, true) > .5f ? GragasVisuals.KegCurl : default;
                boundBody.GripOverride = (left, grip) => visuals == null ? grip
                    : SmashClock >= 0 ? visuals.SmashGrip(left, grip, SmashClock, smashDirection)
                    : left ? grip
                    : Swinging ? visuals.SwingGrip(grip, CurrentSwing)
                    : visuals.DrinkGrip(grip, DrinkProgress);
            }
        }

        public override void OnUnequip()
        {
            // The charge's coroutine stops with the kit: give the stick and the view back.
            EndCharge();
            if (boundBody)
            {
                boundBody.Posed -= OnPosed;
                boundBody.KitCurl = null;
                boundBody.GripOverride = null;
            }
            boundBody = null;
            if (barrel != null)
            {
                barrel.done = true;
                if (barrel.view)
                    Object.Destroy(barrel.view);
                barrel = null;
            }
            if (cask != null)
            {
                cask.done = true;
                if (cask.view)
                    Object.Destroy(cask.view);
                cask = null;
            }
            visuals?.Destroy();
            visuals = null;
            ResetState();
        }

        void ResetState()
        {
            happyHourReady = drinkUntil = resistUntil = empoweredUntil = 0;
            aimingBarrel = aimingSlam = aimingCask = false;
            smashStart = -1;
            smashTarget = null;
            CancelSwing();
        }

        public override void OnDeath()
        {
            // A barrel already out keeps fermenting and bursts on its own, as in League.
            EndCharge();
            aimingBarrel = aimingSlam = aimingCask = false;
            drinkUntil = resistUntil = empoweredUntil = 0;
            smashStart = -1;
            smashTarget = null;
            CancelSwing();
            visuals?.HideAll();
        }

        void OnPosed() => visuals?.Pose(DrinkProgress, Empowered, SmashClock, smashDirection, CurrentSwing);

        public override void Tick()
        {
            if (visuals == null)
                return;
            // Previews only while their button is held.
            visuals.BarrelAim(aimingBarrel, aimingBarrel ? Landing(false, QRange) : default);
            visuals.CaskAim(aimingCask, aimingCask ? Landing(true, RRange) : default);
            // The charge preview shows once E has been held past a tap, aimed with the right hand from then on.
            bool aimed = aimingSlam && Time.time - slamHeldSince >= ETap;
            visuals.SlamAim(aimed, aimed ? PlanSlam() : default);
            if (Drinking)
                visuals.Drinking(DrinkProgress);
            if (smashStart >= 0 && !smashLanded && Time.time - smashStart >= SmashLift + SmashDown)
                LandSmash();
            if (swingStart >= 0 && !swingLanded && Time.time - swingStart >= swingTime * GragasVisuals.SwingState.Strike)
                LandSwing();
            // A barrel or cask whose coroutine stopped (the player was switched off mid-flight) bursts where it is
            // headed instead of hanging in the air and locking Q.
            if (barrel != null && !barrel.done && Time.frameCount - barrel.step > 3)
            {
                if (barrel.landedAt < 0)
                    barrel.landedAt = Time.time;
                Burst(barrel);
            }
            if (cask != null && !cask.done && Time.frameCount - cask.step > 3)
                Explode(cask);
        }

        // ---------- Passive: Happy Hour ----------

        void HappyHour()
        {
            if (Time.time < happyHourReady || !Health.IsAlive)
                return;
            happyHourReady = Time.time + HappyHourCooldown;
            Health.Heal(Health.maxHealth * HappyHourHeal);
            Player.Emit("HappyHour", Player.Feet + Vector3.up, Vector3.up);
            visuals?.Swig();
        }

        // ---------- Attacks ----------

        /// <summary>The keg is the weapon: from his fist to the far side of the keg.</summary>
        public override bool WeaponEdge(out Vector3 from, out Vector3 to)
        {
            from = to = default;
            return visuals != null && !Drinking && visuals.KegEdge(out from, out to);
        }

        /// <summary>
        /// The trigger's attack: he swings the keg at the enemy in front himself (League's Attack1 hook and Attack2 bonk in
        /// turn, the Crit swing when the hit will be a critical strike), and the hit lands when the keg does. With Drunken
        /// Rage up it is the two-handed smash.
        /// </summary>
        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var targets = Player.SweepTargets(origin, direction, Player.AttackReach, .45f);
            if (targets.Count == 0)
                return;
            if (Empowered && !Smashing)
                BeginSmash(targets[0]);
            else
                BeginSwing(targets[0]);
        }

        /// <summary>
        /// Starts a basic swing: about 60% of the attack time (0.35-0.62 s), the hit at its strike. The critical strike is
        /// rolled now, so a crit shows from the wind-up.
        /// </summary>
        void BeginSwing(Combatant target)
        {
            swingTarget = target;
            swingStart = Time.time;
            swingLanded = false;
            swingTime = Mathf.Clamp(Player.AttackInterval * .6f, .35f, .62f);
            bool crit = Player.Economy && Player.Economy.RollNextCritical();
            var kind = crit ? GragasVisuals.Swing.Crit : swingCount++ % 2 == 0 ? GragasVisuals.Swing.Hook : GragasVisuals.Swing.Bonk;
            if (crit)
                Player.Emit("CritSwing", Player.AttackOrigin, Player.AttackDirection);
            Vector3 toward = Geo.FlatDirection(target.transform.position - Player.Feet, Player.PlanarDirection(Player.head.transform.forward));
            swing = new GragasVisuals.SwingState(kind, 0, toward);
        }

        /// <summary>
        /// The keg lands: the attack hits the target if it is still within reach (+1 m: it may have stepped back during the
        /// wind-up). If it died or got away, the attack is spent on nothing, so the next one may start at once.
        /// </summary>
        void LandSwing()
        {
            swingLanded = true;
            var target = swingTarget;
            swingTarget = null;
            if (!target || !target.IsAlive || Geo.FlatDistance(target.transform.position, Player.Feet) > Player.AttackReach + 1)
            {
                Player.ResetAttackTimer();
                return;
            }
            float dealt = Player.Hit(target, AttackDamage(target), DamageKind.Physical, "Attack1", true, target.AimPosition);
            if (dealt <= 0)
                return;
            bool crit = Player.Economy && Player.Economy.LastAttackCritical;
            Player.Emit(crit ? "CritHit" : "Hit", target.AimPosition, swing.forward);
            visuals?.SwingImpact(target, swing.kind, crit);
            OnAttackHit(target, dealt);
        }

        /// <summary>An ability cuts a basic swing short (as in League); a hit that already landed stays.</summary>
        void CancelSwing()
        {
            swingStart = -1;
            swingTarget = null;
        }

        /// <summary>With Drunken Rage up, swinging the keg into an enemy starts the two-handed smash on it.</summary>
        public override void WeaponHit(Combatant target, Vector3 point, Vector3 swing)
        {
            if (Empowered && !Smashing)
                BeginSmash(target);
            else
                base.WeaponHit(target, point, swing);
        }

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            if (Empowered)
                EmpoweredSplash(target);
        }

        /// <summary>
        /// League's Attack_Passive in VR: both arms take the keg, lift it over his head and smash it down in front of him
        /// toward the target. The attack and the splash land when the keg comes down (<see cref="LandSmash"/>).
        /// </summary>
        void BeginSmash(Combatant target)
        {
            CancelSwing();
            smashTarget = target;
            smashStart = Time.time;
            smashLanded = false;
            smashDirection = Geo.FlatDirection(target.transform.position - Player.Feet, Player.PlanarDirection(Player.head.transform.forward));
            // Both hands are on the keg: aimed abilities are put away.
            Player.CancelSlot(0);
            Player.CancelSlot(2);
            Player.CancelSlot(3);
            Player.Emit("WSmashCast", Player.AttackOrigin, smashDirection);
            visuals?.SmashBegin();
        }

        /// <summary>
        /// The keg comes down: the attack and Drunken Rage's splash land on the target if it is still in reach (it may
        /// have stepped back a little during the lift). Smashing empty ground keeps Drunken Rage for the next hit.
        /// </summary>
        void LandSmash()
        {
            smashLanded = true;
            var target = smashTarget;
            smashTarget = null;
            bool reached = target && target.IsAlive && Geo.FlatDistance(target.transform.position, Player.Feet) <= Player.AttackReach + 1.5f;
            visuals?.SmashImpact(reached ? target.transform.position : Player.Feet + smashDirection * 1.2f, smashDirection);
            Player.Emit("WSmash", Player.Feet + smashDirection, smashDirection);
            if (!reached)
                return;
            float dealt = Player.Hit(target, AttackDamage(target), DamageKind.Physical, "Attack1", true, target.AimPosition);
            if (dealt <= 0)
                return;
            Player.Emit("Hit", target.AimPosition, smashDirection);
            EmpoweredSplash(target);
        }

        /// <summary>Drunken Rage's empowered attack: a splash around the target for bonus magic damage.</summary>
        void EmpoweredSplash(Combatant target)
        {
            if (!target || target.GetComponent<RiftVisionWard>())
                return;
            empoweredUntil = 0;
            Vector3 at = target.AimPosition;
            float flat = ByRank(1, 20, 50, 80, 110, 140) + AP * .7f;
            foreach (var enemy in Player.EnemiesAround(at, WRadius, true))
            {
                bool structure = enemy.GetComponent<RiftStructure>();
                if ((structure && enemy != target) || enemy.GetComponent<RiftVisionWard>())
                    continue;
                float share = enemy.maxHealth * .07f;
                if (enemy.GetComponent<RiftObjective>())
                    share = Mathf.Min(share, 300);
                Player.Hit(enemy, (flat + share) * (structure ? .5f : 1), DamageKind.Magic, "W", false, at);
            }
            Player.Emit("WSplash", at, Vector3.up);
            visuals?.Splash(at);
        }

        // ---------- Abilities ----------

        public override bool Cast(int slot) => slot switch
        {
            0 => BurstBarrel(),
            1 => DrunkenRage(),
            _ => false,
        };

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

        public override bool BeginHold(int slot)
        {
            if (!CanPay(slot))
                return false;
            switch (slot)
            {
                case 0 when barrel == null:
                    aimingBarrel = true;
                    return true;
                case 2 when !Health.Rooted:
                    aimingSlam = true;
                    slamHeldSince = Time.time;
                    return true;
                case 3:
                    aimingCask = true;
                    return true;
            }
            return false;
        }

        public override void ReleaseHold(int slot)
        {
            // Only a hold that was aimed casts, and never with the keg at his lips.
            bool cast = !Drinking;
            switch (slot)
            {
                case 0 when aimingBarrel:
                    aimingBarrel = false;
                    if (cast)
                        RollBarrel();
                    break;
                case 2 when aimingSlam:
                    aimingSlam = false;
                    if (cast)
                        BodySlam(Time.time - slamHeldSince >= ETap);
                    break;
                case 3 when aimingCask:
                    aimingCask = false;
                    if (cast)
                        HurlCask();
                    break;
            }
        }

        public override void CancelHold(int slot)
        {
            if (slot == 0)
                aimingBarrel = false;
            else if (slot == 2)
                aimingSlam = false;
            else if (slot == 3)
                aimingCask = false;
        }

        /// <summary>
        /// Where a barrel or cask lands: the ground the hand points at, up to the ability's range. Aimed at a wall, it lands
        /// at the wall's foot; always on open floor.
        /// </summary>
        public Vector3 Landing(bool left, float range)
        {
            Vector3 origin = left ? Player.OffHandOrigin : Player.AttackOrigin;
            Vector3 direction = left ? Player.OffHandDirection : Player.AttackDirection;
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
            // Walk back toward Gragas until the point is on the Rift's floor (not a wall top or the void).
            var grid = RiftNavGrid.Active;
            if (grid)
            {
                Vector3 toward = Geo.Flat(point - feet);
                float distance = toward.magnitude;
                Vector3 step = distance > .01f ? toward / distance : flatAim;
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

        // ---------- Q: Barrel Roll ----------

        void RollBarrel()
        {
            if (barrel != null)
                return;
            Vector3 hand = Player.AttackOrigin;
            Vector3 target = Landing(false, QRange);
            if (!Player.Commit(0, false, hand, (target - hand).normalized))
                return;
            CancelSwing();
            HappyHour();
            barrel = new Barrel { at = target };
            Player.StartCoroutine(Roll(barrel, hand, target));
        }

        float Ferment(Barrel b) => b.landedAt < 0 ? 0 : Mathf.Clamp01((Time.time - b.landedAt) / QFerment);

        IEnumerator Roll(Barrel b, Vector3 from, Vector3 to)
        {
            float distance = Geo.FlatDistance(from, to);
            float flight = Mathf.Max(.35f, distance / QSpeed);
            b.view = visuals?.SpawnBarrel(from);
            float begin = Time.time;
            b.step = Time.frameCount;
            while (!b.done && Time.time - begin < flight)
            {
                b.step = Time.frameCount;
                float t = (Time.time - begin) / flight;
                visuals?.RollBarrel(b.view, from, to, t);
                yield return null;
            }
            if (b.done)
                yield break;
            visuals?.RollBarrel(b.view, from, to, 1);
            b.landedAt = Time.time;
            Player.Emit("QLand", to, Vector3.up);
            if (b.burstOnLanding)
            {
                Burst(b);
                yield break;
            }
            while (!b.done && Time.time - b.landedAt < QFuse)
            {
                b.step = Time.frameCount;
                visuals?.Ferment(b.view, Ferment(b));
                yield return null;
            }
            Burst(b);
        }

        /// <summary>Pressing B again bursts the barrel (or as soon as it stops, if it is still rolling).</summary>
        bool BurstBarrel()
        {
            if (barrel == null || barrel.done || Drinking)
                return false;
            if (barrel.landedAt < 0)
                barrel.burstOnLanding = true;
            else
                Burst(barrel);
            return true;
        }

        void Burst(Barrel b)
        {
            if (b.done)
                return;
            b.done = true;
            if (barrel == b)
                barrel = null;
            float ferment = Ferment(b);
            float damage = ByRank(0, 80, 120, 160, 200, 240) * (1 + .5f * ferment) + AP * Mathf.Lerp(.8f, 1.2f, ferment);
            float slow = ByRank(0, .4f, .45f, .5f, .55f, .6f) * (1 + .5f * ferment);
            foreach (var enemy in Player.EnemiesAround(b.at + Vector3.up, QRadius))
            {
                if (enemy.GetComponent<RiftStructure>() || enemy.GetComponent<RiftVisionWard>())
                    continue;
                float amount = damage * (enemy.GetComponent<RiftMinion>() ? .7f : 1);
                if (Player.Hit(enemy, amount, DamageKind.Magic, "Q", false, b.at) > 0)
                {
                    enemy.ApplySlow(1 - Mathf.Min(.9f, slow), 2);
                    Player.Emit("SpellHit", enemy.AimPosition, Vector3.up);
                }
            }
            Player.Emit("QBurst", b.at, Vector3.up);
            visuals?.BarrelBurst(b.view, b.at, ferment);
            b.view = null;
        }

        // ---------- W: Drunken Rage ----------

        bool DrunkenRage()
        {
            if (Drinking || !Player.Commit(1, false, Player.head.transform.position, Vector3.up))
                return false;
            CancelSwing();
            // Drinking takes his hands (League: no attacks or abilities during the drink), so aimed abilities are put away.
            Player.CancelSlot(0);
            Player.CancelSlot(2);
            Player.CancelSlot(3);
            HappyHour();
            drinkUntil = Time.time + WDrink;
            // The reduction starts with the drink. The research does not say whether League's 2.5 s runs from the cast or from the end of the drink.
            resistUntil = Time.time + WResist;
            empoweredUntil = 0;
            Player.StartCoroutine(Drink(drinkUntil));
            return true;
        }

        IEnumerator Drink(float end)
        {
            while (Time.time < end)
                yield return null;
            if (!Health.IsAlive || drinkUntil != end)
                yield break;
            empoweredUntil = Time.time + WWindow;
            Player.Emit("Empowered", Player.Feet + Vector3.up, Vector3.up);
        }

        // ---------- E: Body Slam ----------

        /// <summary>
        /// The charge as it would go now: where you walk; standing still, where your right hand points when E was held to
        /// aim (<paramref name="aimWithHand"/>) and where you look for a tap. It runs to the first enemy in its path (its
        /// "auto focus") or a wall.
        /// </summary>
        public Slam PlanSlam(bool aimWithHand = true)
        {
            Vector3 feet = Player.Feet;
            Vector3 direction = Player.LocomotionDirection(out bool walking);
            if (!walking && aimWithHand)
                direction = Player.PlanarDirection(Player.AttackDirection);
            var plan = new Slam { start = feet, direction = direction, distance = ERange };
            float nearest = float.MaxValue;
            foreach (var enemy in Player.SweepTargets(feet + Vector3.up * .9f, direction, ERange, EHalfWidth))
            {
                if (enemy.GetComponent<RiftStructure>() || enemy.GetComponent<RiftVisionWard>())
                    continue;
                float along = Vector3.Dot(Geo.Flat(enemy.transform.position - feet), direction);
                if (along < nearest)
                {
                    nearest = along;
                    plan.first = enemy;
                }
            }
            // He stops 0.7 m short of the first enemy's centre; one already at his belly still gets a short lunge.
            if (plan.first)
                plan.distance = Mathf.Clamp(Mathf.Max(nearest - .7f, Mathf.Min(ELunge, nearest - .35f)), 0, ERange);
            // Walls, ledges and crowds stop the charge short: back off (up to 2 m) to the last point he can stand on.
            float wanted = plan.distance;
            plan.distance = 0;
            for (int i = 0; i < 8 && wanted - i * .25f > .15f; i++)
                if (Player.StepPoint(feet + direction * (wanted - i * .25f), out var point))
                {
                    plan.distance = Geo.FlatDistance(feet, point);
                    break;
                }
            if (plan.first && !Meets(feet + direction * plan.distance, plan.first))
                plan.first = null;
            return plan;
        }

        /// <summary>
        /// True when a charge that stops at <paramref name="stop"/> reaches <paramref name="enemy"/>: close to it with no
        /// wall in between. A wall that stops the charge short hits nobody.
        /// </summary>
        bool Meets(Vector3 stop, Combatant enemy) => enemy && enemy.IsAlive
            && Geo.FlatDistance(stop, enemy.transform.position) <= ERadius
            && Player.ClearAttackLine(stop + Vector3.up * .9f, enemy.AimPosition, enemy);

        void BodySlam(bool aimed)
        {
            if (Health.Rooted || Charging)
                return;
            var plan = PlanSlam(aimed);
            if (!Player.Commit(2, false, plan.start + Vector3.up, plan.direction))
                return;
            CancelSwing();
            HappyHour();
            if (plan.distance > .15f && ComfortSettings.RushCharges)
            {
                Player.StartCoroutine(Charge(plan));
                return;
            }
            // Blink charges (comfort setting): a short fade over an instant move, like the other dashes.
            if (plan.distance > .15f)
            {
                Player.ComfortBlink(.12f);
                Player.Dash(plan.direction, plan.distance);
            }
            visuals?.SlamTrail(plan.start, Player.Feet, plan.direction);
            LandSlam(plan.direction, plan.first && Meets(Player.Feet, plan.first) ? plan.first : null);
        }

        /// <summary>
        /// The charge, as League plays it: he runs belly first along the planned line at 900 units per second and the view
        /// rushes with him behind a comfort vignette, until his belly meets the first enemy in the way (it may step into
        /// the line) or the line ends. A stun or root stops him where he is, with no slam.
        /// </summary>
        IEnumerator Charge(Slam plan)
        {
            Charging = true;
            Player.CancelSlot(0);
            Player.CancelSlot(3);
            Player.FreezeLocomotion(true);
            Player.RushVignette(true);
            visuals?.ChargeBegin(plan.start, plan.direction);
            float seconds = Mathf.Max(.05f, plan.distance / ESpeed);
            // Contact only counts once he has covered the lunge, so an enemy already at his belly still sees him move.
            float lunge = Mathf.Min(ELunge, plan.distance) - .01f;
            float begin = Time.time, nextStride = 0;
            Combatant met = null;
            bool stopped = false;
            float height = plan.start.y;
            while (Player.Kit == this && Health.IsAlive)
            {
                if (Health.Rooted)
                {
                    stopped = true;
                    break;
                }
                float t = Mathf.Clamp01((Time.time - begin) / seconds);
                Vector3 at = plan.start + plan.direction * (plan.distance * t);
                // Up and down ramps the feet keep to the floor: the floor nearest the height he is at, never a step up
                // onto something beside the path.
                if (RiftMatch.Instance && RiftMatch.Instance.Ground(new Vector3(at.x, height + .1f, at.z), out var floor) && Mathf.Abs(floor.y - height) < .3f)
                    at.y = floor.y;
                else
                    at.y = height;
                height = at.y;
                Player.MoveFeet(at);
                visuals?.Charging(at, plan.direction);
                if (Time.time >= nextStride)
                {
                    nextStride = Time.time + .16f;
                    Player.Emit("Stride", at, plan.direction);
                }
                met = plan.distance * t >= lunge ? Contact(at, plan.direction) : null;
                if (met || t >= 1)
                    break;
                yield return null;
            }
            bool slam = Charging && !stopped && Player.Kit == this && Health.IsAlive;
            EndCharge();
            if (slam)
                LandSlam(plan.direction, met);
        }

        /// <summary>Gives the stick and the view back after a charge (also when the kit or the champion goes mid-charge).</summary>
        void EndCharge()
        {
            if (!Charging)
                return;
            Charging = false;
            Player.RushVignette(false);
            Player.FreezeLocomotion(false);
        }

        /// <summary>The first enemy his belly runs into: just ahead of him and within the charge's width.</summary>
        Combatant Contact(Vector3 at, Vector3 direction)
        {
            Combatant first = null;
            float nearest = float.MaxValue;
            foreach (var enemy in Player.EnemiesAround(at + Vector3.up * .9f, EContact + EHalfWidth + .5f))
            {
                if (!enemy.IsAlive || enemy.GetComponent<RiftStructure>() || enemy.GetComponent<RiftVisionWard>())
                    continue;
                Vector3 offset = Geo.Flat(enemy.transform.position - at);
                float along = Vector3.Dot(offset, direction);
                if (along < -.2f || along > EContact || (offset - direction * along).magnitude > EHalfWidth + .25f)
                    continue;
                if (along < nearest)
                {
                    nearest = along;
                    first = enemy;
                }
            }
            return first;
        }

        /// <summary>The slam that ends the charge: the enemy he met and everyone around it are hit, knocked up and stunned.</summary>
        void LandSlam(Vector3 direction, Combatant met)
        {
            bool hit = false;
            Vector3 impact = Player.Feet + direction * .7f;
            if (met)
            {
                impact = met.transform.position;
                float damage = ByRank(2, 80, 125, 170, 215, 260) + AP * .6f;
                foreach (var enemy in Player.EnemiesAround(impact + Vector3.up, ERadius))
                {
                    if (enemy.GetComponent<RiftStructure>() || enemy.GetComponent<RiftVisionWard>())
                        continue;
                    if (Player.Hit(enemy, damage, DamageKind.Magic, "E", false, impact) <= 0)
                        continue;
                    hit = true;
                    // Knocked up a little way along the charge, then stunned for the rest of the second.
                    UnitDisplacement.Push(enemy, direction, EShove, EHopTime, EHop);
                    enemy.ApplyStun(EStun);
                    Player.Emit("SpellHit", enemy.AimPosition, direction);
                }
            }
            if (hit)
            {
                Player.ReduceCooldown(2, Player.Cooldown(2) * .4f);
                Player.Emit("SlamHit", impact, direction);
            }
            visuals?.SlamImpact(impact, hit);
        }

        // ---------- R: Explosive Cask ----------

        void HurlCask()
        {
            Vector3 palm = visuals != null ? visuals.CaskInHand() : Player.OffHandOrigin;
            Vector3 target = Landing(true, RRange);
            if (!Player.Commit(3, false, palm, (target - palm).normalized))
                return;
            CancelSwing();
            HappyHour();
            cask = new Cask { from = palm, to = target, step = Time.frameCount };
            Player.StartCoroutine(Fly(cask));
        }

        IEnumerator Fly(Cask c)
        {
            c.view = visuals?.SpawnCask(c.from);
            float begin = Time.time;
            while (!c.done && Time.time - begin < RTravel)
            {
                c.step = Time.frameCount;
                visuals?.FlyCask(c.view, c.from, c.to, (Time.time - begin) / RTravel);
                yield return null;
            }
            if (!c.done)
            {
                visuals?.FlyCask(c.view, c.from, c.to, 1);
                Explode(c);
            }
        }

        void Explode(Cask c)
        {
            if (c.done)
                return;
            c.done = true;
            if (cask == c)
                cask = null;
            Vector3 from = c.from, to = c.to;
            var view = c.view;
            c.view = null;
            float damage = ByRank(3, 200, 300, 400) + AP * .8f;
            Vector3 fallback = Geo.Flat(to - from);
            foreach (var enemy in Player.EnemiesAround(to + Vector3.up, RRadius))
            {
                if (enemy.GetComponent<RiftStructure>() || enemy.GetComponent<RiftVisionWard>())
                    continue;
                if (Player.Hit(enemy, damage, DamageKind.Magic, "R", false, to) <= 0)
                    continue;
                Vector3 away = Geo.Flat(enemy.transform.position - to);
                UnitDisplacement.Push(enemy, away.sqrMagnitude > .01f ? away : fallback, RKnockback, RKnockTime, 1.2f);
                Player.Emit("SpellHit", enemy.AimPosition, away);
            }
            Player.Emit("RBurst", to, Vector3.up);
            visuals?.CaskBurst(view, to);
        }
    }
}
