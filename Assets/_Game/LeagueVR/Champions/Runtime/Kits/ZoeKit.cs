using System.Collections;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Zoe, the Aspect of Twilight, built for VR: a ranged mage whose spells are things you hold and throw.
    /// Numbers follow the League wiki (patch 26.x); ranges are scaled like her 550-unit attack (10 m).
    /// <list type="bullet">
    /// <item>Attacks (right trigger): a star bolt from the palm at the enemy you point at (soft lock, ringed in gold).</item>
    /// <item>More Sparkles!: after any spell the right hand sparkles; the next attack or Spell Thief bolt deals bonus damage.</item>
    /// <item>Q Paddle Star!: hold B to make a star in your palm and release while throwing: it flies where you threw it
    /// (behind you works). Press B again while it flies or hovers and point: it is paddled toward the point you aim at.
    /// The further it travels, the bigger it grows and the harder it hits.</item>
    /// <item>W Spell Thief: minions Zoe kills sometimes drop spell shards; walk over or touch one to take it. It floats
    /// over your left palm. Hold X and release to use it: Heal, Barrier, Ghost and Cleanse burst from your hand, Ignite
    /// and Exhaust are thrown at the enemy you aim at. Every use adds speed and three orbiting bolts that fire at enemies.</item>
    /// <item>E Sleepy Trouble Bubble: hold A to blow a bubble, release while lobbing it. It arcs (over walls too), bounces
    /// on, and waits as a trap; the first enemy hit gets drowsy, then falls asleep. The next hit on it deals double.</item>
    /// <item>R Portal Jump: hold the left trigger and aim an arc; release to blink there through a portal and come back
    /// a second later. Attacks are reset.</item>
    /// </list>
    /// </summary>
    public class ZoeKit : RangedKit
    {
        public static readonly Color Gold = new(1f, .84f, .42f, 1f);
        public static readonly Color Pink = new(1f, .42f, .82f, 1f);
        public static readonly Color Violet = new(.6f, .42f, 1f, 1f);
        public static readonly Color Cyan = new(.45f, .9f, 1f, 1f);

        // League ranges in VR metres (Zoe's 550-unit attack is 10 m).
        public const float StarRange = 13, StarSpeed = 16, PaddleSpeed = 30, StarLinger = 1, StarBlast = 1.6f, MaxDamageTravel = 24;
        public const float BubbleRange = 14, BubbleGravity = 18, TrapRadius = 1.2f, TrapLife = 6;
        public const float PortalRange = 9, PortalStay = 1;
        const float BoltRange = 14, ShardChance = .1f, ShardGroundLife = 20, ShardHoldLife = 60, ThrowSpeed = 1.6f;

        PaddleStar star;
        float passiveUntil, nextNoShardNotice;
        bool holdingStar, aimingPaddle, holdingBubble, holdingShard, aimingPortal;
        float bubbleHoldStart;

        ZoeShardKind? shard;
        float shardUntil;
        int boltsLeft;
        float boltsUntil, nextBolt, orbitAngle;

        bool portalActive;
        Vector3 portalFrom;
        float portalBackAt;

        ChampionBodyBase boundBody;
        GameObject heldStar, heldBubble, heldShard, reticleObject;
        Renderer heldShardRenderer;
        ParticleSystem sparkles;
        LineRenderer paddleGuide, portalArc, portalRing, reticle, tether;
        GameObject portalA, portalB;
        readonly GameObject[] bolts = new GameObject[3];
        Combatant softTarget;

        public bool PassiveReady => Time.time < passiveUntil;
        public PaddleStar Star => star;
        public ZoeShardKind? Shard => shard;
        public int BoltsLeft => boltsLeft;
        public bool PortalActive => portalActive;

        public override bool HoldToCast(int slot) => true;
        public override bool CanRecast(int slot) => slot == 0 && star && star.CanPaddle;
        protected override float MissileSpeed => 20;

        public override string SlotStatus(int slot) => slot switch
        {
            0 when star && star.CanPaddle => "PADDLE!",
            0 when star => "FLYING",
            1 when shard.HasValue => $"{ZoeShard.Name(shard.Value)} {Mathf.CeilToInt(shardUntil - Time.time)}s",
            1 => "NO SHARD",
            3 when portalActive => $"BACK IN {Mathf.Max(0, portalBackAt - Time.time):0.0}",
            _ => null,
        };

        public override string StateText => boltsLeft > 0 ? $"WHEEEE! BOLTS {boltsLeft}" : PassiveReady ? "MORE SPARKLES READY" : shard.HasValue ? "W: " + ZoeShard.Name(shard.Value) + " SHARD" : "MORE SPARKLES!";

        // ---------- Lifecycle ----------

        public override void OnEquip()
        {
            ResetState();
            BuildVisuals();
            boundBody = Body;
            if (boundBody)
                boundBody.Posed += OnPosed;
        }

        public override void OnUnequip()
        {
            ReturnFromPortal();
            if (boundBody)
                boundBody.Posed -= OnPosed;
            boundBody = null;
            if (star)
                Object.Destroy(star.gameObject);
            foreach (var s in ZoeShard.Active.ToArray())
                if (s && s.owner == Player)
                    Object.Destroy(s.gameObject);
            DestroyVisuals();
            ResetState();
        }

        void ResetState()
        {
            star = null;
            passiveUntil = shardUntil = boltsUntil = 0;
            shard = null;
            boltsLeft = 0;
            holdingStar = aimingPaddle = holdingBubble = holdingShard = aimingPortal = false;
            portalActive = false;
        }

        public override void OnDeath()
        {
            ReturnFromPortal();
            CancelAll();
            boltsLeft = 0;
        }

        void CancelAll() => holdingStar = aimingPaddle = holdingBubble = holdingShard = aimingPortal = false;

        public override void Tick()
        {
            if (shard.HasValue && Time.time > shardUntil)
                shard = null;
            PickUpShards();
            FireBolts();
            if (portalActive && Time.time >= portalBackAt)
                ReturnFromPortal();
            UpdateReticle();
        }

        // ---------- Passive: More Sparkles! ----------

        float LevelLerp(float a, float b) => Mathf.Lerp(a, b, (Player.Level - 1) / 17f);

        float PassiveDamage => LevelLerp(16, 150) + AP * .2f;

        /// <summary>Every spell cast charges the next attack or Spell Thief bolt.</summary>
        void Sparkle() => passiveUntil = Time.time + 5;

        float ConsumePassive()
        {
            if (!PassiveReady)
                return 0;
            passiveUntil = 0;
            return PassiveDamage;
        }

        protected override void OnAttackHit(Combatant target, float dealt)
        {
            float bonus = ConsumePassive();
            if (bonus > 0 && target && target.IsAlive)
            {
                Player.Hit(target, bonus, DamageKind.Magic, "Passive", false, target.AimPosition);
                Burst(target.AimPosition, Pink, .5f, 18);
            }
        }

        protected override AbilityProjectile LaunchAttack(Vector3 origin, Combatant target)
        {
            bool sparkly = PassiveReady;
            var p = AbilityProjectile.Launch(Player, origin, (target.AimPosition - origin).normalized, AD, DamageKind.Physical, "Attack1", MissileSpeed, Player.AttackReach + 4, false, Gold, StarTemplate(sparkly ? .2f : .13f, sparkly ? Pink : Gold));
            return p;
        }

        // ---------- Q: Paddle Star! ----------

        public float StarGrowth(float travelled) => Mathf.Clamp01(travelled / MaxDamageTravel);

        public float StarDamage(float travelled)
        {
            float min = LevelLerp(2, 58) + ByRank(0, 50, 80, 110, 140, 170) + AP * .6f;
            float max = LevelLerp(5, 145) + ByRank(0, 125, 200, 275, 350, 425) + AP * 1.5f;
            return Mathf.Lerp(min, max, StarGrowth(travelled));
        }

        /// <summary>The star bursts on the first enemy it meets, damaging everything around it.</summary>
        public void StarHit(PaddleStar from, Vector3 at)
        {
            float damage = StarDamage(from.Travelled);
            foreach (var t in Player.EnemiesAround(at, StarBlast))
                if (!t.GetComponent<RiftVisionWard>())
                {
                    float dealt = Player.Hit(t, damage, DamageKind.Magic, "Q", false, at);
                    if (dealt > 0)
                        Player.Emit("Hit", t.AimPosition, (t.AimPosition - at).normalized);
                }
            Burst(at, Gold, StarBlast * .6f, 40 + (int)(40 * StarGrowth(from.Travelled)));
            Player.Emit("StarBurst", at, Vector3.up);
            Player.Track(AbilityFx.Ring(new Vector3(at.x, from.GroundY, at.z), StarBlast, .45f, Gold, .06f));
        }

        bool BeginStar()
        {
            if (star && star.CanPaddle)
            {
                aimingPaddle = true;
                return true;
            }
            if (!CanPay(0))
                return false;
            holdingStar = true;
            return true;
        }

        void ReleaseStar()
        {
            if (aimingPaddle)
            {
                aimingPaddle = false;
                if (!star || !star.CanPaddle)
                    return;
                Vector3 target = PaddleTarget();
                if (!Player.Commit(0, true, star.transform.position, (target - star.transform.position).normalized))
                    return;
                star.Paddle(target);
                Sparkle();
                return;
            }
            if (!holdingStar)
                return;
            holdingStar = false;
            Vector3 direction = ThrowDirection(false, out _);
            Vector3 start = Body ? Body.Palm(false) : Player.AttackOrigin;
            if (!Player.Commit(0, false, start, direction))
                return;
            if (star)
                Object.Destroy(star.gameObject);
            star = PaddleStar.Launch(this, Player, start, direction);
            Sparkle();
        }

        /// <summary>The ground point the right hand points at, at most a long throw away.</summary>
        Vector3 PaddleTarget()
        {
            var aim = RightAim();
            Vector3 direction = aim.rotation * Vector3.forward;
            if (Player.GroundAim(aim.position, direction, StarRange * 2, out var ground))
                return ground;
            return Player.Feet + Player.PlanarDirection(direction) * StarRange;
        }

        // ---------- W: Spell Thief ----------

        public override void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            // Enemy minions Zoe kills have a 10% chance to carry a shard (Barrier, Cleanse, Exhaust, Ghost, Heal or Ignite).
            if (hit.source != Health || victim.team == Health.team || !victim.GetComponent<RiftMinion>() || Random.value > ShardChance)
                return;
            var kind = (ZoeShardKind)Random.Range(0, System.Enum.GetValues(typeof(ZoeShardKind)).Length);
            ZoeShard.Drop(Player, kind, victim.transform.position, ShardGroundLife);
            RiftMatch.Instance?.Notify("A " + ZoeShard.Name(kind) + " spell shard dropped! Walk over or touch it.");
        }

        void PickUpShards()
        {
            if (ZoeShard.Active.Count == 0 || !Health.IsAlive)
                return;
            Vector3 feet = Player.Feet;
            Vector3 left = Body ? Body.Palm(true) : Player.OffHandOrigin, right = Body ? Body.Palm(false) : Player.AttackOrigin;
            for (int i = ZoeShard.Active.Count - 1; i >= 0; i--)
            {
                var s = ZoeShard.Active[i];
                if (!s || s.owner != Player)
                    continue;
                Vector3 p = s.transform.position;
                if (Geo.FlatDistance(p, feet) < 1f || Vector3.Distance(p, left) < .3f || Vector3.Distance(p, right) < .3f)
                {
                    // A new shard replaces the one in hand.
                    shard = s.kind;
                    shardUntil = Time.time + ShardHoldLife;
                    Burst(p, ZoeShard.Tint(s.kind), .25f, 16);
                    Player.Emit("Shard", p, Vector3.up);
                    Object.Destroy(s.gameObject);
                    if (heldShardRenderer)
                        heldShardRenderer.sharedMaterial = AbilityFx.Glass(ZoeShard.Tint(s.kind), true, true);
                }
            }
        }

        bool BeginShard()
        {
            if (!shard.HasValue)
            {
                if (Time.time > nextNoShardNotice)
                {
                    RiftMatch.Instance?.Notify("No spell shard: minions you finish off sometimes drop one.");
                    nextNoShardNotice = Time.time + 4;
                }
                return false;
            }
            holdingShard = true;
            return true;
        }

        void ReleaseShard()
        {
            if (!holdingShard || !shard.HasValue)
                return;
            holdingShard = false;
            var kind = shard.Value;
            Vector3 palm = Body ? Body.Palm(true) : Player.OffHandOrigin;
            Combatant target = null;
            if (ZoeShard.Targeted(kind))
            {
                Vector3 direction = ThrowDirection(true, out float speed);
                target = Player.ConeTarget(palm, direction, BoltRange, speed > ThrowSpeed ? 28 : 20);
                if (!target)
                {
                    RiftMatch.Instance?.Notify(ZoeShard.Name(kind) + " needs a target: aim your left hand at an enemy.");
                    return;
                }
            }
            if (!Player.Commit(1, false, palm, target ? (target.AimPosition - palm).normalized : Vector3.up))
                return;
            shard = null;
            // Wheeee! first, so a longer Ghost is not cut short by the shorter haste.
            Health.ApplySpeed(ByRank(1, .3f, .4f, .5f, .6f, .7f), ByRank(1, 2, 2.25f, 2.5f, 2.75f, 3));
            boltsLeft = 3;
            boltsUntil = Time.time + 10;
            nextBolt = Time.time + .35f;
            CastShard(kind, palm, target);
            Sparkle();
        }

        void CastShard(ZoeShardKind kind, Vector3 palm, Combatant target)
        {
            var color = ZoeShard.Tint(kind);
            switch (kind)
            {
                case ZoeShardKind.Heal:
                    Health.Heal(LevelLerp(80, 318));
                    Health.ApplySpeed(.3f, 1);
                    break;
                case ZoeShardKind.Barrier:
                    Health.AddShield(LevelLerp(105, 411), 2.5f);
                    break;
                case ZoeShardKind.Ghost:
                    Health.ApplySpeed(LevelLerp(.24f, .48f), 10);
                    break;
                case ZoeShardKind.Cleanse:
                    Health.ClearCrowdControl();
                    break;
                case ZoeShardKind.Ignite:
                case ZoeShardKind.Exhaust:
                    ThrowShard(kind, palm, target);
                    return;
            }
            // Self shards shatter in the hand.
            Burst(palm, color, .35f, 30);
            Player.Ring(Player.Feet, .8f, .5f);
        }

        void ThrowShard(ZoeShardKind kind, Vector3 palm, Combatant target)
        {
            var go = AbilityFx.MeshObject(ZoeShard.Name(kind) + " shard", AbilityFx.CrystalMesh, ZoeShard.Tint(kind), true, null, .14f);
            go.transform.position = palm;
            go.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 720, 0);
            AbilityFx.Motes(go.transform, ZoeShard.Tint(kind), .05f, .4f, 60, ParticleSystemShapeType.Sphere, .05f);
            var seeker = go.AddComponent<FxSeeker>();
            seeker.target = target;
            seeker.speed = 20;
            seeker.arrive = t =>
            {
                if (!Player.IsEnemy(t))
                    return;
                Burst(t.AimPosition, ZoeShard.Tint(kind), .4f, 24);
                if (kind == ZoeShardKind.Ignite)
                    Player.StartCoroutine(Ignite(t));
                else
                {
                    // Exhaust: slowed and weakened for 3 seconds.
                    t.ApplySlow(.7f, 3);
                    t.ApplyAttackSpeedSlow(.35f, 3);
                }
            };
            Player.Track(go);
        }

        IEnumerator Ignite(Combatant target)
        {
            // 70-410 true damage over 5 seconds, and 40% reduced healing.
            float perTick = LevelLerp(70, 410) / 5;
            target.ApplyGrievousWounds(.4f, 5);
            for (int i = 0; i < 5 && Player.IsEnemy(target); i++)
            {
                yield return new WaitForSeconds(1);
                if (Player.IsEnemy(target))
                {
                    Player.Hit(target, perTick, DamageKind.True, "Passive", false, target.AimPosition);
                    Burst(target.AimPosition, new Color(1f, .5f, .15f), .2f, 8);
                }
            }
        }

        /// <summary>Wheeee!: three bolts orbit Zoe and fire one at a time at the nearest awake enemy, her last target first.</summary>
        void FireBolts()
        {
            if (boltsLeft <= 0)
                return;
            if (Time.time > boltsUntil)
            {
                boltsLeft = 0;
                return;
            }
            if (Time.time < nextBolt)
                return;
            var target = BoltTarget();
            if (!target)
                return;
            int index = 3 - boltsLeft;
            Vector3 from = bolts[index] && bolts[index].activeSelf ? bolts[index].transform.position : Player.head.transform.position - Vector3.up * .4f;
            float damage = ByRank(1, 15, 25, 35, 45, 55) + AP * .1f + ConsumePassive();
            var p = AbilityProjectile.Launch(Player, from, (target.AimPosition - from).normalized, damage, DamageKind.Magic, "W", 18, BoltRange + 6, false, Cyan, StarTemplate(.1f, Cyan));
            p.homing = target;
            p.hit = (t, dealt) => Player.Emit("Hit", t.AimPosition, p.direction);
            boltsLeft--;
            nextBolt = Time.time + .3f;
        }

        Combatant BoltTarget()
        {
            var feet = Player.Feet;
            var last = Health.LastHitTarget;
            if (last && Player.IsEnemy(last) && !last.Asleep && Time.time - Health.LastHitTime < 3 && Geo.FlatDistance(last.transform.position, feet) < BoltRange)
                return last;
            Combatant best = null;
            float bestDistance = BoltRange;
            foreach (var t in Player.EnemiesAround(feet, BoltRange))
            {
                if (t.Asleep || t.GetComponent<RiftVisionWard>())
                    continue;
                float d = Geo.FlatDistance(t.transform.position, feet);
                if (d < bestDistance)
                {
                    best = t;
                    bestDistance = d;
                }
            }
            return best;
        }

        // ---------- E: Sleepy Trouble Bubble ----------

        bool BeginBubble()
        {
            if (!CanPay(2))
                return false;
            holdingBubble = true;
            bubbleHoldStart = Time.time;
            return true;
        }

        void ReleaseBubble()
        {
            if (!holdingBubble)
                return;
            holdingBubble = false;
            Vector3 direction = ThrowDirection(false, out float speed);
            Vector3 start = Body ? Body.Palm(false) : Player.AttackOrigin;
            // The throw sets the arc: a hard throw lands near the end of the range, a plain release lobs it all the way.
            float elevation = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(direction.y, -1, 1)) * Mathf.Rad2Deg, 8, 55);
            float distance = speed > ThrowSpeed ? Mathf.Clamp(speed * 2.6f, 6, BubbleRange) : BubbleRange;
            float launch = Mathf.Sqrt(BubbleGravity * distance / Mathf.Max(.2f, Mathf.Sin(2 * elevation * Mathf.Deg2Rad)));
            Vector3 flat = Player.PlanarDirection(direction);
            Vector3 velocity = (flat * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)) * launch;
            if (!Player.Commit(2, false, start, flat))
                return;
            SleepyBubble.Throw(this, Player, start, velocity);
            Sparkle();
        }

        /// <summary>The bubble found someone: damage now, drowsy for 1.4 s, then asleep for 2.25 s.</summary>
        public void BubbleHit(Combatant target, Vector3 at)
        {
            float damage = ByRank(2, 70, 110, 150, 190, 230) + AP * .45f;
            float dealt = Player.Hit(target, damage, DamageKind.Magic, "E", false, at);
            if (dealt > 0 && target.countsAsChampion)
                Player.RefundCooldown(2, ByRank(2, .16f, .195f, .23f, .265f, .3f));
            Burst(at, Pink, .6f, 36);
            Player.Emit("Pop", at, Vector3.up);
            Player.StartCoroutine(Drowsy(target, damage));
        }

        IEnumerator Drowsy(Combatant target, float damage)
        {
            var fx = target.gameObject.AddComponent<ZoeSleepFx>();
            float maxSlow = ByRank(2, .1f, .15f, .2f, .25f, .3f);
            float start = Time.time;
            while (Time.time - start < 1.4f)
            {
                if (!target || !target.IsAlive)
                    yield break;
                float t = (Time.time - start) / 1.4f;
                target.ApplySlow(1 - maxSlow * t, .15f);
                yield return null;
            }
            if (!target || !target.IsAlive)
                yield break;
            // Asleep: the next damage wakes it with bonus damage up to the bubble's damage; magic resist is shredded meanwhile.
            target.ApplySleep(2.25f, damage, Health);
            target.ShredMagicResistance(.3f, 1, 2.25f);
            Player.Emit("Sleep", target.AimPosition, Vector3.up);
            if (fx)
                fx.Sleep();
        }

        // ---------- R: Portal Jump ----------

        bool BeginPortal()
        {
            if (portalActive || Health.Rooted || !CanPay(3))
                return false;
            aimingPortal = true;
            return true;
        }

        void ReleasePortal()
        {
            if (!aimingPortal)
                return;
            aimingPortal = false;
            if (portalArc)
                portalArc.enabled = false;
            if (portalRing)
                portalRing.enabled = false;
            if (Health.Rooted || !PortalTarget(out var target))
                return;
            if (!Player.Commit(3, false, Player.Feet, (target - Player.Feet).normalized))
                return;
            Sparkle();
            portalFrom = Player.Feet;
            portalActive = true;
            portalBackAt = Time.time + PortalStay;
            OpenPortals(portalFrom, target);
            Player.ComfortBlink(.14f);
            Player.MoveFeet(target);
            Player.ResetAttackTimer();
        }

        /// <summary>Where the left hand's arc lands: the aimed ground point, at most 9 m away and on clear ground.</summary>
        bool PortalTarget(out Vector3 target)
        {
            var aim = LeftAim();
            Vector3 direction = aim.rotation * Vector3.forward;
            Vector3 feet = Player.Feet;
            Vector3 flat = Player.PlanarDirection(direction);
            float distance = PortalRange;
            if (Player.GroundAim(aim.position, direction, PortalRange * 3, out var ground))
                distance = Mathf.Min(PortalRange, Geo.FlatDistance(ground, feet));
            // Walk back toward Zoe until the portal fits on walkable ground.
            for (float d = distance; d >= 1.5f; d -= .5f)
            {
                if (Floor(feet + flat * d, out var g) && Mathf.Abs(g.y - feet.y) < 3 && Player.ClearDestination(g))
                {
                    target = g;
                    return true;
                }
            }
            target = feet + flat * distance;
            return false;
        }

        bool Floor(Vector3 probe, out Vector3 ground)
        {
            var match = RiftMatch.Instance;
            if (match)
                return match.Ground(probe + Vector3.up * .5f, out ground);
            bool found = Player.TerrainRay(probe + Vector3.up * 2, Vector3.down, 6, out var hit);
            ground = found ? hit.point : probe;
            return found;
        }

        void OpenPortals(Vector3 from, Vector3 to)
        {
            ClosePortals();
            portalA = Portal(from);
            portalB = Portal(to);
            // A ghost of Zoe waits by the way home, and a sparkling tether shows the way back.
            var mesh = Body ? Body.BakeBody() : null;
            if (mesh)
            {
                var t0 = Body.Skin.transform;
                var ghost = AbilityFx.Ghost(mesh, t0.position, t0.rotation, Vector3.one, new Color(.7f, .5f, 1f, .35f), PortalStay + .3f);
                ghost.GetComponent<GhostFade>().destroyMesh = true;
                Player.Track(ghost);
            }
            if (!tether)
                tether = AbilityFx.Line("Portal tether", new Color(.75f, .55f, 1f, .6f), .03f, 12);
            tether.gameObject.SetActive(true);
        }

        GameObject Portal(Vector3 at)
        {
            var go = new GameObject("Portal Jump portal");
            go.transform.position = at + Vector3.up * 1.05f;
            go.transform.rotation = Quaternion.LookRotation(Player.PlanarDirection(Player.head.transform.forward));
            var ring = AbilityFx.MeshObject("Portal ring", AbilityFx.StarMesh, new Color(.65f, .45f, 1f, .5f), true, go.transform, 1.3f);
            ring.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 0, 120);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(go.transform, false);
            disc.transform.localScale = new Vector3(1.1f, 1.7f, .08f);
            var r = disc.GetComponent<Renderer>();
            r.sharedMaterial = AbilityFx.Glass(new Color(.35f, .2f, .7f, .35f), false, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AbilityFx.Motes(go.transform, new Color(.8f, .6f, 1f, .9f), .06f, 1.2f, 70, ParticleSystemShapeType.Circle, .8f);
            Player.Track(go);
            return go;
        }

        void ClosePortals()
        {
            if (portalA)
                Object.Destroy(portalA);
            if (portalB)
                Object.Destroy(portalB);
            portalA = portalB = null;
            if (tether)
                tether.gameObject.SetActive(false);
        }

        void ReturnFromPortal()
        {
            if (!portalActive)
                return;
            portalActive = false;
            ClosePortals();
            if (!Player)
                return;
            Player.ComfortBlink(.14f);
            Player.MoveFeet(portalFrom);
            Player.Emit("PortalBack", Player.Feet, Vector3.up);
        }

        // ---------- Casting ----------

        public override bool Cast(int slot) => false;

        public override bool BeginHold(int slot) => slot switch
        {
            0 => BeginStar(),
            1 => BeginShard(),
            2 => BeginBubble(),
            _ => BeginPortal(),
        };

        public override void ReleaseHold(int slot)
        {
            switch (slot)
            {
                case 0:
                    ReleaseStar();
                    break;
                case 1:
                    ReleaseShard();
                    break;
                case 2:
                    ReleaseBubble();
                    break;
                default:
                    ReleasePortal();
                    break;
            }
        }

        public override void CancelHold(int slot)
        {
            switch (slot)
            {
                case 0:
                    holdingStar = aimingPaddle = false;
                    break;
                case 1:
                    holdingShard = false;
                    break;
                case 2:
                    holdingBubble = false;
                    break;
                default:
                    aimingPortal = false;
                    break;
            }
        }

        /// <summary>Cooldown and mana allow the slot (checked when the hold starts, so a throw is never wasted).</summary>
        bool CanPay(int slot)
        {
            if (Player.Cooldown(slot) > 0)
                return false;
            return !(Player.Economy && Definition.UsesMana && Player.Economy.Mana < Definition.spells[slot].Cost(Rank(slot)));
        }

        /// <summary>A throw aims along the hand's motion; a plain release aims where the open hand points.</summary>
        Vector3 ThrowDirection(bool left, out float speed)
        {
            Vector3 velocity = Player.HandVelocity(left, true);
            speed = velocity.magnitude;
            if (speed > ThrowSpeed)
                return velocity / speed;
            return (left ? LeftAim() : RightAim()).rotation * Vector3.forward;
        }

        Pose RightAim() => Body ? Body.Aim(false) : new Pose(Player.AttackOrigin, Quaternion.LookRotation(Player.AttackDirection));

        Pose LeftAim() => Body ? Body.Aim(true) : new Pose(Player.OffHandOrigin, Quaternion.LookRotation(Player.OffHandDirection));

        // ---------- Visuals ----------

        /// <summary>A gold star with a soft glow, for projectiles (instantiated per shot).</summary>
        GameObject starTemplate;
        Color starTemplateColor;
        float starTemplateSize;

        GameObject StarTemplate(float size, Color color)
        {
            if (!starTemplate || starTemplateColor != color || !Mathf.Approximately(starTemplateSize, size))
            {
                if (starTemplate)
                    Object.Destroy(starTemplate);
                starTemplate = StarVisual(null, size, color);
                starTemplate.SetActive(false);
                starTemplateColor = color;
                starTemplateSize = size;
            }
            return starTemplate;
        }

        /// <summary>A spinning star: a bright core and a larger additive glow.</summary>
        public static GameObject StarVisual(Transform parent, float size, Color glow)
        {
            var root = new GameObject("Zoe star");
            if (parent)
                root.transform.SetParent(parent, false);
            var core = AbilityFx.MeshObject("Star core", AbilityFx.StarMesh, Color.Lerp(glow, Color.white, .55f), false, root.transform, size);
            core.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 0, 400);
            var halo = AbilityFx.MeshObject("Star glow", AbilityFx.StarMesh, new Color(glow.r, glow.g, glow.b, .45f), true, root.transform, size * 1.6f);
            halo.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 0, -250);
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = AbilityFx.Glass(new Color(glow.r, glow.g, glow.b, .7f), true);
            trail.time = .25f;
            trail.startWidth = size * .8f;
            trail.endWidth = 0;
            trail.minVertexDistance = .04f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        /// <summary>A short burst of glowing motes.</summary>
        public void Burst(Vector3 at, Color color, float radius, int count)
        {
            var go = new GameObject("Zoe burst");
            go.transform.position = at;
            var ps = AbilityFx.Motes(go.transform, color, .06f + radius * .06f, .6f, 0, ParticleSystemShapeType.Sphere, radius * .5f);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f + radius, 1.5f + radius * 3);
            ps.Emit(count);
            Object.Destroy(go, 1.5f);
            Player.Track(go);
        }

        void BuildVisuals()
        {
            DestroyVisuals();
            heldStar = StarVisual(null, .12f, Gold);
            Object.Destroy(heldStar.GetComponent<TrailRenderer>());
            heldBubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(heldBubble.GetComponent<Collider>());
            heldBubble.name = "Held bubble";
            var bubbleRenderer = heldBubble.GetComponent<Renderer>();
            bubbleRenderer.sharedMaterial = AbilityFx.Glass(new Color(1f, .55f, .9f, .35f), false, true);
            bubbleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            AbilityFx.Motes(heldBubble.transform, new Color(1f, .7f, .95f, .8f), .02f, .5f, 25, ParticleSystemShapeType.Sphere, .3f);
            heldShard = AbilityFx.MeshObject("Held spell shard", AbilityFx.CrystalMesh, Color.white, true, null, .09f);
            heldShard.AddComponent<FxSpin>().degreesPerSecond = new Vector3(0, 160, 0);
            heldShardRenderer = heldShard.GetComponent<Renderer>();
            var sparkleHost = new GameObject("More Sparkles hand");
            sparkles = AbilityFx.Motes(sparkleHost.transform, new Color(1f, .75f, .95f, .9f), .025f, .45f, 45, ParticleSystemShapeType.Sphere, .06f);
            paddleGuide = AbilityFx.Line("Paddle Star guide", new Color(1f, .9f, .55f, .55f), .025f, 2);
            portalArc = AbilityFx.Line("Portal Jump arc", new Color(.75f, .55f, 1f, .7f), .02f, 16);
            portalRing = AbilityFx.Line("Portal Jump target", new Color(.75f, .55f, 1f, .8f), .03f, 40);
            portalRing.loop = true;
            reticle = AbilityFx.Line("Attack target ring", new Color(1f, .85f, .45f, .55f), .02f, 32);
            reticle.loop = true;
            reticleObject = reticle.gameObject;
            for (int i = 0; i < bolts.Length; i++)
                bolts[i] = StarVisual(null, .07f, Cyan);
            foreach (var go in new[] { heldStar, heldBubble, heldShard, sparkleHost, paddleGuide.gameObject, portalArc.gameObject, portalRing.gameObject, reticleObject })
                Player.Track(go);
            foreach (var b in bolts)
                Player.Track(b);
            HideHeld();
        }

        void HideHeld()
        {
            foreach (var go in new[] { heldStar, heldBubble, heldShard })
                if (go)
                    go.SetActive(false);
            foreach (var b in bolts)
                if (b)
                    b.SetActive(false);
            foreach (var line in new[] { paddleGuide, portalArc, portalRing, reticle })
                if (line)
                    line.enabled = false;
        }

        void DestroyVisuals()
        {
            foreach (var go in new[] { heldStar, heldBubble, heldShard, sparkles ? sparkles.transform.parent.gameObject : null, paddleGuide ? paddleGuide.gameObject : null, portalArc ? portalArc.gameObject : null, portalRing ? portalRing.gameObject : null, reticleObject, tether ? tether.gameObject : null, starTemplate })
                if (go)
                    Object.Destroy(go);
            for (int i = 0; i < bolts.Length; i++)
            {
                if (bolts[i])
                    Object.Destroy(bolts[i]);
                bolts[i] = null;
            }
            ClosePortals();
            heldStar = heldBubble = heldShard = reticleObject = starTemplate = null;
            sparkles = null;
            paddleGuide = portalArc = portalRing = reticle = tether = null;
        }

        /// <summary>Hand-held effects follow the posed hands (called after every body pose, also right before rendering).</summary>
        void OnPosed()
        {
            var body = Body;
            if (!body || !heldStar)
                return;
            bool alive = Health.IsAlive;
            var right = body.Aim(false);
            var left = body.Aim(true);
            Vector3 rightForward = right.rotation * Vector3.forward;
            // The right palm faces the grip's -X, the left palm its +X.
            Vector3 rightPalmFacing = body.RightGrip().rotation * Vector3.left;
            Vector3 leftPalmFacing = body.LeftGrip().rotation * Vector3.right;
            float scale = body.BodyScale;

            heldStar.SetActive(alive && holdingStar);
            if (holdingStar)
                heldStar.transform.SetPositionAndRotation(right.position + rightPalmFacing * .05f * scale + rightForward * .03f, right.rotation);

            heldBubble.SetActive(alive && holdingBubble);
            if (holdingBubble)
            {
                // The bubble inflates while held and wobbles.
                float grow = Mathf.Clamp01((Time.time - bubbleHoldStart) / .35f);
                float wobble = 1 + .06f * Mathf.Sin(Time.time * 17);
                float size = Mathf.Lerp(.05f, .24f, grow) * scale;
                heldBubble.transform.position = right.position + rightPalmFacing * (size * .5f + .02f) + rightForward * .04f;
                heldBubble.transform.localScale = new Vector3(size * wobble, size / wobble, size);
            }

            bool showShard = alive && shard.HasValue && !Player.HandStowed(true);
            heldShard.SetActive(showShard);
            if (showShard)
            {
                float size = (holdingShard ? .13f : .09f) * scale * (1 + .08f * Mathf.Sin(Time.time * 5));
                heldShard.transform.position = left.position + leftPalmFacing * (.09f * scale) + Vector3.up * .03f * Mathf.Sin(Time.time * 2.5f);
                heldShard.transform.localScale = Vector3.one * size;
            }

            if (sparkles)
            {
                sparkles.transform.parent.position = right.position;
                var emission = sparkles.emission;
                emission.enabled = alive && PassiveReady;
            }

            // Paddle aim: a guide from the star to where the hand points.
            bool guide = aimingPaddle && star;
            paddleGuide.enabled = guide;
            if (guide)
            {
                Vector3 target = PaddleTarget();
                Vector3 from = star.transform.position;
                paddleGuide.SetPosition(0, from);
                paddleGuide.SetPosition(1, new Vector3(target.x, from.y, target.z));
            }

            // Portal aim: an arc from the left hand to the landing spot.
            portalArc.enabled = portalRing.enabled = aimingPortal && alive;
            if (aimingPortal && alive)
            {
                bool valid = PortalTarget(out var target);
                var color = valid ? new Color(.75f, .55f, 1f, .8f) : new Color(1f, .3f, .3f, .6f);
                portalArc.sharedMaterial = portalRing.sharedMaterial = AbilityFx.Glass(color, true);
                Vector3 start = left.position;
                Vector3 control = (start + target) * .5f + Vector3.up * (1 + Geo.FlatDistance(start, target) * .25f);
                for (int i = 0; i < portalArc.positionCount; i++)
                {
                    float t = i / (portalArc.positionCount - 1f);
                    portalArc.SetPosition(i, Vector3.Lerp(Vector3.Lerp(start, control, t), Vector3.Lerp(control, target, t), t));
                }
                AbilityFx.SetCircle(portalRing, target, .55f);
            }

            // Wheeee bolts circle at chest height.
            Vector3 centre = Player.head.transform.position - Vector3.up * .45f * scale;
            orbitAngle += 220 * Time.deltaTime;
            for (int i = 0; i < bolts.Length; i++)
            {
                bool show = alive && i >= 3 - boltsLeft;
                bolts[i].SetActive(show);
                if (show)
                {
                    float a = (orbitAngle + i * 120) * Mathf.Deg2Rad;
                    bolts[i].transform.position = centre + new Vector3(Mathf.Cos(a), .12f * Mathf.Sin(a * 2), Mathf.Sin(a)) * .62f * scale;
                }
            }

            if (portalActive && tether)
            {
                Vector3 a = portalFrom + Vector3.up * 1.05f, b = Player.Feet + Vector3.up * 1.05f;
                for (int i = 0; i < tether.positionCount; i++)
                {
                    float t = i / (tether.positionCount - 1f);
                    tether.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * .8f + Random.insideUnitSphere * .03f);
                }
            }
        }

        /// <summary>Gold ring under the enemy a basic attack would hit, so pointing feels precise.</summary>
        void UpdateReticle()
        {
            if (!reticle)
                return;
            if (Time.frameCount % 3 == 0)
                softTarget = Health.IsAlive && Player.CanAct ? AttackTarget(Player.AttackOrigin, Player.AttackDirection) : null;
            bool show = softTarget && softTarget.IsAlive;
            reticle.enabled = show;
            if (show)
                AbilityFx.SetCircle(reticle, softTarget.transform.position, softTarget.GetComponent<RiftStructure>() ? 2.2f : .6f);
        }
    }
}
