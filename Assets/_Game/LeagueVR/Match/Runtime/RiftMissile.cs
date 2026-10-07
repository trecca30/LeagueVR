using UnityEngine;
namespace LeagueVR.Match
{
    public class RiftMissile:MonoBehaviour
    {
        LeagueUnitAudio audio;string impactKey;Combatant owner,target;float damage,speed;DamageKind kind;
        public static void Launch(Combatant from,Combatant to,Vector3 point,float amount,float velocity,Material material,DamageKind kind=DamageKind.Physical,float size=.1f)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Attack projectile";Destroy(go.GetComponent<Collider>());go.transform.position=point;go.transform.localScale=Vector3.one*size;
            go.GetComponent<Renderer>().sharedMaterial=material;var missile=go.AddComponent<RiftMissile>();missile.owner=from;missile.target=to;missile.damage=amount;missile.speed=velocity;missile.kind=kind;missile.audio=from?from.GetComponent<LeagueUnitAudio>():null;missile.impactKey=missile.audio?missile.audio.ImpactKey:null;Destroy(go,8);
        }
        void Update()
        {
            if(!target || !target.IsTargetableBy(owner) || !RiftMatch.Instance || !RiftMatch.Instance.Running){Destroy(gameObject);return;}
            transform.position=Vector3.MoveTowards(transform.position,target.AimPosition,speed*Time.deltaTime);
            if(Vector3.Distance(transform.position,target.AimPosition)<.15f){target.TakeDamage(new DamageHit(owner,owner?owner.AimPosition:transform.position,damage,kind){isBasicAttack=true});if(audio)audio.Hit(target.AimPosition,impactKey);Destroy(gameObject);}
        }
    }
}
