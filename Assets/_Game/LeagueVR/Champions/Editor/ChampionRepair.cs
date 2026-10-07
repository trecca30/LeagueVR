using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem.XR;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Champions.Editor
{
    [InitializeOnLoad]public static class ChampionRepairWork
    {
        static ChampionRepairWork(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=State;}
        static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying||!File.Exists("Temp/LeagueChampions.repair"))return;File.Delete("Temp/LeagueChampions.repair");SessionState.SetBool("Champions.RepairPending",true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        static void State(PlayModeStateChange s){if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Champions.RepairPending",false)){SessionState.SetBool("Champions.RepairPending",false);new GameObject("Targeted champion repair check").AddComponent<ChampionRepairProbe>();}}
    }
    public class ChampionRepairProbe:MonoBehaviour
    {
        List<string> passes=new(),failures=new(),debug=new(),errors=new();RiftMatch m;GwenAbilities p;ChampionRoster roster;ChampionAbilities a;RiftEconomy e;Vector3 arena;int layer;Camera camera;bool done;
        void Check(bool ok,string text){(ok?passes:failures).Add(text);File.WriteAllText("Logs/ChampionExpansion/Repair-progress.txt",text+"\n"+passes.Count+" passed / "+failures.Count+" failed");}
        void Pose(){p.origin.transform.position=arena;p.head.transform.SetPositionAndRotation(arena+Vector3.up*1.65f,Quaternion.identity);p.leftHand.SetPositionAndRotation(p.head.transform.position+new Vector3(-.24f,-.35f,.45f),Quaternion.identity);p.rightHand.SetPositionAndRotation(p.head.transform.position+new Vector3(.24f,-.35f,.45f),Quaternion.identity);Physics.SyncTransforms();}
        IEnumerator Start(){Application.logMessageReceived+=Log;yield return null;yield return null;var s=Suite();while(true){bool more;object value=null;try{more=s.MoveNext();if(more)value=s.Current;}catch(Exception ex){failures.Add(ex.ToString());break;}if(!more)break;yield return value;}Finish();}
        void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text+"\n"+trace);}
        IEnumerator Suite()
        {
            m=Object.FindAnyObjectByType<RiftMatch>();p=m.player;roster=p.GetComponent<ChampionRoster>();a=roster.abilities;e=m.economy;
            foreach(var t in p.origin.GetComponentsInChildren<TrackedPoseDriver>(true))t.enabled=false;foreach(var c in p.origin.GetComponentsInChildren<MonoBehaviour>(true))if(c&&c.GetType().Namespace!=null&&c.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion"))c.enabled=false;p.GetComponent<GwenVRInput>().enabled=false;p.DesktopMode=true;p.origin.GetComponent<CharacterController>().enabled=false;e.enabled=false;
            foreach(var t in m.structures)t.enabled=false;foreach(var t in m.objectives)t.enabled=false;foreach(var t in m.fountains)t.enabled=false;
            arena=new Vector3(-5.6f,0,1.4f);for(int i=0;i<32;i++)if((p.combatMask.value&(1<<i))!=0){layer=i;break;}
            camera=new GameObject("Repair audit camera").AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.025f;camera.fieldOfView=80;camera.farClipPlane=350;
            for(int index=1;index<7;index++)
            {
                var d=roster.champions[index];m.ui.OpenMenu();yield return null;roster.Select(index);m.Play();typeof(RiftMatch).GetProperty("NextWave").SetValue(m,100000f);Pose();yield return null;
                Check(!p.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": Gwen scissors stay disabled after menu closes");
                var go=GameObject.CreatePrimitive(PrimitiveType.Capsule);go.name="Targeting repair target";go.layer=layer;go.transform.position=arena+Vector3.forward*2.3f;var collider=go.GetComponent<CapsuleCollider>();collider.center=Vector3.up;var target=go.AddComponent<Combatant>();target.team=1-p.Health.team;target.maxHealth=5000;target.armor=target.magicResistance=0;target.ResetHealth();Physics.SyncTransforms();
                debug.Add(d.name+" head="+p.head.transform.position+" feet="+p.Feet+" aim="+p.AttackOrigin+" dir="+p.AttackDirection+" target="+target.transform.position+" mask="+p.combatMask.value+" world="+p.worldMask.value+" enemy="+a.Enemy(target));
                foreach(var hit in Physics.SphereCastAll(p.AttackOrigin,.35f,p.AttackDirection,d.attackReach,p.worldMask|p.combatMask,QueryTriggerInteraction.Collide).OrderBy(h=>h.distance))debug.Add(" ray "+hit.collider.name+" layer="+hit.collider.gameObject.layer+" distance="+hit.distance+" target="+hit.collider.GetComponentInParent<Combatant>());
                Check(a.Aim(d.attackReach,p.AttackOrigin,p.AttackDirection,true)==target,d.name+": controller ray selects enemy");
                float old=target.Health;p.Health.SetHealth(p.Health.maxHealth*.5f);float self=p.Health.Health;p.ResetAttackTimer();Check(p.BasicAttack(),d.name+": basic accepted");yield return new WaitForSeconds(.3f);Check(target.Health<old,d.name+": basic deals damage");if(d.id==ChampionId.Aatrox)Check(p.Health.Health>self,"Aatrox: passive restores health");
                if(d.id==ChampionId.Akshan){var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=8;wall.transform.position=p.AttackOrigin+Vector3.forward*5;wall.transform.localScale=new Vector3(2,3,.2f);Physics.SyncTransforms();Check(a.TerrainRay(p.AttackOrigin,p.AttackDirection,12,out var anchor)&&anchor.collider==wall.GetComponent<Collider>(),"Akshan: grapple chooses terrain beyond own ward");wall.SetActive(false);Destroy(wall);}
                if(d.id==ChampionId.Brand){a.ResetState();e.Mana=e.MaxMana;Check(p.CastE(),"Brand: targeted E casts");Check(p.CastR(),"Brand: targeted R casts");yield return new WaitForSeconds(1.4f);Check(target.Health<old-200,"Brand: R and burn combo damages target");}
                if(d.id==ChampionId.Pantheon){a.ResetState();e.Mana=e.MaxMana;Pose();Check(p.CastW(),"Pantheon: targeted W vault casts");Check(target.Stunned,"Pantheon: vault stuns target");Pose();for(int i=0;i<5;i++){p.ResetAttackTimer();p.BasicAttack();}Check(a.Stacks==5,"Pantheon: five attacks charge Mortal Will");Check(p.CastQ()&&a.Stacks==0,"Pantheon: empowered Q consumes stacks");}
                if(d.id==ChampionId.Zoe){a.ResetState();e.Mana=e.MaxMana;Check(p.CastE(),"Zoe: bubble launches");float bubbleHP=target.Health;float started=Time.time;bool slept=false;
                    while(Time.time-started<3&&!slept){yield return new WaitForSeconds(.1f);slept=target.Stunned;debug.Add("Zoe bubble t="+(Time.time-started)+" health="+target.Health+" slow="+target.SlowMultiplier+" sleep="+slept);}
                    Check(target.Health<bubbleHP,"Zoe: bubble hits enemy");Check(slept,"Zoe: bubble sleeps target");p.ResetAttackTimer();p.BasicAttack();yield return new WaitForSeconds(.3f);Check(!target.Stunned,"Zoe: attack wakes sleeping target");}
                if(d.id==ChampionId.Yunara){a.ResetState();e.Mana=e.MaxMana;for(int i=0;i<8;i++){p.ResetAttackTimer();p.BasicAttack();yield return new WaitForSeconds(.2f);}Check(a.Stacks==8,"Yunara: eight landed attacks build Spirit");Check(p.CastQ(),"Yunara: Q activates at eight stacks");a.ResetState();e.CriticalChance=1;old=target.Health;p.ResetAttackTimer();p.BasicAttack();yield return new WaitForSeconds(.3f);Check(target.Health<old-p.tuning.attackDamage*e.CriticalDamage,"Yunara: critical attack adds magic damage");}
                target.gameObject.SetActive(false);Destroy(go);a.ResetState();Pose();p.DesktopMode=false;roster.avatar.UpdatePose();camera.transform.SetPositionAndRotation(p.head.transform.position,Quaternion.Euler(12,0,0));ChampionBuild.Capture(camera,"Repair-"+d.name+"-hands");
                var rack=e.GetComponent<RiftItemRack>();e.inventory.Clear();e.inventory.Add(new InventorySlot(2003));e.Recalculate();yield return null;Check(rack.EquipSlot(0,1),d.name+": potion equips in right hand");roster.avatar.UpdatePose();Check(!p.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": potion handling never restores Gwen scissors");rack.ReturnHeld(1);Check(!p.GetComponent<GwenAvatar>().scissorsRoot.gameObject.activeSelf,d.name+": returning potion never restores Gwen scissors");p.DesktopMode=true;
            }
            Check(errors.Count==0,"No errors in targeted repair checks");
        }
        void Finish(){done=true;Application.logMessageReceived-=Log;File.WriteAllText("Logs/ChampionExpansion/Repair.md","# Targeted post-run repair check\n\n"+passes.Count+" passed / "+failures.Count+" failed\n\n## Failures\n"+string.Join("\n",failures.Select(x=>"- "+x))+"\n\n## Passed\n"+string.Join("\n",passes.Select(x=>"- "+x)));File.WriteAllText("Logs/ChampionExpansion/Repair-debug.txt",string.Join("\n",debug));File.WriteAllText("Logs/ChampionExpansion/Repair-errors.txt",string.Join("\n",errors));EditorApplication.isPlaying=false;}
        void OnDestroy(){Application.logMessageReceived-=Log;if(!done)Time.timeScale=1;}
    }
}
