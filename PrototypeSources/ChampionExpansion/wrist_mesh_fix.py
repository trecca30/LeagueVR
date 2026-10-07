from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionBuild.cs';s=p.read_text();old='n.Contains("uparm")||n.Contains("shoulder");';new='n.Contains("uparm")||n.Contains("shoulder")||n.Contains("arm_twist")||n.Contains("hand_twist")||n.EndsWith("_bracelet");';assert old in s;s=s.replace(old,new);p.write_text(s)
shutil.copy2(P/'Logs/ChampionExpansion/Build.txt',P/'Logs/ChampionExpansion/Build-before-wrist-fix.txt')
print('Retain weighted wrist twist and bracelet joints in derived first-person meshes.')
