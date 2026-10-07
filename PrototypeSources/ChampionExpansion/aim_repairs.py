from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
F=P/'Assets/_Game/LeagueVR'
def edit(path,old,new):
    p=F/path;s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old);p.write_text(s.replace(old,new),encoding='utf-8')
edit(Path('Match/Runtime/RiftItemRack.cs'),'public bool IsHandHolding(int hand)=>', '''public bool OwnsCollider(Collider collider)
        {
            if (!collider) return false;
            foreach (var slot in slots) if (slot.model && collider.transform.IsChildOf(slot.model.transform)) return true;
            return false;
        }
        public bool IsHandHolding(int hand)=>''')
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'public Combatant Aim(float range', '''public bool IsOwnCollider(Collider collider)=>collider&&(collider.transform.IsChildOf(Player.origin.transform)||(economy&&economy.GetComponent<RiftItemRack>() is RiftItemRack rack&&rack.OwnsCollider(collider)));
        public Combatant Aim(float range''')
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'{var t=h.collider.GetComponentInParent<Combatant>();if(Enemy(t))','{if(IsOwnCollider(h.collider))continue;var t=h.collider.GetComponentInParent<Combatant>();if(Enemy(t))')
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'missile.basic=true;','missile.basic=true;missile.homing=target;')
edit(Path('Champions/Runtime/ChampionProjectile.cs'),'var target=h.collider.GetComponentInParent<Combatant>();','if(source.IsOwnCollider(h.collider))continue;\n                var target=h.collider.GetComponentInParent<Combatant>();')
# Equipment is not terrain: exclude it consistently from ground and movement queries.
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'if(Physics.Raycast(from,direction,out var hit,range,Player.worldMask,QueryTriggerInteraction.Ignore)&&hit.normal.y>.65f){result=hit.point;return true;}', '''var hits=Physics.RaycastAll(from,direction,range,Player.worldMask,QueryTriggerInteraction.Ignore).Where(h=>!IsOwnCollider(h.collider)).OrderBy(h=>h.distance);
            foreach(var hit in hits){if(hit.normal.y>.65f){result=hit.point;return true;}break;}''')
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'public bool ClearDestination(Vector3 at)=>!Physics.CheckCapsule(at+Vector3.up*.4f,at+Vector3.up*1.4f,.22f,Player.worldMask,QueryTriggerInteraction.Ignore);','public bool ClearDestination(Vector3 at)=>!Physics.OverlapCapsule(at+Vector3.up*.4f,at+Vector3.up*1.4f,.22f,Player.worldMask,QueryTriggerInteraction.Ignore).Any(c=>!IsOwnCollider(c));')
edit(Path('Champions/Runtime/ChampionAbilities.cs'),'if(Physics.CapsuleCast(Player.Feet+Vector3.up*.38f,Player.Feet+Vector3.up*1.4f,.22f,delta.normalized,out var block,distance,Player.worldMask,QueryTriggerInteraction.Ignore))desired=Player.Feet+delta.normalized*Mathf.Max(0,block.distance-.15f);','foreach(var block in Physics.CapsuleCastAll(Player.Feet+Vector3.up*.38f,Player.Feet+Vector3.up*1.4f,.22f,delta.normalized,distance,Player.worldMask,QueryTriggerInteraction.Ignore).Where(h=>!IsOwnCollider(h.collider)).OrderBy(h=>h.distance)){desired=Player.Feet+delta.normalized*Mathf.Max(0,block.distance-.15f);break;}')
preservation=Path(r'C:\Users\Kakad\AppData\Local\Temp\LeagueChampions\preservation.py')
s=preservation.read_text();s=s.replace("k=b.splitlines()[0].strip();block=b.split('--- !u!')[0];out[k]=", "k=b.splitlines()[0].strip();block=b.split('--- !u!')[0]\n  if 'm_LocalPosition:' not in block: continue\n  out[k]=")
preservation.write_text(s)
for name in ['Repair.md','Repair-debug.txt','Repair-errors.txt']:
    src=P/'Logs/ChampionExpansion'/name
    if src.exists():shutil.copy2(src,src.with_name(src.stem+'-before-aim-fix'+src.suffix))
print('Filtered owned item colliders; ranged basic attacks follow the selected target.')
