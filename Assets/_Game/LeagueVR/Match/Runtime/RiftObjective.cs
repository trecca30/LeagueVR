using UnityEngine;
namespace LeagueVR.Match
{
    public class RiftObjective:RiftActor
    {
        public Transform visual;public string objective;public float spawnTime=300,respawnDelay=300,damage=80,attackInterval=1.5f,leash=10;public int reward=100;public float experience=250;
        public float NextSpawn{get;private set;}Vector3 home;float nextAttack;DamageHit lastHit;Combatant attacker;
        protected override void Awake(){base.Awake();neutral=true;home=transform.position;NextSpawn=spawnTime;health.Damaged+=OnHit;health.onDeath.AddListener(Die);}
        void OnHit(DamageHit hit,float amount){lastHit=hit;if(hit.source)attacker=hit.source;}
        public void ResetObjective(){home=transform.position;NextSpawn=spawnTime;attacker=null;health.ResetHealth();Visible(false);}
        void Visible(bool value){if(visual)visual.gameObject.SetActive(value);foreach(var c in GetComponentsInChildren<Collider>(true))c.enabled=value;if(label)label.gameObject.SetActive(value);}
        public override bool Targetable=>base.Targetable && RiftMatch.Instance && RiftMatch.Instance.Seconds>=NextSpawn;
        void Update()
        {
            var match=RiftMatch.Instance;if(!match || !match.Running)return;
            if(match.Seconds<NextSpawn){Visible(false);return;}
            if(!health.IsAlive){health.ResetHealth();attacker=null;}Visible(true);
            if(!attacker || !attacker.IsAlive)return;
            if(GwenAbilities.FlatDistance(home,attacker.transform.position)>leash){attacker=null;health.Heal(health.maxHealth);return;}
            transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(Vector3.ProjectOnPlane(attacker.AimPosition-transform.position,Vector3.up)),Time.deltaTime*3);
            if(Time.time>=nextAttack){nextAttack=Time.time+attackInterval;RiftMissile.Launch(health,attacker,health.AimPosition,damage,10,match.neutralMaterial,DamageKind.Physical,.25f);}
        }
        void Die()
        {
            var match=RiftMatch.Instance;if(!match)return;match.AwardUnit(health,lastHit,reward,experience);match.Notify(objective+" slain");
            if(lastHit.source==match.player.Health)match.economy.ObjectiveReward(objective);
            NextSpawn=respawnDelay>0?match.Seconds+respawnDelay:float.MaxValue;Visible(false);
        }
    }
}
