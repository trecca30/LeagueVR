using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using TMPro;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Champions.Editor
{
    [InitializeOnLoad]public static class ChampionTests
    {
        static ChampionTests(){EditorApplication.playModeStateChanged+=State;}
        public static void Begin(int run){if(EditorApplication.isPlaying)throw new Exception("Start tests from edit mode");if(SessionState.GetBool("Champions.FullRun"+run,false)&&File.Exists("Logs/ChampionExpansion/Run"+run+".md"))throw new Exception("Completed run already used");SessionState.SetBool("Champions.FullRun"+run,true);SessionState.SetInt("Champions.Pending",run);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        static void State(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode){int run=SessionState.GetInt("Champions.Pending",0);if(run>0){SessionState.SetInt("Champions.Pending",0);new GameObject("Champion full test "+run).AddComponent<ChampionProbe>().run=run;}}}
    }
    public class ChampionProbe:MonoBehaviour
    {
        public int run;RiftMatch match;GwenAbilities player;ChampionRoster roster;ChampionAbilities abilities;RiftEconomy economy;Camera camera;
        List<string> passes=new(),failures=new(),errors=new();List<GameObject> fixtures=new();Vector3 arena;
        bool done;int layer;float timer;
        void Check(bool condition,string label){(condition?passes:failures).Add(label);File.WriteAllText("Logs/ChampionExpansion/Run"+run+"-progress.txt",label+"\n"+passes.Count+" passed / "+failures.Count+" failed");}
        IEnumerator Start(){Application.logMessageReceived+=Log;yield return null;yield return null;var suite=Suite();while(true){bool more;object current=null;try{more=suite.MoveNext();if(more)current=suite.Current;}catch(Exception e){failures.Add("Test exception: "+e);break;}if(!more)break;yield return current;}Finish();}
        void Log(string msg,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(msg+"\n"+stack);}
        Combatant Enemy(Vector3 at,string name="Champion ability target")
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);fixtures.Add(go);go.name=name;go.layer=layer;go.transform.position=at;go.transform.localScale=new Vector3(.65f,1,.65f);var collider=go.GetComponent<CapsuleCollider>();collider.center=Vector3.up*.85f;
            var h=go.AddComponent<Combatant>();h.team=1-player.Health.team;h.maxHealth=5000;h.armor=h.magicResistance=0;h.ResetHealth();var aim=new GameObject("Aim").transform;aim.SetParent(go.transform,false);aim.localPosition=Vector3.up*1.1f;h.aimPoint=aim;
            go.GetComponent<Renderer>().sharedMaterial=match.redMaterial;Physics.SyncTransforms();return h;
        }
        void Clear(){foreach(var go in fixtures)if(go){go.SetActive(false);Destroy(go);}fixtures.Clear();foreach(var t in FindObjectsByType<ChampionDebuff>(FindObjectsSortMode.None))Destroy(t);}
        void Place()
        {
            player.origin.transform.position=arena;player.head.transform.position=arena+Vector3.up*1.65f;player.head.transform.rotation=Quaternion.identity;
            player.leftHand.SetPositionAndRotation(player.head.transform.position+new Vector3(-.24f,-.35f,.45f),Quaternion.identity);player.rightHand.SetPositionAndRotation(player.head.transform.position+new Vector3(.24f,-.35f,.45f),Quaternion.identity);Physics.SyncTransforms();
        }
        void AimGround(){player.head.transform.rotation=Quaternion.Euler(15,0,0);player.leftHand.rotation=player.rightHand.rotation=Quaternion.Euler(15,0,0);}
        void Capture(string name){camera.transform.SetPositionAndRotation(player.head.transform.position,player.head.transform.rotation*(name.EndsWith("-hands")?Quaternion.Euler(12,0,0):Quaternion.identity));ChampionBuild.Capture(camera,"Run"+run+"-"+name);}
        IEnumerator Suite()
        {
            timer=Time.realtimeSinceStartup;match=Object.FindAnyObjectByType<RiftMatch>();player=match.player;roster=player.GetComponent<ChampionRoster>();abilities=roster.abilities;economy=match.economy;
            Check(roster&&roster.champions.Length==7,"Seven champions initialize in existing LeagueVR scene");
            var poses=player.origin.GetComponentsInChildren<TrackedPoseDriver>(true);Check(poses.Length>=3&&poses.All(p=>p.enabled&&p.updateType==TrackedPoseDriver.UpdateType.UpdateAndBeforeRender),"Existing XR tracking and before-render updates intact");
            Check(player.origin.GetComponent<CharacterController>()&&player.GetComponent<RiftPlayerView>(),"XR origin, collision movement and controller suppression retained");
            foreach(var pose in poses)pose.enabled=false;foreach(var c in player.origin.GetComponentsInChildren<MonoBehaviour>(true))if(c&&c.GetType().Namespace!=null&&c.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion"))c.enabled=false;
            player.GetComponent<GwenVRInput>().enabled=false;player.DesktopMode=run<4;var cc=player.origin.GetComponent<CharacterController>();cc.enabled=false;
            for(int i=0;i<32;i++)if((player.combatMask.value&(1<<i))!=0){layer=i;break;}
            foreach(var s in match.structures)s.enabled=false;foreach(var o in match.objectives)o.enabled=false;foreach(var f in match.fountains)f.enabled=false;economy.enabled=false;
            camera=new GameObject("Champion audit camera").AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.025f;camera.farClipPlane=350;camera.fieldOfView=80;
            arena=match.lanes[1].points.First(point=>
            {
                if(match.structures.Any(t=>GwenAbilities.FlatDistance(t.transform.position,point)<9))return false;
                for(int n=0;n<12;n++){float a=n*Mathf.PI/6;var test=point+new Vector3(Mathf.Cos(a)*6,.3f,Mathf.Sin(a)*6);if(!match.Ground(test,out var ground)||Mathf.Abs(ground.y-point.y)>.6f||Physics.CheckSphere(ground+Vector3.up*.6f,.3f,player.worldMask,QueryTriggerInteraction.Ignore))return false;}return true;
            });
            File.WriteAllText("Logs/ChampionExpansion/Run"+run+"-arena.txt",arena.ToString("F3"));
            foreach(var d in roster.champions)
            {
                match.ui.OpenMenu();yield return null;var choice=match.ui.Buttons.First(b=>b.key=="champion-"+d.id);choice.Click();Check(roster.Selected==d,d.name+": menu card selects correct champion");match.Play();typeof(RiftMatch).GetProperty("NextWave").SetValue(match,100000f);Place();yield return null;
                Check(player.ChampionName==d.name&&roster.Active==d,"Select and start "+d.name);Check(economy.Gold==10000,d.name+": new match starts with 10,000 gold");Check(Mathf.Abs(player.Health.maxHealth-d.health)<.1f,d.name+": base HP from champion profile");
                Check(d.portrait&&d.spells.Length==4&&d.spells.All(s=>s.icon)&&d.passiveIcon,d.name+": portrait and five original ability icons");
                match.ui.OpenMenu();yield return null;Check(match.ui.Buttons.Count(b=>b.key.StartsWith("champion-")&&b.key!="champion-guide")==7,d.name+": all seven menu choices");
                Check(match.ui.Buttons.First(b=>b.key=="champion-guide").Click(),d.name+": ability guide opens");yield return null;Check(match.ui.CurrentScreen=="Champion ability guide",d.name+": guide screen and descriptions");match.ui.OpenMenu();yield return null;Capture(d.name+"-menu");
                var texts=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeInHierarchy&&t.text.Length>0).ToArray();Check(texts.All(t=>t.fontSharedMaterial&&t.fontSharedMaterial.shader.isSupported),d.name+": UI text uses supported materials");
                match.ui.Close();
                var selectedNext=(roster.selected+1)%7;roster.Select(selectedNext);match.ui.OpenMenu();yield return null;match.ui.Close();Check(roster.Active==d,d.name+": closing menu preserves active champion despite changed next selection");roster.Select((int)d.id);
                if(d.id!=ChampionId.Gwen)
                {
                    Check(roster.avatar.Instance&&roster.avatar.LeftPalm&&roster.avatar.RightPalm,d.name+": both skinned champion hands bound");Check(!player.GetComponent<GwenAvatar>().enabled&&!player.GetComponent<GwenAvatar>().visualRoot.gameObject.activeSelf,d.name+": Gwen visual and arm writer disabled");
                    Check(roster.avatar.Instance.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m&&m.mainTexture)),d.name+": every material has an imported base texture");
                    var before=player.head.transform.rotation;player.DesktopMode=false;float worst=0;
                    for(int pose=0;pose<120;pose++){float t=pose*.08f;player.leftHand.position=player.head.transform.position+new Vector3(-.3f,-.24f+Mathf.Sin(t)*.15f,.4f);player.rightHand.position=player.head.transform.position+new Vector3(.3f,-.24f+Mathf.Cos(t)*.15f,.4f);player.leftHand.rotation=Quaternion.Euler(0,0,pose*2);roster.avatar.UpdatePose();worst=Mathf.Max(worst,Vector3.Distance(roster.avatar.LeftPalm.position,player.leftHand.TransformPoint(new Vector3(0,-.025f,-.015f))),Vector3.Distance(roster.avatar.RightPalm.position,player.rightHand.TransformPoint(new Vector3(0,-.025f,-.015f))));}
                    Check(worst<.001f,d.name+": 120 hand poses, error "+worst.ToString("F6")+" m");Check(Quaternion.Angle(before,player.head.transform.rotation)<.0001f,d.name+": hands never rotate head camera");
                    Place();roster.avatar.UpdatePose();yield return null;Capture(d.name+"-hands");player.DesktopMode=run<4;
                    if(d.id==ChampionId.Brand||d.id==ChampionId.Yunara){var hands=roster.avatar.Instance.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.Contains("original tracked hand")).ToArray();Check(hands.Length==2,d.name+": two original floating hand meshes");Check(hands.All(f=>f.sharedMesh.bounds.size.magnitude<.5f),d.name+": hand mesh bounds exclude discarded body vertices");foreach(var hand in hands){var world=hand.sharedMesh.vertices.Select(v=>hand.transform.TransformPoint(v));float reach=world.Max(v=>Vector3.Dot(v-hand.transform.parent.position,player.rightHand.forward));Check(reach>.10f&&reach<.3f,d.name+": "+hand.name+" includes complete forward fingers");}}
                    var allClips=AssetDatabase.LoadAllAssetsAtPath("Assets/_Game/LeagueVR/Champions/Art/"+d.name+"/"+d.name+".glb").OfType<AnimationClip>().ToArray();Check(allClips.Length>5,d.name+": original animation library retained ("+allClips.Length+")");
                }
                Place();
                if(d.id!=ChampionId.Gwen){yield return null;Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": Gwen scissors remain hidden after menu and selection changes");}
                var target=Enemy(arena+Vector3.forward*2.3f);if(d.id==ChampionId.Aatrox)player.Health.SetHealth(player.Health.maxHealth*.5f);float selfHP=player.Health.Health;float hp=target.Health;economy.Mana=economy.MaxMana;player.ResetAttackTimer();Check(player.BasicAttack(),d.name+": basic attack accepts input");yield return new WaitForSeconds(.5f);Check(target.Health<hp,d.name+": basic attack damages enemy");if(d.id==ChampionId.Aatrox)Check(player.Health.Health>selfHP,"Aatrox: passive and champion damage heal");
                if(d.id==ChampionId.Gwen)
                {
                    Check(player.CastQ(),"Gwen: Q snips cast");yield return new WaitForSeconds(.8f);Check(player.CastW()&&player.MistActive,"Gwen: protective mist remains functional");Check(player.CastR(),"Gwen: needle volley cast");yield return new WaitForSeconds(.3f);AimGround();Check(player.CastE(),"Gwen: dash collision check");player.ResetPractice();
                }
                else
                {
                    abilities.ResetState();Place();target.transform.position=arena+Vector3.forward*2.3f;target.ResetHealth();Physics.SyncTransforms();economy.Mana=economy.MaxMana;
                    if(d.id==ChampionId.Yunara){for(int n=0;n<8;n++){player.ResetAttackTimer();player.BasicAttack();yield return new WaitForSeconds(.18f);}Check(abilities.Stacks==8,"Yunara: eight attacks build Spirit");}
                    hp=target.Health;if(d.id==ChampionId.Zoe){player.head.transform.rotation=player.rightHand.rotation=Quaternion.Euler(-30,30,0);}Check(player.CastQ(),d.name+": Q cast");if(d.id==ChampionId.Zoe){yield return new WaitForSeconds(.1f);var star=FindObjectsByType<ChampionProjectile>(FindObjectsSortMode.None).FirstOrDefault(p=>p.source==abilities&&p.ability=="Q");var direction=star?(target.AimPosition-star.transform.position).normalized:Vector3.forward;player.head.transform.rotation=player.rightHand.rotation=Quaternion.LookRotation(direction);Check(player.CastQ(),"Zoe: star redirects on second Q");}if(d.id==ChampionId.Aatrox){yield return new WaitForSeconds(.35f);Check(player.CastQ(),"Aatrox: Q2 recast");yield return new WaitForSeconds(.35f);Check(player.CastQ(),"Aatrox: Q3 recast");}
                    yield return new WaitForSeconds(.7f);Check(d.id==ChampionId.Yunara||target.Health<hp,d.name+": Q damage or empowered state");Check(d.id==ChampionId.Zoe||d.id==ChampionId.Yunara||!player.CastQ(),d.name+": Q cooldown blocks repeat");
                    abilities.ResetState();Place();economy.Mana=economy.MaxMana;target.transform.position=arena+Vector3.forward*2.3f;target.ResetHealth();Physics.SyncTransforms();
                    if(d.id==ChampionId.Zoe){var shard=new GameObject("Test spell shard");fixtures.Add(shard);shard.transform.position=player.Feet;var s=shard.AddComponent<SpellShard>();s.owner=abilities;s.kind=0;s.expires=Time.time+20;}
                    if(d.id==ChampionId.Brand){AimGround();abilities.GroundAim(player.AttackOrigin,player.AttackDirection,12,out var at);target.transform.position=at;Physics.SyncTransforms();}if(d.id==ChampionId.Akshan){target.transform.position=arena+Vector3.forward*7;Physics.SyncTransforms();}hp=target.Health;Check(player.CastW(),d.name+": W cast");yield return new WaitForSeconds(1.8f);
                    if(d.id==ChampionId.Brand||d.id==ChampionId.Aatrox||d.id==ChampionId.Pantheon||d.id==ChampionId.Yunara)Check(target.Health<hp,d.name+": W effect damages target");
                    if(d.id==ChampionId.Akshan)Check(abilities.Camouflaged&&!player.Health.IsTargetable,"Akshan: W enters camouflage and hides from minion targeting");
                    abilities.ResetState();Place();economy.Mana=economy.MaxMana;target.transform.position=arena+Vector3.forward*2.3f;target.ResetHealth();Physics.SyncTransforms();
                    GameObject wall=null;if(d.id==ChampionId.Akshan){wall=GameObject.CreatePrimitive(PrimitiveType.Cube);fixtures.Add(wall);wall.layer=FirstLayer(player.worldMask);wall.transform.position=player.AttackOrigin+Vector3.forward*5;wall.transform.localScale=new Vector3(2,3,.2f);Physics.SyncTransforms();}
                    var rotation=player.head.transform.rotation;Check(player.CastE(),d.name+": E cast");yield return new WaitForSeconds(d.id==ChampionId.Zoe?1.4f:.3f);Check(Quaternion.Angle(rotation,player.head.transform.rotation)<.0001f,d.name+": E does not rotate POV");
                    if(d.id==ChampionId.Zoe){float until=Time.time+3;bool slept=target.Stunned;while(!slept&&Time.time<until){yield return new WaitForSeconds(.1f);slept=target.Stunned;}Check(slept,"Zoe: bubble transitions to sleep after its slow");player.ResetAttackTimer();player.BasicAttack();yield return new WaitForSeconds(.3f);Check(!target.Stunned,"Zoe: basic attack wakes sleeping target");}if(d.id==ChampionId.Pantheon){var h=player.Health.Health;player.Health.TakeDamage(new DamageHit(target,player.Feet+Vector3.forward*3,50,DamageKind.Physical));Check(player.Health.Health==h,"Pantheon: raised shield blocks frontal damage");target.transform.position=player.Feet-Vector3.forward*3;Physics.SyncTransforms();player.Health.TakeDamage(new DamageHit(target,target.AimPosition,50,DamageKind.Physical));Check(player.Health.Health<h,"Pantheon: rear attacks pass shield");}
                    if(wall){wall.SetActive(false);Destroy(wall);}abilities.ResetState();Place();economy.Mana=economy.MaxMana;target.transform.position=arena+Vector3.forward*2.3f;target.ResetHealth();Physics.SyncTransforms();
                    if(d.id==ChampionId.Zoe||d.id==ChampionId.Pantheon)AimGround();Vector3 feet=player.Feet;rotation=player.head.transform.rotation;hp=target.Health;Check(player.CastR(),d.name+": R cast");yield return new WaitForSeconds(1.4f);Check(Quaternion.Angle(rotation,player.head.transform.rotation)<.0001f,d.name+": ultimate never forces camera rotation");
                    if(d.id==ChampionId.Zoe)Check(GwenAbilities.FlatDistance(player.Feet,feet)<.05f,"Zoe: portal returns to starting location");if(d.id==ChampionId.Aatrox||d.id==ChampionId.Yunara)Check(abilities.Transcendent,d.name+": ultimate state active");if(d.id==ChampionId.Brand||d.id==ChampionId.Akshan)Check(target.Health<hp,d.name+": ultimate damage applied");
                    Check(!player.CastR(),d.name+": ultimate cooldown prevents repeat");
                    abilities.ResetState();Place();economy.Mana=0;Check(!d.UsesMana||!player.CastQ(),d.name+": mana gating respects resource type");economy.Mana=economy.MaxMana;
                    player.Health.ApplyStun(1);Check(!player.CastQ()&&!player.BasicAttack(),d.name+": crowd control blocks combat actions");player.Health.ClearCrowdControl();
                    if(d.id==ChampionId.Brand){yield return null;target.ResetHealth();var burn=target.GetComponent<ChampionDebuff>();if(!burn)burn=target.gameObject.AddComponent<ChampionDebuff>();burn.Blaze(abilities);burn.Blaze(abilities);burn.Blaze(abilities);hp=target.Health;yield return new WaitForSeconds(2.15f);Check(target.Health<hp-300,"Brand: three burn stacks detonate a champion");}
                    if(d.id==ChampionId.Pantheon){for(int n=0;n<5;n++){player.ResetAttackTimer();player.BasicAttack();}Check(abilities.Stacks==5,"Pantheon: five attacks build Mortal Will");player.CastQ();Check(abilities.Stacks==0,"Pantheon: empowered spear consumes Mortal Will");}
                    if(d.id==ChampionId.Yunara){abilities.ResetState();economy.CriticalChance=1;target.ResetHealth();hp=target.Health;player.ResetAttackTimer();player.BasicAttack();yield return new WaitForSeconds(.3f);Check(target.Health<hp-ADForTest()*economy.CriticalDamage,"Yunara: critical hit adds passive magic damage");economy.Recalculate();}
                }
                if(d.id!=ChampionId.Gwen){
                    abilities.ResetState();Place();economy.Mana=economy.MaxMana;
                    var rack=economy.GetComponent<RiftItemRack>();economy.inventory.Clear();economy.inventory.Add(new InventorySlot(2003));economy.Recalculate();player.DesktopMode=true;yield return null;
                    Check(rack.EquipSlot(0,1),d.name+": original potion equips in right hand");roster.avatar.UpdatePose();Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": equipping potion keeps Gwen scissors hidden");
                    if(d.id==ChampionId.Aatrox||d.id==ChampionId.Akshan||d.id==ChampionId.Pantheon){string bone=d.id==ChampionId.Pantheon?"Spear":"Weapon";var weapon=roster.avatar.Instance.GetComponentsInChildren<Transform>().First(t=>t.name==bone);Check(weapon.localScale==Vector3.zero,d.name+": held item hides hand weapon");rack.ReturnHeld(1);roster.avatar.UpdatePose();Check(weapon.localScale.sqrMagnitude>0,d.name+": holstering item restores champion weapon");}else rack.ReturnHeld(1);
                    Check(!player.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": holstering potion keeps Gwen scissors hidden");economy.inventory.Clear();economy.Recalculate();player.DesktopMode=run<4;
                }
                Clear();player.ResetPractice();Place();match.MoveToFountain();yield return null;match.ui.OpenShop();Check(match.ui.ShopIsOpen,d.name+": shop available at own fountain");Check(economy.Buy(1056),d.name+": buying item works");Check(economy.AbilityPower>0&&economy.Owns(1056),d.name+": purchased stats applied");match.ui.Close();
                economy.inventory.Add(new InventorySlot(3157));economy.Recalculate();Check(economy.Effects.Activate(3157,player.Feet,Vector3.forward),d.name+": physical active effect activates");Check(economy.Stasis&&!player.BasicAttack(),d.name+": stasis blocks champion combat");economy.Effects.ResetEffects();
                Place();Check(!match.AtShop&&!economy.Buy(1056),d.name+": purchases are rejected outside the fountain");match.ui.OpenShop();yield return null;Check(!match.ui.ShopIsOpen,d.name+": shop screen is blocked away from fountain");match.ui.Close();player.ResetPractice();match.Recall();yield return null;Check(match.IsRecalling,d.name+": recall begins");match.Recall();Check(!match.IsRecalling,d.name+": recall cancels");
                Check(player.origin.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.name.Contains("Controller")&&r is not LineRenderer).All(r=>r.forceRenderingOff),d.name+": template controller models stay hidden");
            }
            roster.Select(0);match.Play();typeof(RiftMatch).GetProperty("NextWave").SetValue(match,100000f);Place();var minion=match.SpawnMinion(1-player.Health.team,1,MinionKind.Melee);var start=minion.transform.position;minion.health.ApplyStun(.7f);yield return new WaitForSeconds(.4f);Check(Vector3.Distance(minion.transform.position,start)<.001f,"New crowd control stops minion movement");
            match.MoveToFountain();economy.inventory.Clear();economy.Gold=10000;economy.Recalculate();Check(economy.Buy(1043)&&economy.Buy(1026)&&economy.Buy(3108),"Item recipe components can be purchased");int price=economy.Cost(match.catalog.Find(3115),out var consumed);int gold=economy.Gold;Check(consumed.Count==3&&economy.Buy(3115)&&economy.Gold==gold-price&&economy.inventory.Count==1,"Nashor upgrade consumes components and charges remaining price");Check(economy.Sell(0)&&economy.inventory.Count==0,"Selling upgraded item removes it");
            Check(match.catalog.items.Length>180&&match.catalog.items.All(i=>i.icon),"Existing full item catalog and original icons retained");
            Check(match.lanes.All(l=>l.points.Length>50),"Existing three corrected lane routes retained");Check(match.structures.Length==30,"Thirty existing structures retained");match.ui.OpenMenu();yield return null;Capture("Final-menu");Check(errors.Count==0,"No runtime errors or exceptions during full run");
        }
        float ADForTest()=>player.tuning.attackDamage;
        int FirstLayer(LayerMask mask){for(int i=0;i<32;i++)if((mask.value&(1<<i))!=0)return i;return 0;}
        void Finish()
        {
            done=true;Time.timeScale=1;Clear();Application.logMessageReceived-=Log;
            string report="# Champion expansion full run "+run+"\n\n"+passes.Count+" passed; "+failures.Count+" failed. Duration "+(Time.realtimeSinceStartup-timer).ToString("F1")+" s.\n\n## Failures\n"+string.Join("\n",failures.Select(s=>"- "+s))+"\n\n## Passed\n"+string.Join("\n",passes.Select(s=>"- "+s))+"\n\n## Limits\nControlled editor poses and rendered camera captures; run 3 uses desktop aiming and run 4 uses both controller origins. No wearer comfort judgement or headset frame-time benchmark.\n";
            File.WriteAllText("Logs/ChampionExpansion/Run"+run+".md",report);File.WriteAllText("Logs/ChampionExpansion/Run"+run+"-errors.txt",string.Join("\n",errors));EditorApplication.isPlaying=false;
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(!done)Time.timeScale=1;}
    }
}
