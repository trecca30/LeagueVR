using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Unity.XR.CoreUtils;

namespace LeagueVR
{
    [RequireComponent(typeof(Combatant))]
    public class GwenAbilities : MonoBehaviour, IDamageGuard
    {
        public GwenTuning tuning;
        public XROrigin origin;
        public Camera head;
        public Transform rightHand, leftHand, spawn;
        public LayerMask worldMask, combatMask;
        public GameObject needleVisualPrefab;
        public Material effectMaterial;
        public UnityEvent<string> onAbility = new UnityEvent<string>();
        public event Action<string, Vector3, Vector3> Cast;
        public Combatant Health { get; private set; }
        public int QStacks { get; private set; }
        public int RStage { get; private set; }
        public int Defeated { get; set; }
        public LeagueVR.Champions.ChampionAbilities Other => GetComponent<LeagueVR.Champions.ChampionAbilities>();
        public bool OtherActive => Other && Other.IsActive;
        public string ChampionName => OtherActive ? Other.Definition.name : "Gwen";
        public bool Busy => OtherActive ? Other.Busy : qCasting;
        bool SpendMana(float amount) => !GetComponent<LeagueVR.Match.RiftEconomy>() || GetComponent<LeagueVR.Match.RiftEconomy>().TrySpendMana(amount);
        public void AdvanceBasicCooldowns(float seconds) { if(OtherActive){Other.Advance(seconds);return;} qReady=Mathf.Max(Time.time,qReady-seconds);wReady=Mathf.Max(Time.time,wReady-seconds);eReady=Mathf.Max(Time.time,eReady-seconds); }
        public void AdvanceUltimateCooldown(float seconds) { if(OtherActive){Other.Advance(seconds,true);return;} rReady=Mathf.Max(Time.time,rReady-seconds); }
        public void ResetAttackTimer() { attackReady=0;if(OtherActive)Other.ResetAttack(); }
        public bool MistActive => !OtherActive && Time.time < mistUntil && Health && Health.IsAlive;
        public bool Empowered => !OtherActive && Time.time < empoweredUntil;
        public Vector3 MistCenter { get; private set; }
        public float MistRemaining => Mathf.Max(0, mistUntil - Time.time);
        public float BonusResistance => MistActive ? tuning.wResistance : 0;
        public bool DesktopMode { get; set; }
        bool CombatLocked => LeagueVR.Match.RiftUI.BlocksCombat || (GetComponent<LeagueVR.Match.RiftEconomy>()?.Stasis ?? false);
        public Vector3 Feet => new Vector3(head.transform.position.x, origin.transform.position.y, head.transform.position.z);
        public Vector3 AttackOrigin => DesktopMode ? head.transform.position - head.transform.up * .18f : !OtherActive&&GetComponent<GwenAvatar>() is GwenAvatar avatar&&avatar.enabled?GwenTracking.Grip(this,false).position+GwenTracking.Grip(this,false).rotation*avatar.rightGripOffset:rightHand.position;
        public Vector3 AttackDirection => DesktopMode ? head.transform.forward : !OtherActive&&GetComponent<GwenAvatar>() is GwenAvatar avatar&&avatar.enabled&&avatar.handPoses?avatar.WeaponRotation*Vector3.forward:rightHand.forward;
        public float Cooldown(string key) => OtherActive ? Other.Cooldown("QWER".IndexOf(key)) : Mathf.Max(0, (key == "Q" ? qReady : key == "W" ? wReady : key == "E" ? eReady : rReady) - Time.time);
        float qReady, wReady, eReady, rReady, attackReady, mistUntil, empoweredUntil, stackUntil, rWindowUntil, rNext;
        bool qCasting, mistMoved, eRefunded;
        float wRecastAt;
        readonly HashSet<Combatant> needleVictims=new();
        Coroutine respawnRoutine;
        void Awake() { Health = GetComponent<Combatant>(); Health.RefreshGuards(); }
        void OnEnable() { if (!Health) Health = GetComponent<Combatant>(); Health.onDeath.AddListener(OnDeath); }
        void OnDisable() { Health.onDeath.RemoveListener(OnDeath); StopAllCoroutines(); qCasting = false; respawnRoutine = null; }
        void Update()
        {
            if (Time.time > stackUntil) QStacks = 0;
            if (RStage > 0 && Time.time > rWindowUntil) RStage = 0;
            if (MistActive && FlatDistance(Feet, MistCenter) > tuning.wRadius)
            { if (!mistMoved) { MistCenter = Feet; mistMoved = true; } else mistUntil = 0; }
            if (Health.IsAlive && spawn && origin.transform.position.y < spawn.position.y - 12) OnDeath();
        }
        public static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a,b); }
        public bool Blocks(DamageHit hit) => MistActive && hit.source && !hit.source.GetComponent<LeagueVR.Match.RiftStructure>() && FlatDistance(hit.source ? hit.source.AimPosition : hit.origin, MistCenter) > tuning.wRadius;
        public float ApplyPassive(Combatant target)
        {
            if (!target || !target.IsAlive || target.GetComponent<LeagueVR.Match.RiftStructure>() || target.GetComponent<LeagueVR.Match.RiftVisionWard>()) return 0;
            var economy=GetComponent<LeagueVR.Match.RiftEconomy>();float ap=economy?economy.AbilityPower:0;
            float amount=target.maxHealth*tuning.passiveMaxHealthFraction;
            if(target.GetComponent<LeagueVR.Match.RiftObjective>())amount=Mathf.Min(amount,3+.05f*ap);
            float dealt=target.TakeDamage(new DamageHit(Health,AttackOrigin,amount,DamageKind.Magic){ability="Passive"});
            if(target.countsAsChampion)Health.Heal(Mathf.Min(dealt*tuning.passiveChampionHealFraction,Mathf.Lerp(12,40,((economy?economy.Level:1)-1)/17f)+.07f*ap));
            return dealt;
        }
        public bool BasicAttack()
        {
            if(OtherActive)return Other.BasicAttack();
            if(Health.Stunned)return false;
            if (CombatLocked || !Health.IsAlive || Busy || Time.time < attackReady) return false;
            var definition=GetComponent<LeagueVR.Match.RiftEconomy>()?.ChampionBase;
            float baseInterval=definition?1/definition.attackSpeed:1/.69f;
            // E contributes base attack speed; purchased speed is not multiplied again.
            float rate=1/Mathf.Max(.1f,tuning.attackInterval)+(Empowered?(1/tuning.eAttackIntervalMultiplier-1)/baseInterval:0);
            attackReady=Time.time+Health.AttackIntervalMultiplier/rate;
            Signal("Attack1", AttackOrigin, AttackDirection);
            var targets = FindTargets(AttackOrigin, AttackDirection, tuning.attackReach + (GetComponent<LeagueVR.Match.RiftEconomy>()?.Effects?.BonusAttackRange ?? 0) + (Empowered ? .35f : 0), .45f);
            if (targets.Count == 0) return true;
            var target = targets[0];
            var economy=GetComponent<LeagueVR.Match.RiftEconomy>();
            float amount=economy?economy.AttackDamage(tuning.attackDamage,target):tuning.attackDamage;
            float dealt = target.TakeDamage(new DamageHit(Health, AttackOrigin, amount, DamageKind.Physical) { isBasicAttack = true, isCritical = economy && economy.LastAttackCritical });
            if(dealt>0 && economy) economy.OnAttack(target,dealt);
            if (dealt > 0)
            {
                ApplyPassive(target);
                if (Empowered) { target.TakeDamage(new DamageHit(Health, AttackOrigin, tuning.eBonusDamage, DamageKind.Magic)); if (!eRefunded) { eReady = Mathf.Max(Time.time, eReady - tuning.eCooldown * tuning.eCooldownRefundFraction); eRefunded = true; } }
                QStacks = Mathf.Min(4, QStacks + 1); stackUntil = Time.time + tuning.qStackLifetime;
                Signal("Hit",target.AimPosition,AttackDirection);
            }
            return true;
        }
        public bool CastQ()
        {
            if(OtherActive)return Other.CastQ();
            if(Health.Stunned)return false;
            if (CombatLocked || !Health.IsAlive || Busy || Cooldown("Q") > 0) return false;
            if (!SpendMana(40)) return false;
            int snips = 2 + QStacks; QStacks = 0; qReady = Time.time + tuning.qCooldown;
            Vector3 direction=PlanarDirection(AttackDirection);Signal("Q",AttackOrigin,direction);StartCoroutine(Snip(snips,direction)); return true;
        }
        IEnumerator Snip(int snips,Vector3 forward)
        {
            qCasting=true;
            for(int i=0;i<snips&&Health.IsAlive;i++)
            {
                // Heading is locked at cast; E may still reposition the snip origin.
                Vector3 start=AttackOrigin;Signal("Snip",start,forward);GetComponent<GwenAudio>()?.Snip(i,snips);
                foreach(var target in FindTargets(start,forward,tuning.qRange,tuning.qHalfWidth,true))
                {
                    if(target.GetComponent<LeagueVR.Match.RiftStructure>()||target.GetComponent<LeagueVR.Match.RiftVisionWard>())continue;
                    Vector3 delta=Vector3.ProjectOnPlane(target.AimPosition-start,Vector3.up);
                    bool center=Vector3.Cross(delta,forward).magnitude<=tuning.qCenterHalfWidth+.15f;
                    float amount=i==snips-1?tuning.qFinalDamage:tuning.qSnipDamage;
                    if(target.GetComponent<LeagueVR.Match.RiftMinion>())amount*=target.Health/target.maxHealth<tuning.qExecuteThreshold?11:tuning.qMinionModifier;
                    float dealt=target.TakeDamage(new DamageHit(Health,start,amount*(center?.5f:1),DamageKind.Magic){ability="Q"});
                    if(center){dealt+=target.TakeDamage(new DamageHit(Health,start,amount*.5f,DamageKind.True){ability="Q"});if(dealt>0)ApplyPassive(target);}
                    if(dealt>0){Signal("Hit",target.AimPosition,forward);GetComponent<GwenAudio>()?.SnipHit(target.AimPosition,i==snips-1);}
                }
                if(i<snips-1)yield return new WaitForSeconds(tuning.qDuration/(snips-1));
            }
            qCasting=false;
        }
        public bool CastW()
        {
            if(OtherActive)return Other.CastW();
            if(Health.Stunned)return false;
            if (CombatLocked || !Health.IsAlive) return false;
            if (MistActive && !mistMoved) { if(Time.time<wRecastAt)return false; MistCenter = Feet; mistMoved = true; Signal("W",MistCenter,Vector3.up); return true; }
            if (Cooldown("W") > 0 || !SpendMana(60)) return false;
            MistCenter = Feet; mistMoved = false; mistUntil = Time.time + tuning.wDuration; wReady = Time.time + tuning.wCooldown;wRecastAt=Time.time+.5f;
            Signal("W",MistCenter,Vector3.up); return true;
        }
        public bool CastE()
        {
            if(OtherActive)return Other.CastE();
            if(Health.Stunned)return false;
            if (CombatLocked || !Health.IsAlive || Cooldown("E") > 0) return false;
            Vector3 direction = Vector3.ProjectOnPlane(AttackDirection, Vector3.up).normalized;
            if (direction.sqrMagnitude < .1f) direction = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            var cc = origin.GetComponent<CharacterController>();
            Vector3 feet = Feet;
            float radius = cc ? cc.radius : .25f;
            float height = Mathf.Max(1.2f, head.transform.position.y - feet.y);
            float distance = tuning.eDistance;
            foreach(var block in Physics.CapsuleCastAll(feet+Vector3.up*(radius+.12f),feet+Vector3.up*(height-radius),radius,direction,distance,worldMask,QueryTriggerInteraction.Ignore))if(!OwnCollider(block.collider))distance=Mathf.Min(distance,Mathf.Max(0,block.distance-.12f));
            Vector3 desired = feet + direction * distance;
            if (Health.Rooted || distance < .15f || !Physics.Raycast(desired+Vector3.up*.65f,Vector3.down,out var ground,1.5f,worldMask,QueryTriggerInteraction.Ignore) || ground.normal.y < .7f) return false;
            if (!SpendMana(35)) return false;
            Vector3 displacement = ground.point - feet + Vector3.up*.05f;
            // An instantaneous, collision-checked short step avoids a forced camera animation in VR.
            if (cc && cc.enabled) cc.Move(displacement); else origin.transform.position += displacement;
            eReady = Time.time + tuning.eCooldown; empoweredUntil = Time.time + tuning.eDuration; eRefunded = false; attackReady = 0;
            Signal("E",feet,direction); return true;
        }
        public bool CastR()
        {
            if(OtherActive)return Other.CastR();
            if(Health.Stunned)return false;
            if (CombatLocked || !Health.IsAlive || Time.time < rNext || (RStage == 0 && Cooldown("R") > 0)) return false;
            if (RStage == 0 && !SpendMana(100)) return false;
            if (RStage == 0) { rReady=Time.time+tuning.rCooldown;needleVictims.Clear(); }
            rWindowUntil=Time.time+tuning.rWindow;
            int stage = RStage + 1; RStage = stage >= 3 ? 0 : stage; rNext = Time.time+tuning.rRecastDelay;
            Vector3 start = DesktopMode ? AttackOrigin : leftHand.position;
            Vector3 dir = DesktopMode ? AttackDirection : leftHand.forward;
            StartCoroutine(NeedleVolley(2*stage-1,start,dir.normalized)); Signal("R",start,dir); return true;
        }
        IEnumerator NeedleVolley(int count, Vector3 start, Vector3 forward)
        {
            for (int i=0; i<count && Health.IsAlive; i++)
            {
                var go = new GameObject("Needlework"); go.transform.SetPositionAndRotation(start,Quaternion.LookRotation(forward));
                if (needleVisualPrefab) Instantiate(needleVisualPrefab,go.transform);
                else { var lr=go.AddComponent<LineRenderer>(); lr.sharedMaterial=effectMaterial; lr.positionCount=2; lr.startWidth=.04f; lr.endWidth=.01f; lr.useWorldSpace=false; lr.SetPosition(0,Vector3.zero); lr.SetPosition(1,Vector3.back*.6f); }
                var p = go.AddComponent<CombatProjectile>(); p.owner=Health; p.ability="R"; p.damage=tuning.rDamage; p.direction=forward; p.speed=tuning.rSpeed; p.range=tuning.rRange; p.piercing=true;p.ignoreStructures=true;p.slowForTarget=target=>needleVictims.Add(target)?tuning.rSlowMultiplier:.85f; p.worldMask=worldMask; p.combatMask=combatMask; p.slowMultiplier=tuning.rSlowMultiplier; p.slowDuration=tuning.rSlowDuration; p.onHit=target=>{ApplyPassive(target);Signal("Hit",target.AimPosition,forward);GetComponent<GwenAudio>()?.NeedleHit(target);};
                yield return new WaitForSeconds(.1f);
            }
        }
        Vector3 PlanarDirection(Vector3 direction)
        {var flat=Vector3.ProjectOnPlane(direction,Vector3.up);if(flat.sqrMagnitude<.01f)flat=Vector3.ProjectOnPlane(head.transform.forward,Vector3.up);return flat.sqrMagnitude>.01f?flat.normalized:origin.transform.forward;}
        public bool OwnCollider(Collider c)=>c&&(c.transform.IsChildOf(origin.transform)||(GetComponent<LeagueVR.Match.RiftItemRack>()?.OwnsCollider(c)??false));
        public bool ClearAttackLine(Vector3 start,Vector3 end,Combatant target)
        {var delta=end-start;foreach(var hit in Physics.RaycastAll(start,delta.normalized,delta.magnitude,worldMask,QueryTriggerInteraction.Ignore))if(!OwnCollider(hit.collider)&&hit.collider.GetComponentInParent<Combatant>()!=target)return false;return true;}
        List<Combatant> FindTargets(Vector3 start,Vector3 forward,float range,float radius,bool cone=false)
        {
            var found=new List<Combatant>();forward=PlanarDirection(forward);
            foreach(var c in Physics.OverlapSphere(start,range+1.5f,combatMask,QueryTriggerInteraction.Collide))
            {
                var t=c.GetComponentInParent<Combatant>();
                if(!t||t==Health||!t.IsTargetableBy(Health)||t.team==Health.team||found.Contains(t))continue;
                // Ground-plane assistance lets chest-height scissors hit short minions.
                Vector3 delta=Vector3.ProjectOnPlane(c.bounds.center-start,Vector3.up);float along=Vector3.Dot(delta,forward);
                if(along<0||c.bounds.min.y>start.y+1.5f||c.bounds.max.y<Feet.y-.6f)continue;
                Vector3 nearest=c.ClosestPoint(start+forward*Mathf.Clamp(along,0,range));Vector3 edge=Vector3.ProjectOnPlane(nearest-start,Vector3.up);
                float width=cone?Mathf.Lerp(.08f,radius,Mathf.Clamp01(along/range)):radius;
                if(Vector3.Dot(edge,forward)>range||Vector3.Cross(edge,forward).magnitude>width||FlatDistance(c.ClosestPoint(start),start)>range)continue;
                if(!ClearAttackLine(start,c.ClosestPoint(start),t))continue;found.Add(t);
            }
            float Score(Combatant t){var d=Vector3.ProjectOnPlane(t.AimPosition-start,Vector3.up);return Vector3.Cross(d,forward).magnitude*3+d.magnitude*.2f;}
            found.Sort((a,b)=>Score(a).CompareTo(Score(b)));return found;
        }
        public void Emit(string name,Vector3 pos,Vector3 dir) => Signal(name,pos,dir);
        void Signal(string name,Vector3 pos,Vector3 dir) { onAbility.Invoke(name); Cast?.Invoke(name,pos,dir); }
        void OnDeath() { if (respawnRoutine == null) respawnRoutine=StartCoroutine(Respawn()); }
        IEnumerator Respawn()
        {
            mistUntil=empoweredUntil=0; QStacks=RStage=0; Signal("Death",Feet,Vector3.up);
            yield return new WaitForSeconds(2);
            var cc=origin.GetComponent<CharacterController>(); bool wasEnabled=cc && cc.enabled; if (cc) cc.enabled=false;
            if (spawn) { var offset=head.transform.position-origin.transform.position; offset.y=0; origin.transform.position=spawn.position-offset; }
            if (cc) cc.enabled=wasEnabled;
            qCasting=false;Other?.ResetState();Health.ResetHealth(); qReady=wReady=eReady=rReady=attackReady=0; respawnRoutine=null; Signal("Respawn",Feet,Vector3.up);
        }
        public void ResetPractice() { StopAllCoroutines();qCasting=false;respawnRoutine=null;mistUntil=empoweredUntil=0;QStacks=RStage=0;qReady=wReady=eReady=rReady=attackReady=0;Other?.ResetState();Health.ResetHealth(); }
    }
}
