using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using Object=UnityEngine.Object;

namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRValidation
    {
        const string Report="Logs/LeagueVR-validation.txt";
        static readonly List<string> results=new List<string>();
        static LeagueVRValidation()
        {
            EditorApplication.playModeStateChanged+=OnPlayState;

        }
        static void Check(bool condition,string message) { results.Add((condition?"PASS ":"FAIL ")+message); File.WriteAllLines(Report,results); }
        [MenuItem("Tools/League VR/Validate Scene")]
        public static void Validate()
        {
            results.Clear();
            var g=Object.FindFirstObjectByType<GwenAbilities>();
            Check(g!=null,"Gwen champion is installed"); if(!g)return;
            Check(Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None).Length==1,"Exactly one existing XR Origin");
            Check(g.head && g.rightHand && g.leftHand && g.spawn && g.tuning,"Champion scene references are assigned");
            Check(g.GetComponent<GwenVRInput>().combatActions.FindAction("Combat/Needle")!=null,"Dedicated combat action map exists");
            Check(g.GetComponent<CharacterController>()!=null,"Existing character controller preserved");
            Check(g.GetComponentInChildren<TeleportationProvider>(true)!=null,"Existing teleport provider preserved");
            Check(Object.FindObjectsByType<TeleportationArea>(FindObjectsSortMode.None).Length>0,"Map supports XR teleportation");
            Check(!SceneManager.GetActiveScene().GetRootGameObjects().Any(r=>new[]{"Interactables","UI","Environment","Teleport Area Setup"}.Contains(r.name)),"Demo roots removed from playable scene");
            Check(Object.FindObjectsByType<TrainingSentinel>(FindObjectsSortMode.None).Length>=3,"Practice enemies are present");
            Check(Physics.Raycast(g.spawn.position+Vector3.up,Vector3.down,out var hit,2,g.worldMask) && hit.normal.y>.8f,"Spawn has upward-facing map collision beneath it");
            var map=GameObject.Find("Summoner's Rift");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb");
            Check(map && Quaternion.Angle(map.transform.rotation,source.transform.rotation)<.01f,"Map visual orientation matches the upright source");
            Check(map && Mathf.Abs(map.transform.localScale.x-6)<.01f,"Rift uses the intended champion-scale environment");
            Check(map && map.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m && m.name!="Merged_materials")),"Untextured export surfaces have a terrain fallback");
            Check(g.GetComponent<GwenAvatar>().animationPlayer!=null,"Imported Gwen animation player is connected");
            int missing=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Check(missing==0,"No missing scripts in the active scene");
            Check(Mathf.Abs(Combatant.Mitigate(100,100)-50)<.001f,"Damage mitigation respects 100 resistance");
            Debug.Log("LEAGUE VR SCENE VALIDATION: "+results.Count(r=>r.StartsWith("PASS"))+" passed, "+results.Count(r=>r.StartsWith("FAIL"))+" failed.");
        }
        [MenuItem("Tools/League VR/Run Gameplay Smoke Tests")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            Validate(); if(results.Any(r=>r.StartsWith("FAIL")))return;
            EditorSceneManager.SaveOpenScenes(); SessionState.SetBool("LeagueVR.RunTests",true); EditorApplication.isPlaying=true;
        }
        static void OnPlayState(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("LeagueVR.RunTests",false))return;
            SessionState.SetBool("LeagueVR.RunTests",false);
            results.Clear();results.Add("PLAY MODE SMOKE TESTS");
            var g=Object.FindFirstObjectByType<GwenAbilities>();g.StartCoroutine(Guard(Tests(g)));
        }
        static IEnumerator Guard(IEnumerator routine)
        {
            while(true)
            {
                bool more=false;object current=null;
                try{more=routine.MoveNext();if(more)current=routine.Current;}
                catch(Exception e){Check(false,e.ToString());}
                if(!more)break;yield return current;
            }
            File.WriteAllLines(Report,results);
            Debug.Log("LEAGUE VR GAMEPLAY VALIDATION: "+results.Count(r=>r.StartsWith("PASS"))+" passed, "+results.Count(r=>r.StartsWith("FAIL"))+" failed.");
            EditorApplication.delayCall+=()=>EditorApplication.isPlaying=false;
        }
        static IEnumerator Tests(GwenAbilities g)
        {
            // Isolate the fixture before waiting: the UI click that starts Play can be seen as fire input.
            g.GetComponent<GwenVRInput>().enabled=false;g.DesktopMode=true;
            yield return new WaitForSeconds(1);
            Check(g.Health.IsAlive,"Champion starts alive");
            Check(Vector3.Distance(g.Feet,g.spawn.position)<1,"Player remains on the map at startup");
            foreach(var e in Object.FindObjectsByType<TrainingSentinel>(FindObjectsSortMode.None))e.gameObject.SetActive(false);
            g.GetComponent<GwenVRInput>().enabled=false;g.DesktopMode=true;
            g.ResetPractice();
            g.head.transform.localRotation=Quaternion.identity;
            var go=new GameObject("Validation target");go.layer=LayerMask.NameToLayer("LeagueCombat");go.transform.position=g.AttackOrigin+g.AttackDirection*1.5f;
            var target=go.AddComponent<Combatant>();target.team=1;target.maxHealth=5000;target.armor=target.magicResistance=0;target.aimPoint=go.transform;target.ResetHealth();
            var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.size=Vector3.one*.5f;
            Physics.SyncTransforms();
            float initial=target.Health;
            Check(g.BasicAttack(),"Basic attack can fire");Check(!g.BasicAttack(),"Attack rate is limited");
            Check(target.Health<initial && g.QStacks==1,"Basic hit damages enemy and builds a Q stack");
            Check(target.TakeDamage(new DamageHit(target,target.AimPosition,100,DamageKind.True))==0,"Self damage is rejected");
            float hp=g.Health.Health; g.Health.TakeDamage(new DamageHit(target,target.AimPosition,100,DamageKind.True));
            Check(Mathf.Abs(g.Health.Health-(hp-100))<.01f,"True damage bypasses resistance");
            float beforeHeal=g.Health.Health;g.ApplyPassive(target);Check(g.Health.Health>beforeHeal,"Passive heals Gwen from champion damage");
            initial=target.Health;Check(g.CastQ(),"Q can cast");Check(!g.CastQ(),"Q cannot be spammed during cast/cooldown");
            yield return new WaitForSeconds(.6f);
            Check(target.Health<initial-50 && g.QStacks==0,"Q sequence applies damage and consumes stacks");
            Check(g.CastW(),"Mist can cast");
            go.transform.position=g.Feet+Vector3.forward*10;hp=g.Health.Health;
            g.Health.TakeDamage(new DamageHit(target,target.AimPosition,100,DamageKind.Magic));Check(Mathf.Approximately(hp,g.Health.Health),"Mist blocks damage from an enemy outside its boundary");
            go.transform.position=g.Feet+Vector3.forward;g.Health.TakeDamage(new DamageHit(target,target.AimPosition,100,DamageKind.Magic));Check(g.Health.Health<hp,"Enemy inside the mist can damage Gwen");
            var beforeE=g.Feet;bool dashed=g.CastE();Check(dashed && Vector3.Distance(beforeE,g.Feet)>.1f,"E moves the existing XR body on valid ground");Check(g.Empowered,"E enables empowered attacks");
            go.transform.position=g.AttackOrigin+g.AttackDirection*1.5f;Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);
            float eCooldown=g.Cooldown("E");g.BasicAttack();Check(g.Cooldown("E")<eCooldown-1,"First empowered hit refunds part of E cooldown");
            float beforeR=target.Health;
            Check(g.CastR(),"R first volley can cast");Check(!g.CastR(),"R recast delay is enforced");
            yield return new WaitForSeconds(.7f);Check(g.CastR(),"R second volley can cast");
            yield return new WaitForSeconds(.7f);Check(g.CastR(),"R third volley can cast");
            yield return new WaitForSeconds(.8f);Check(target.Health<beforeR-150,"Needle volleys hit through projectile collision");Check(g.RStage==0 && !g.CastR(),"R cannot fire a fourth volley during cooldown");
            Check(target.SlowMultiplier<1,"Needles apply slow");
            // A world barrier must prevent a ranged attack from reaching a combat target.
            var barrier=GameObject.CreatePrimitive(PrimitiveType.Cube);barrier.layer=LayerMask.NameToLayer("LeagueWorld");barrier.transform.position=g.AttackOrigin+g.AttackDirection*.6f;barrier.transform.localScale=new Vector3(2,2,.15f);Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);initial=target.Health;g.BasicAttack();Check(Mathf.Approximately(initial,target.Health),"Melee hit detection does not pass through world geometry");Object.Destroy(barrier);
            go.transform.position=g.Feet;g.Health.TakeDamage(new DamageHit(target,target.AimPosition,10000,DamageKind.True));Check(!g.Health.IsAlive,"Lethal damage enters the death state");
            yield return new WaitForSeconds(2.2f);Check(g.Health.IsAlive && Vector3.Distance(g.Feet,g.spawn.position)<.5f,"Gwen respawns at the map spawn");
            Object.Destroy(go);
        }
    }
}
