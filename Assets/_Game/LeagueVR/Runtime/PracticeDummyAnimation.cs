using UnityEngine;
namespace LeagueVR
{
    [RequireComponent(typeof(Animation))]
    public class PracticeDummyAnimation:MonoBehaviour
    {
        public Combatant health;
        public string idle="Idle.anm",hit="Stunned";
        Animation player;float returnToIdle;
        void Awake(){player=GetComponent<Animation>();}
        void OnEnable()
        {
            if(!player)player=GetComponent<Animation>();
            if(health)health.Damaged+=OnHit;
            PlayIdle();
        }
        void OnDisable(){if(health)health.Damaged-=OnHit;}
        void PlayIdle()
        {
            returnToIdle=0;if(player[idle]==null)return;
            player[idle].wrapMode=WrapMode.Loop;player.CrossFade(idle,.12f);
        }
        void OnHit(DamageHit damage,float amount)
        {
            if(!health.IsAlive || player[hit]==null)return;
            player[hit].wrapMode=WrapMode.Once;player[hit].time=0;
            player.CrossFade(hit,.06f);returnToIdle=Time.time+.32f;
        }
        void Update(){if(returnToIdle>0 && Time.time>=returnToIdle)PlayIdle();}
    }
}
