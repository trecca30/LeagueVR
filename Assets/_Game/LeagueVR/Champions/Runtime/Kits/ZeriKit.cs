using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Zeri, the Spark of Zaun, built for VR around the gun in her right hand. Numbers follow Data Dragon 16.19.1 and the
    /// League wiki; League units are scaled 55 to the metre (her 550-unit attack reaches 10 m).
    /// <list type="bullet">
    /// <item>Q Burst Fire (right trigger): seven rounds from the muzzle, each along wherever the gun points at that
    /// moment, so sweeping the gun sprays the burst. Rounds stop at the first enemy (all enemies with Spark Surge) and at
    /// walls. Fire rate is her attack speed, hard capped at 1.5 per second like League; attack speed past the cap becomes
    /// bonus attack damage for the burst.</item>
    /// <item>Living Battery (B): her League basic attack, a battery shot at the enemy the gun points at (it shares the
    /// attack timer with Burst Fire). Walking and bursts charge the Sparkpack (the gun's coils glow); a full charge adds
    /// damage based on max health. Any battery shot executes an enemy it leaves under the threshold; the ring under the
    /// aimed enemy turns red when the shot would.</item>
    /// <item>W Ultrashock Laser (X): hold until the pulse in the left palm is charged (League's cast time), then release
    /// to fire where the palm points; a quick tap cancels for free. It slows the first enemy; if it meets a wall, a laser
    /// fires through the wall 0.85 s later.</item>
    /// <item>E Spark Surge (A): a blink (with a short comfort fade) where you walk or where the gun points, vaulting over
    /// a wall within reach. For 5 s Burst Fire pierces and its first target takes bonus damage.</item>
    /// <item>R Lightning Crash (left trigger): hold, then release to discharge a nova; a quick tap cancels for free.
    /// Hitting an enemy Overcharges her: faster attacks and movement, and Burst Fire becomes three quick rounds whose first
    /// hit chains lightning to four more enemies.</item>
    /// </list>
    /// This game has no enemy champions yet, so the hits League counts on champions (Overcharge) count any enemy unit.
    /// Overcharged movement speed is capped at +25% for comfort in VR.
    /// </summary>
    public class ZeriKit : RangedKit
    {
        public static readonly Color Spark = new(.62f, 1f, .38f, 1f);
        public static readonly Color Volt = new(1f, .93f, .38f, 1f);
        public static readonly Color Plasma = new(.45f, .85f, 1f, 1f);
        public static readonly Color Danger = new(1f, .3f, .25f, 1f);

        // League units / 55 = metres.
        public const float BurstRange = 13.6f, RoundRadius = .3f, RoundSpacing = .035f, OverchargedSpacing = .03f;
        public const float PulseRange = 21.8f, PulseSpeed = 45, LaserRange = 28, LaserHalfWidth = 1.8f, LaserDelay = .85f;
        public const float SurgeDistance = 5.5f, SurgeWallReach = .9f, SurgeMaxVault = 12, SurgeBuff = 5;
        public const float NovaRadius = 15, ChainRange = 11.8f, OverchargeDuration = 5, OverchargeExtend = 2.5f;
        /// <summary>League caps Burst Fire at 1.5 attacks a second; Overcharged raises it by 12.5% (1.6875).</summary>
        public const float SpeedCap = 1.5f, OverchargedSpeedCap = SpeedCap * 1.125f;
        const float MetresPerCharge = 40f / 55f, BatteryCone = 10, BatteryQueueTime = 1.6f, ChargeCap = 100, CrashCastTime = .25f, MoveDeadZone = .002f;
        const int Rounds = 7, OverchargedRounds = 3, ChainTargets = 4;

        float charge = ChargeCap;
        float surgeUntil, overchargedUntil, hyperUntil, nextHum, nextHoldHint, stillSince, batteryQueuedUntil;
        bool wasOvercharged;
        int hyperStacks;
        Vector3 lastFeet;
        bool haveFeet, holdingPulse, holdingCrash;
        float pulseHoldStart, crashHoldStart;

        // Burst Fire in flight: rounds left, the next round's time and per-burst state.
        int roundsLeft;
        float nextRound;
        bool burstOvercharged, burstCrit, burstFirstHit;

        ChampionBodyBase boundBody;
        ZeriVisuals visuals;

        public float Charge => charge;
        public bool FullyCharged => charge >= ChargeCap - .01f;
        public bool Surging => Time.time < surgeUntil;
        public bool Overcharged => Time.time < overchargedUntil;
        public int HyperStacks => Time.time < hyperUntil ? hyperStacks : 0;
        public bool BurstInFlight => roundsLeft > 0;

        public override bool HoldToCast(int slot) => slot == 1 || slot == 3;
        public override float BonusAttackSpeed => Overcharged ? .3f : 0;
        public override float AttackSpeedCap => Overcharged ? OverchargedSpeedCap : SpeedCap;
        protected override float AimCone => BatteryCone;

        public override string TriggerHint => "Burst Fire from your gun (hold to keep firing)";

        public override string ButtonHint(int slot) => slot switch
        {
            0 => "Living Battery shot (executes low-health enemies)",
            1 => "W: hold, release to fire Ultrashock Laser (tap cancels)",
            3 => "R: Lightning Crash (tap cancels)",
            _ => null,
        };

        public override string SlotStatus(int slot) => slot switch
        {
            0 when BatteryQueued => "QUEUED",
            0 => FullyCharged ? "CHARGED" : $"{Mathf.FloorToInt(charge)}%",
            1 when holdingPulse => PulseReady ? "RELEASE!" : "CHARGING",
            2 when Surging => $"PIERCE {surgeUntil - Time.time:0.0}",
            3 when holdingCrash => Time.time - crashHoldStart >= CrashCastTime ? "RELEASE!" : "CHARGING",
            3 when Overcharged => $"OVERCHARGED {overchargedUntil - Time.time:0.0}",
            _ => null,
        };

        public override string StateText => Overcharged ? $"OVERCHARGED · {HyperStacks} STACKS" : Surging ? "LIGHTNING ROUNDS" : FullyCharged ? "SPARKPACK CHARGED" : $"SPARKPACK {Mathf.FloorToInt(charge)}%";

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            ResetState();
            boundBody = Body;
            visuals = new ZeriVisuals(this, Player);
            if (boundBody)
                boundBody.Posed += OnPosed;
        }

        public override void OnUnequip()
        {
            // A champion switch stops the vault's coroutine mid-air: give the body and the stick back.
            if (Vaulting)
            {
                Vaulting = false;
                if (boundBody)
                    boundBody.Hidden = false;
                Player.FreezeLocomotion(false);
            }
            if (boundBody)
                boundBody.Posed -= OnPosed;
            boundBody = null;
            visuals?.Destroy();
            visuals = null;
            ResetState();
        }

        void ResetState()
        {
            charge = ChargeCap;
            surgeUntil = overchargedUntil = hyperUntil = 0;
            hyperStacks = 0;
            roundsLeft = 0;
            haveFeet = holdingPulse = holdingCrash = wasOvercharged = false;
            stillSince = batteryQueuedUntil = 0;
        }

        public override void OnDeath()
        {
            visuals?.HideAll();
            holdingPulse = holdingCrash = wasOvercharged = false;
            roundsLeft = 0;
            overchargedUntil = surgeUntil = batteryQueuedUntil = 0;
        }

        // League refills the Sparkpack on respawn.
        public override void OnRespawn()
        {
            charge = ChargeCap;
            haveFeet = false;
        }

        public override void Tick()
        {
            // Stuns, menus and stasis stop a burst mid-air and pause the Sparkpack.
            if (!Player.CanAct)
            {
                roundsLeft = 0;
                haveFeet = false;
            }
            else
            {
                ChargeFromMovement();
                FireRounds();
                if (BatteryQueued && Player.AttackReady && roundsLeft == 0 && BatteryShot())
                    batteryQueuedUntil = 0;
            }
            if (Overcharged)
                Health.ApplySpeed(OverchargedSpeed, .25f);
            else if (wasOvercharged)
                Player.Emit("OverchargeEnd", Player.Feet + Vector3.up, Vector3.up);
            wasOvercharged = Overcharged;
            HoldFeedback();
            visuals?.Tick(holdingPulse ? Mathf.Clamp01((Time.time - pulseHoldStart) / PulseCastTime) : -1, holdingCrash ? Mathf.Clamp01((Time.time - crashHoldStart) / CrashCastTime) : -1);
        }

        void OnPosed() => visuals?.Pose();

        /// <summary>A soft hum in the left hand while a pulse or the nova charges, ticking faster once it is ready.</summary>
        void HoldFeedback()
        {
            if (!(holdingPulse || holdingCrash) || Time.time < nextHum)
                return;
            bool ready = holdingPulse ? PulseReady : Time.time - crashHoldStart >= CrashCastTime;
            nextHum = Time.time + (ready ? .08f : .14f);
            Player.Emit(ready ? "HoldReady" : "HoldHum", Player.OffHandOrigin, Vector3.up);
        }

        // ---------- Passive: Living Battery ----------

        float LevelLerp(float a, float b) => Mathf.Lerp(a, b, (Player.Level - 1) / 17f);

        /// <summary>
        /// 1 charge per 40 units moved, by stick or by walking around the room. Teleports do not count, and tiny head
        /// movements (tracking noise while standing) are ignored.
        /// </summary>
        void ChargeFromMovement()
        {
            Vector3 feet = Player.Feet;
            if (haveFeet)
            {
                float moved = Geo.FlatDistance(feet, lastFeet);
                if (moved < MoveDeadZone)
                {
                    if (stillSince <= 0)
                        stillSince = Time.time;
                    return;
                }
                // Setting off after standing still a while: Zeri sometimes says something (League's move lines).
                if (stillSince > 0 && Time.time - stillSince > 8)
                    Player.Emit("Moving", feet, Vector3.up);
                stillSince = 0;
                if (moved < 1.5f)
                    AddCharge(moved / MetresPerCharge);
            }
            lastFeet = feet;
            haveFeet = true;
        }

        void AddCharge(float amount)
        {
            bool wasFull = FullyCharged;
            charge = Mathf.Min(ChargeCap, charge + amount);
            if (!wasFull && FullyCharged)
                Player.Emit("Charged", Muzzle(out _), Vector3.up);
        }

        public float ExecuteThreshold => LevelLerp(70, 170.59f) + AP * .2f;

        static bool IsWard(Combatant target) => target.GetComponent<RiftVisionWard>();

        /// <summary>Epic and jungle monsters (League's "monsters"); this game has no enemy champions.</summary>
        static bool IsMonster(Combatant target) => target.GetComponent<RiftObjective>();

        /// <summary>The enemy the gun points at for a battery shot (soft lock, then a thick ray); wards are skipped.</summary>
        public Combatant BatteryTarget()
        {
            Vector3 origin = Muzzle(out var direction);
            var target = Player.ConeTarget(origin, direction, Player.AttackReach, BatteryCone, true, t => !IsWard(t));
            if (target)
                return target;
            target = Player.AimTarget(Player.AttackReach, origin, direction, true);
            return target && !IsWard(target) ? target : null;
        }

        /// <summary>Damage of the next battery shot on a target, before resistances (the max-health part caps at 300 on monsters).</summary>
        public float BatteryDamage(Combatant target)
        {
            if (!FullyCharged)
                return LevelLerp(10, 27.35f) + AP * .03f;
            float share = target.maxHealth * LevelLerp(.01f, .1218f);
            if (IsMonster(target))
                share = Mathf.Min(300, share);
            return LevelLerp(75, 170) + AP * 1.1f + share;
        }

        /// <summary>The battery shot's damage after the target's magic resistance and Zeri's magic penetration.</summary>
        float MitigatedBatteryDamage(Combatant target)
        {
            float resistance = target.magicResistance;
            var items = Player.Economy;
            if (items && resistance > 0)
                resistance = Mathf.Max(0, resistance * (1 - items.MagicPenPercent) - items.MagicPen);
            return Combatant.Mitigate(BatteryDamage(target), resistance);
        }

        /// <summary>
        /// True when a battery shot would leave the target at or under the execute threshold with no shield left (the red
        /// ring). Shields soak the shot first, and a target that still has a shield is not executed.
        /// </summary>
        public bool WouldExecute(Combatant target)
        {
            if (!target || !target.IsAlive || target.GetComponent<RiftStructure>())
                return false;
            float damage = MitigatedBatteryDamage(target);
            return target.Shield <= damage && target.Health - (damage - target.Shield) <= ExecuteThreshold;
        }

        bool BatteryQueued => batteryQueuedUntil > Time.time;

        /// <summary>
        /// B while the gun is still cycling (a burst in the air or the attack timer running) queues the shot for a
        /// moment instead of swallowing the press; it fires as soon as the gun is ready, ahead of the next burst.
        /// </summary>
        bool BatteryShotOrQueue()
        {
            if (BatteryShot())
            {
                batteryQueuedUntil = 0;
                return true;
            }
            if ((!Player.AttackReady || roundsLeft > 0) && BatteryTarget())
                batteryQueuedUntil = Time.time + BatteryQueueTime;
            return false;
        }

        /// <summary>B: Zeri's League basic attack. A full charge adds max-health damage; it executes what it leaves low.</summary>
        bool BatteryShot()
        {
            if (!Player.AttackReady || roundsLeft > 0)
                return false;
            var target = BatteryTarget();
            if (!target)
                return false;
            Player.SpendAttack();
            Vector3 origin = Muzzle(out _);
            Vector3 direction = (target.AimPosition - origin).normalized;
            bool charged = FullyCharged;
            float before = target.Health + target.Shield;
            Player.Hit(target, BatteryDamage(target), DamageKind.Magic, "Passive", false, origin);
            bool executed = false;
            // League: the shot executes what it leaves under the threshold, unless a shield is still up.
            if (target.IsAlive && target.Shield <= 0 && target.Health <= ExecuteThreshold && !target.GetComponent<RiftStructure>())
            {
                Player.Hit(target, target.Health + 1, DamageKind.True, "Passive", false, origin);
                executed = !target.IsAlive;
            }
            // A shot soaked by a shield still lands (it spends the charge and refunds Spark Surge).
            bool landed = executed || target.Health + target.Shield < before;
            if (charged && landed)
                charge = 0;
            visuals?.BatteryBolt(origin, target.AimPosition, charged || executed);
            KickGun(charged ? .9f : .45f);
            Player.Emit(charged ? "ChargedShot" : "Zap", origin, direction);
            if (!landed)
                return true;
            Player.Emit(executed ? "Execute" : charged ? "ChargedHit" : "BatteryHit", target.AimPosition, direction);
            if (executed)
                visuals?.ExecuteMark(target.transform.position);
            // League: only charged attacks refund Spark Surge and feed Overcharge.
            if (charged)
            {
                Player.ReduceCooldown(2, .5f);
                ExtendOvercharge(target, 1);
            }
            return true;
        }

        // ---------- Q: Burst Fire ----------

        /// <summary>The gun's muzzle and where it points (desktop: the camera).</summary>
        public Vector3 Muzzle(out Vector3 direction)
        {
            if (!Player.DesktopMode && Body is ChampionVRAvatar avatar && avatar.WeaponMuzzle(out var position, out direction))
                return position;
            direction = Player.AttackDirection;
            return Player.AttackOrigin;
        }

        void KickGun(float amount)
        {
            if (Body is ChampionVRAvatar avatar)
                avatar.Kick(amount);
        }

        // Burst Fire is a skill shot: it fires at whatever the gun points at, enemy or not.
        // A queued battery shot takes the next attack instead of another burst.
        public override bool HasAttackTarget(Vector3 origin, Vector3 direction) => roundsLeft == 0 && !(BatteryQueued && BatteryTarget());

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            burstOvercharged = Overcharged;
            roundsLeft = burstOvercharged ? OverchargedRounds : Rounds;
            nextRound = Time.time;
            burstCrit = burstFirstHit = false;
            AddCharge(10);
            Player.Emit(burstOvercharged ? "Volley3" : Surging ? "VolleyPierce" : "Volley", Muzzle(out var aim), aim);
            FireRounds();
        }

        /// <summary>Attack speed past the cap, which League turns into bonus attack damage (60%).</summary>
        float ExcessAttackSpeed
        {
            get
            {
                float uncapped = 1 / Mathf.Max(.01f, Stats.AttackInterval(BonusAttackSpeed) * Health.AttackIntervalMultiplier);
                return Mathf.Max(0, uncapped - AttackSpeedCap);
            }
        }

        float BurstAD => AD + ExcessAttackSpeed * 100 * .6f;

        float RoundDamage => (ByRank(0, 22, 26, 30, 34, 38) + BurstAD * ByRank(0, 1.02f, 1.04f, 1.06f, 1.08f, 1.10f)) / Rounds;

        /// <summary>Burst Fire crits for 200% (League), plus whatever items add over the usual 175%.</summary>
        float CritMultiplier => 2 + (Player.Economy ? Mathf.Max(0, Player.Economy.CriticalDamage - 1.75f) : 0);

        void FireRounds()
        {
            while (roundsLeft > 0 && Time.time >= nextRound)
            {
                roundsLeft--;
                nextRound += burstOvercharged ? OverchargedSpacing : RoundSpacing;
                FireRound();
            }
        }

        /// <summary>
        /// One round along the gun's current aim, with League's slight spread. Overcharged rounds carry the whole burst's
        /// damage between three of them.
        /// </summary>
        void FireRound()
        {
            Vector3 origin = Muzzle(out var direction);
            float spread = burstOvercharged ? .8f : 1.6f;
            direction = Quaternion.AngleAxis(Random.Range(-spread, spread), Vector3.up) * Quaternion.AngleAxis(Random.Range(-spread * .5f, spread * .5f), Vector3.Cross(Vector3.up, direction).normalized) * direction;
            float range = BurstRange;
            if (Player.TerrainRay(origin, direction, BurstRange, out var wall))
                range = wall.distance;
            float damage = RoundDamage * (burstOvercharged ? (float)Rounds / OverchargedRounds : 1);
            var hits = RoundTargets(origin, direction, range);
            Vector3 end = origin + direction * range;
            if (hits.Count > 0 && !Surging)
                end = hits[0].AimPosition;
            visuals?.Tracer(origin, end, burstOvercharged);
            KickGun(burstOvercharged ? .5f : .3f);
            Player.Emit("Round", origin, direction);
            for (int i = 0; i < hits.Count; i++)
            {
                var target = hits[i];
                bool first = !burstFirstHit && i == 0;
                float amount = damage * (i == 0 ? 1 : ByRank(2, .8f, .85f, .9f, .95f, 1f));
                if (burstCrit)
                    amount *= CritMultiplier;
                // The burst's first hit is the "attack": it rolls the crit and triggers on-hit items once.
                float dealt = Player.Hit(target, amount, DamageKind.Physical, "Q", first, origin);
                if (first)
                {
                    burstFirstHit = true;
                    burstCrit = Player.Economy && Player.Economy.LastAttackCritical;
                    if (burstCrit && dealt > 0)
                        Player.Hit(target, dealt * (CritMultiplier / Mathf.Max(1, Player.Economy.CriticalDamage) - 1), DamageKind.Physical, "Q", false, origin);
                    if (Surging)
                        Player.Hit(target, (ByRank(2, 22, 24, 26, 28, 30) + AP * .2f) * (1 + (Player.Economy ? Player.Economy.CriticalChance : 0)), DamageKind.Magic, "E", false, origin);
                    if (dealt > 0 && !IsWard(target))
                    {
                        Player.ReduceCooldown(2, burstCrit ? 1.5f : .5f);
                        ExtendOvercharge(target, burstCrit ? 3 : 1);
                        if (burstOvercharged)
                            ChainLightning(target);
                    }
                }
                if (dealt > 0)
                {
                    visuals?.Impact(target.AimPosition, burstOvercharged ? Plasma : Volt);
                    if (i == 0)
                        Player.Emit("Hit", target.AimPosition, direction);
                }
            }
            if (hits.Count == 0 && range < BurstRange)
                visuals?.Impact(origin + direction * range, Volt);
        }

        /// <summary>
        /// Enemies along a round's path, nearest first: the first one only, or all of them with Spark Surge. A ward stops
        /// the round when it is the first thing hit and is otherwise passed through.
        /// </summary>
        List<Combatant> RoundTargets(Vector3 origin, Vector3 direction, float range)
        {
            var found = new List<(Combatant target, float along)>();
            foreach (var target in Player.EnemiesAround(origin + direction * range * .5f, range * .5f + RoundRadius + 1, true))
            {
                Vector3 to = target.AimPosition - origin;
                float along = Vector3.Dot(to, direction);
                if (along < 0 || along > range)
                    continue;
                float off = (to - direction * along).magnitude;
                if (off <= RoundRadius + TargetRadius(target))
                    found.Add((target, along));
            }
            found.Sort((a, b) => a.along.CompareTo(b.along));
            var result = new List<Combatant>();
            foreach (var f in found)
            {
                bool ward = IsWard(f.target);
                if (ward && result.Count > 0)
                    continue;
                result.Add(f.target);
                if (ward || !Surging || f.target.GetComponent<RiftStructure>())
                    break;
            }
            return result;
        }

        static float TargetRadius(Combatant target) => target.GetComponent<RiftStructure>() ? 1.4f : .45f;

        /// <summary>Overcharged Burst Fire: lightning jumps from the first target to up to four more enemies nearby.</summary>
        void ChainLightning(Combatant from)
        {
            var hit = new HashSet<Combatant> { from };
            var current = from;
            // 40% AD per jump; a critical burst adds 12% with Infinity Edge (League).
            float ratio = .4f + (burstCrit && Player.Economy && Player.Economy.Owns(3031) ? .12f : 0);
            for (int jump = 0; jump < ChainTargets && Health.IsAlive; jump++)
            {
                Combatant next = null;
                float best = ChainRange * ChainRange;
                foreach (var candidate in Player.EnemiesAround(current.AimPosition, ChainRange))
                {
                    if (hit.Contains(candidate) || IsWard(candidate))
                        continue;
                    float d = (candidate.AimPosition - current.AimPosition).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        next = candidate;
                    }
                }
                if (!next)
                    break;
                visuals?.Chain(current.AimPosition, next.AimPosition);
                float dealt = Player.Hit(next, AD * ratio, DamageKind.Physical, "R", false, current.AimPosition);
                if (dealt > 0)
                    Player.Emit("Chain", next.AimPosition, (next.AimPosition - current.AimPosition).normalized);
                hit.Add(next);
                current = next;
            }
        }

        // ---------- W: Ultrashock Laser ----------

        /// <summary>League's 0.55 s cast, shortened by bonus attack speed down to 0.3 s: how long X must be held.</summary>
        float PulseCastTime => Mathf.Lerp(.55f, .3f, Mathf.Clamp01(Stats.BonusAttackSpeed + BonusAttackSpeed));

        bool PulseReady => holdingPulse && Time.time - pulseHoldStart >= PulseCastTime;

        public float PulseDamage => ByRank(1, 30, 70, 110, 150, 190) + AD + AP * .5f;

        float PulseSlow => 1 - ByRank(1, .3f, .35f, .4f, .45f, .5f);

        public Pose PalmAim()
        {
            if (!Player.DesktopMode && Body)
                return Body.Aim(true);
            return new Pose(Player.OffHandOrigin, Quaternion.LookRotation(Player.OffHandDirection));
        }

        void ReleasePulse()
        {
            if (!holdingPulse)
                return;
            bool ready = PulseReady;
            holdingPulse = false;
            if (!ready)
            {
                HoldHint("Ultrashock Laser: hold X until your palm crackles, then release.");
                return;
            }
            var aim = PalmAim();
            Vector3 direction = aim.rotation * Vector3.forward;
            if (!Player.Commit(1, false, aim.position, direction))
                return;
            ZeriPulse.Launch(this, Player, aim.position, direction);
        }

        void HoldHint(string text)
        {
            if (Time.time < nextHoldHint)
                return;
            nextHoldHint = Time.time + 4;
            RiftMatch.Instance?.Notify(text);
        }

        /// <summary>The pulse met an enemy: damage and slow it. Only the laser can critically strike (League).</summary>
        public void PulseHit(Combatant target, Vector3 at, Vector3 direction)
        {
            if (!Health.IsAlive)
                return;
            float dealt = Player.Hit(target, PulseDamage, DamageKind.Physical, "W", false, at);
            if (dealt <= 0)
                return;
            target.ApplySlow(PulseSlow, 2);
            Player.Emit("PulseHit", at, direction);
            visuals?.PulseImpact(at, target.transform.position);
            ExtendOvercharge(target, 1);
        }

        /// <summary>
        /// The pulse met a wall: the wall glows and 0.85 s later a laser fires from it, level and straight on through, to
        /// the laser's full range from where Zeri cast it.
        /// </summary>
        public void PulseWall(Vector3 origin, Vector3 wall, Vector3 normal, Vector3 direction)
        {
            // A pulse shot nearly straight up or down has no sensible line through the wall.
            if (Mathf.Abs(direction.y) > .85f)
            {
                visuals?.Impact(wall, Plasma);
                return;
            }
            Vector3 flat = Player.PlanarDirection(direction);
            float remaining = Mathf.Max(4, LaserRange - Geo.FlatDistance(origin, wall));
            Vector3 end = wall + flat * remaining;
            Player.Emit("LaserCharge", wall, flat);
            visuals?.LaserTelegraph(wall, normal, end, LaserDelay);
            Player.StartCoroutine(FireLaser(wall, flat, end, remaining));
        }

        System.Collections.IEnumerator FireLaser(Vector3 start, Vector3 direction, Vector3 end, float length)
        {
            yield return new WaitForSeconds(LaserDelay);
            if (Player.Kit != this || !Health.IsAlive)
                yield break;
            visuals?.Laser(start, end);
            Player.Emit("Laser", start, direction);
            float damage = PulseDamage;
            // League: the laser critically strikes champions and monsters for 150% (165% with Infinity Edge); there are
            // no enemy champions here, so monsters.
            float crit = 1.5f + (Player.Economy && Player.Economy.Owns(3031) ? .15f : 0);
            foreach (var target in Player.EnemiesAround((start + end) * .5f, length * .5f + LaserHalfWidth))
            {
                if (!Health.IsAlive)
                    break;
                Vector3 to = target.AimPosition - start;
                float along = Vector3.Dot(to, direction);
                if (along < -.5f || along > length || Geo.Flat(to - direction * along).magnitude > LaserHalfWidth)
                    continue;
                float dealt = Player.Hit(target, damage * (IsMonster(target) ? crit : 1), DamageKind.Physical, "W", false, target.AimPosition - direction);
                if (dealt > 0)
                {
                    target.ApplySlow(PulseSlow, 2);
                    Player.Emit("PulseHit", target.AimPosition, direction);
                    visuals?.PulseImpact(target.AimPosition, target.transform.position);
                    ExtendOvercharge(target, 1);
                }
            }
        }

        // ---------- E: Spark Surge ----------

        /// <summary>A planned Spark Surge: where it starts and lands, and how long it spends over a wall or tower.</summary>
        public struct SurgePlan
        {
            public Vector3 start, landing, direction;
            public bool vaulted;
            /// <summary>Seconds of the whole dash, and of the part over the obstacle, at League's dash speed.</summary>
            public float seconds, overObstacle;
        }

        /// <summary>
        /// League's dash speed, 600 units per second plus her move speed, in metres per second. Long vaults (more than
        /// <see cref="VaultViewThreshold"/> seconds over the obstacle) are watched from outside her body.
        /// </summary>
        public float DashSpeed => (600 + Player.Stats.MoveSpeed) / 100f;

        public const float VaultViewThreshold = .3f;

        public bool Vaulting { get; private set; }

        /// <summary>
        /// Dashes where you walk (or where the gun points), vaulting a wall or tower you are close to. A short hop (a
        /// thin wall or a tower, at most 0.3 s over it) is a first-person blink. A long vault cuts behind a brief fade
        /// to a view behind and above her: the view follows Zeri's arc over the wall at a fixed distance, then the view
        /// cuts back into her body where she lands.
        /// </summary>
        bool SparkSurge()
        {
            if (Vaulting)
                return false;
            Vector3 direction = Player.LocomotionDirection(out bool walking);
            if (!walking)
            {
                Muzzle(out var aim);
                direction = Player.PlanarDirection(aim);
            }
            Vector3 feet = Player.Feet;
            if (!PlanSurge(feet, direction, out var plan))
            {
                RiftMatch.Instance?.Notify("Spark Surge: no room to land there.");
                return false;
            }
            if (!Player.Commit(2, false, feet + Vector3.up, direction))
                return false;
            haveFeet = false;
            surgeUntil = Time.time + SurgeBuff;
            if (plan.vaulted && plan.overObstacle > VaultViewThreshold && ComfortSettings.SkyView && Definition.model && !(Player.BrushView && Player.BrushView.Active))
            {
                Player.StartCoroutine(VaultFromOutside(plan));
                return true;
            }
            Player.ComfortBlink(.14f);
            visuals?.SurgeTrail(feet + Vector3.up * .9f, plan.landing + Vector3.up * .9f);
            Player.MoveFeet(plan.landing);
            Player.Emit("Surge", plan.landing, direction);
            if (plan.vaulted)
                RiftMatch.Instance?.Notify("Vaulted!");
            return true;
        }

        /// <summary>
        /// A wall or tower within reach is vaulted: the landing is the full dash distance past it if there is room,
        /// further out for a thick wall (up to 12 m), measured on the Rift's collision grid (towers by a ray back from
        /// beyond). Without one the dash stops short of walls.
        /// </summary>
        public bool PlanSurge(Vector3 feet, Vector3 direction, out SurgePlan plan)
        {
            plan = new SurgePlan { start = feet, direction = direction };
            float speed = Mathf.Max(1, DashSpeed);
            Vector3 chest = feet + Vector3.up * .9f;
            if (Player.TerrainRay(chest, direction, SurgeWallReach + .3f, out var wall) && wall.normal.y < .5f)
            {
                float exit = ObstacleExit(feet, chest, direction, wall);
                if (exit > 0)
                    for (float along = Mathf.Max(exit + .5f, SurgeDistance); along <= SurgeMaxVault; along += .35f)
                        if (VaultLanding(feet, direction, along, out var ground))
                        {
                            plan.landing = ground;
                            plan.vaulted = true;
                            plan.seconds = along / speed;
                            plan.overObstacle = Mathf.Max(0, exit - wall.distance) / speed;
                            return true;
                        }
            }
            if (!Player.StepPoint(feet + direction * SurgeDistance, out var point))
                return false;
            plan.landing = point;
            plan.seconds = Geo.FlatDistance(feet, point) / speed;
            return true;
        }

        /// <summary>Distance from the feet to where the obstacle in front ends: the grid for map walls, a ray back for the rest.</summary>
        float ObstacleExit(Vector3 feet, Vector3 chest, Vector3 direction, RaycastHit wall)
        {
            var grid = RiftNavGrid.Active;
            if (grid && wall.collider && wall.collider.GetComponent<RiftCollision>() && grid.CrossWall(feet, direction, SurgeMaxVault + 1, .5f, out _, out float end))
                return end;
            float reach = SurgeMaxVault + 1;
            return wall.collider && wall.collider.Raycast(new Ray(chest + direction * reach, -direction), out var back, reach) ? reach - back.distance : -1;
        }

        bool VaultLanding(Vector3 feet, Vector3 direction, float along, out Vector3 ground)
        {
            ground = default;
            var match = RiftMatch.Instance;
            var grid = RiftNavGrid.Active;
            Vector3 at = feet + direction * along;
            if (grid && !grid.Walkable(at))
                return false;
            return match && match.Ground(at + Vector3.up * 3, out ground) && Mathf.Abs(ground.y - feet.y) < 1.2f && Player.ClearDestination(ground);
        }

        /// <summary>
        /// The long vault, seen from outside. Behind a short spark-coloured fade the view moves to a spot behind and above
        /// the vault (whichever way the player is looking), the first-person body is hidden and her full model vaults
        /// along the real path with League's vault animation. The view keeps its distance from her as she travels, so
        /// it follows her over the wall. Then the view cuts back into her body on landing.
        /// While the view is away she cannot be targeted, because her body is not where the view is.
        /// </summary>
        System.Collections.IEnumerator VaultFromOutside(SurgePlan plan)
        {
            Vaulting = true;
            var spark = new Color(1f, .95f, .55f);
            Player.FreezeLocomotion(true);
            Player.ScreenFade(spark, .07f, .05f, .15f, .85f);
            yield return new WaitForSeconds(.07f);
            GameObject puppet = null;
            if (Player && Health.IsAlive && Player.Kit == this)
            {
                var head = Player.head.transform;
                Vector3 eyeAboveFeet = head.position - Player.Feet;
                Vector3 view = Player.PlanarDirection(head.forward);
                Vector3 middle = (plan.start + plan.landing) * .5f;
                float span = Geo.FlatDistance(plan.start, plan.landing);
                Vector3 eye = middle - view * Mathf.Max(4.5f, span * .75f) + Vector3.up * (2.4f + span * .12f);
                // The view's offset from her, kept for the whole vault so the camera follows her arc.
                Vector3 offset = eye - plan.start;
                Player.MoveFeet(eye - eyeAboveFeet);
                if (Body)
                    Body.Hidden = true;
                puppet = VaultPuppet(plan);
                visuals?.SurgeTrail(plan.start + Vector3.up * .9f, plan.landing + Vector3.up * .9f);
                float arc = Mathf.Clamp(1.2f + span * .18f, 1.4f, 3.2f);
                float begin = Time.time;
                while (Time.time - begin < plan.seconds && Player && Health.IsAlive && Player.Kit == this)
                {
                    float t = (Time.time - begin) / plan.seconds;
                    Vector3 at = Vector3.Lerp(plan.start, plan.landing, t) + Vector3.up * arc * 4 * t * (1 - t);
                    if (puppet)
                        puppet.transform.position = at;
                    Player.MoveFeet(at + offset - eyeAboveFeet);
                    yield return null;
                }
                Player.ScreenFade(spark, .06f, .04f, .16f, .85f);
                yield return new WaitForSeconds(.06f);
            }
            if (puppet)
                Object.Destroy(puppet);
            if (Body)
                Body.Hidden = false;
            if (Player && Health.IsAlive && Player.Kit == this)
            {
                Player.MoveFeet(plan.landing);
                Player.Emit("Surge", plan.landing, plan.direction);
            }
            Player.FreezeLocomotion(false);
            Vaulting = false;
        }

        /// <summary>Her full model playing League's vault animation (short, medium or long), stretched over the dash.</summary>
        GameObject VaultPuppet(SurgePlan plan)
        {
            var puppet = ChampionPuppet.Spawn(Player, plan.start, Quaternion.LookRotation(plan.direction));
            if (!puppet)
                return null;
            float span = Geo.FlatDistance(plan.start, plan.landing);
            puppet.Play(false, plan.seconds, span > 9 ? "Spell3_Vault_Long" : span > 6.5f ? "Spell3_Vault_Med" : "Spell3_Vault_Short", "Spell3");
            return puppet.gameObject;
        }

        public override bool HiddenFrom(Combatant attacker) => Vaulting;

        public override bool Blocks(DamageHit hit) => Vaulting;

        // ---------- R: Lightning Crash ----------

        /// <summary>+15% movement while Overcharged, +1.5% per Hypercharged stack, capped at +25% for comfort in VR.</summary>
        float OverchargedSpeed => Mathf.Min(.25f, .15f + .015f * HyperStacks);

        void ReleaseCrash()
        {
            if (!holdingCrash)
                return;
            bool ready = Time.time - crashHoldStart >= CrashCastTime;
            holdingCrash = false;
            if (!ready)
            {
                HoldHint("Lightning Crash: hold the left trigger a moment, then release.");
                return;
            }
            Vector3 feet = Player.Feet;
            if (!Player.Commit(3, false, feet + Vector3.up, Vector3.up))
                return;
            float damage = ByRank(3, 150, 250, 350) + BonusAD * .6f + AP * 1.1f;
            bool hitEnemy = false;
            foreach (var target in Player.EnemiesAround(feet, NovaRadius))
            {
                float dealt = Player.Hit(target, damage, DamageKind.Magic, "R", false, feet + Vector3.up);
                if (dealt <= 0)
                    continue;
                visuals?.Strike(target.AimPosition, target.transform.position);
                if (!IsWard(target) && !target.GetComponent<RiftStructure>())
                    hitEnemy = true;
            }
            visuals?.Nova(feet);
            Player.Emit("Crash", feet + Vector3.up, Vector3.up);
            // League Overcharges only when the nova hits an enemy (a champion; here any enemy unit).
            if (!hitEnemy)
                return;
            overchargedUntil = Time.time + OverchargeDuration;
            hyperStacks = 0;
            Player.Emit("Overcharge", feet + Vector3.up, Vector3.up);
        }

        /// <summary>
        /// Hits while Overcharged keep it going (2.5 s per hit, never past its full 5 s) and add Hypercharged stacks.
        /// League counts champions only; with no enemy champions in this game, any enemy unit counts.
        /// </summary>
        void ExtendOvercharge(Combatant target, int stacks)
        {
            if (!Overcharged || !target || target.GetComponent<RiftStructure>() || IsWard(target))
                return;
            overchargedUntil = Mathf.Min(Time.time + OverchargeDuration, overchargedUntil + OverchargeExtend);
            hyperStacks = (Time.time < hyperUntil ? hyperStacks : 0) + stacks;
            hyperUntil = Time.time + 2.5f;
        }

        // ---------- Input ----------

        public override bool Cast(int slot)
        {
            switch (slot)
            {
                case 0:
                    return BatteryShotOrQueue();
                case 2:
                    return SparkSurge();
                default:
                    return false;
            }
        }

        public override bool BeginHold(int slot)
        {
            var definition = Definition;
            float cost = definition ? definition.spells[slot].Cost(Rank(slot)) : 0;
            if (Player.Economy && definition && definition.UsesMana && Player.Economy.Mana < cost)
            {
                RiftMatch.Instance?.Notify("Not enough mana.");
                return false;
            }
            if (slot == 1)
            {
                holdingPulse = true;
                pulseHoldStart = Time.time;
                nextHum = 0;
                Player.Emit("PulseCharge", PalmAim().position, Vector3.up);
                return true;
            }
            if (slot == 3)
            {
                holdingCrash = true;
                crashHoldStart = Time.time;
                nextHum = 0;
                Player.Emit("CrashCharge", Player.Feet + Vector3.up, Vector3.up);
                return true;
            }
            return false;
        }

        public override void ReleaseHold(int slot)
        {
            if (slot == 1)
                ReleasePulse();
            else if (slot == 3)
                ReleaseCrash();
        }

        public override void CancelHold(int slot)
        {
            if (slot == 1)
                holdingPulse = false;
            else if (slot == 3)
                holdingCrash = false;
        }

        public override bool Busy => roundsLeft > 0;
    }
}
