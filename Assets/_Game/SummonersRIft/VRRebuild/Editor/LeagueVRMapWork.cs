using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using LeagueVR.Match;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor {
 [InitializeOnLoad] public static class LeagueVRMapWork {
  static double next; static LeagueVRMapWork(){EditorApplication.update+=Tick;}
  static void Tick(){
   if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling || EditorApplication.isUpdating)return; next=EditorApplication.timeSinceStartup+1;
   const string request="Temp/LeagueVRMap.request";if(!File.Exists(request))return;
   string action=File.ReadAllText(request).Trim();File.Delete(request);Directory.CreateDirectory("Logs/MapRebuild");
   try{if(action=="Open")Open();else if(action.StartsWith("Pass"))Survey(action);else if(action=="Audit")LeagueVRMapAudit.Run();else if(action=="Rebuild")LeagueVRMapRebuild.Apply();else if(action=="Polish")LeagueVRMapPolish.Apply();else if(action=="Navigation")LeagueVRMapNavigation.Apply();else if(action=="Frame")LeagueVRMapNavigation.Frame();else if(action=="PlayTest")LeagueVRMapPlayTest.Begin();else throw new InvalidOperationException("Unknown map action");}
   catch(Exception e){File.WriteAllText("Logs/MapRebuild/error.txt",e.ToString());Debug.LogException(e);}
  }
  [MenuItem("Tools/League VR/Open Playable Scene")]public static void Open(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit play mode");
   Directory.CreateDirectory("PrototypeBackups");EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),"PrototypeBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-SessionBeforeMapRebuild.unity",true);
   EditorSceneManager.OpenScene("Assets/Scenes/LeagueVR.unity");Selection.activeGameObject=GameObject.Find("Summoner's Rift");SceneView.lastActiveSceneView?.FrameSelected();File.WriteAllText("Logs/MapRebuild/open.txt",SceneManager.GetActiveScene().path);
  }
  public static string PreservedState(){return string.Join("\n",SceneManager.GetActiveScene().GetRootGameObjects().Where(r=>r.name!="Summoner's Rift").SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>$"{AnimationUtility.CalculateTransformPath(t,null)}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|"+string.Join(",",t.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().FullName))));}
  public static bool Ground(Vector3 at,out RaycastHit hit){
   var map=GameObject.Find("Summoner's Rift");hit=Physics.RaycastAll(new Vector3(at.x,25,at.z),Vector3.down,50,1<<LayerMask.NameToLayer("LeagueWorld"),QueryTriggerInteraction.Ignore).Where(h=>h.collider.transform.IsChildOf(map.transform) && h.normal.y>.65f && h.point.y>-12 && h.point.y<8).OrderBy(h=>Mathf.Abs(h.point.y-at.y)).FirstOrDefault();return hit.collider;
  }
  public static void Survey(string pass){
   if(EditorApplication.isPlayingOrWillChangePlaymode && pass!="Pass3")throw new InvalidOperationException("Survey in edit mode");
   var map=GameObject.Find("Summoner's Rift");if(!map)throw new InvalidOperationException("Map is not open");string dir="Logs/MapRebuild/"+pass;Directory.CreateDirectory(dir);Physics.SyncTransforms();
   var report=new StringBuilder();report.AppendLine("Scene: "+SceneManager.GetActiveScene().path+"; "+DateTime.Now);report.AppendLine($"Map: {map.transform.position} / {map.transform.eulerAngles} / {map.transform.localScale}");
   var renderers=map.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();report.AppendLine("Active map renderers: "+renderers.Length);
   foreach(var r in renderers)foreach(var mat in r.sharedMaterials){if(!mat){report.AppendLine("MISSING MATERIAL "+r.name);continue;}string p=mat.HasProperty("_BaseMap")?"_BaseMap":mat.HasProperty("baseColorTexture")?"baseColorTexture":null;report.AppendLine($"{r.name}: {mat.name} | {mat.shader.name} | {(p==null?"array/other":mat.GetTexture(p)?mat.GetTexture(p).name:"NO BASE TEXTURE")}");}
   File.WriteAllText(dir+"/preserved-state.txt",PreservedState());var match=Object.FindFirstObjectByType<RiftMatch>();int total=0,miss=0;var missing=new StringBuilder();
   foreach(var lane in match.lanes)for(int i=1;i<lane.points.Length;i++){int count=Mathf.CeilToInt(Vector3.Distance(lane.points[i-1],lane.points[i])/.75f);for(int k=0;k<=count;k++){var p=Vector3.Lerp(lane.points[i-1],lane.points[i],k/(float)count);total++;if(!Ground(p,out _)){miss++;missing.AppendLine($"{lane.name}|{p.x:R},{p.y:R},{p.z:R}");}}}
   report.AppendLine($"Lane coverage: {total-miss}/{total}; missing {miss}");File.WriteAllText(dir+"/lane-holes.txt",missing.ToString());if(pass=="Pass1")Export(map,dir);
   var go=new GameObject("Temporary map inspection camera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.enabled=false;cam.GetUniversalAdditionalCameraData().allowXRRendering=false;cam.clearFlags=CameraClearFlags.Skybox;cam.backgroundColor=new Color(.10f,.15f,.19f);cam.cullingMask=~(1<<LayerMask.NameToLayer("UI"));cam.nearClipPlane=.05f;cam.farClipPlane=500;var rt=new RenderTexture(1280,720,24);cam.targetTexture=rt;
   try{cam.orthographic=true;cam.orthographicSize=92;cam.transform.SetPositionAndRotation(new Vector3(3,200,3),Quaternion.Euler(90,0,0));bool fog=RenderSettings.fog;RenderSettings.fog=false;Capture(cam,rt,dir+"/overview.png");RenderSettings.fog=fog;cam.orthographic=false;cam.fieldOfView=80;
    var positions=new[]{new Vector3(-67,0,-54),new Vector3(-53,0,-38),new Vector3(-12,0,-2),new Vector3(1,0,11),new Vector3(20,0,34),new Vector3(45,0,55),new Vector3(-69,0,26),new Vector3(-42,0,65),new Vector3(37,0,-53),new Vector3(64,0,10),new Vector3(-22,0,28),new Vector3(28,0,-16)};
    for(int i=0;i<positions.Length;i++){var p=positions[i];bool found=Ground(p,out var hit);float y=found?hit.point.y:0;report.AppendLine($"View {i:D2}: {p.x},{p.z} ground={found} y={y:0.00}");for(int d=0;d<4;d++){cam.transform.SetPositionAndRotation(new Vector3(p.x,y+1.65f,p.z),Quaternion.Euler(7,d*90,0));Capture(cam,rt,$"{dir}/view-{i:D2}-{d}.png");}}
   }finally{cam.targetTexture=null;Object.DestroyImmediate(go);rt.Release();Object.DestroyImmediate(rt);}File.WriteAllText(dir+"/report.txt",report.ToString());Debug.Log(pass+" survey complete. "+(total-miss)+"/"+total+" lane coverage.");
  }
  static void Export(GameObject map,string dir){
   var f=map.GetComponentsInChildren<MeshFilter>().First(x=>x.name=="Object_2");using(var w=new BinaryWriter(File.Create(dir+"/world-meshes.bin"))){var all=map.GetComponentsInChildren<MeshFilter>().Where(x=>x.GetComponent<MeshRenderer>()?.enabled==true).ToArray();w.Write(all.Length);foreach(var mf in all){var m=mf.sharedMesh;w.Write(mf.name);w.Write(m.vertexCount);foreach(var p in m.vertices){var v=mf.transform.TransformPoint(p);w.Write(v.x);w.Write(v.y);w.Write(v.z);}var indices=m.triangles;w.Write(indices.Length);foreach(int i in indices)w.Write(i);}}
   File.WriteAllText(dir+"/coordinate-reference.json",JsonUtility.ToJson(new Reference{local=f.sharedMesh.vertices.Take(6).ToArray(),world=f.sharedMesh.vertices.Take(6).Select(p=>f.transform.TransformPoint(p)).ToArray()},true));
  }
  [Serializable]class Reference{public Vector3[] local,world;}
  public static void Capture(Camera cam,RenderTexture rt,string path){var prior=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);try{cam.Render();RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());}finally{RenderTexture.active=prior;Object.DestroyImmediate(t);}}
 }

}






