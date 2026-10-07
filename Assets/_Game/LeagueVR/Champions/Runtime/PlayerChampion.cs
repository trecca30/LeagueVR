using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Unity.XR.CoreUtils;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    /// <summary>
    /// The champion the player controls. Owns the active <see cref="ChampionKit"/>, cooldowns, mana spending,
    /// the attack timer, death and respawn, and the VR anchors abilities aim from.
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    [DefaultExecutionOrder(-50)]
    public partial class PlayerChampion : MonoBehaviour, IDamageGuard, ITargetFilter
    {
        public XROrigin origin;
        public Camera head;
        public Transform rightHand, leftHand, spawn;
        public LayerMask worldMask, combatMask;
        public UnityEvent<string> onAbility = new();

        /// <summary>Raised for every ability signal: "Attack1", "Hit", "Q".."R", kit-specific names, "Death", "Respawn".</summary>
        public event Action<string, Vector3, Vector3> Cast;
        public event Action<ChampionKit> KitChanged;

        public Combatant Health { get; private set; }
        public RiftEconomy Economy { get; private set; }
        public ChampionStats Stats { get; } = new();
        public ChampionKit Kit { get; private set; }
        public ChampionDefinition Definition => Kit?.Definition;
        public string ChampionName => Definition ? Definition.name : "Champion";
        public int Level => Economy ? Economy.Level : 1;
        public int Defeated { get; set; }
        public bool DesktopMode { get; set; }
        public float RespawnAt { get; private set; }
        public float RespawnRemaining => Health && !Health.IsAlive ? Mathf.Max(0, RespawnAt - Time.time) : 0;

        /// <summary>
        /// Where the right-hand weapon (or the empty palm) sits on the active body, so melee checks and spells start at
        /// the visible blade or hand. Null when no body is active.
        /// </summary>
        public Func<Pose> WeaponPose => Body && Body.isActiveAndEnabled ? Body.WeaponPoseGetter : null;

        /// <summary>The first-person body currently shown (Gwen's own body or the prefab champion body).</summary>
        public ChampionBodyBase Body { get; set; }

        readonly HashSet<object> leftStowed = new(), rightStowed = new();

        /// <summary>
        /// Puts a hand's weapon or shield away while something else uses that hand (menus, held items). Every caller
        /// passes its own key, so overlapping reasons never un-stow each other.
        /// </summary>
        public void Stow(object key, bool left, bool stowed)
        {
            var set = left ? leftStowed : rightStowed;
            if (stowed)
                set.Add(key);
            else
                set.Remove(key);
        }

        public bool HandStowed(bool left) => (left ? leftStowed : rightStowed).Count > 0;

        readonly float[] readyAt = new float[4];
        float attackReadyAt;
        Coroutine respawnRoutine;

        public Vector3 Feet => new(head.transform.position.x, origin.transform.position.y, head.transform.position.z);

        public Vector3 AttackOrigin
        {
            get
            {
                if (DesktopMode)
                    return head.transform.position - head.transform.up * .18f;
                return WeaponPose != null ? WeaponPose().position : XRPoses.Grip(this, false).position;
            }
        }

        public Vector3 AttackDirection
        {
            get
            {
                if (DesktopMode)
                    return head.transform.forward;
                return WeaponPose != null ? WeaponPose().rotation * Vector3.forward : rightHand.forward;
            }
        }

        public Vector3 OffHandOrigin => DesktopMode ? AttackOrigin : leftHand.position;
        public Vector3 OffHandDirection => DesktopMode ? AttackDirection : leftHand.forward;

        public bool CombatLocked => RiftUI.BlocksCombat || (Economy && Economy.Stasis);
        public bool CanAct => Kit != null && Health && Health.IsAlive && !Health.Stunned && !CombatLocked;
        public bool Busy => Kit != null && Kit.Busy;
        public float AttackInterval => Stats.AttackInterval(Kit?.BonusAttackSpeed ?? 0) * Health.AttackIntervalMultiplier;
        public float AttackReach => Stats.AttackReach + (Kit?.BonusAttackRange ?? 0) + (Economy && Economy.Effects ? Economy.Effects.BonusAttackRange : 0);

        void Awake()
        {
            Health = GetComponent<Combatant>();
            Economy = GetComponent<RiftEconomy>();
            Health.RefreshGuards();
        }

        void OnEnable()
        {
            Health.onDeath.AddListener(OnDeath);
            Combatant.Defeated += OnUnitDefeated;
        }

        void OnDisable()
        {
            Health.onDeath.RemoveListener(OnDeath);
            Combatant.Defeated -= OnUnitDefeated;
            StopAllCoroutines();
            respawnRoutine = null;
        }

        void Update()
        {
            SampleHands();
            if (Kit == null || !Health.IsAlive)
            {
                CancelHolds();
                return;
            }
            if (!CanAct)
                CancelHolds();
            Kit.Tick();
            UpdateWeaponSweep();
            // Falling out of the world counts as a death instead of leaving the player stuck under the map.
            if (spawn && origin.transform.position.y < spawn.position.y - 12)
                Health.TakeDamage(new DamageHit(null, Feet, Health.Health + Health.Shield + 1, DamageKind.True));
        }

        // ---------- Hands: velocity history for throws and swings ----------

        readonly HandMotion leftMotion = new(), rightMotion = new();

        void SampleHands()
        {
            var rig = origin.transform;
            leftMotion.Sample(rig.InverseTransformPoint(XRPoses.Grip(this, true).position), Time.time);
            rightMotion.Sample(rig.InverseTransformPoint(XRPoses.Grip(this, false).position), Time.time);
        }

        /// <summary>World-space velocity of a hand caused by the arm alone (locomotion excluded).</summary>
        public Vector3 HandVelocity(bool left, bool peak = false)
        {
            var motion = left ? leftMotion : rightMotion;
            return origin.transform.TransformDirection(peak ? motion.PeakVelocity() : motion.Velocity());
        }

        // ---------- Hold-to-cast ----------

        readonly bool[] holding = new bool[4];

        public bool IsHolding(int slot) => slot >= 0 && slot < 4 && holding[slot];

        /// <summary>Button pressed: starts a hold for kits that aim/charge/throw, otherwise casts immediately.</summary>
        public bool PressSlot(int slot)
        {
            if (Kit == null || !Kit.HoldToCast(slot))
                return CastSlot(slot);
            if (!CanAct || holding[slot])
                return false;
            bool recast = Kit.CanRecast(slot);
            if ((Kit.BlocksCasts && !recast) || (Cooldown(slot) > 0 && !recast))
                return false;
            holding[slot] = Kit.BeginHold(slot);
            return holding[slot];
        }

        /// <summary>Button released: fires a held ability.</summary>
        public void ReleaseSlot(int slot)
        {
            if (slot < 0 || slot >= 4 || !holding[slot])
                return;
            holding[slot] = false;
            if (CanAct)
                Kit.ReleaseHold(slot);
            else
                Kit.CancelHold(slot);
        }

        void CancelHolds()
        {
            for (int i = 0; i < 4; i++)
                if (holding[i])
                {
                    holding[i] = false;
                    Kit?.CancelHold(i);
                }
        }

        // ---------- Physical weapon hits ----------

        const float MinimumSwingSpeed = 2.2f;
        Vector3 lastEdgeFrom, lastEdgeTo;
        bool haveEdge;

        /// <summary>
        /// Sweeps the kit's weapon edge from last frame to this frame. A fast enough swing that passes through an enemy
        /// lands a basic attack on it if the attack timer is ready.
        /// </summary>
        void UpdateWeaponSweep()
        {
            if (DesktopMode || !Kit.WeaponEdge(out var from, out var to))
            {
                haveEdge = false;
                return;
            }
            if (haveEdge && CanAct && !Busy && Time.time >= attackReadyAt)
            {
                var rig = origin.transform;
                // Tip speed in rig space so walking into a minion does not count as a swing.
                Vector3 tipMove = rig.InverseTransformPoint(to) - rig.InverseTransformPoint(lastEdgeTo);
                float speed = Time.deltaTime > 0 ? tipMove.magnitude / Time.deltaTime : 0;
                if (speed > MinimumSwingSpeed && WeaponSweep(lastEdgeFrom, lastEdgeTo, from, to, .1f, out var target, out var point))
                {
                    attackReadyAt = Time.time + AttackInterval;
                    Vector3 swing = (to - lastEdgeTo).normalized;
                    Emit("Attack1", point, swing);
                    Kit.WeaponHit(target, point, swing);
                }
            }
            lastEdgeFrom = from;
            lastEdgeTo = to;
            haveEdge = true;
        }

        /// <summary>Equips a champion's kit. Clears cooldowns and every effect of the previous kit.</summary>
        public void SetChampion(ChampionDefinition definition)
        {
            Kit?.OnUnequip();
            StopAllCoroutines();
            respawnRoutine = null;
            Kit = ChampionKits.Create(definition.id);
            Kit.Bind(this, definition);
            Stats.Reset(definition);
            Array.Clear(readyAt, 0, readyAt.Length);
            attackReadyAt = 0;
            Kit.OnEquip();
            Health.RefreshGuards();
            KitChanged?.Invoke(Kit);
        }

        // ---------- Cooldowns and casting ----------

        public float Cooldown(int slot) => slot >= 0 && slot < 4 ? Mathf.Max(0, readyAt[slot] - Time.time) : 0;

        public float Cooldown(string key) => Cooldown("QWER".IndexOf(key, StringComparison.Ordinal));

        /// <summary>Base cooldown of the current rank before ability haste.</summary>
        public float BaseCooldown(int slot) => Definition ? Definition.spells[slot].Cooldown(Definition.Rank(slot, Level)) : 0;

        public void StartCooldown(int slot, float seconds) => readyAt[slot] = Time.time + seconds * Stats.CooldownMultiplier;

        public void ReduceCooldown(int slot, float seconds) => readyAt[slot] = Mathf.Max(Time.time, readyAt[slot] - seconds);

        /// <summary>Refunds a fraction of the slot's full cooldown (Gwen E, Axiom Arc...).</summary>
        public void RefundCooldown(int slot, float fraction) => ReduceCooldown(slot, BaseCooldown(slot) * Stats.CooldownMultiplier * fraction);

        public void AdvanceBasicCooldowns(float seconds)
        {
            for (int i = 0; i < 3; i++)
                ReduceCooldown(i, seconds);
        }

        public void AdvanceUltimateCooldown(float seconds) => ReduceCooldown(3, seconds);

        public void ResetAttackTimer() => attackReadyAt = 0;

        /// <summary>
        /// Commits a validated cast: checks cooldown and mana, starts the cooldown and emits the QWER signal.
        /// Recasts skip cooldown and cost. Returns false when the cast cannot be paid for.
        /// </summary>
        public bool Commit(int slot, bool recast = false, Vector3? signalOrigin = null, Vector3? signalDirection = null)
        {
            if (!CanAct)
                return false;
            if (!recast)
            {
                if (Cooldown(slot) > 0)
                    return false;
                int rank = Definition.Rank(slot, Level);
                if (Economy && Definition.UsesMana && !Economy.TrySpendMana(Definition.spells[slot].Cost(rank)))
                    return false;
                StartCooldown(slot, Definition.spells[slot].Cooldown(rank));
            }
            bool offHand = slot == 1 || slot == 3;
            Emit("QWER"[slot].ToString(), signalOrigin ?? (offHand ? OffHandOrigin : AttackOrigin), signalDirection ?? (offHand ? OffHandDirection : AttackDirection));
            return true;
        }

        public bool BasicAttack()
        {
            if (!CanAct || Busy || Time.time < attackReadyAt || !Kit.HasAttackTarget(AttackOrigin, AttackDirection))
                return false;
            attackReadyAt = Time.time + AttackInterval;
            Emit("Attack1", AttackOrigin, AttackDirection);
            Kit.BasicAttack(AttackOrigin, AttackDirection);
            return true;
        }

        /// <summary>
        /// Casts an ability. A kit that is merely busy (for example Gwen's Q snips) still allows its other abilities;
        /// only kits that report <see cref="ChampionKit.BlocksCasts"/> (channels, leaps) lock every slot.
        /// </summary>
        public bool CastSlot(int slot)
        {
            if (!CanAct)
                return false;
            bool recast = Kit.CanRecast(slot);
            if (Kit.BlocksCasts && !recast)
                return false;
            if (Cooldown(slot) > 0 && !recast)
                return false;
            return Kit.Cast(slot);
        }

        public bool CastQ() => CastSlot(0);
        public bool CastW() => CastSlot(1);
        public bool CastE() => CastSlot(2);
        public bool CastR() => CastSlot(3);

        public void Emit(string signal, Vector3 position, Vector3 direction)
        {
            onAbility.Invoke(signal);
            Cast?.Invoke(signal, position, direction);
        }

        // ---------- Damage guard and targeting ----------

        public bool Blocks(DamageHit hit) => Kit != null && Kit.Blocks(hit);

        public float BonusResistance => Kit?.BonusResistance ?? 0;

        public bool HiddenFrom(Combatant attacker) => Kit != null && Kit.HiddenFrom(attacker);

        // ---------- Death and respawn ----------

        void OnUnitDefeated(Combatant victim, DamageHit hit)
        {
            if (victim != Health)
                Kit?.OnUnitDefeated(victim, hit);
        }

        void OnDeath()
        {
            if (respawnRoutine == null)
                respawnRoutine = StartCoroutine(Respawn());
        }

        IEnumerator Respawn()
        {
            Kit?.OnDeath();
            Emit("Death", Feet, Vector3.up);
            var match = RiftMatch.Instance;
            float wait = match ? match.rules.DeathTimer(Level, match.Seconds) : 6;
            RespawnAt = Time.time + wait;
            while (Time.time < RespawnAt)
                yield return null;
            if (match)
                match.MoveToFountain();
            else
                MoveFeet(spawn ? spawn.position : Feet);
            Health.ResetHealth();
            Array.Clear(readyAt, 0, readyAt.Length);
            attackReadyAt = 0;
            respawnRoutine = null;
            Kit?.OnRespawn();
            Emit("Respawn", Feet, Vector3.up);
        }

        /// <summary>Full reset for a new match: health, cooldowns and kit state.</summary>
        public void ResetPractice()
        {
            StopAllCoroutines();
            respawnRoutine = null;
            RespawnAt = 0;
            Array.Clear(readyAt, 0, readyAt.Length);
            attackReadyAt = 0;
            Defeated = 0;
            if (Kit != null)
            {
                Kit.OnUnequip();
                Kit.OnEquip();
            }
            Health.ResetHealth();
        }
    }
}
