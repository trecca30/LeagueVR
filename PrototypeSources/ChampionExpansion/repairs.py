from pathlib import Path
import shutil
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing');S=Path(r'C:\Users\Kakad\AppData\Local\Temp\LeagueChampions');R=P/'Assets/_Game/LeagueVR'
shutil.copy2(S/'ChampionVRAvatar.cs',R/'Champions/Runtime/ChampionVRAvatar.cs')
p=R/'Champions/Runtime/ChampionAbilities.cs';s=p.read_text(encoding='utf-8')
s=s.replace('public bool Camouflaged=>Definition&&Definition.id==ChampionId.Akshan&&Time.time<stealthUntil&&!Around(Player.Feet,4).Any(t=>t.countsAsChampion);','public bool Camouflaged=>Definition&&Definition.id==ChampionId.Akshan&&Time.time<stealthUntil&&!Around(Player.Feet,4).Any(t=>t.countsAsChampion)&&!(RiftMatch.Instance?.structures.Any(t=>t.health.team!=Player.Health.team&&t.health.IsAlive&&GwenAbilities.FlatDistance(t.transform.position,Player.Feet)<t.range)??false);')
s=s.replace('owned.RemoveAll(go=>!go);','owned.RemoveAll(go=>!go);')
s=s.replace('Player.LeftHand','Player.leftHand')
p.write_text(s,encoding='utf-8')
p=R/'Runtime/Combatant.cs';s=p.read_text(encoding='utf-8').replace('public bool IsTargetable => IsAlive &&','public bool IsTargetable => IsAlive && !(GetComponent<LeagueVR.Champions.ChampionAbilities>()?.Camouflaged ?? false) &&');p.write_text(s,encoding='utf-8')
p=R/'Champions/Editor/ChampionTests.cs';s=p.read_text(encoding='utf-8')
s=s.replace('b.key.StartsWith("champion-")','b.key.StartsWith("champion-")&&b.key!="champion-guide"')
s=s.replace('arena=match.lanes[1].points[Math.Min(10,match.lanes[1].points.Length-1)];','''arena=match.lanes[1].points.First(point=>
            {
                if(match.structures.Any(t=>GwenAbilities.FlatDistance(t.transform.position,point)<9))return false;
                for(int n=0;n<12;n++){float a=n*Mathf.PI/6;var test=point+new Vector3(Mathf.Cos(a)*6,.3f,Mathf.Sin(a)*6);if(!match.Ground(test,out var ground)||Mathf.Abs(ground.y-point.y)>.6f||Physics.CheckSphere(ground+Vector3.up*.6f,.3f,player.worldMask,QueryTriggerInteraction.Ignore))return false;}return true;
            });
            File.WriteAllText("Logs/ChampionExpansion/Run"+run+"-arena.txt",arena.ToString("F3"));''')
s=s.replace('if(d.id==ChampionId.Brand)AimGround();hp=target.Health;', 'if(d.id==ChampionId.Brand){AimGround();abilities.GroundAim(player.AttackOrigin,player.AttackDirection,12,out var at);target.transform.position=at;Physics.SyncTransforms();}if(d.id==ChampionId.Akshan){target.transform.position=arena+Vector3.forward*7;Physics.SyncTransforms();}hp=target.Health;')
s=s.replace('player.Health.TakeDamage(new DamageHit(target,player.Feet-Vector3.forward*3,50,DamageKind.Physical));','target.transform.position=player.Feet-Vector3.forward*3;Physics.SyncTransforms();player.Health.TakeDamage(new DamageHit(target,target.AimPosition,50,DamageKind.Physical));')
s=s.replace('economy.Buy(match.catalog.Find(1056))','economy.Buy(1056)')
p.write_text(s,encoding='utf-8')
print('Weapon pose and test fixture repairs installed')
