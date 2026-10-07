using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor
{
 public partial class GwenFocusProbe
 {
  XRController simulatedLeft,simulatedRight;
  void CleanupControllers(){foreach(var d in new[]{simulatedLeft,simulatedRight})if(d!=null&&d.added)InputSystem.RemoveDevice(d);}
  void Button(XRController d,string control,float value)=>InputSystem.QueueDeltaStateEvent((ButtonControl)d[control],value);
  void PoseDevice(XRController d,Vector3 position,Quaternion rotation,bool tracked=true)
  {InputSystem.QueueDeltaStateEvent(d.devicePosition,position);InputSystem.QueueDeltaStateEvent(d.deviceRotation,rotation);InputSystem.QueueDeltaStateEvent(d.isTracked,tracked?1f:0f);InputSystem.QueueDeltaStateEvent(d.trackingState,tracked?3:0);}
  GameObject Wall(Vector3 at)
  {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Temporary blocking wall";go.layer=Enumerable.Range(0,32).First(i=>(p.worldMask.value&(1<<i))!=0);go.transform.position=at;go.transform.localScale=new Vector3(2,3,.2f);fixtures.Add(go);Physics.SyncTransforms();return go;}
  IEnumerator Edges()
  {
   Clear();Place();var target=Enemy(arena+new Vector3(.24f,0,1.8f));target.team=p.Health.team;p.BasicAttack();Record("Friendly target","damage="+(5000-target.Health)+" stacks="+p.QStacks,target.Health==5000&&p.QStacks==0);Clear();
   target=Enemy(arena+new Vector3(.24f,0,-1));p.BasicAttack();Record("Enemy behind player","damage="+(5000-target.Health),target.Health==5000);Clear();
   target=Enemy(arena+new Vector3(.24f,0,8));p.BasicAttack();Record("Enemy beyond melee range","damage="+(5000-target.Health),target.Health==5000);Clear();
   target=Enemy(arena+new Vector3(.24f,0,1.8f));Wall(arena+new Vector3(0,1,1));p.BasicAttack();Record("Attack through solid wall","damage="+(5000-target.Health),target.Health==5000);Clear();
   target=Enemy(arena+new Vector3(.24f,0,1.8f));var extra=target.gameObject.AddComponent<SphereCollider>();extra.isTrigger=true;extra.radius=.3f;extra.center=Vector3.up*.7f;p.BasicAttack();float lost=5000-target.Health;bool spam=p.BasicAttack();Record("Duplicate colliders and held-trigger cooldown","damage="+lost+" immediate second attack="+spam+" stacks="+p.QStacks,Mathf.Abs(lost-113)<.1f&&!spam&&p.QStacks==1);Clear();
   target=Enemy(arena+new Vector3(.24f,0,1.8f));for(int i=0;i<5;i++){p.ResetAttackTimer();p.BasicAttack();}Record("Four stack cap","stacks="+p.QStacks,p.QStacks==4);target.ResetHealth();int qPackets=0,passives=0,basicHitSignals=0,snipSignals=0;Action<DamageHit,float> damage=(hit,n)=>{if(hit.ability=="Q")qPackets++;if(hit.ability=="Passive")passives++;};target.Damaged+=damage;
   Action<string,Vector3,Vector3> events=(name,pos,dir)=>{if(name=="Hit")basicHitSignals++;if(name=="Snip")snipSignals++;};p.Cast+=events;float mana=economy.Mana;bool q=p.CastQ(),qSpam=p.CastQ();yield return new WaitForSeconds(.65f);p.Cast-=events;
   Record("Four-stack Q damage and animation hooks","Q packets="+qPackets+" passives="+passives+" snips="+snipSignals+" damage="+(5000-target.Health)+" mana="+(mana-economy.Mana),q&&!qSpam&&qPackets==12&&passives==6&&snipSignals==6&&Mathf.Abs(5000-target.Health-410)<.1f&&Mathf.Abs(mana-economy.Mana-40)<.01f);
   Record("Q uses spell impact rather than basic-hit cue","basic-hit signals="+basicHitSignals,basicHitSignals==0);Clear();
   target=Enemy(arena+new Vector3(.24f,0,1.8f));p.BasicAttack();yield return new WaitForSeconds(6.1f);Record("Q stacks expire without attacks","stacks="+p.QStacks,p.QStacks==0);Clear();
   economy.Mana=0;bool noQ=p.CastQ(),noW=p.CastW(),noE=p.CastE(),noR=p.CastR();Record("Insufficient mana consumes no cooldown","casts="+noQ+noW+noE+noR,!noQ&&!noW&&!noE&&!noR&&p.Cooldown("Q")==0&&p.Cooldown("W")==0&&p.Cooldown("E")==0&&p.Cooldown("R")==0);Clear();
   target=Enemy(arena+Vector3.forward*5);p.CastW();bool early=p.CastW();Record("W target selection outside mist","targetable="+p.Health.IsTargetableBy(target)+" immediate recast="+early,!p.Health.IsTargetableBy(target)&&!early);target.transform.position=arena+Vector3.forward;Record("W target selection inside mist","targetable="+p.Health.IsTargetableBy(target),p.Health.IsTargetableBy(target));yield return new WaitForSeconds(.55f);bool recast=p.CastW();p.origin.transform.position+=Vector3.right*4;yield return null;Record("W one recenter then exit","manual recast="+recast+" mist="+p.MistActive,recast&&!p.MistActive);Clear();Place();
   p.Health.ApplyRoot(1);var before=p.Feet;bool rooted=p.CastE();Record("Root prevents dash without cost","cast="+rooted+" movement="+Vector3.Distance(before,p.Feet),!rooted&&p.Cooldown("E")==0);Clear();
   Wall(arena+new Vector3(0,1,.55f));before=p.Feet;bool blocked=p.CastE();Record("Wall blocks E","cast="+blocked+" movement="+Vector3.Distance(before,p.Feet),Vector3.Distance(before,p.Feet)<.55f);Clear();Place();
   p.origin.GetComponent<CharacterController>().enabled=true;before=p.Feet;bool e=p.CastE();Record("E with the existing CharacterController enabled","cast="+e+" displacement="+Vector3.Distance(before,p.Feet),e&&Vector3.Distance(before,p.Feet)>1.5f);p.origin.GetComponent<CharacterController>().enabled=false;
   target=Enemy(p.Feet+new Vector3(.24f,0,1.8f));float cd=p.Cooldown("E");p.BasicAttack();float refund=cd-p.Cooldown("E");p.ResetAttackTimer();cd=p.Cooldown("E");p.BasicAttack();Record("First empowered attack refunds E once","first refund="+refund+" second="+(cd-p.Cooldown("E")),Mathf.Abs(refund-3.25f)<.05f&&Mathf.Abs(cd-p.Cooldown("E"))<.05f);Clear();Place();
   target=Enemy(arena+new Vector3(-.24f,0,4),2.4f);var behind=Enemy(arena+new Vector3(-.24f,0,6),2.4f);bool first=p.CastR(),tooEarly=p.CastR();yield return new WaitForSeconds(.55f);Record("R pierces two targets with recast lockout","first="+first+" immediate="+tooEarly+" damage="+(5000-target.Health)+","+(5000-behind.Health),first&&!tooEarly&&Mathf.Abs(5000-target.Health-80)<.1f&&Mathf.Abs(5000-behind.Health-80)<.1f);Clear();
   bool fresh=p.CastR();Record("Practice reset clears R recast timer","new R="+fresh,fresh);Clear();
   target=Enemy(arena+Vector3.right*3);target.ApplySlow(.6f,1.5f);yield return new WaitForSeconds(1);target.ApplySlow(.85f,1.5f);yield return new WaitForSeconds(.6f);Record("Weaker needle slow cannot extend strong slow","multiplier="+target.SlowMultiplier,Mathf.Abs(target.SlowMultiplier-.85f)<.001f);yield return new WaitForSeconds(1);Record("Needle slow fully expires","multiplier="+target.SlowMultiplier,target.SlowMultiplier==1);Clear();Place();
   var prefab=m.minions[4+(int)MinionKind.Melee];var minion=Object.Instantiate(prefab,arena+new Vector3(.24f,0,1.8f),Quaternion.identity).GetComponent<RiftMinion>();fixtures.Add(minion.gameObject);minion.Initialize(m,1-p.Health.team,1,MinionKind.Melee,new[]{minion.transform.position,minion.transform.position+Vector3.forward*5});minion.enabled=false;target=minion.health;float hp=target.Health;p.BasicAttack();Record("Actual imported melee minion prefab","health="+hp+" damage="+(hp-target.Health)+" meshes="+minion.GetComponentsInChildren<Renderer>().Length,target.Health<hp&&minion.GetComponentsInChildren<Renderer>().Length>0);
   eye.transform.SetPositionAndRotation(p.head.transform.position-p.head.transform.right*.032f,Quaternion.Euler(15,0,0));Capture("actual-minion-engagement");Clear();
   // Exercise the existing item-stat calculation without changing inventory or shop assets.
   economy.inventory.Add(new InventorySlot(1052));economy.Recalculate();float ap=economy.AbilityPower;Record("Existing item AP flows into Gwen","AP="+ap+" Q="+p.tuning.qSnipDamage+" E="+p.tuning.eBonusDamage+" W="+p.tuning.wResistance,ap>0&&Mathf.Abs(p.tuning.eBonusDamage-(15+.2f*ap))<.01f&&Mathf.Abs(p.tuning.wResistance-(22+.07f*ap))<.01f&&Mathf.Abs(p.tuning.qSnipDamage-(10+.05f*ap))<.01f);economy.inventory.Clear();economy.Recalculate();Clear();
   var audio=p.GetComponent<GwenAudio>();int soundBefore=LeagueSoundBank.PlayedLayers;audio.Cue("Gwen.Attack");audio.Cue("Gwen.QCast");audio.Cue("Gwen.W");audio.Cue("Gwen.E");audio.Cue("Gwen.R");Record("Original League sound dispatch","layers added="+(LeagueSoundBank.PlayedLayers-soundBefore)+" sourceMute="+audio.EffectsSource.mute+" listenerVolume="+AudioListener.volume+" activeListeners="+Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.isActiveAndEnabled),LeagueSoundBank.PlayedLayers>soundBefore&&!audio.EffectsSource.mute&&AudioListener.volume>0&&Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.isActiveAndEnabled)==1);
   Record("Template controller suppression","cached meshes="+p.GetComponent<RiftPlayerView>().HiddenTemplateMeshCount,p.GetComponent<RiftPlayerView>().HiddenTemplateMeshCount>0);
   var input=InputChecks();while(input.MoveNext())yield return input.Current;
  }
  IEnumerator InputChecks()
  {
   InputSystem.RegisterLayout(@"{""name"":""GwenFocusController"",""extend"":""XRController"",""controls"":[{""name"":""triggerPressed"",""layout"":""Button""},{""name"":""gripPressed"",""layout"":""Button""},{""name"":""primaryButton"",""layout"":""Button""},{""name"":""secondaryButton"",""layout"":""Button""}]}");
   simulatedLeft=(XRController)InputSystem.AddDevice("GwenFocusController");InputSystem.SetDeviceUsage(simulatedLeft,CommonUsages.LeftHand);simulatedRight=(XRController)InputSystem.AddDevice("GwenFocusController");InputSystem.SetDeviceUsage(simulatedRight,CommonUsages.RightHand);
   var input=p.GetComponent<GwenVRInput>();input.enableDesktopFallback=false;input.enabled=true;
   Place();Vector3 lp=p.leftHand.localPosition,rp=p.rightHand.localPosition;PoseDevice(simulatedLeft,lp,Quaternion.identity);PoseDevice(simulatedRight,rp,Quaternion.identity);yield return null;yield return null;
   var target=Enemy(arena+new Vector3(.24f,0,1.8f));Button(simulatedRight,"triggerPressed",1);yield return null;yield return null;Button(simulatedRight,"triggerPressed",0);yield return null;Record("Real action bindings: right trigger","damage="+(5000-target.Health)+" stacks="+p.QStacks,target.Health<5000&&p.QStacks==1);Clear();
   target=Enemy(arena+new Vector3(.24f,0,1.8f));Button(simulatedRight,"secondaryButton",1);yield return null;Button(simulatedRight,"secondaryButton",0);yield return new WaitForSeconds(.7f);Record("Real action bindings: B casts Q","damage="+(5000-target.Health)+" cooldown="+p.Cooldown("Q"),target.Health<5000&&p.Cooldown("Q")>0);Clear();
   Button(simulatedLeft,"primaryButton",1);yield return null;yield return null;Button(simulatedLeft,"primaryButton",0);Record("Real action bindings: X casts W","mist="+p.MistActive,p.MistActive);yield return null;Clear();
   var before=p.Feet;Button(simulatedRight,"primaryButton",1);yield return null;yield return null;Button(simulatedRight,"primaryButton",0);Record("Real action bindings: A casts E","displacement="+Vector3.Distance(before,p.Feet),Vector3.Distance(before,p.Feet)>1);yield return null;Clear();Place();
   Button(simulatedLeft,"triggerPressed",1);yield return null;yield return null;Button(simulatedLeft,"triggerPressed",0);Record("Real action bindings: left trigger casts R","stage="+p.RStage,p.RStage==1);yield return null;Clear();
   int swings=0;Action<string,Vector3,Vector3> count=(n,a,b)=>{if(n=="Attack1")swings++;};p.Cast+=count;
   Button(simulatedRight,"gripPressed",1);yield return null;yield return null;p.origin.transform.position+=Vector3.forward*3;p.origin.transform.rotation=Quaternion.Euler(0,30,0);yield return null;yield return null;Record("Rig translation and snap turn do not generate swings","attacks="+swings,swings==0);
   for(int i=1;i<=4;i++){PoseDevice(simulatedRight,rp+Vector3.forward*(.035f*i),Quaternion.identity);yield return null;}yield return null;Record("Grip plus deliberate hand motion","attacks="+swings,swings==1);Button(simulatedRight,"gripPressed",0);p.Cast-=count;yield return null;Clear();p.origin.transform.rotation=Quaternion.identity;Place();
   PoseDevice(simulatedRight,rp,Quaternion.identity,false);yield return null;yield return null;target=Enemy(arena+new Vector3(.24f,0,1.8f));Button(simulatedRight,"triggerPressed",1);yield return null;yield return null;avatar.UpdateTrackedVisuals();Record("Lost controller tracking gates combat and visuals","damage="+(5000-target.Health)+" weaponHidden="+avatar.scissorsRoot.GetComponentsInChildren<Renderer>().All(r=>r.forceRenderingOff),target.Health==5000&&avatar.scissorsRoot.GetComponentsInChildren<Renderer>().All(r=>r.forceRenderingOff));Button(simulatedRight,"triggerPressed",0);yield return null;
   PoseDevice(simulatedRight,rp,Quaternion.identity,true);yield return null;yield return null;avatar.UpdateTrackedVisuals();float error=Vector3.Distance(avatar.rightPalm.position,GwenTracking.Grip(p,false).position+GwenTracking.Grip(p,false).rotation*avatar.rightGripOffset);Record("Tracking restored: grip and weapon axis","palm error="+error+" aim angle="+Vector3.Angle(p.AttackDirection,avatar.scissorsRoot.forward),error<.001f&&Vector3.Angle(p.AttackDirection,avatar.scissorsRoot.forward)<.01f);
   InputSystem.RemoveDevice(simulatedRight);yield return null;Record("Disconnected controller remains untracked","tracked="+GwenTracking.Tracked(false),!GwenTracking.Tracked(false));input.enabled=false;CleanupControllers();Clear();
  }
 }
}

