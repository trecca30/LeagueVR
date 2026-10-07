using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using LeagueVR.Champions;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor
{
 public static class GwenFocusVisuals
 {
  static Transform Find(GameObject g,string name)=>g.GetComponentsInChildren<Transform>().First(t=>t.name==name);
  static bool Finger(string name)=>new[]{"Thumb","Index","Middle","Ring","Pinky"}.Any(name.Contains)&&!name.Contains("Buffbone");
  static Quaternion Alignment(Transform palm)
  {
   var middle=palm.Find(palm.name.StartsWith("L_")?"L_Middle1":"R_Middle1");var index=palm.Find(palm.name.StartsWith("L_")?"L_Index1":"R_Index1");var pinky=palm.Find(palm.name.StartsWith("L_")?"L_Pinky1":"R_Pinky1");
   Vector3 f=(middle.position-palm.position).normalized,n=Vector3.Cross(index.position-pinky.position,f).normalized;if(Vector3.Dot(n,Vector3.up)<0)n=-n;
   return Quaternion.Inverse(Quaternion.LookRotation(f,n))*palm.rotation;
  }
  static void MeshAsset(Mesh mesh,string path)
  {var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(mesh);}else AssetDatabase.CreateAsset(mesh,path);}
  public static void Apply()
  {
   if(EditorApplication.isPlaying||EditorSceneManager.GetActiveScene().path!="Assets/Scenes/LeagueVR.unity")throw new Exception("Apply to LeagueVR in edit mode");
   var player=Object.FindAnyObjectByType<GwenAbilities>();var avatar=player.GetComponent<GwenAvatar>();
   var source=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gwen/gwen.glb"));source.hideFlags=HideFlags.HideAndDontSave;
   var baked=new Mesh();
   try
   {
    source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=Vector3.one;
    var anim=source.GetComponentInChildren<Animation>();anim.Play("Idle.anm");anim["Idle.anm"].time=0;anim.Sample();
    var skin=source.GetComponentInChildren<SkinnedMeshRenderer>();skin.BakeMesh(baked);
    var right=Find(source,"R_Hand");var left=Find(source,"L_Hand");var hingeA=Find(source,"Scissors_A");var hingeB=Find(source,"Scissors_B");var tip=Find(source,"Buffbone_Scissors_A_Tip");
    // Recover the grip and blade plane from the original rig rather than guessing a pivot from bounds.
    Vector3 direction=(tip.position-hingeA.position).normalized;Vector3 up=Vector3.ProjectOnPlane(right.position-hingeA.position,direction).normalized;
    Quaternion frame=Quaternion.LookRotation(direction,up);Quaternion inverse=Quaternion.Inverse(frame);Vector3 grip=right.position;
    string posePath="Assets/_Game/LeagueVR/Generated/GwenVRHandPoses.asset";var poses=AssetDatabase.LoadAssetAtPath<GwenHandPoses>(posePath);
    if(!poses){poses=ScriptableObject.CreateInstance<GwenHandPoses>();AssetDatabase.CreateAsset(poses,posePath);}
    poses.leftGripAlignment=Alignment(left);
    // The holding hand is oriented by the authored weapon grip, not its already-curled knuckles.
    poses.rightGripAlignment=Quaternion.Inverse(frame)*right.rotation;poses.weaponGripAlignment=Quaternion.identity;
    var joints=source.GetComponentsInChildren<Transform>().Where(t=>Finger(t.name)).ToArray();var relaxed=joints.ToDictionary(t=>t.name,t=>t.localRotation);
    var vertices=baked.vertices;var uv=baked.uv;var weights=skin.sharedMesh.boneWeights;var bones=skin.bones;
    var triangles=skin.sharedMesh.GetTriangles(1);var a=new List<int>();var b=new List<int>();
    int Dominant(BoneWeight w){int n=w.boneIndex0;float best=w.weight0;if(w.weight1>best){n=w.boneIndex1;best=w.weight1;}if(w.weight2>best){n=w.boneIndex2;best=w.weight2;}if(w.weight3>best)n=w.boneIndex3;return n;}
    for(int i=0;i<triangles.Length;i+=3){var bone=bones[Dominant(weights[triangles[i]])];var list=bone==hingeB||bone.IsChildOf(hingeB)?b:a;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
    const float scale=.65f;
    void BuildBlade(List<int> indices,Transform hinge,Transform destination,string name)
    {
     var unique=indices.Distinct().ToArray();var map=unique.Select((old,n)=>(old,n)).ToDictionary(t=>t.old,t=>t.n);
     var mesh=new Mesh{name=name};mesh.vertices=unique.Select(i=>inverse*(skin.transform.TransformPoint(vertices[i])-hinge.position)*scale).ToArray();mesh.uv=unique.Select(i=>uv[i]).ToArray();mesh.triangles=indices.Select(i=>map[i]).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
     string path="Assets/_Game/LeagueVR/Generated/"+name+".asset";MeshAsset(mesh,path);destination.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
     destination.localScale=Vector3.one;destination.localPosition=inverse*(hinge.position-grip)*scale;destination.localRotation=Quaternion.identity;
    }
    BuildBlade(a,hingeA,avatar.bladeA,"GwenVRScissorsA");BuildBlade(b,hingeB,avatar.bladeB,"GwenVRScissorsB");
    anim.Play("Attack1");anim["Attack1"].time=.1f;anim.Sample();
    poses.joints=joints.Select(t=>new GwenHandPoses.Joint{name=t.name,relaxed=relaxed[t.name],holding=t.localRotation}).ToArray();EditorUtility.SetDirty(poses);avatar.handPoses=poses;EditorUtility.SetDirty(avatar);
    var t=player.tuning;t.attackDamage=63;t.attackInterval=1/.69f;t.passiveMaxHealthFraction=.01f;t.passiveChampionHealFraction=.67f;
    t.qCooldown=6.5f;t.qSnipDamage=10;t.qFinalDamage=60;t.qDuration=.5f;t.qInterval=.1f;t.qMinionModifier=.8f;t.qExecuteThreshold=.2f;
    t.wCooldown=22;t.wDuration=4;t.wResistance=22;t.eCooldown=13;t.eBonusDamage=15;t.eAttackIntervalMultiplier=1/1.3f;t.eCooldownRefundFraction=.25f;t.rCooldown=120;t.rRecastDelay=1;t.rDamage=30;EditorUtility.SetDirty(t);
    var d=AssetDatabase.LoadAssetAtPath<ChampionDefinition>("Assets/_Game/LeagueVR/Champions/Data/Gwen.asset");d.health=620;d.armor=39;d.magicResist=32;d.attackDamage=63;d.attackSpeed=.69f;EditorUtility.SetDirty(d);
    AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    File.WriteAllText(GwenFocusWork.Folder+"/VisualIntegration.txt","Derived original Idle/Attack1 finger poses including thumbs; original R_Hand grip, Scissors_A/B pivots, blade plane and texture UVs. Weapon model scale 0.65. Compact indexed blade meshes. No original GLB modification. Rank 1 PC16.19.1 tuning, original VR distances retained. Saved LeagueVR scene.\nWeapon offset="+poses.weaponGripAlignment.eulerAngles+"\n");
   }finally{Object.DestroyImmediate(source);Object.DestroyImmediate(baked);}
  }
 }
}
