using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LeagueVR.Match;
namespace LeagueVR.Champions
{
    [RequireComponent(typeof(GwenAbilities))]
    public class ChampionAbilities : MonoBehaviour, IDamageGuard
    {
        public GwenAbilities Player { get; private set; }
        public ChampionDefinition Definition { get; private set; }
        public bool IsActive => Definition && Definition.id != ChampionId.Gwen;
        public float AP => economy ? economy.AbilityPower : 0;
        public float AD => Player.tuning.attackDamage;
        public float BonusResistance => 0;
        public int Stacks { get; private set; }
        public int RecastStage { get; private set; }
        public bool Transcendent => Time.time < ultimateUntil;
        public bool Blocking => Definition && Definition.id == ChampionId.Pantheon && Time.time < guardUntil;
        public bool Camouflaged => Definition && Definition.id == ChampionId.Akshan && Time.time < stealthUntil && !Around(Player.Feet, 4).Any(t => t.countsAsChampion) && !(RiftMatch.Instance?.structures.Any(t => t.health.team != Player.Health.team && t.health.IsAlive && GwenAbilities.FlatDistance(t.transform.position, Player.Feet) < t.range) ?? false);
        public bool Busy => Time.time < busyUntil;
        public string StateText => Definition == null ? "" : Definition.id switch { ChampionId.Zoe => shard >= 0 ? "W: SHARD READY" : star ? "Q: REDIRECT" : "W: FIND A SHARD", ChampionId.Aatrox => Transcendent ? "WORLD ENDER" : RecastStage > 0 ? "Q: SLASH " + (RecastStage + 1) : "DARKIN BLADE", ChampionId.Akshan => Camouflaged ? "CAMOUFLAGED" : "DIRTY FIGHTING", ChampionId.Brand => "BLAZE: 3 HITS TO DETONATE", ChampionId.Pantheon => Blocking ? "SHIELDING" : $"MORTAL WILL {Stacks}/5", ChampionId.Yunara => Transcendent ? "TRANSCENDENT" : $"SPIRIT {Stacks}/8", _ => "" };
        RiftEconomy economy;
        readonly float[] ready = new float[4];
        float attackAt, busyUntil, qWindow, passiveAt, ultimateUntil, guardUntil, stealthUntil;
        ChampionProjectile star;
        int shard = -1, deaths;
        Vector3 portalFrom;
        bool portalActive;
        Material material;
        readonly Dictionary<Combatant, (int count, float until)> dirty = new();
        readonly Dictionary<Combatant, List<Combatant>> scoundrels = new();
        readonly List<GameObject> owned = new();

        void Awake()
        {
            Player = GetComponent<GwenAbilities>();
            economy = GetComponent<RiftEconomy>();
        }

        void OnEnable()
        {
            Combatant.Defeated += Died;
        }

        void OnDisable()
        {
            Combatant.Defeated -= Died;
            ResetState();
        }

        public void SetChampion(ChampionDefinition d)
        {
            ResetState();
            Definition = d;
            if (material)
                Destroy(material);
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", d.color);
        }

        public void ResetState()
        {
            foreach (var debuff in FindObjectsByType<ChampionDebuff>(FindObjectsSortMode.None))
                if (debuff.owner == this)
                    Destroy(debuff);
            StopAllCoroutines();
            if (portalActive && Player)
                MoveFeet(portalFrom);
            portalActive = false;
            foreach (var go in owned)
                if (go)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            owned.Clear();
            star = null;
            Stacks = RecastStage = 0;
            shard = -1;
            attackAt = busyUntil = qWindow = passiveAt = ultimateUntil = guardUntil = stealthUntil = 0;
            Array.Clear(ready, 0, 4);
            dirty.Clear();
            scoundrels.Clear();
            if (Player && Player.Health)
                Player.Health.ClearCrowdControl();
        }

        public void Advance(float seconds, bool ultimate = false)
        {
            if (ultimate)
                ready[3] = Mathf.Max(Time.time, ready[3] - seconds);
            else
                for (int i = 0; i < 3; i++)
                    ready[i] = Mathf.Max(Time.time, ready[i] - seconds);
        }

        public void ResetAttack() => attackAt = 0;

        public float Cooldown(int slot) => Mathf.Max(0, ready[slot] - Time.time);

        public bool Blocks(DamageHit hit)
        {
            if (!IsActive || !Blocking || hit.source && hit.source.GetComponent<RiftStructure>())
                return false;
            Vector3 front = Player.DesktopMode ? Player.head.transform.forward : Player.leftHand.forward;
            return Vector3.Dot((hit.source ? hit.source.AimPosition : hit.origin) - Player.head.transform.position, front) > .01f;
        }

        void Update()
        {
            if (!IsActive)
                return;
            owned.RemoveAll(go => !go);
            if (!Player.Health.IsAlive)
            {
                if (portalActive)
                {
                    MoveFeet(portalFrom);
                    portalActive = false;
                }
                guardUntil = stealthUntil = ultimateUntil = 0;
                return;
            }
            if (RecastStage > 0 && Time.time > qWindow)
                RecastStage = 0;
            if (Definition.id == ChampionId.Zoe && shard < 0)
            {
                foreach (var s in FindObjectsByType<SpellShard>(FindObjectsSortMode.None))
                    if (s.owner == this && Vector3.Distance(s.transform.position, Player.leftHand.position) < .3f)
                    {
                        shard = s.kind;
                        Destroy(s.gameObject);
                        Player.Emit("Shard", Player.leftHand.position, Vector3.up);
                        break;
                    }
            }
        }

        public bool Enemy(Combatant t) => t && t != Player.Health && t.team != Player.Health.team && t.IsTargetable;

        public List<Combatant> Around(Vector3 p, float radius, bool structures = false)
        {
            return Physics.OverlapSphere(p, radius, Player.combatMask, QueryTriggerInteraction.Collide).Select(c => c.GetComponentInParent<Combatant>()).Where(t => Enemy(t) && (structures || !t.GetComponent<RiftStructure>())).Distinct().ToList();
        }

        public bool IsOwnCollider(Collider collider) => collider && (collider.transform.IsChildOf(Player.origin.transform) || (economy && economy.GetComponent<RiftItemRack>() is RiftItemRack rack && rack.OwnsCollider(collider)));

        public Combatant Aim(float range, Vector3 position, Vector3 direction, bool structures = false)
        {
            foreach (var h in Physics.SphereCastAll(position, .35f, direction, range, Player.worldMask | Player.combatMask, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (IsOwnCollider(h.collider))
                    continue;
                var t = h.collider.GetComponentInParent<Combatant>();
                if (Enemy(t))
                {
                    if (structures || !t.GetComponent<RiftStructure>())
                        return t;
                }
                else if (!t && (Player.worldMask.value & (1 << h.collider.gameObject.layer)) != 0)
                    return null;
            }
            return null;
        }

        public float Hit(Combatant target, float amount, DamageKind kind, string ability, bool basic = false)
        {
            if (!Enemy(target))
                return 0;
            float damage = basic && economy ? economy.AttackDamage(amount, target) : amount;
            float dealt = target.TakeDamage(new DamageHit(Player.Health, Player.Health.AimPosition, damage, kind) { ability = ability, isBasicAttack = basic, isCritical = basic && economy && economy.LastAttackCritical });
            if (dealt > 0)
            {
                if (basic)
                    economy?.OnAttack(target, dealt);
                if (Definition.id == ChampionId.Aatrox && target.countsAsChampion)
                    Player.Health.Heal(dealt * (Transcendent ? .28f : .16f));
                Spark(target.AimPosition, .08f);
            }
            return dealt;
        }

        bool CanAct() => IsActive && !RiftUI.BlocksCombat && Player.Health.IsAlive && !Player.Health.Stunned;

        bool Use(int slot, bool recast = false)
        {
            if (!CanAct() || (!recast && (Busy || Cooldown(slot) > 0)))
                return false;
            int rank = Definition.Rank(slot, economy ? economy.Level : 1);
            if (!recast && Definition.UsesMana && economy && !economy.TrySpendMana(Definition.spells[slot].Cost(rank)))
                return false;
            if (!recast)
                ready[slot] = Time.time + Definition.spells[slot].Cooldown(rank) * 100 / (100 + (economy ? economy.AbilityHaste : 0));
            stealthUntil = 0;
            Player.Emit("QWER"[slot].ToString(), slot == 1 || slot == 3 ? LeftOrigin : Player.AttackOrigin, slot == 1 || slot == 3 ? LeftDirection : Player.AttackDirection);
            return true;
        }
        Vector3 LeftOrigin => Player.DesktopMode ? Player.AttackOrigin : Player.leftHand.position;
        Vector3 LeftDirection => Player.DesktopMode ? Player.AttackDirection : Player.leftHand.forward;

        public bool BasicAttack()
        {
            if (!CanAct() || Busy || Time.time < attackAt)
                return false;
            attackAt = Time.time + Player.tuning.attackInterval * Player.Health.AttackIntervalMultiplier;
            stealthUntil = 0;
            Player.Emit("Attack1", Player.AttackOrigin, Player.AttackDirection);
            var target = Aim(Definition.attackReach + (economy?.Effects?.BonusAttackRange ?? 0), Player.AttackOrigin, Player.AttackDirection, true);
            if (!target)
                return true;
            if (Definition.id == ChampionId.Aatrox || Definition.id == ChampionId.Pantheon)
            {
                float dealt = Hit(target, AD * (Transcendent && Definition.id == ChampionId.Aatrox ? 1.2f : 1), DamageKind.Physical, "Attack1", true);
                if (dealt > 0 && Definition.id == ChampionId.Aatrox && Time.time >= passiveAt && !target.GetComponent<RiftStructure>())
                {
                    float p = Hit(target, target.maxHealth * .06f, DamageKind.Magic, "Passive");
                    Player.Health.Heal(p);
                    passiveAt = Time.time + 15;
                }
                if (dealt > 0 && Definition.id == ChampionId.Pantheon)
                    Stacks = Mathf.Min(5, Stacks + 1);
                Beam(Player.AttackOrigin, target.AimPosition, .06f);
                return true;
            }
            var missile = Projectile(Player.AttackOrigin, Player.AttackDirection, AD, DamageKind.Physical, "Attack1", 22, Definition.attackReach, false);
            missile.basic = true;
            missile.homing = target;
            missile.hit = (t, d) => BasicHit(t, d);
            return true;
        }

        void BasicHit(Combatant t, float dealt)
        {
            switch (Definition.id)
            {
                case ChampionId.Zoe:
                    if (Time.time < passiveAt)
                    {
                        Hit(t, 20 + AP * .2f, DamageKind.Magic, "Passive");
                        passiveAt = 0;
                    }
                    break;
                case ChampionId.Akshan:
                    Dirty(t);
                    StartCoroutine(DoubleShot(t));
                    break;
                case ChampionId.Yunara:
                    Stacks = Mathf.Min(8, Stacks + 1);
                    if (economy && economy.LastAttackCritical)
                        Hit(t, AD * .3f + AP * .1f, DamageKind.Magic, "Passive");
                    if (Time.time < guardUntil || Transcendent)
                    {
                        foreach (var near in Around(t.AimPosition, 1.8f))
                            if (near != t)
                                Hit(near, AD * .25f, DamageKind.Magic, "Q");
                    }
                    break;
            }
        }

        IEnumerator DoubleShot(Combatant t)
        {
            var p = Player.Feet;
            yield return new WaitForSeconds(.13f);
            if (!Player.Health.IsAlive || !Enemy(t))
                yield break;
            if (GwenAbilities.FlatDistance(p, Player.Feet) > .12f)
            {
                Player.Health.ApplySpeed(.15f, 1);
                yield break;
            }
            if (GwenAbilities.FlatDistance(t.AimPosition, Player.AttackOrigin) > Definition.attackReach)
                yield break;
            Hit(t, AD * (t.countsAsChampion ? .5f : 1), DamageKind.Physical, "Attack1", true);
            Dirty(t);
            Beam(Player.AttackOrigin, t.AimPosition, .035f);
        }

        void Dirty(Combatant t)
        {
            if (t.GetComponent<RiftStructure>())
                return;
            var entry = dirty.TryGetValue(t, out var e) && e.until > Time.time ? e : (0, 0f);
            int n = entry.Item1 + 1;
            dirty[t] = (n, Time.time + 5);
            if (n >= 3)
            {
                dirty[t] = (0, 0);
                Hit(t, 20 + AP * .6f, DamageKind.Magic, "Passive");
                if (t.countsAsChampion)
                    Player.Health.AddRefreshableShield("Dirty Fighting", 60, 60, 2);
            }
        }

        public bool CastQ()
        {
            if (Definition.id == ChampionId.Zoe && star && Time.time < qWindow)
            {
                if (!Use(0, true))
                    return false;
                star.Redirect(Player.AttackDirection);
                star = null;
                passiveAt = Time.time + 5;
                return true;
            }
            if (Definition.id == ChampionId.Aatrox && RecastStage > 0 && Time.time < qWindow)
            {
                if (!Use(0, true))
                    return false;
                StartCoroutine(Darkin(RecastStage + 1));
                return true;
            }
            if (Definition.id == ChampionId.Yunara && Stacks < 8 && !Transcendent)
            {
                RiftMatch.Instance.Notify("Build 8 Spirit stacks with basic attacks.");
                return false;
            }
            if (!Use(0))
                return false;
            switch (Definition.id)
            {
                case ChampionId.Zoe:
                    passiveAt = Time.time + 5;
                    star = Projectile(Player.AttackOrigin, Player.AttackDirection, 60 + AP * .6f, DamageKind.Magic, "Q", 9, 7, false);
                    star.damageAtDistance = d => (60 + AP * .6f) * (1 + Mathf.Min(1.5f, d * .08f));
                    qWindow = Time.time + 1.25f;
                    star.hit = (t, d) =>
                    {
                        foreach (var n in Around(t.AimPosition, 1.2f))
                            if (n != t)
                                Hit(n, 40 + AP * .3f, DamageKind.Magic, "Q");
                    };
                    break;
                case ChampionId.Aatrox:
                    StartCoroutine(Darkin(1));
                    break;
                case ChampionId.Akshan:
                    var b = Projectile(Player.AttackOrigin, Player.AttackDirection, 45 + AD * .8f, DamageKind.Physical, "Q", 14, 11, true);
                    b.returning = true;
                    b.hit = (t, d) =>
                    {
                        Dirty(t);
                        b.range = Mathf.Max(b.range, b.travelled + 4);
                    };
                    break;
                case ChampionId.Brand:
                    var fire = Projectile(Player.AttackOrigin, Player.AttackDirection, 80 + AP * .6f, DamageKind.Magic, "Q", 16, 12, false);
                    fire.hit = (t, d) =>
                    {
                        bool burning = t.GetComponent<ChampionDebuff>()?.blazeUntil > Time.time;
                        if (burning)
                            t.ApplyStun(1.5f);
                        Blaze(t);
                    };
                    break;
                case ChampionId.Pantheon:
                    bool empowered = Will();
                    var spear = Projectile(Player.AttackOrigin, Player.AttackDirection, 70 + AD * .8f + (empowered ? 80 : 0), DamageKind.Physical, "Q", 22, 12, true);
                    spear.hit = (t, d) =>
                    {
                        if (t.Health / t.maxHealth < .2f)
                            Hit(t, 70, DamageKind.Physical, "Q");
                    };
                    break;
                case ChampionId.Yunara:
                    Stacks = 0;
                    guardUntil = Time.time + 5;
                    Player.Health.ApplyAttackSpeed(.4f, 5);
                    Spark(Player.rightHand.position, .25f);
                    break;
            }
            return true;
        }

        IEnumerator Darkin(int stage)
        {
            busyUntil = Time.time + .3f;
            RecastStage = stage >= 3 ? 0 : stage;
            qWindow = Time.time + 4;
            var aim = Vector3.ProjectOnPlane(Player.AttackDirection, Vector3.up).normalized;
            if (aim.sqrMagnitude < .1f)
                aim = Player.head.transform.forward;
            var from = Player.Feet;
            Ring(from + aim * (stage == 3 ? 2 : 2.8f), stage == 3 ? 1.6f : .9f, .35f);
            yield return new WaitForSeconds(.25f);
            foreach (var t in Around(from + aim * 2.4f, 3.4f))
            {
                var delta = Vector3.ProjectOnPlane(t.transform.position - from, Vector3.up);
                float forward = Vector3.Dot(delta, aim), side = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, aim)));
                bool inside = stage == 3 ? Vector3.Distance(t.transform.position, from + aim * 2) < 1.8f : forward > .3f && forward < 4.3f && side < (stage == 2 ? 2 : .85f);
                if (!inside)
                    continue;
                bool sweet = stage == 3 ? Vector3.Distance(t.transform.position, from + aim * 2) < .9f : stage == 2 ? side > 1.0f : forward > 3;
                Hit(t, (35 + AD * .65f) * stage * (sweet ? 1.6f : 1), DamageKind.Physical, "Q");
                if (sweet)
                    t.ApplyStun(.5f);
            }
        }

        public bool CastW()
        {
            Combatant aimed = null;
            Vector3 ground = default;
            if (Definition.id == ChampionId.Zoe && shard < 0)
            {
                var near = FindObjectsByType<SpellShard>(FindObjectsSortMode.None).FirstOrDefault(s => s.owner == this && GwenAbilities.FlatDistance(s.transform.position, Player.Feet) < 1.7f);
                if (near)
                {
                    shard = near.kind;
                    Destroy(near.gameObject);
                }
                else
                {
                    RiftMatch.Instance.Notify("Collect a glowing spell shard from fallen minions.");
                    return false;
                }
            }
            if (Definition.id == ChampionId.Pantheon)
            {
                aimed = Aim(7, LeftOrigin, LeftDirection);
                if (!aimed || !StepPoint(aimed.transform.position - (aimed.transform.position - Player.Feet).normalized * .9f, out ground))
                    return false;
            }
            if (Definition.id == ChampionId.Brand && !GroundAim(LeftOrigin, LeftDirection, 12, out ground))
                return false;
            if (!Use(1))
                return false;
            switch (Definition.id)
            {
                case ChampionId.Zoe:
                    int stolen = shard;
                    shard = -1;
                    passiveAt = Time.time + 5;
                    if (stolen == 0)
                        Player.Health.Heal(100);
                    else if (stolen == 1)
                    {
                        if (StepPoint(Player.Feet + Flat(LeftDirection) * 2.5f, out var at))
                            MoveFeet(at);
                    }
                    else
                    {
                        var t = Aim(10, LeftOrigin, LeftDirection);
                        if (t)
                            Hit(t, 90, DamageKind.True, "W");
                    }
                    StartCoroutine(ShardMissiles());
                    Player.Health.ApplySpeed(.25f, 2);
                    break;
                case ChampionId.Aatrox:
                    var chains = Projectile(LeftOrigin, LeftDirection, 40 + AD * .4f, DamageKind.Physical, "W", 14, 10, false);
                    chains.hit = (t, d) =>
                    {
                        t.ApplySlow(.75f, 1.5f);
                        if (t.countsAsChampion || t.GetComponent<RiftObjective>())
                            StartCoroutine(PullChain(t));
                    };
                    break;
                case ChampionId.Akshan:
                    stealthUntil = Time.time + 6;
                    Spark(LeftOrigin, .25f);
                    break;
                case ChampionId.Brand:
                    Ring(ground, 2.3f, .65f);
                    StartCoroutine(Pillar(ground));
                    break;
                case ChampionId.Pantheon:
                    bool empowered = Will();
                    MoveFeet(ground);
                    Hit(aimed, 60 + AP * .6f, DamageKind.Physical, "W");
                    aimed.ApplyStun(1);
                    if (empowered)
                        for (int i = 0; i < 3; i++)
                            Hit(aimed, AD * .4f, DamageKind.Physical, "W", true);
                    break;
                case ChampionId.Yunara:
                    var bead = Projectile(LeftOrigin, LeftDirection, 90 + AP * .5f + AD * .4f, DamageKind.Magic, "W", Transcendent ? 30 : 13, 16, Transcendent);
                    bead.radius = Transcendent ? .35f : .2f;
                    bead.hit = (t, d) => t.ApplySlow(.55f, 1.3f);
                    if (Transcendent)
                        Beam(LeftOrigin, LeftOrigin + LeftDirection * 16, .14f);
                    break;
            }
            return true;
        }

        IEnumerator ShardMissiles()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(.18f);
                var t = Around(Player.Feet, 10).OrderBy(t => Vector3.Distance(t.AimPosition, Player.Feet)).FirstOrDefault();
                if (!t)
                    yield break;
                var p = Projectile(LeftOrigin, (t.AimPosition - LeftOrigin).normalized, 20 + AP * .13f, DamageKind.Magic, "W", 16, 15, false);
                p.homing = t;
            }
        }

        IEnumerator PullChain(Combatant target)
        {
            var centre = target.transform.position;
            Ring(centre, 2.5f, 1.5f);
            yield return new WaitForSeconds(1.5f);
            if (Enemy(target) && GwenAbilities.FlatDistance(target.transform.position, centre) < 2.5f)
            {
                if (RiftMatch.Instance.Ground(centre, out var p))
                    target.transform.position = p;
                Hit(target, 40 + AD * .4f, DamageKind.Physical, "W");
                target.ApplyRoot(.25f);
            }
        }

        IEnumerator Pillar(Vector3 centre)
        {
            yield return new WaitForSeconds(.6f);
            foreach (var t in Around(centre + Vector3.up, 2.3f))
            {
                bool burning = t.GetComponent<ChampionDebuff>()?.blazeUntil > Time.time;
                Hit(t, (75 + AP * .6f) * (burning ? 1.25f : 1), DamageKind.Magic, "W");
                Blaze(t);
            }
            Spark(centre + Vector3.up, .5f);
        }

        public bool CastE()
        {
            Combatant aimed = null;
            Vector3 point = default, hook = default;
            var aim = Flat(Player.AttackDirection);
            if (Definition.id == ChampionId.Aatrox || Definition.id == ChampionId.Yunara && Transcendent)
            {
                if (Player.Health.Rooted || !StepPoint(Player.Feet + aim * 2.4f, out point))
                    return false;
            }
            if (Definition.id == ChampionId.Akshan)
            {
                if (Player.Health.Rooted || !TerrainRay(Player.AttackOrigin, Player.AttackDirection, 12, out var anchor))
                    return false;
                hook = anchor.point;
                var toward = Flat(hook - Player.Feet);
                if (!StepPoint(Player.Feet + toward * 2.5f + Vector3.Cross(Vector3.up, toward) * .6f, out point))
                    return false;
            }
            if (Definition.id == ChampionId.Brand)
            {
                aimed = Aim(10, Player.AttackOrigin, Player.AttackDirection);
                if (!aimed)
                    return false;
            }
            if (!Use(2))
                return false;
            switch (Definition.id)
            {
                case ChampionId.Zoe:
                    passiveAt = Time.time + 5;
                    var bubble = Projectile(Player.AttackOrigin, Player.AttackDirection, 65 + AP * .4f, DamageKind.Magic, "E", 12, 12, false);
                    bubble.radius = .25f;
                    bubble.hit = (t, d) => StartCoroutine(Sleep(t));
                    break;
                case ChampionId.Aatrox:
                    MoveFeet(point);
                    attackAt = 0;
                    break;
                case ChampionId.Akshan:
                    Beam(Player.AttackOrigin, hook, .025f);
                    MoveFeet(point);
                    var t = Aim(Definition.attackReach, Player.AttackOrigin, Player.AttackDirection);
                    if (t)
                    {
                        Hit(t, AD * .5f, DamageKind.Physical, "E", true);
                        Dirty(t);
                    }
                    break;
                case ChampionId.Brand:
                    bool burning = aimed.GetComponent<ChampionDebuff>()?.blazeUntil > Time.time;
                    foreach (var victim in Around(aimed.AimPosition, burning ? 3.5f : 1.8f))
                    {
                        Hit(victim, 60 + AP * .5f, DamageKind.Magic, "E");
                        Blaze(victim);
                    }
                    break;
                case ChampionId.Pantheon:
                    bool empowered = Will();
                    guardUntil = Time.time + (empowered ? 2.5f : 1.5f);
                    StartCoroutine(Aegis());
                    break;
                case ChampionId.Yunara:
                    if (Transcendent)
                        MoveFeet(point);
                    else
                        Player.Health.ApplySpeed(.3f, 2);
                    break;
            }
            return true;
        }

        IEnumerator Sleep(Combatant target)
        {
            target.ApplySlow(.5f, 1.2f);
            Ring(target.transform.position, .65f, 1.2f);
            yield return new WaitForSeconds(1.2f);
            if (Enemy(target))
                target.ApplySleep(2, 65 + AP * .4f, Player.Health);
        }

        IEnumerator Aegis()
        {
            float until = guardUntil;
            while (Time.time < until && Player.Health.IsAlive)
            {
                foreach (var t in Around(Player.Feet, 3.5f))
                {
                    var d = t.AimPosition - LeftOrigin;
                    if (Vector3.Dot(d.normalized, LeftDirection) > .65f)
                        Hit(t, AD * .15f, DamageKind.Physical, "E");
                }
                yield return new WaitForSeconds(.25f);
            }
        }

        public bool CastR()
        {
            Vector3 ground = default;
            Combatant target = null;
            if (Definition.id == ChampionId.Zoe || Definition.id == ChampionId.Pantheon)
            {
                if (Player.Health.Rooted || !GroundAim(LeftOrigin, LeftDirection, Definition.id == ChampionId.Zoe ? 6 : 24, out ground) || !ClearDestination(ground))
                    return false;
            }
            if (Definition.id == ChampionId.Akshan || Definition.id == ChampionId.Brand)
            {
                target = Aim(Definition.id == ChampionId.Akshan ? 25 : 12, LeftOrigin, LeftDirection);
                if (!target)
                    return false;
            }
            if (!Use(3))
                return false;
            switch (Definition.id)
            {
                case ChampionId.Zoe:
                    passiveAt = Time.time + 5;
                    StartCoroutine(Portal(ground));
                    break;
                case ChampionId.Aatrox:
                    ultimateUntil = Time.time + 10;
                    Player.Health.ApplySpeed(.3f, 10);
                    Ring(Player.Feet, 1.3f, .7f);
                    break;
                case ChampionId.Akshan:
                    busyUntil = Time.time + .6f;
                    StartCoroutine(Comeuppance(target));
                    break;
                case ChampionId.Brand:
                    StartCoroutine(Pyroclasm(target));
                    break;
                case ChampionId.Pantheon:
                    StartCoroutine(Starfall(ground));
                    break;
                case ChampionId.Yunara:
                    ultimateUntil = Time.time + 12;
                    guardUntil = ultimateUntil;
                    Stacks = 8;
                    Player.Health.ApplyAttackSpeed(.4f, 12);
                    ready[1] = ready[2] = 0;
                    Ring(Player.Feet, 1, .5f);
                    break;
            }
            return true;
        }

        IEnumerator Portal(Vector3 at)
        {
            portalFrom = Player.Feet;
            portalActive = true;
            Ring(portalFrom, .65f, 1);
            MoveFeet(at);
            Ring(at, .65f, 1);
            yield return new WaitForSeconds(1);
            if (portalActive)
            {
                MoveFeet(portalFrom);
                portalActive = false;
            }
        }

        IEnumerator Comeuppance(Combatant target)
        {
            Ring(target.transform.position, .8f, .6f);
            yield return new WaitForSeconds(.6f);
            for (int i = 0; i < 5 && Enemy(target) && Player.Health.IsAlive; i++)
            {
                var p = Projectile(LeftOrigin, (target.AimPosition - LeftOrigin).normalized, (30 + AD * .25f) * (1 + 2 * (1 - target.Health / target.maxHealth)), DamageKind.Physical, "R", 25, 35, false);
                p.homing = target;
                yield return new WaitForSeconds(.12f);
            }
        }

        IEnumerator Pyroclasm(Combatant first)
        {
            Combatant current = first, previous = null;
            for (int i = 0; i < 5 && Enemy(current) && Player.Health.IsAlive; i++)
            {
                var start = previous ? previous.AimPosition : LeftOrigin;
                Beam(start, current.AimPosition, .15f);
                Hit(current, 100 + AP * .25f, DamageKind.Magic, "R");
                current.ApplySlow(.6f, .5f);
                Blaze(current);
                previous = current;
                yield return new WaitForSeconds(.25f);
                var others = Around(previous.AimPosition, 5).Where(t => t != previous).OrderBy(t => t.countsAsChampion ? 0 : 1).ThenBy(t => Vector3.Distance(t.AimPosition, previous.AimPosition));
                current = others.FirstOrDefault();
                if (!current && GwenAbilities.FlatDistance(previous.transform.position, Player.Feet) < 5)
                {
                    Beam(previous.AimPosition, Player.Health.AimPosition, .1f);
                    yield return new WaitForSeconds(.12f);
                    current = previous;
                }
            }
        }

        IEnumerator Starfall(Vector3 at)
        {
            busyUntil = Time.time + 1;
            Ring(at, 3.4f, 1);
            yield return new WaitForSeconds(1);
            if (!Player.Health.IsAlive || !ClearDestination(at))
                yield break;
            MoveFeet(at);
            Stacks = 5;
            foreach (var t in Around(at + Vector3.up, 3.4f))
            {
                Hit(t, 300 + AP * .8f, DamageKind.Magic, "R");
                t.ApplySlow(.5f, 2);
            }
            Ring(at, 3.4f, .5f);
        }

        bool Will()
        {
            bool empowered = Stacks >= 5;
            Stacks = empowered ? 0 : Mathf.Min(5, Stacks + 1);
            return empowered;
        }

        void Blaze(Combatant target)
        {
            if (target.GetComponent<RiftStructure>())
                return;
            var b = target.GetComponent<ChampionDebuff>();
            if (!b)
                b = target.gameObject.AddComponent<ChampionDebuff>();
            b.Blaze(this);
        }

        static Vector3 Flat(Vector3 d)
        {
            d.y = 0;
            return d.sqrMagnitude > .001f ? d.normalized : Vector3.forward;
        }

        public bool TerrainRay(Vector3 from, Vector3 direction, float range, out RaycastHit hit)
        {
            foreach (var candidate in Physics.RaycastAll(from, direction, range, Player.worldMask, QueryTriggerInteraction.Ignore).Where(h => !IsOwnCollider(h.collider)).OrderBy(h => h.distance))
            {
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }

        public bool GroundAim(Vector3 from, Vector3 direction, float range, out Vector3 result)
        {
            var hits = Physics.RaycastAll(from, direction, range, Player.worldMask, QueryTriggerInteraction.Ignore).Where(h => !IsOwnCollider(h.collider)).OrderBy(h => h.distance);
            foreach (var hit in hits)
            {
                if (hit.normal.y > .65f)
                {
                    result = hit.point;
                    return true;
                }
                break;
            }
            return RiftMatch.Instance.Ground(Player.Feet + Flat(direction) * range, out result);
        }

        public bool ClearDestination(Vector3 at) => !Physics.OverlapCapsule(at + Vector3.up * .4f, at + Vector3.up * 1.4f, .22f, Player.worldMask, QueryTriggerInteraction.Ignore).Any(c => !IsOwnCollider(c));

        public bool StepPoint(Vector3 desired, out Vector3 point)
        {
            point = default;
            var delta = desired - Player.Feet;
            delta.y = 0;
            float distance = delta.magnitude;
            foreach (var block in Physics.CapsuleCastAll(Player.Feet + Vector3.up * .38f, Player.Feet + Vector3.up * 1.4f, .22f, delta.normalized, distance, Player.worldMask, QueryTriggerInteraction.Ignore).Where(h => !IsOwnCollider(h.collider)).OrderBy(h => h.distance))
            {
                desired = Player.Feet + delta.normalized * Mathf.Max(0, block.distance - .15f);
                break;
            }
            return GwenAbilities.FlatDistance(Player.Feet, desired) > .15f && RiftMatch.Instance.Ground(desired + Vector3.up * .3f, out point) && Mathf.Abs(point.y - Player.Feet.y) < .65f && ClearDestination(point);
        }

        public void MoveFeet(Vector3 point)
        {
            var cc = Player.origin.GetComponent<CharacterController>();
            bool enabled = cc && cc.enabled;
            if (cc)
                cc.enabled = false;
            Player.origin.transform.position += point - Player.Feet + Vector3.up * .05f;
            if (cc)
                cc.enabled = enabled;
        }

        public ChampionProjectile Projectile(Vector3 start, Vector3 dir, float damage, DamageKind kind, string spell, float speed, float range, bool piercing)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = Definition.name + " " + spell;
            go.transform.position = start;
            go.transform.localScale = Vector3.one * .14f;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var p = go.AddComponent<ChampionProjectile>();
            p.source = this;
            p.direction = dir.normalized;
            p.damage = damage;
            p.kind = kind;
            p.ability = spell;
            p.speed = speed;
            p.range = range;
            p.piercing = piercing;
            owned.Add(go);
            return p;
        }

        public void Ring(Vector3 position, float radius, float seconds)
        {
            var go = new GameObject("Ability ground telegraph");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.positionCount = 49;
            lr.startWidth = lr.endWidth = .03f;
            for (int i = 0; i < 49; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                lr.SetPosition(i, position + new Vector3(Mathf.Cos(a) * radius, .06f, Mathf.Sin(a) * radius));
            }
            go.AddComponent<ChampionVFX>().duration = seconds;
            owned.Add(go);
        }

        public void Beam(Vector3 from, Vector3 to, float width)
        {
            var go = new GameObject("Champion ability trail");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.startWidth = width;
            lr.endWidth = width * .3f;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            go.AddComponent<ChampionVFX>().duration = .15f;
            owned.Add(go);
        }

        public void Spark(Vector3 position, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Champion hit spark";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * radius;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var fx = go.AddComponent<ChampionVFX>();
            fx.duration = .15f;
            fx.expand = true;
            owned.Add(go);
        }

        void Died(Combatant victim, DamageHit hit)
        {
            if (!IsActive)
                return;
            if (Definition.id == ChampionId.Zoe && victim.GetComponent<RiftMinion>() && victim.team != Player.Health.team && ++deaths % 4 == 0)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = "Spell Thief shard";
                go.transform.position = victim.transform.position + Vector3.up * .65f;
                go.transform.localScale = Vector3.one * .22f;
                go.GetComponent<Renderer>().sharedMaterial = material;
                var s = go.AddComponent<SpellShard>();
                s.owner = this;
                s.kind = deaths / 4 % 3;
                s.expires = Time.time + 30;
                owned.Add(go);
            }
            if (Definition.id == ChampionId.Brand && hit.source == Player.Health && victim.GetComponent<ChampionDebuff>())
                economy.RestoreMana(20);
            if (Definition.id == ChampionId.Aatrox && hit.source == Player.Health && victim.countsAsChampion && Transcendent)
                ultimateUntil = Mathf.Max(ultimateUntil, Time.time + 5);
            if (Definition.id == ChampionId.Akshan)
            {
                if (victim.countsAsChampion && victim.team == Player.Health.team && hit.source && hit.source.team != victim.team)
                {
                    if (!scoundrels.TryGetValue(hit.source, out var list))
                        scoundrels[hit.source] = list = new List<Combatant>();
                    list.Add(victim);
                }
                if (hit.source == Player.Health && victim.countsAsChampion)
                {
                    ready[2] = 0;
                    if (scoundrels.TryGetValue(victim, out var allies))
                    {
                        foreach (var ally in allies)
                            if (ally)
                                ally.ResetHealth();
                        economy.AddGold(100, true);
                        scoundrels.Clear();
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (material)
                Destroy(material);
        }
    }
}
