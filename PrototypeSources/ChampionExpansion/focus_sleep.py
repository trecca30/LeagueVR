from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Runtime/ChampionAbilities.cs';s=p.read_text()
s=s.replace('!Physics.Raycast(Player.AttackOrigin,Player.AttackDirection,out var anchor,12,Player.worldMask,QueryTriggerInteraction.Ignore)', '!TerrainRay(Player.AttackOrigin,Player.AttackDirection,12,out var anchor)')
old='public bool GroundAim(Vector3 from,Vector3 direction,float range,out Vector3 result)'
new='''public bool TerrainRay(Vector3 from,Vector3 direction,float range,out RaycastHit hit)
        {
            foreach(var candidate in Physics.RaycastAll(from,direction,range,Player.worldMask,QueryTriggerInteraction.Ignore).Where(h=>!IsOwnCollider(h.collider)).OrderBy(h=>h.distance)){hit=candidate;return true;}
            hit=default;return false;
        }
        public bool GroundAim(Vector3 from,Vector3 direction,float range,out Vector3 result)'''
assert old in s;s=s.replace(old,new);p.write_text(s)
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionRepair.cs';s=p.read_text()
old='yield return new WaitForSeconds(1.45f);Check(target.Stunned,"Zoe: bubble sleeps target");'
new='''float bubbleHP=target.Health;float started=Time.time;bool slept=false;
                    while(Time.time-started<3&&!slept){yield return new WaitForSeconds(.1f);slept=target.Stunned;debug.Add("Zoe bubble t="+(Time.time-started)+" health="+target.Health+" slow="+target.SlowMultiplier+" sleep="+slept);}
                    Check(target.Health<bubbleHP,"Zoe: bubble hits enemy");Check(slept,"Zoe: bubble sleeps target");'''
assert old in s;s=s.replace(old,new)
old='if(d.id==ChampionId.Brand){a.ResetState();'
new='''if(d.id==ChampionId.Akshan){var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=8;wall.transform.position=p.AttackOrigin+Vector3.forward*5;wall.transform.localScale=new Vector3(2,3,.2f);Physics.SyncTransforms();Check(a.TerrainRay(p.AttackOrigin,p.AttackDirection,12,out var anchor)&&anchor.collider==wall.GetComponent<Collider>(),"Akshan: grapple chooses terrain beyond own ward");wall.SetActive(false);Destroy(wall);}
                if(d.id==ChampionId.Brand){a.ResetState();'''
assert old in s;s=s.replace(old,new);p.write_text(s)
for name in ['Repair.md','Repair-debug.txt','Repair-errors.txt']:
    src=P/'Logs/ChampionExpansion'/name
    if src.exists():shutil.copy2(src,src.with_name(src.stem+'-after-aim-fix'+src.suffix))
print('Added bounded sleep observation and terrain ray filtering for grappling.')
