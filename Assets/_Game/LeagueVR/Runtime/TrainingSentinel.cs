using TMPro;
using UnityEngine;
namespace LeagueVR
{
    [RequireComponent(typeof(Combatant))]
    public class TrainingSentinel : MonoBehaviour
    {
        public GwenAbilities player;
        public Transform visuals;
        public TMP_Text label;
        public Material boltMaterial;
        public bool attacksPlayer;
        public bool moves;
        public float attackRange=13, attackInterval=3.5f;
        Combatant health;
        Vector3 home;
        float nextAttack, respawnAt;
        void Awake() { health=GetComponent<Combatant>(); home=transform.position; }
        void OnEnable() { if(!health)health=GetComponent<Combatant>(); health.onDeath.AddListener(Killed); }
        void OnDisable() { health.onDeath.RemoveListener(Killed); }
        void Killed() { respawnAt=Time.time+5; player.Defeated++; if(visuals)visuals.gameObject.SetActive(false); }
        void Update()
        {
            if (!health.IsAlive) { if(Time.time>=respawnAt) { transform.position=home; health.ResetHealth(); if(visuals)visuals.gameObject.SetActive(true); } else return; }
            if(label) { label.text=$"{gameObject.name}\n{health.Health:0} / {health.maxHealth:0}"+(health.SlowMultiplier<1?"  SLOWED":""); label.transform.rotation=Quaternion.LookRotation(label.transform.position-player.head.transform.position); }
            if(!player.Health.IsAlive) return;
            Vector3 aim=player.head.transform.position, start=health.AimPosition;
            if(moves && Vector3.Distance(transform.position,home)<4)
            {
                Vector3 step=Vector3.ProjectOnPlane(aim-transform.position,Vector3.up).normalized*.65f*health.SlowMultiplier*Time.deltaTime;
                if(Vector3.Distance(aim,start)>2 && !Physics.SphereCast(transform.position+Vector3.up*.5f,.35f,step.normalized,out _,step.magnitude,player.worldMask) && Physics.Raycast(transform.position+step+Vector3.up,Vector3.down,out var floor,2,player.worldMask) && floor.normal.y>.8f) transform.position=floor.point;
            }
            if(!attacksPlayer || Time.time<nextAttack || Vector3.Distance(start,aim)>attackRange || Physics.Linecast(start,aim,player.worldMask)) return;
            nextAttack=Time.time+attackInterval;
            var go=new GameObject("Sentinel bolt"); go.transform.position=start;
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=boltMaterial; line.positionCount=2; line.startWidth=line.endWidth=.08f; line.useWorldSpace=false; line.SetPosition(0,Vector3.zero); line.SetPosition(1,Vector3.back*.3f);
            go.transform.rotation=Quaternion.LookRotation(aim-start);
            var projectile=go.AddComponent<CombatProjectile>(); projectile.owner=health; projectile.damage=35; projectile.speed=5; projectile.range=attackRange+2; projectile.direction=(aim-start).normalized; projectile.worldMask=player.worldMask; projectile.combatMask=player.combatMask;
        }
    }
}
