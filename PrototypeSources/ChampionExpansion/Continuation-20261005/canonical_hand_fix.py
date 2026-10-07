from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionBuild.cs';s=p.read_text()
old='var baked=new Mesh();renderer.BakeMesh(baked);var source=renderer.sharedMesh;var bones=renderer.bones;var weights=source.boneWeights;'
new='var source=renderer.sharedMesh;var baked=Object.Instantiate(source);var bones=renderer.bones;var weights=source.boneWeights;var bindposes=source.bindposes;'
assert old in s;s=s.replace(old,new)
old='''var middle=bones.First(b=>b.name.StartsWith(side+"_Middle",StringComparison.OrdinalIgnoreCase));var forward=(middle.position-palm.position).normalized;'''
new='''Vector3 RestPosition(Transform bone)=>renderer.transform.TransformPoint(bindposes[Array.IndexOf(bones,bone)].inverse.MultiplyPoint3x4(Vector3.zero));
                var middle=bones.First(b=>b.name.StartsWith(side+"_Middle",StringComparison.OrdinalIgnoreCase));var indexFinger=bones.First(b=>b.name.StartsWith(side+"_Index",StringComparison.OrdinalIgnoreCase));var pinky=bones.First(b=>b.name.StartsWith(side+"_Pinky",StringComparison.OrdinalIgnoreCase));
                var wrist=RestPosition(palm);var forward=(RestPosition(middle)-wrist).normalized;var up=Vector3.Cross(RestPosition(indexFinger)-RestPosition(pinky),forward).normalized;if(Vector3.Dot(up,Vector3.up)<0)up=-up;var frame=Quaternion.LookRotation(forward,up);var inverseFrame=Quaternion.Inverse(frame);'''
assert old in s;s=s.replace(old,new)
s=s.replace('vertices.Add(palm.InverseTransformPoint(v.position));normalList.Add(palm.InverseTransformDirection(v.normal).normalized);','vertices.Add(inverseFrame*(v.position-wrist));normalList.Add((inverseFrame*v.normal).normalized);')
s=s.replace('Vector3.Distance(centre,palm.position)>.27f','Vector3.Distance(centre,wrist)>.27f')
s=s.replace('Vector3.Dot(first.position-palm.position,forward)','Vector3.Dot(first.position-wrist,forward)')
s=s.replace('Vector3.Dot(next.position-palm.position,forward)','Vector3.Dot(next.position-wrist,forward)')
s=s.replace('hand.transform.SetParent(palm,false);hand.AddComponent<MeshFilter>()','hand.transform.SetParent(palm,false);hand.transform.localRotation=Quaternion.Inverse(ChampionVRAvatar.Alignment(palm));hand.AddComponent<MeshFilter>()')
s=s.replace('// Keep the sampled hand shape, clip at the wrist, and close the cut. Finger tips','// Use the original bind-pose shape in controller space, clip at the wrist, and close the cut. Finger tips')
s=s.replace(' complete hand triangles=',' neutral hand triangles=')
p.write_text(s,encoding='utf-8')
p=P/'Assets/_Game/LeagueVR/Champions/Runtime/ChampionVRAvatar.cs';s=p.read_text();assert 'static Quaternion Alignment(' in s;s=s.replace('static Quaternion Alignment(','public static Quaternion Alignment(');p.write_text(s,encoding='utf-8')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionTests.cs';s=p.read_text()
old='if(d.id==ChampionId.Brand){target.ResetHealth();var burn='
new='if(d.id==ChampionId.Brand){yield return null;target.ResetHealth();var burn='
assert old in s;s=s.replace(old,new)
# Launch away from the nearby target, then redirect at it; a destroyed star cannot be redirected.
old='hp=target.Health;Check(player.CastQ(),d.name+": Q cast");if(d.id==ChampionId.Zoe){yield return new WaitForSeconds(.15f);Check(player.CastQ(),"Zoe: star redirects on second Q");}'
new='''hp=target.Health;if(d.id==ChampionId.Zoe){player.head.transform.rotation=player.rightHand.rotation=Quaternion.Euler(-30,30,0);}Check(player.CastQ(),d.name+": Q cast");if(d.id==ChampionId.Zoe){yield return new WaitForSeconds(.1f);var star=FindObjectsByType<ChampionProjectile>(FindObjectsSortMode.None).FirstOrDefault(p=>p.source==abilities&&p.ability=="Q");var direction=star?(target.AimPosition-star.transform.position).normalized:Vector3.forward;player.head.transform.rotation=player.rightHand.rotation=Quaternion.LookRotation(direction);Check(player.CastQ(),"Zoe: star redirects on second Q");}'''
assert old in s;s=s.replace(old,new)
p.write_text(s,encoding='utf-8')
print('Extracted neutral original hand geometry in controller space; corrected deferred-cleanup Brand fixture and Zoe redirect setup.')
