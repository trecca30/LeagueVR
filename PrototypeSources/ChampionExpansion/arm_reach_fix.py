from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Runtime/ChampionVRAvatar.cs';s=p.read_text()
old='public Arm(Transform u,Transform l,Transform p){upper=u;lower=l;palm=p;lowerPos=l.localPosition;palmPos=p.localPosition;upperRot=u.localRotation;lowerRot=l.localRotation;palmRot=p.localRotation;align=Alignment(p);}'
new='''public readonly List<(Transform bone,Vector3 position)> helpers=new();
            public Arm(Transform u,Transform l,Transform p){upper=u;lower=l;palm=p;lowerPos=l.localPosition;palmPos=p.localPosition;upperRot=u.localRotation;lowerRot=l.localRotation;palmRot=p.localRotation;align=Alignment(p);
                foreach(var bone in lower.GetComponentsInChildren<Transform>())if(bone!=lower&&!bone.IsChildOf(palm))helpers.Add((bone,bone.localPosition));}'''
assert old in s;s=s.replace(old,new)
s=s.replace('Mathf.Clamp(delta.magnitude/(x+y)*1.003f,1,1.5f)','Mathf.Clamp(delta.magnitude/(x+y)*1.003f,1,4f)')
s=s.replace('a.palm.localPosition*=stretch;x*=stretch;y*=stretch;', 'a.palm.localPosition*=stretch;foreach(var helper in a.helpers)helper.bone.localPosition=helper.position*stretch;x*=stretch;y*=stretch;')
p.write_text(s)
print('Stretch controller arm chains and their wrist helper bones together.')
