from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionBuild.cs';s=p.read_text()
s=s.replace('if(d.id==ChampionId.Brand||d.id==ChampionId.Yunara)RetargetForearms(mesh,renderer);','')
start=s.index('        static void RetargetForearms(');end=s.index('        static string Clean(',start);s=s[:start]+s[end:]
old='if(hand||arm||finger)arms.Add(i);'
new='if(hand||finger||((d.id!=ChampionId.Brand&&d.id!=ChampionId.Yunara)&&arm))arms.Add(i);'
assert old in s;s=s.replace(old,new);p.write_text(s)
print('Brand and Yunara use tracked original hand meshes; other champion arm views retained.')
