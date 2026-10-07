using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Pantheon, the Unbreakable Spear, built for VR: a real spear in the right hand, a real shield on the left.
    /// Numbers follow the League wiki (patch 26.x); ranges are scaled to the VR Rift.
    /// <list type="bullet">
    /// <item>Attacks: thrust or swing the spear through enemies, or hold the right trigger to jab what the spear points at.</item>
    /// <item>Mortal Will: attacks and spells add stacks (pips on the shield rim); at five the spear blazes and the next
    /// spell is empowered. Full at the start, on respawn and in the fountain.</item>
    /// <item>Q Comet Spear: tap B for a spectral thrust along the spear (60% of the cooldown comes back); hold B until the
    /// spear ignites and throw it like a javelin. Low-health enemies take execute damage.</item>
    /// <item>W Shield Vault: hold X and point the shield at an enemy, release to vault onto it: stun and a max-health bash.
    /// Empowered, the next attack strikes three times.</item>
    /// <item>E Aegis Assault: press A to brace. For 1.5 s everything hitting the front of the real shield is blocked
    /// (turrets get through) while spectral spears stab out from behind it; then (or on a second press) the shield slams.</item>
    /// <item>R Grand Starfall: hold the left trigger to raise a hologram of the Rift over the shield hand; point the spear
    /// at where you will land and release. After a two second channel Pantheon leaps: the view cuts to the sky above
    /// the target (or fades, see Settings), a comet spear strikes first, then he crashes down with a shockwave.</item>
    /// </list>
    /// </summary>
    public class PantheonKit : MeleeKit
    {
        public static readonly Color Gold = new(1f, .8f, .35f, 1f);
        public static readonly Color Ember = new(1f, .55f, .2f, 1f);

        // League ranges in VR metres.
        const float ThrustRange = 5.3f, ThrustHalfWidth = .55f, SpearRange = 18, SpearSpeed = 32, ChargeTime = .35f, MaxCharge = 4;
        const float VaultRange = 7.5f, VaultCone = 25;
        const float BlockHalfAngle = 55, StrikeRange = 3.6f, StrikeHalfAngle = 35, SlamRange = 4.5f, SlamHalfAngle = 40, AegisSeconds = 1.5f;
        const float StarfallRange = 52, StarfallRadius = 5, SkyHeight = 16, ThrowSpeed = 1.6f;

        int will;
        float lastBlockCue;
        bool holdingQ, aimingVault, aimingStarfall, aegisEmpowered;
        float qHoldStart, tripleUntil, aegisUntil, nextStrike, resistUntil, spearBackAt;
        Combatant vaultTarget;
        readonly Dictionary<Combatant, float> strikeDamage = new();

        // Grand Starfall
        enum Leap { None, Channel, Air }
        Leap leap;
        float leapStart;
        Vector3 leapTarget, leapOrigin, skyFeet;
        bool spearDropped, landed, inSky, skyFadeOut;

        ChampionVRAvatar avatar;
        GameObject spearGlow, aegisGhost, vaultRing, landingRing, pillar, spearTemplate;
        readonly GameObject[] pips = new GameObject[5];
        Material pipLit, pipDark;
        StarfallMap map;
        ParticleSystem chargeMotes;

        public int Will => will;
        /// <summary>The Grand Starfall hologram while it exists (built on the first aim).</summary>
        public StarfallMap Map => map;
        public bool Aegis => Time.time < aegisUntil;
        public bool Leaping => leap != Leap.None;

        public override bool Busy => leap != Leap.None;
        public override bool BlocksCasts => leap != Leap.None;
        public override bool HoldToCast(int slot) => slot != 2;
        public override bool CanRecast(int slot) => slot == 2 && Aegis;
        public override float BonusResistance => Time.time < resistUntil ? LevelLerp(5, 32.94f) + BonusHealth * .025f : 0;

        float BonusHealth => Mathf.Max(0, Health.maxHealth - ChampionStats.Grow(Definition.health, Definition.healthGrowth, Player.Level));
        public override float ArmorPenetration => ByRank(3, .1f, .2f, .3f);

        public override string SlotStatus(int slot) => slot switch
        {
            0 when holdingQ && Time.time - qHoldStart >= ChargeTime => "THROW!",
            2 when Aegis => $"BRACED {aegisUntil - Time.time:0.0}s",
            3 when leap == Leap.Channel => "HOLD STILL",
            3 when leap == Leap.Air => "STARFALL",
            _ => null,
        };

        public override string StateText => leap != Leap.None ? "GRAND STARFALL" : Aegis ? "AEGIS ASSAULT" : Time.time < tripleUntil ? "NEXT ATTACK STRIKES 3x" : will >= 5 ? "MORTAL WILL: EMPOWERED" : $"MORTAL WILL {will}/5";

        float LevelLerp(float a, float b) => Mathf.Lerp(a, b, (Player.Level - 1) / 17f);

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            avatar = Body as ChampionVRAvatar;
            ResetState();
            will = 5;
            BuildVisuals();
            if (avatar)
                avatar.Posed += OnPosed;
            Player.Economy?.Recalculate();
        }

        public override void OnUnequip()
        {
            EndLeap(false);
            if (avatar)
                avatar.Posed -= OnPosed;
            DestroyVisuals();
            ResetState();
            avatar = null;
        }

        void ResetState()
        {
            holdingQ = aimingVault = aimingStarfall = aegisEmpowered = false;
            tripleUntil = aegisUntil = resistUntil = spearBackAt = 0;
            strikeDamage.Clear();
            leap = Leap.None;
            if (avatar && avatar.Weapon != null)
                avatar.Weapon.hidden = false;
        }

        public override void OnDeath()
        {
            EndLeap(false);
            holdingQ = aimingVault = aimingStarfall = false;
            aegisUntil = 0;
        }

        public override void OnRespawn() => will = 5;

        public override void Tick()
        {
            var match = RiftMatch.Instance;
            if (match && match.AtShop)
                will = 5;
            if (holdingQ && Time.time - qHoldStart > MaxCharge)
                Player.ReleaseSlot(0);
            if (avatar && avatar.Weapon != null && avatar.Weapon.hidden && leap == Leap.None && Time.time >= spearBackAt)
            {
                // A new spear forms in the hand.
                avatar.Weapon.hidden = false;
                Flash(avatar.PropPoint(avatar.Weapon, avatar.Weapon.tip * .4f), Gold, .4f, 24);
            }
            if (Aegis)
                AegisTick();
            else if (aegisUntil > 0)
                Slam();
            if (leap != Leap.None)
                LeapTick();
            UpdateVisuals();
        }

        // ---------- Passive: Mortal Will ----------

        protected override void OnAttackHit(Combatant target, float dealt) => SetWill(will + 1);

        /// <summary>Mortal Will stacks; reaching five rings the shield.</summary>
        void SetWill(int value)
        {
            value = Mathf.Clamp(value, 0, 5);
            if (value == 5 && will < 5 && Player.Health.IsAlive)
                Player.Emit("Will", ShieldCentre(), Vector3.up);
            will = value;
        }

        /// <summary>Casting spends five stacks for an empowered spell, otherwise adds one.</summary>
        bool SpendWill()
        {
            if (will >= 5)
            {
                will = 0;
                return true;
            }
            SetWill(will + 1);
            return false;
        }

        // ---------- Attacks ----------

        public override void BasicAttack(Vector3 origin, Vector3 direction)
        {
            var targets = Player.SweepTargets(origin, direction, Player.AttackReach, .45f);
            var target = targets.Count > 0 ? targets[0] : null;
            Jab(origin, target ? (target.AimPosition - origin).normalized : direction, .7f);
            if (target)
                StrikeTarget(target, target.AimPosition, direction);
        }

        public override void WeaponHit(Combatant target, Vector3 point, Vector3 swing) => StrikeTarget(target, point, swing);

        void StrikeTarget(Combatant target, Vector3 point, Vector3 direction)
        {
            if (Time.time < tripleUntil)
            {
                // Empowered Shield Vault: the attack becomes three rapid strikes of 40-55% AD that apply on-hit effects.
                tripleUntil = 0;
                Player.StartCoroutine(TripleStrike(target, direction));
                return;
            }
            float dealt = Player.Hit(target, AD, DamageKind.Physical, "Attack1", true, point);
            if (dealt > 0)
            {
                Player.Emit("Hit", point, direction);
                OnAttackHit(target, dealt);
            }
        }

        IEnumerator TripleStrike(Combatant target, Vector3 direction)
        {
            float each = AD * LevelLerp(.4f, .55f);
            for (int i = 0; i < 3 && Player.IsEnemy(target); i++)
            {
                Vector3 from = Player.AttackOrigin;
                Jab(from, (target.AimPosition - from).normalized, .9f);
                float dealt = Player.Hit(target, each, DamageKind.Physical, "Attack1", true, target.AimPosition);
                if (dealt > 0)
                {
                    Player.Emit("Hit", target.AimPosition, direction);
                    OnAttackHit(target, dealt);
                }
                yield return new WaitForSeconds(.1f);
            }
        }

        /// <summary>A golden spectral spear lunging from the hand (attacks, thrusts, Aegis strikes).</summary>
        void Jab(Vector3 from, Vector3 direction, float reach, float scale = 1, float alpha = .5f)
        {
            if (!avatar || avatar.Weapon == null || !avatar.Weapon.mesh)
                return;
            var spec = avatar.Weapon;
            float s = spec.scale * avatar.BodyScale * scale;
            var rotation = Quaternion.LookRotation(direction) * spec.fit;
            Vector3 start = from + direction * reach * .2f - rotation * (spec.handle * s);
            var ghost = AbilityFx.Ghost(spec.mesh, start, rotation, Vector3.one * s, new Color(Gold.r, Gold.g, Gold.b, alpha), .22f);
            ghost.AddComponent<FxLunge>().velocity = direction * reach / .2f;
            Player.Track(ghost);
        }

        // ---------- Q: Comet Spear ----------

        float QDamage(Combatant target, bool empowered)
        {
            bool low = target.Health < target.maxHealth * .2f;
            float damage = low ? ByRank(0, 155, 230, 305, 380, 455) + BonusAD * 2.3f : ByRank(0, 70, 100, 130, 160, 190) + BonusAD * 1.15f;
            if (empowered)
                damage += LevelLerp(20, 240) + BonusAD * .1f;
            if (target.GetComponent<RiftMinion>())
                damage *= .7f;
            else if (target.GetComponent<RiftObjective>())
                damage *= .8f;
            return damage;
        }

        bool BeginSpear()
        {
            if (!CanPay(0) || (avatar && avatar.Weapon != null && avatar.Weapon.hidden))
                return false;
            holdingQ = true;
            qHoldStart = Time.time;
            return true;
        }

        void ReleaseSpear()
        {
            if (!holdingQ)
                return;
            holdingQ = false;
            if (Time.time - qHoldStart < ChargeTime)
                Thrust();
            else
                Hurl();
        }

        /// <summary>Tap: a spectral spear thrusts along the real spear; 60% of the cooldown comes back.</summary>
        void Thrust()
        {
            Vector3 origin = Player.AttackOrigin;
            Vector3 direction = Player.PlanarDirection(Player.AttackDirection);
            if (!Player.Commit(0, false, origin, direction))
                return;
            bool empowered = SpendWill();
            Jab(origin, direction, ThrustRange, 2.2f, .6f);
            Vector3 start = new(origin.x, Player.Feet.y + .9f, origin.z);
            foreach (var t in Player.SweepTargets(start, direction, ThrustRange, ThrustHalfWidth))
            {
                if (t.GetComponent<RiftStructure>() || t.GetComponent<RiftVisionWard>())
                    continue;
                if (Player.Hit(t, QDamage(t, empowered), DamageKind.Physical, "Q", false, origin) > 0)
                    Player.Emit("Hit", t.AimPosition, direction);
            }
            Player.RefundCooldown(0, .6f);
        }

        /// <summary>Hold: the charged spear is thrown like a javelin, along the throw (or where it points).</summary>
        void Hurl()
        {
            Vector3 throwVelocity = Player.HandVelocity(false, true);
            Vector3 aim = throwVelocity.magnitude > ThrowSpeed ? throwVelocity : Player.AttackDirection;
            Vector3 direction = Player.PlanarDirection(aim);
            Vector3 origin = Player.AttackOrigin;
            if (!Player.Commit(0, false, origin, direction))
                return;
            bool empowered = SpendWill();
            var victims = new HashSet<Combatant>();
            if (!spearTemplate)
                spearTemplate = SpearVisual(1.2f);
            var spear = AbilityProjectile.Launch(Player, origin, direction, 0, DamageKind.Physical, "Q", SpearSpeed, SpearRange, true, Gold, spearTemplate);
            spear.flightHeight = 1.2f;
            // The first enemy takes full damage, the rest half.
            spear.damageFor = t => QDamage(t, empowered) * (victims.Add(t) && victims.Count == 1 ? 1 : .5f);
            spear.hit = (t, dealt) =>
            {
                Player.Emit("Hit", t.AimPosition, direction);
                Flash(t.AimPosition, Ember, .4f, 20);
            };
            if (avatar && avatar.Weapon != null)
            {
                avatar.Weapon.hidden = true;
                spearBackAt = Time.time + .5f;
            }
        }

        /// <summary>A copy of the real spear (its own textures) for throws, pointed along +Z.</summary>
        GameObject SpearVisual(float scale)
        {
            if (!avatar || avatar.Weapon == null || !avatar.Weapon.mesh)
                return null;
            var spec = avatar.Weapon;
            var root = new GameObject("Comet Spear");
            var model = new GameObject("Spear model");
            model.transform.SetParent(root.transform, false);
            float s = spec.scale * avatar.BodyScale * scale;
            model.transform.localRotation = spec.fit;
            model.transform.localScale = Vector3.one * s;
            // Centre the spear on the projectile, tip forward.
            model.transform.localPosition = -(spec.fit * (Vector3.Lerp(spec.tip, spec.handle, .5f) * s));
            model.AddComponent<MeshFilter>().sharedMesh = spec.mesh;
            var r = model.AddComponent<MeshRenderer>();
            r.sharedMaterial = spec.material;
            var glow = AbilityFx.MeshObject("Comet glow", spec.mesh, new Color(1f, .7f, .3f, .35f), true, model.transform, 1.04f);
            glow.transform.localPosition = Vector3.zero;
            AbilityFx.Motes(root.transform, Ember, .06f, .4f, 90, ParticleSystemShapeType.Sphere, .15f);
            root.SetActive(false);
            Player.Track(root);
            return root;
        }

        // ---------- W: Shield Vault ----------

        Combatant VaultCandidate()
        {
            var aim = Body ? Body.Aim(true) : new Pose(Player.OffHandOrigin, Quaternion.LookRotation(Player.OffHandDirection));
            return Player.ConeTarget(aim.position, aim.rotation * Vector3.forward, VaultRange, VaultCone, false, t => !t.GetComponent<RiftVisionWard>());
        }

        bool BeginVault()
        {
            if (!CanPay(1) || Health.Rooted)
                return false;
            aimingVault = true;
            return true;
        }

        void ReleaseVault()
        {
            if (!aimingVault)
                return;
            aimingVault = false;
            var target = VaultCandidate();
            if (!target || Health.Rooted)
                return;
            Vector3 feet = Player.Feet;
            Vector3 toFeet = Geo.FlatDirection(feet - target.transform.position, -Player.PlanarDirection(Player.head.transform.forward));
            Vector3 landing = target.transform.position + toFeet * 1.1f;
            var match = RiftMatch.Instance;
            if (match && match.Ground(landing + Vector3.up * .5f, out var ground))
                landing = ground;
            if (!Player.ClearDestination(landing))
                landing = feet;
            if (!Player.Commit(1, false, Player.OffHandOrigin, (target.AimPosition - Player.OffHandOrigin).normalized))
                return;
            bool empowered = SpendWill();
            Player.ComfortBlink(.12f);
            Player.MoveFeet(landing);
            float percent = ByRank(1, 6, 6.5f, 7, 7.5f, 8) + AP * .015f;
            float damage = target.maxHealth * percent / 100;
            if (!target.countsAsChampion)
                damage = Mathf.Clamp(damage, 60, 150);
            Player.Hit(target, damage, DamageKind.Physical, "W", false, landing);
            target.ApplyStun(1);
            Player.Emit("Hit", target.AimPosition, -toFeet);
            Player.Emit("Bash", target.AimPosition, -toFeet);
            Flash(target.AimPosition, Gold, .7f, 36);
            Player.Track(AbilityFx.Ring(target.transform.position, 1.2f, .4f, Gold, .06f));
            if (empowered)
                tripleUntil = Time.time + 4;
            Player.ResetAttackTimer();
        }

        // ---------- E: Aegis Assault ----------

        public override bool Cast(int slot)
        {
            if (slot != 2)
                return false;
            if (Aegis)
            {
                // Pressing again slams early.
                aegisUntil = Time.time;
                Slam();
                return true;
            }
            Vector3 front = ShieldFront();
            if (!Player.Commit(2, false, Player.OffHandOrigin, front))
                return false;
            aegisEmpowered = SpendWill();
            aegisUntil = Time.time + AegisSeconds;
            nextStrike = Time.time;
            strikeDamage.Clear();
            Flash(ShieldCentre(), Gold, .5f, 30);
            return true;
        }

        /// <summary>Where the real shield faces, on the ground plane.</summary>
        Vector3 ShieldFront()
        {
            if (Player.DesktopMode || !Body)
                return Player.PlanarDirection(Player.head.transform.forward);
            return Player.PlanarDirection(Body.Aim(true).rotation * Vector3.forward);
        }

        Vector3 ShieldCentre() => avatar && avatar.Shield != null ? avatar.PropPoint(avatar.Shield, Vector3.zero) : Player.OffHandOrigin;

        void AegisTick()
        {
            if (Time.time < nextStrike)
                return;
            nextStrike = Time.time + .125f;
            // Spectral spears stab out from behind the shield; each strike is 8.3% AD, halved on minions, 100% AD at most per target.
            Vector3 front = ShieldFront();
            Vector3 centre = ShieldCentre();
            Vector3 side = Vector3.Cross(Vector3.up, front);
            Jab(centre + side * Random.Range(-.35f, .35f) + Vector3.up * Random.Range(-.25f, .2f), front, StrikeRange, 1.1f, .4f);
            Vector3 feet = Player.Feet;
            foreach (var t in Player.EnemiesAround(feet + front * StrikeRange * .5f, StrikeRange))
            {
                if (t.GetComponent<RiftStructure>() || t.GetComponent<RiftVisionWard>())
                    continue;
                Vector3 to = Geo.Flat(t.transform.position - feet);
                if (to.magnitude > StrikeRange + .4f || Vector3.Angle(front, to) > StrikeHalfAngle)
                    continue;
                strikeDamage.TryGetValue(t, out float done);
                float amount = Mathf.Min(AD * .083f * (t.GetComponent<RiftMinion>() ? .5f : 1), AD - done);
                if (amount <= 0)
                    continue;
                strikeDamage[t] = done + amount;
                Player.Hit(t, amount, DamageKind.Physical, "E", false, centre);
            }
        }

        void Slam()
        {
            aegisUntil = 0;
            Vector3 front = ShieldFront();
            Vector3 feet = Player.Feet;
            float damage = ByRank(2, 55, 105, 155, 205, 255) + BonusAD * 1.5f;
            foreach (var t in Player.EnemiesAround(feet + front * SlamRange * .5f, SlamRange))
            {
                if (t.GetComponent<RiftStructure>() || t.GetComponent<RiftVisionWard>())
                    continue;
                Vector3 to = Geo.Flat(t.transform.position - feet);
                if (to.magnitude > SlamRange + .4f || Vector3.Angle(front, to) > SlamHalfAngle)
                    continue;
                if (Player.Hit(t, damage, DamageKind.Physical, "E", false, feet) > 0)
                    Player.Emit("Hit", t.AimPosition, front);
            }
            Shockwave(feet, front, SlamRange, SlamHalfAngle);
            Flash(ShieldCentre(), Gold, .8f, 40);
            Player.Emit("Slam", ShieldCentre(), front);
            if (aegisEmpowered)
            {
                resistUntil = Time.time + 4;
                Health.ApplySpeed(.6f, 1.5f);
            }
            aegisEmpowered = false;
        }

        /// <summary>Aegis Assault blocks non-turret damage arriving at the front of the real shield.</summary>
        public override bool Blocks(DamageHit hit)
        {
            if (leap == Leap.Air)
                return true;
            if (!Aegis || (hit.source && hit.source.GetComponent<RiftStructure>()))
                return false;
            Vector3 from = hit.source ? hit.source.AimPosition : hit.origin;
            Vector3 to = Geo.Flat(from - Player.Feet);
            bool blocked = to.sqrMagnitude > 1e-4f && Vector3.Angle(ShieldFront(), to) <= BlockHalfAngle;
            if (blocked && Time.time - lastBlockCue > .15f)
            {
                lastBlockCue = Time.time;
                Player.Emit("Block", ShieldCentre(), -to.normalized);
            }
            return blocked;
        }

        public override bool HiddenFrom(Combatant attacker) => leap == Leap.Air;

        // ---------- R: Grand Starfall ----------

        bool BeginStarfall()
        {
            if (leap != Leap.None || Health.Rooted || !CanPay(3))
                return false;
            if (map == null)
                map = new StarfallMap(Player);
            map.Show(true);
            map.SetTarget(InitialTarget());
            aimingStarfall = true;
            return true;
        }

        Vector3 InitialTarget()
        {
            var aim = Body ? Body.Aim(true) : new Pose(Player.OffHandOrigin, Quaternion.LookRotation(Player.OffHandDirection));
            if (Player.GroundAim(aim.position, aim.rotation * Vector3.forward, StarfallRange, out var ground))
                return ground;
            return Player.Feet + Player.PlanarDirection(aim.rotation * Vector3.forward) * 12;
        }

        void ReleaseStarfall()
        {
            if (!aimingStarfall)
                return;
            aimingStarfall = false;
            map?.Show(false);
            if (landingRing)
                landingRing.SetActive(false);
            if (map == null || Health.Rooted || !LandingSpot(map.Target, out var target))
                return;
            if (!Player.Commit(3, false, Player.Feet, (target - Player.Feet).normalized))
                return;
            leap = Leap.Channel;
            leapStart = Time.time;
            leapTarget = target;
            leapOrigin = Player.origin.transform.position;
            spearDropped = landed = skyFadeOut = false;
            BuildLeapMarkers(target);
        }

        /// <summary>A clear, walkable landing spot near the chosen point and within range.</summary>
        bool LandingSpot(Vector3 wanted, out Vector3 spot)
        {
            spot = wanted;
            var match = RiftMatch.Instance;
            Vector3 feet = Player.Feet;
            Vector3 flat = Geo.Flat(wanted - feet);
            if (flat.magnitude > StarfallRange)
                wanted = feet + flat.normalized * StarfallRange;
            for (float radius = 0; radius <= 4; radius += 1)
                for (int i = 0; i < (radius == 0 ? 1 : 8); i++)
                {
                    float a = i * Mathf.PI / 4;
                    Vector3 probe = wanted + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                    if (match && !match.Ground(probe + Vector3.up * 2, out probe))
                        continue;
                    if (Player.ClearDestination(probe))
                    {
                        spot = probe;
                        return true;
                    }
                }
            return false;
        }

        void LeapTick()
        {
            float t = Time.time - leapStart;
            if (leap == Leap.Channel)
            {
                // Walking, stuns or death interrupt the first two seconds; the ultimate then waits 30 seconds.
                bool moved = Geo.FlatDistance(Player.origin.transform.position, leapOrigin) > .6f;
                if (moved || Health.Stunned || !Health.IsAlive)
                {
                    EndLeap(false);
                    Player.StartCooldown(3, 30);
                    RiftMatch.Instance?.Notify("Grand Starfall interrupted: hold still while you gather your strength.");
                    return;
                }
                if (t >= 2)
                {
                    leap = Leap.Air;
                    leapStart = Time.time;
                    inSky = ComfortSettings.SkyView;
                    // Cut away behind a golden fade: up into the sky above the target, or just stay faded.
                    Player.ScreenFade(new Color(1f, .78f, .35f), .2f, inSky ? .15f : 1.85f, inSky ? .25f : .25f);
                    Player.FreezeLocomotion(true);
                }
                return;
            }
            // In the air (2.25 s): invulnerable and untargetable.
            if (inSky && t >= .2f && t < 1.6f && !LocomotionInSky())
                EnterSky();
            if (!spearDropped && (t >= .8f || SkyThrow()))
                DropSpear();
            if (inSky && !skyFadeOut && t >= 1.6f)
            {
                skyFadeOut = true;
                Player.ScreenFade(new Color(1f, .78f, .35f), .2f, .2f, .25f);
            }
            if (!landed && t >= 1.9f)
                Land();
            if (t >= 2.25f)
                Crash();
        }

        bool LocomotionInSky() => Player.origin.transform.position.y > leapTarget.y + SkyHeight * .5f;

        void EnterSky()
        {
            // Hover above and a little behind the target, looking down at it; the rig stays perfectly still.
            Vector3 back = -Player.PlanarDirection(leapTarget - Player.Feet);
            skyFeet = leapTarget + Vector3.up * SkyHeight + back * 7;
            Player.MoveFeet(skyFeet);
            // From above, the landing ring marks the spot; the beacon would only blind.
            if (pillar)
                pillar.SetActive(false);
        }

        /// <summary>From the sky the spear can be hurled down by hand before it falls by itself.</summary>
        bool SkyThrow()
        {
            if (!inSky || !LocomotionInSky())
                return false;
            var v = Player.HandVelocity(false, true);
            return v.magnitude > 3 && v.y < -1;
        }

        void DropSpear()
        {
            spearDropped = true;
            // 40-190 (Comet Spear rank) +115% bonus AD +50% AP and a 50% slow for 2 s around the aim point.
            Vector3 at = leapTarget;
            if (inSky && LocomotionInSky())
            {
                var v = Player.HandVelocity(false, true);
                if (v.magnitude > 3 && v.y < -1 && Player.TerrainRay(Player.AttackOrigin, v, 60, out var hit) && Geo.FlatDistance(hit.point, leapTarget) < StarfallRadius + 2)
                    at = hit.point;
            }
            float damage = ByRank(0, 40, 77.5f, 115, 152.5f, 190) + BonusAD * 1.15f + AP * .5f;
            foreach (var target in Player.EnemiesAround(at + Vector3.up, 2.6f))
            {
                if (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                    continue;
                Player.Hit(target, damage, DamageKind.Physical, "R", false, at);
                target.ApplySlow(.5f, 2);
            }
            var comet = SpearVisual(2.2f);
            if (comet)
            {
                comet.SetActive(true);
                comet.transform.SetPositionAndRotation(at + Vector3.up * 12, Quaternion.LookRotation(Vector3.down));
                comet.AddComponent<FxLunge>().velocity = Vector3.down * 60;
                Object.Destroy(comet, .2f);
            }
            Flash(at + Vector3.up * .3f, Ember, 1.2f, 50);
            Player.Track(AbilityFx.Ring(at, 2.6f, .6f, Ember, .08f));
        }

        void Land()
        {
            landed = true;
            Player.MoveFeet(leapTarget);
            Player.FreezeLocomotion(false);
        }

        void Crash()
        {
            // 300/500/700 (+100% AP) magic damage, half at the edge of the shockwave. Full Mortal Will on landing.
            float damage = ByRank(3, 300, 500, 700) + AP;
            Vector3 at = Player.Feet;
            foreach (var target in Player.EnemiesAround(at + Vector3.up, StarfallRadius))
            {
                if (target.GetComponent<RiftStructure>() || target.GetComponent<RiftVisionWard>())
                    continue;
                float edge = Mathf.Clamp01(Geo.FlatDistance(target.transform.position, at) / StarfallRadius);
                if (Player.Hit(target, damage * Mathf.Lerp(1, .5f, edge), DamageKind.Magic, "R", false, at) > 0)
                    Player.Emit("Hit", target.AimPosition, Vector3.up);
            }
            Shockwave(at, Vector3.forward, StarfallRadius, 180);
            Flash(at + Vector3.up * .5f, Gold, 2f, 90);
            Player.Emit("Crash", at, Vector3.up);
            SetWill(5);
            EndLeap(true);
        }

        void EndLeap(bool completed)
        {
            if (leap == Leap.Air && !landed && Player && Health.IsAlive)
                Player.MoveFeet(leapTarget);
            if (Player)
                Player.FreezeLocomotion(false);
            leap = Leap.None;
            inSky = false;
            foreach (var go in new[] { landingRing, pillar })
                if (go)
                    go.SetActive(false);
        }

        void BuildLeapMarkers(Vector3 target)
        {
            if (!landingRing)
            {
                landingRing = AbilityFx.Line("Grand Starfall landing", new Color(1f, .75f, .3f, .85f), .12f, 64).gameObject;
                landingRing.GetComponent<LineRenderer>().loop = true;
                Player.Track(landingRing);
            }
            landingRing.SetActive(true);
            AbilityFx.SetCircle(landingRing.GetComponent<LineRenderer>(), target, StarfallRadius);
            if (!pillar)
            {
                pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(pillar.GetComponent<Collider>());
                pillar.name = "Grand Starfall beacon";
                var r = pillar.GetComponent<Renderer>();
                r.sharedMaterial = AbilityFx.Glass(new Color(1f, .8f, .4f, .22f), true, true);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Player.Track(pillar);
            }
            pillar.SetActive(true);
            pillar.transform.position = target + Vector3.up * 20;
            pillar.transform.localScale = new Vector3(1.2f, 20, 1.2f);
        }

        // ---------- Casting ----------

        public override bool BeginHold(int slot) => slot switch
        {
            0 => BeginSpear(),
            1 => BeginVault(),
            3 => BeginStarfall(),
            _ => false,
        };

        public override void ReleaseHold(int slot)
        {
            switch (slot)
            {
                case 0:
                    ReleaseSpear();
                    break;
                case 1:
                    ReleaseVault();
                    break;
                case 3:
                    ReleaseStarfall();
                    break;
            }
        }

        public override void CancelHold(int slot)
        {
            if (slot == 0)
                holdingQ = false;
            else if (slot == 1)
                aimingVault = false;
            else if (slot == 3)
            {
                aimingStarfall = false;
                map?.Show(false);
            }
        }

        bool CanPay(int slot)
        {
            if (Player.Cooldown(slot) > 0)
                return false;
            return !(Player.Economy && Definition.UsesMana && Player.Economy.Mana < Definition.spells[slot].Cost(Rank(slot)));
        }

        // ---------- Visuals ----------

        void Flash(Vector3 at, Color color, float radius, int count)
        {
            var go = new GameObject("Pantheon flash");
            go.transform.position = at;
            var ps = AbilityFx.Motes(go.transform, color, .07f + radius * .05f, .6f, 0, ParticleSystemShapeType.Sphere, radius * .4f);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f + radius, 2 + radius * 3);
            ps.Emit(count);
            Object.Destroy(go, 1.5f);
            Player.Track(go);
        }

        /// <summary>An expanding golden arc (the shield slam) or ring (the Starfall crash) on the ground.</summary>
        void Shockwave(Vector3 at, Vector3 front, float radius, float halfAngle)
        {
            var line = AbilityFx.Line("Shockwave", new Color(1f, .8f, .4f, .9f), .14f, 33, true);
            line.gameObject.AddComponent<FxShockwave>().Begin(at, front, radius, halfAngle, .45f);
            Player.Track(line.gameObject);
        }

        void BuildVisuals()
        {
            DestroyVisuals();
            if (!avatar)
                return;
            pipLit = AbilityFx.Glass(new Color(1f, .85f, .4f, 1f), true, true);
            pipDark = AbilityFx.Glass(new Color(.35f, .27f, .12f, .45f), false, true);
            if (avatar.Weapon != null && avatar.Weapon.mesh)
            {
                spearGlow = AbilityFx.MeshObject("Mortal Will blaze", avatar.Weapon.mesh, new Color(1f, .6f, .2f, .45f), true, avatar.Weapon.bone, 1.03f);
                chargeMotes = AbilityFx.Motes(spearGlow.transform, Ember, .1f, .35f, 120, ParticleSystemShapeType.Sphere, .3f);
                chargeMotes.transform.localPosition = avatar.Weapon.tip * .7f;
                spearGlow.SetActive(false);
            }
            if (avatar.Shield != null)
            {
                if (avatar.Shield.mesh)
                {
                    aegisGhost = AbilityFx.MeshObject("Aegis", avatar.Shield.mesh, new Color(1f, .8f, .35f, .3f), true, avatar.Shield.bone, 1.6f);
                    aegisGhost.transform.localPosition = new Vector3(-.25f, 0, 0);
                    aegisGhost.SetActive(false);
                }
                // Five Mortal Will pips around the top of the shield rim.
                for (int i = 0; i < pips.Length; i++)
                {
                    float a = Mathf.Lerp(140, 40, i / 4f) * Mathf.Deg2Rad;
                    var pip = AbilityFx.MeshObject("Mortal Will " + (i + 1), AbilityFx.CrystalMesh, Color.white, true, avatar.Shield.bone, .12f);
                    pip.transform.localPosition = new Vector3(0, .07f + Mathf.Sin(a) * .7f, Mathf.Cos(a) * .7f);
                    pip.transform.localRotation = Quaternion.Euler(0, 0, 90 - a * Mathf.Rad2Deg);
                    pips[i] = pip;
                }
            }
            vaultRing = AbilityFx.Line("Shield Vault target", new Color(1f, .8f, .35f, .8f), .04f, 32).gameObject;
            vaultRing.GetComponent<LineRenderer>().loop = true;
            vaultRing.SetActive(false);
            Player.Track(vaultRing);
        }

        void DestroyVisuals()
        {
            foreach (var go in new[] { spearGlow, aegisGhost, vaultRing, landingRing, pillar, spearTemplate })
                if (go)
                    Object.Destroy(go);
            for (int i = 0; i < pips.Length; i++)
            {
                if (pips[i])
                    Object.Destroy(pips[i]);
                pips[i] = null;
            }
            map?.Destroy();
            map = null;
            spearGlow = aegisGhost = vaultRing = landingRing = pillar = spearTemplate = null;
            chargeMotes = null;
        }

        void UpdateVisuals()
        {
            bool charged = holdingQ && Time.time - qHoldStart >= ChargeTime;
            if (spearGlow)
            {
                bool blaze = will >= 5 || charged;
                spearGlow.SetActive(blaze && avatar.Weapon.Visible);
                if (chargeMotes)
                {
                    var emission = chargeMotes.emission;
                    emission.enabled = charged;
                }
            }
            if (aegisGhost)
                aegisGhost.SetActive(Aegis);
            for (int i = 0; i < pips.Length; i++)
                if (pips[i])
                {
                    pips[i].SetActive(avatar.Shield.Visible);
                    pips[i].GetComponent<Renderer>().sharedMaterial = i < will ? pipLit : pipDark;
                }
            if (vaultRing)
            {
                vaultTarget = aimingVault ? VaultCandidate() : null;
                vaultRing.SetActive(vaultTarget);
                if (vaultTarget)
                    AbilityFx.SetCircle(vaultRing.GetComponent<LineRenderer>(), vaultTarget.transform.position, .8f);
            }
        }

        /// <summary>The Starfall hologram follows the shield hand, and the spear points at the landing spot on it.</summary>
        void OnPosed()
        {
            if (!aimingStarfall || map == null || !Body)
                return;
            var left = Body.Aim(true);
            var palmFacing = Body.LeftGrip().rotation * Vector3.right;
            map.Place(left.position + Vector3.up * .16f * Body.BodyScale + palmFacing * .05f, Player.head.transform.position);
            var weapon = Player.WeaponPose != null ? Player.WeaponPose() : new Pose(Player.AttackOrigin, Quaternion.LookRotation(Player.AttackDirection));
            if (map.Point(new Ray(weapon.position, weapon.rotation * Vector3.forward), out var world))
                map.SetTarget(world);
            if (!landingRing)
            {
                landingRing = AbilityFx.Line("Grand Starfall landing", new Color(1f, .75f, .3f, .85f), .12f, 64).gameObject;
                landingRing.GetComponent<LineRenderer>().loop = true;
                Player.Track(landingRing);
            }
            landingRing.SetActive(true);
            AbilityFx.SetCircle(landingRing.GetComponent<LineRenderer>(), map.Target, StarfallRadius);
            map.ShowRange(Player.Feet, StarfallRange, StarfallRadius);
        }
    }
}
