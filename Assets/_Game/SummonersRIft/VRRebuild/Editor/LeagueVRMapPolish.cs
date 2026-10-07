using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor {
 public static class LeagueVRMapPolish {
  public static void Apply(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode");var map=GameObject.Find("Summoner's Rift");var scene=SceneManager.GetActiveScene();string before=LeagueVRMapWork.PreservedState();
   string fog=$"Previous fog: enabled={RenderSettings.fog}; mode={RenderSettings.fogMode}; start={RenderSettings.fogStartDistance}; end={RenderSettings.fogEndDistance}; density={RenderSettings.fogDensity}; color={RenderSettings.fogColor}";
   RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=100;RenderSettings.fogEndDistance=240;RenderSettings.fogColor=new Color(.29f,.39f,.43f);
   var rebuilt=map.transform.Find("VR rebuilt map");var floor=rebuilt.Find("Foundation visible repairs").GetComponent<MeshFilter>();var collision=rebuilt.Find("Foundation collision").GetComponent<MeshFilter>();floor.sharedMesh=collision.sharedMesh;floor.GetComponent<MeshRenderer>().sharedMaterial.renderQueue=1980;
   var border=rebuilt.Find("Boundary foundations");if(!border.GetComponent<MeshCollider>()){
    var visual=border.GetComponent<MeshFilter>().sharedMesh;var mesh=Object.Instantiate(visual);mesh.name="Playable map boundary collision";var points=mesh.vertices;for(int i=0;i<points.Length;i++){var world=map.transform.TransformPoint(points[i]);if(world.y>-.1f)world.y=4;points[i]=map.transform.InverseTransformPoint(world);}mesh.vertices=points;var one=mesh.triangles;var both=new int[one.Length*2];Array.Copy(one,both,one.Length);for(int i=0;i<one.Length;i+=3){both[one.Length+i]=one[i+2];both[one.Length+i+1]=one[i+1];both[one.Length+i+2]=one[i];}mesh.triangles=both;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,"Assets/_Game/SummonersRIft/VRRebuild/Meshes/Playable boundary collision.asset");border.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
   }
   // Old visual components are replaced by editable source meshes. Original collision and teleport components remain.
   if(PrefabUtility.IsPartOfPrefabInstance(map))PrefabUtility.UnpackPrefabInstance(map,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   foreach(var renderer in map.GetComponentsInChildren<MeshRenderer>(true).Where(r=>!r.enabled).ToArray()){var mf=renderer.GetComponent<MeshFilter>();Object.DestroyImmediate(renderer);if(mf)Object.DestroyImmediate(mf);}
   if(LeagueVRMapWork.PreservedState()!=before)throw new InvalidOperationException("Objects outside map changed");
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText("Logs/MapRebuild/polish.txt",fog+"\nNew fog: linear 100-240 m, blue-green atmospheric color.\nContinuous recessed floor now covers all terrain seams.\nUnused GLB visual components removed; original GLB unchanged on disk, original map transform hierarchy/colliders/teleport components retained.\nXR, Gwen and gameplay object snapshot unchanged.\n");
  }
 }
}

