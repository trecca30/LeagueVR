from pathlib import Path
import shutil,hashlib,json,urllib.request,struct
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
S=Path(r'C:\Users\Kakad\AppData\Local\Temp\LeagueChampions')
backup=P/'PrototypeBackups/ChampionExpansion-20261004'
for rel in ['Assets/Scenes/LeagueVR.unity','Assets/_Game/LeagueVR/Runtime/GwenAbilities.cs','Assets/_Game/LeagueVR/Runtime/GwenFeedback.cs','Assets/_Game/LeagueVR/Runtime/GwenAudio.cs','Assets/_Game/LeagueVR/Runtime/GwenVRInput.cs','Assets/_Game/LeagueVR/Runtime/Combatant.cs','Assets/_Game/LeagueVR/Match/Runtime/RiftUI.cs','Assets/_Game/LeagueVR/Match/Runtime/RiftEconomy.cs','Assets/_Game/LeagueVR/Match/Runtime/RiftMatch.cs','Assets/_Game/LeagueVR/Match/Runtime/RiftMinion.cs','Assets/_Game/LeagueVR/Match/Runtime/RiftVRHUD.cs']:
    target=backup/rel
    if not target.exists(): target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(P/rel,target)
art=P/'Assets/_Game/LeagueVR/Champions/Art';art.mkdir(parents=True,exist_ok=True)
log=P/'Logs/ChampionExpansion';log.mkdir(parents=True,exist_ok=True)
shutil.copy2(S/'asset-inspection.json',log/'asset-inspection.json')
versions=json.load(urllib.request.urlopen('https://ddragon.leagueoflegends.com/api/versions.json',timeout=30));version=versions[0]
manifest={'patch':version,'assets':[]}
for name in ['Gwen','Zoe','Aatrox','Akshan','Brand','Pantheon','Yunara']:
    dest=art/name;dest.mkdir(exist_ok=True)
    if name!='Gwen':
        src=Path('C:/Users/Kakad/Desktop')/(name.lower()+'.glb');out=dest/(name+'.glb');shutil.copy2(src,out)
        manifest['assets'].append({'champion':name,'source':str(src),'sha256':hashlib.sha256(out.read_bytes()).hexdigest()})
    url=f'https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion/{name}.json'
    b=urllib.request.urlopen(url,timeout=30).read();(dest/(name+'.json')).write_bytes(b)
    data=json.loads(b)['data'][name]
    for filename in [name+'.png']+[x['image']['full'] for x in data['spells']]+[data['passive']['image']['full']]:
        category='champion' if filename==name+'.png' else 'passive' if filename==data['passive']['image']['full'] else 'spell'
        if not (dest/filename).exists():(dest/filename).write_bytes(urllib.request.urlopen(f'https://ddragon.leagueoflegends.com/cdn/{version}/img/{category}/{filename}',timeout=30).read())
    print(name,data['stats'],[(x['name'],x['cooldown'],x['cost']) for x in data['spells']])
(log/'Sources.json').write_text(json.dumps(manifest,indent=2))
print('Imported six original GLBs; Data Dragon',version)
