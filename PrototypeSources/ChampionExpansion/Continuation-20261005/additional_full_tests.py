from pathlib import Path
P=Path(r'F:\Vr\VR quirky testing gpt astra\League of legends test\league vr imoport testing')
p=P/'Assets/_Game/LeagueVR/Champions/Editor/ChampionTests.cs'
s=p.read_text()
s=s.replace('player.GetComponent<GwenVRInput>().enabled=false;player.DesktopMode=true;','player.GetComponent<GwenVRInput>().enabled=false;player.DesktopMode=run<4;')
s=s.replace('Capture(d.name+"-hands");player.DesktopMode=true;','Capture(d.name+"-hands");player.DesktopMode=run<4;')
old='if(d.id==ChampionId.Zoe)Check(target.Stunned,"Zoe: bubble transitions to sleep");'
new='''if(d.id==ChampionId.Zoe){float until=Time.time+3;bool slept=target.Stunned;while(!slept&&Time.time<until){yield return new WaitForSeconds(.1f);slept=target.Stunned;}Check(slept,"Zoe: bubble transitions to sleep after its slow");player.ResetAttackTimer();player.BasicAttack();yield return new WaitForSeconds(.3f);Check(!target.Stunned,"Zoe: basic attack wakes sleeping target");}'''
assert old in s;s=s.replace(old,new)
needle='match.ui.Close();\n                if(d.id!=ChampionId.Gwen)'
repl='''match.ui.Close();
                var selectedNext=(roster.selected+1)%7;roster.Select(selectedNext);match.ui.OpenMenu();yield return null;match.ui.Close();Check(roster.Active==d,d.name+": closing menu preserves active champion despite changed next selection");roster.Select((int)d.id);
                if(d.id!=ChampionId.Gwen)'''
assert needle in s;s=s.replace(needle,repl)
needle='var allClips=AssetDatabase.LoadAllAssetsAtPath('
repl='''if(d.id==ChampionId.Brand||d.id==ChampionId.Yunara){var hands=roster.avatar.Instance.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("original tracked hand")).ToArray();Check(hands.Length==2,d.name+": two original floating hand meshes");Check(hands.All(f=>f.sharedMesh.bounds.size.magnitude<.5f),d.name+": hand mesh bounds exclude discarded body vertices");foreach(var hand in hands){var world=hand.sharedMesh.vertices.Select(v=>hand.transform.TransformPoint(v));float reach=world.Max(v=>Vector3.Dot(v-hand.transform.parent.position,player.rightHand.forward));Check(reach>.10f&&reach<.3f,d.name+": "+hand.name+" includes complete forward fingers");}}
                    var allClips=AssetDatabase.LoadAllAssetsAtPath('''
assert needle in s;s=s.replace(needle,repl)
needle='var target=Enemy(arena+Vector3.forward*2.3f);'
repl='''Place();
                if(d.id!=ChampionId.Gwen){yield return null;Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": Gwen scissors remain hidden after menu and selection changes");}
                var target=Enemy(arena+Vector3.forward*2.3f);'''
assert needle in s;s=s.replace(needle,repl)
needle='Clear();player.ResetPractice();Place();match.MoveToFountain();'
repl='''if(d.id!=ChampionId.Gwen){
                    abilities.ResetState();Place();economy.Mana=economy.MaxMana;
                    var rack=economy.GetComponent<RiftItemRack>();economy.inventory.Clear();economy.inventory.Add(new InventorySlot(2003));economy.Recalculate();player.DesktopMode=true;yield return null;
                    Check(rack.EquipSlot(0,1),d.name+": original potion equips in right hand");roster.avatar.UpdatePose();Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": equipping potion keeps Gwen scissors hidden");
                    if(d.id==ChampionId.Aatrox||d.id==ChampionId.Akshan||d.id==ChampionId.Pantheon){string bone=d.id==ChampionId.Pantheon?"Spear":"Weapon";var weapon=roster.avatar.Instance.GetComponentsInChildren<Transform>().First(t=>t.name==bone);Check(weapon.localScale==Vector3.zero,d.name+": held item hides hand weapon");rack.ReturnHeld(1);roster.avatar.UpdatePose();Check(weapon.localScale.sqrMagnitude>0,d.name+": holstering item restores champion weapon");}else rack.ReturnHeld(1);
                    Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": holstering potion keeps Gwen scissors hidden");economy.inventory.Clear();economy.Recalculate();player.DesktopMode=run<4;
                }
                Clear();player.ResetPractice();Place();match.MoveToFountain();'''
assert needle in s;s=s.replace(needle,repl)
needle='Place();player.ResetPractice();match.Recall();'
repl='''Place();Check(!match.AtShop&&!economy.Buy(1056),d.name+": purchases are rejected outside the fountain");match.ui.OpenShop();yield return null;Check(!match.ui.ShopIsOpen,d.name+": shop screen is blocked away from fountain");match.ui.Close();player.ResetPractice();match.Recall();'''
assert needle in s;s=s.replace(needle,repl)
# Also exercise the item upgrade/crafting path once with the final roster state.
needle='Check(match.lanes.All(l=>l.points.Length>50),'
repl='''match.MoveToFountain();economy.inventory.Clear();economy.Gold=10000;economy.Recalculate();Check(economy.Buy(1043)&&economy.Buy(1026)&&economy.Buy(3108),"Item recipe components can be purchased");int price=economy.Cost(match.catalog.Find(3115),out var consumed);int gold=economy.Gold;Check(consumed.Count==3&&economy.Buy(3115)&&economy.Gold==gold-price&&economy.inventory.Count==1,"Nashor upgrade consumes components and charges remaining price");Check(economy.Sell(0)&&economy.inventory.Count==0,"Selling upgraded item removes it");
            Check(match.catalog.items.Length>180&&match.catalog.items.All(i=>i.icon),"Existing full item catalog and original icons retained");
            Check(match.lanes.All(l=>l.points.Length>50),'''
assert needle in s;s=s.replace(needle,repl)
s=s.replace('Controlled editor poses and rendered camera captures; no wearer comfort judgement or headset frame-time benchmark.','Controlled editor poses and rendered camera captures; run 3 uses desktop aiming and run 4 uses both controller origins. No wearer comfort judgement or headset frame-time benchmark.')
p.write_text(s,encoding='utf-8')
print('Expanded additional full runs with VR aiming, menu selection persistence, complete fingertips, potion/weapon restoration and shop crafting regressions.')
