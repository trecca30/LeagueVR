from pathlib import Path
import re,json,hashlib
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
old=(P/'PrototypeBackups/ChampionExpansion-20261004/Assets/Scenes/LeagueVR.unity').read_text(encoding='utf-8-sig')
new=(P/'Assets/Scenes/LeagueVR.unity').read_text(encoding='utf-8-sig')
def transforms(text):
    out={}
    for block in text.split('--- !u!4 &')[1:]:
        key=block.splitlines()[0].strip();block=block.split('--- !u!')[0]
        if 'm_LocalPosition:' not in block:continue
        out[key]={f:re.search(r'  '+f+r': (.*)',block).group(1) for f in ['m_LocalPosition','m_LocalRotation','m_LocalScale','m_Father']}
    return out
a,b=transforms(old),transforms(new)
changes=[{'id':k,'before':v,'after':b.get(k)} for k,v in a.items() if b.get(k)!=v]
manifest=json.loads((P/'Logs/ChampionExpansion/Sources.json').read_text())
assets=[]
for asset in manifest['assets']:
    name=asset['champion'];file=P/f'Assets/_Game/LeagueVR/Champions/Art/{name}/{name}.glb'
    actual=hashlib.sha256(file.read_bytes()).hexdigest()
    assets.append({'name':name,'matches_original_import_sha256':actual==asset['sha256'],'sha256':actual})
report={'date':'2026-10-05','pre_expansion_transform_count':len(a),'current_transform_count':len(b),'changed_existing_transforms':changes,'attached_glbs':assets,'comparison':'Imported GLBs compared against original source SHA-256 recorded at initial import.'}
(P/'Logs/ChampionExpansion/ResumePreservation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(f'Existing transforms: {len(a)} checked, {len(changes)} changed. Original GLB hashes: {sum(x["matches_original_import_sha256"] for x in assets)}/6 match.')
