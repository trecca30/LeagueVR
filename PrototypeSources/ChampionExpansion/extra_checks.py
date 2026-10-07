from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing/Assets/_Game/LeagueVR/Champions/Editor/ChampionTests.cs')
s=P.read_text(encoding='utf-8')
s=s.replace('SessionState.SetInt("Champions.Pending",run);EditorApplication.isPlaying=true;','SessionState.SetInt("Champions.Pending",run);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;')
old='player.Health.ClearCrowdControl();\n                }'
assert old in s
s=s.replace(old,'''player.Health.ClearCrowdControl();
                    if(d.id==ChampionId.Brand){target.ResetHealth();var burn=target.GetComponent<ChampionDebuff>();if(!burn)burn=target.gameObject.AddComponent<ChampionDebuff>();burn.Blaze(abilities);burn.Blaze(abilities);burn.Blaze(abilities);hp=target.Health;yield return new WaitForSeconds(2.15f);Check(target.Health<hp-300,"Brand: three burn stacks detonate a champion");}
                    if(d.id==ChampionId.Pantheon){for(int n=0;n<5;n++){player.ResetAttackTimer();player.BasicAttack();}Check(abilities.Stacks==5,"Pantheon: five attacks build Mortal Will");player.CastQ();Check(abilities.Stacks==0,"Pantheon: empowered spear consumes Mortal Will");}
                    if(d.id==ChampionId.Yunara){abilities.ResetState();economy.CriticalChance=1;target.ResetHealth();hp=target.Health;player.ResetAttackTimer();player.BasicAttack();yield return new WaitForSeconds(.3f);Check(target.Health<hp-ADForTest()*economy.CriticalDamage,"Yunara: critical hit adds passive magic damage");economy.Recalculate();}
                }''')
P.write_text(s,encoding='utf-8');print('Added combo checks; test startup will clear the editor pause')
