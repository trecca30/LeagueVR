from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
backup=P/'PrototypeBackups/ChampionResume-20261005'
for name in ['ChampionBuild.cs','ChampionVRAvatar.cs','ChampionTests.cs']:
    directory='Runtime' if name=='ChampionVRAvatar.cs' else 'Editor'
    src=P/'Assets/_Game/LeagueVR/Champions'/directory/name
    backup.mkdir(parents=True,exist_ok=True)
    shutil.copy2(src,backup/name)
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionBuild.cs'
s=p.read_text()
s=s.replace('else if(request=="Test2")ChampionTests.Begin(2);','else if(request=="Test2")ChampionTests.Begin(2);else if(request=="Test3")ChampionTests.Begin(3);else if(request=="Test4")ChampionTests.Begin(4);')
needle='var palm=bones.First(b=>string.Equals(b.name,side+"_Hand",StringComparison.OrdinalIgnoreCase));var handMesh='
replacement='''var palm=bones.First(b=>string.Equals(b.name,side+"_Hand",StringComparison.OrdinalIgnoreCase));
                foreach(var bone in bones.Where(b=>b.name.StartsWith(side+"_",StringComparison.OrdinalIgnoreCase)&&new[]{"hand","middle","index","pinky"}.Any(n=>b.name.ToLowerInvariant().Contains(n))))log.AppendLine(champion+" "+bone.name+" palm local="+palm.InverseTransformPoint(bone.position).ToString("F4"));
                foreach(string part in new[]{"hand","middle","index","pinky","thumb","elbow"}){
                    var indices=Enumerable.Range(0,weights.Length).Where(i=>bones[weights[i].boneIndex0].name.StartsWith(side+"_",StringComparison.OrdinalIgnoreCase)&&bones[weights[i].boneIndex0].name.ToLowerInvariant().Contains(part)).ToArray();
                    if(indices.Length>0){var mean=indices.Aggregate(Vector3.zero,(v,i)=>v+world[i])/indices.Length;log.AppendLine(champion+" "+side+" weighted "+part+" mean="+palm.InverseTransformPoint(mean).ToString("F4")+" count="+indices.Length);}
                }
                var handMesh='''
assert needle in s
s=s.replace(needle,replacement)
p.write_text(s,encoding='utf-8')
print('Saved resume backup and hand anatomy diagnostics; added full test runs 3 and 4.')
