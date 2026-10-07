using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace LeagueVR.Champions
{
    public class ChampionProjectile:MonoBehaviour
    {
        public ChampionAbilities source;public string ability;public Vector3 direction;public float speed=14,range=16,radius=.13f,damage,travelled;
        public DamageKind kind;public bool basic,piercing,returning,returnPhase;public Combatant homing;
        public Action<Combatant,float> hit;public Func<float,float> damageAtDistance;
        readonly HashSet<Combatant> victims=new();Vector3 launched;
        void Start(){launched=transform.position;}
        public void Redirect(Vector3 aim){direction=aim.normalized;range=travelled+18;victims.Clear();}
        void Update()
        {
            if(!source||!source.Player.Health.IsAlive||!source.IsActive){Destroy(gameObject);return;}
            if(returnPhase){var delta=source.Player.AttackOrigin-transform.position;if(delta.magnitude<.25f){Destroy(gameObject);return;}direction=delta.normalized;}
            else if(homing&&homing.IsTargetable)direction=(homing.AimPosition-transform.position).normalized;
            float step=Mathf.Min(speed*Time.deltaTime,Mathf.Max(0,range-travelled));
            var hits=Physics.SphereCastAll(transform.position,radius,direction,step,source.Player.worldMask|source.Player.combatMask,QueryTriggerInteraction.Collide).OrderBy(h=>h.distance);
            foreach(var h in hits)
            {
                if(source.IsOwnCollider(h.collider))continue;
                var target=h.collider.GetComponentInParent<Combatant>();
                if(target){if(!source.Enemy(target)||!victims.Add(target))continue;float dealt=source.Hit(target,damageAtDistance!=null?damageAtDistance(travelled):damage,kind,ability,basic);if(dealt>0)hit?.Invoke(target,dealt);if(!piercing){Destroy(gameObject);return;}}
                else if((source.Player.worldMask.value&(1<<h.collider.gameObject.layer))!=0){if(returning&&!returnPhase){returnPhase=true;victims.Clear();break;}Destroy(gameObject);return;}
            }
            transform.position+=direction*step;travelled+=step;
            if(travelled>=range){if(returning&&!returnPhase){returnPhase=true;range=travelled+40;victims.Clear();}else Destroy(gameObject);}
            if(travelled>100)Destroy(gameObject);
        }
    }
    public class ChampionVFX:MonoBehaviour
    {
        public float duration=.4f;float born;public bool expand;LineRenderer line;Vector3 initial;
        void Awake(){born=Time.time;initial=transform.localScale;line=GetComponent<LineRenderer>();}
        void Update(){float t=(Time.time-born)/duration;if(expand)transform.localScale=initial*(1+t*.6f);if(t>=1)Destroy(gameObject);}
    }
    public class SpellShard:MonoBehaviour
    {
        public int kind;public float expires;public ChampionAbilities owner;
        void Update(){transform.Rotate(0,55*Time.deltaTime,0);if(Time.time>expires||!owner)Destroy(gameObject);}
    }
    public class ChampionDebuff:MonoBehaviour
    {
        public ChampionAbilities owner;public int blazeStacks;public float blazeUntil,detonateAt,immuneUntil;
        Combatant target;float tick;
        void Awake(){target=GetComponent<Combatant>();}
        public void Blaze(ChampionAbilities who)
        {
            owner=who;if(Time.time>blazeUntil)blazeStacks=0;blazeStacks=Mathf.Min(3,blazeStacks+1);blazeUntil=Time.time+4;
            if(blazeStacks>=3&&(target.countsAsChampion||target.GetComponent<LeagueVR.Match.RiftObjective>())&&Time.time>immuneUntil&&detonateAt==0){detonateAt=Time.time+2;immuneUntil=Time.time+6;}
        }
        void Update()
        {
            if(!target.IsAlive||!owner||!owner.IsActive){Destroy(this);return;}
            if(Time.time<blazeUntil&&Time.time>=tick){tick=Time.time+.5f;owner.Hit(target,target.maxHealth*.003f*blazeStacks,DamageKind.Magic,"Passive");}
            if(detonateAt>0&&Time.time>=detonateAt){detonateAt=0;owner.Ring(target.transform.position,2.5f,.35f);foreach(var t in owner.Around(target.AimPosition,2.5f))owner.Hit(t,t.maxHealth*.10f+owner.AP*.25f,DamageKind.Magic,"Passive");}
            if(Time.time>=blazeUntil&&detonateAt==0)Destroy(this);
        }
    }
}
