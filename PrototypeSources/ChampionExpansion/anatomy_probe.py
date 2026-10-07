from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionHands.cs';s=p.read_text()
old='float error=0;var headRotation=p.head.transform.rotation;'
new='''if(roster.Active.id==ChampionId.Brand||roster.Active.id==ChampionId.Yunara)
                {
                    var full=Instantiate(roster.Active.model);full.transform.SetPositionAndRotation(arena+Vector3.forward*2.5f,Quaternion.Euler(0,180,0));roster.Active.idle.SampleAnimation(full,0);
                    foreach(var renderer in roster.avatar.Instance.GetComponentsInChildren<Renderer>())renderer.forceRenderingOff=true;
                    camera.transform.rotation=Quaternion.identity;ChampionBuild.Capture(camera,"Source-"+roster.Active.name+"-anatomy");Destroy(full);
                    foreach(var bone in roster.avatar.Instance.GetComponentsInChildren<Transform>())if(new[]{"l_forearm","r_forearm","l_elbow","r_elbow","l_hand","r_hand","l_hand_twist","r_hand_twist","l_arm_twist","r_arm_twist"}.Contains(bone.name.ToLowerInvariant()))results.Add(roster.Active.name+" "+bone.name+" position="+bone.position+" local="+bone.localPosition+" parent="+bone.parent.name);
                }
                float error=0;var headRotation=p.head.transform.rotation;'''
assert old in s;s=s.replace(old,new);p.write_text(s)
print('Added source model anatomy captures and wrist joint diagnostics.')
