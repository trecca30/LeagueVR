from pathlib import Path
import re,json,hashlib
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing');old=(P/'PrototypeBackups/ChampionExpansion-20261004/Assets/Scenes/LeagueVR.unity').read_text(encoding='utf-8-sig');new=(P/'Assets/Scenes/LeagueVR.unity').read_text(encoding='utf-8-sig')
def transforms(s):
 out={}
 for b in s.split('--- !u!4 &')[1:]:
  k=b.splitlines()[0].strip();block=b.split('--- !u!')[0]
  if 'm_LocalPosition:' not in block: continue
  out[k]={field:re.search(r'  '+field+r': (.*)',block).group(1) for field in ['m_LocalPosition','m_LocalRotation','m_LocalScale','m_Father']}
 return out
a,b=transforms(old),transforms(new);changes=[{'id':k,'before':v,'after':b.get(k)} for k,v in a.items() if b.get(k)!=v]
manifest=json.loads((P/'Logs/ChampionExpansion/Sources.json').read_text());assetchecks=[]
for x in manifest['assets']:
 name=x['champion'];source=Path(x['source']);imported=P/f'Assets/_Game/LeagueVR/Champions/Art/{name}/{name}.glb';assetchecks.append({'name':name,'byte_identical':source.read_bytes()==imported.read_bytes(),'sha256':hashlib.sha256(imported.read_bytes()).hexdigest()})
report={'old_transforms':len(a),'new_transforms':len(b),'changed_existing_transforms':changes,'attached_glbs':assetchecks}
(P/'Logs/ChampionExpansion/Preservation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
