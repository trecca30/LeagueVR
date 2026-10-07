using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using LeagueVR.Match;using Object=UnityEngine.Object;
namespace LeagueVR.Editor
{
 public static class LeagueVRPOVBuild
 {
  const string Art="Assets/_Game/LeagueVR/Match/Art/Items3D";
  [Serializable]class Models{public Model[] models;}[Serializable]class Model{public int id,triangles;public string[] groups;public float[] tip;}
  public static void Apply()
  {
   if(EditorApplication.isPlaying)throw new Exception("Apply in edit mode only");
   var m=Object.FindAnyObjectByType<RiftMatch>();if(!m||EditorSceneManager.GetActiveScene().path!="Assets/Scenes/LeagueVR.unity")throw new Exception("LeagueVR scene required");
   var log=new StringBuilder("POV / HAND / LANE / UI integration\n");
   if(!m.player.GetComponent<RiftPlayerView>())m.player.gameObject.AddComponent<RiftPlayerView>().champion=m.player;
   m.player.head.nearClipPlane=.025f;EditorUtility.SetDirty(m.player.head);
   Vector2[] top={new(-59,-42),new(-60,-36),new(-62.8f,-30),new(-62.5f,-23),new(-64.8f,-17),new(-63.8f,-5),new(-62.5f,10),new(-61.5f,27),new(-61.5f,37),new(-55,48),new(-42,64),new(-26,72),new(-10,70),new(6,70),new(22,70),new(30,70),new(35,65),new(44,63),new(49,65)};
   Vector2[] mid={new(-53,-44),new(-49,-41),new(-43,-35),new(-38,-29),new(-31,-24),new(-25,-18),new(-17,-10),new(-8,-1),new(1,8),new(10,17),new(19,26),new(26,34),new(34,42),new(39,49),new(47,56),new(48,59)};
   Vector2[] bot={new(-55,-50),new(-47,-52),new(-40,-53),new(-32,-54),new(-23,-53),new(-8,-53),new(10,-56),new(27,-56),new(38,-55),new(48,-50),new(55,-39),new(57,-26),new(56,-12),new(55,5),new(55,17),new(59,29),new(58,38),new(58,45),new(55,52),new(55,59)};
   for(int lane=0;lane<3;lane++){var anchors=lane==0?top:lane==1?mid:bot;m.lanes[lane].points=LeagueVRLaneRoutes.Build(m,anchors);log.AppendLine(m.lanes[lane].name+": "+m.lanes[lane].points.Length+" grounded lane points");}
   foreach(var s in m.structures)
   {
    if(s.kind==StructureKind.Inhibitor||s.kind==StructureKind.Nexus)continue;
    var direction=Facing(m,s);s.transform.rotation=Quaternion.LookRotation(direction);
    EditorUtility.SetDirty(s.transform);log.AppendLine(s.name+" yaw "+s.transform.eulerAngles.y.ToString("F1"));
   }
   var models=JsonUtility.FromJson<Models>(File.ReadAllText(Art+"/Models/LeaguePotions/manifest.json"));
   foreach(var model in models.models)
   {
    string path=Art+"/Models/LeaguePotions/"+model.id+".obj";var importer=AssetImporter.GetAtPath(path) as ModelImporter;
    importer.globalScale=1;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;importer.importNormals=ModelImporterNormals.Import;importer.SaveAndReimport();
    var prefabPath=Art+"/Prefabs/"+model.id+".prefab";var root=PrefabUtility.LoadPrefabContents(prefabPath);
    try
    {
     for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
     root.transform.localScale=Vector3.one;root.name=m.catalog.Find(model.id).name+" physical flask";
     var visual=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);visual.name="Icon inspired flask";
     foreach(var r in visual.GetComponentsInChildren<MeshRenderer>())
     {
      var mesh=r.GetComponent<MeshFilter>().sharedMesh;string group=model.groups.FirstOrDefault(g=>r.name.Contains(g)||mesh.name.Contains(g));
      r.sharedMaterials=Enumerable.Range(0,mesh.subMeshCount).Select(i=>PotionMaterial(model.id,group??model.groups[i])).ToArray();r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
     }
     var tip=new GameObject("Drink tip");tip.transform.SetParent(root.transform,false);tip.transform.localPosition=new Vector3(model.tip[0],model.tip[1],model.tip[2]);
     var box=root.GetComponent<BoxCollider>()??root.AddComponent<BoxCollider>();box.isTrigger=true;box.center=new Vector3(0,.018f,0);box.size=new Vector3(.14f,.22f,.12f);
     PrefabUtility.SaveAsPrefabAsset(root,prefabPath);log.AppendLine(model.id+": distinct flask, "+model.triangles+" triangles, grip centre, lip marker, existing prefab GUID retained");
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   EditorUtility.SetDirty(m);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
   File.WriteAllText("Logs/POVOverhaul/Integration.txt",log.ToString());LeagueVRPOVWork.CaptureRoutes(m,"After-routes");CapturePotions();Debug.Log("POV overhaul saved");
  }
  public static Vector3 Facing(RiftMatch m,RiftStructure s)
  {
   if(s.lane<0){var enemy=m.structures.First(n=>n.kind==StructureKind.Nexus&&n.health.team!=s.health.team);return Vector3.ProjectOnPlane(enemy.transform.position-s.transform.position,Vector3.up).normalized;}
   var route=s.health.team==0?m.lanes[s.lane].points:m.lanes[s.lane].points.Reverse().ToArray();int closest=0;float d=float.MaxValue;
   for(int i=0;i<route.Length;i++){float distance=GwenAbilities.FlatDistance(s.transform.position,route[i]);if(distance<d){d=distance;closest=i;}}
   int ahead=closest;float along=0;while(ahead<route.Length-1&&along<7){along+=Vector3.Distance(route[ahead],route[ahead+1]);ahead++;}
   return Vector3.ProjectOnPlane(route[ahead]-s.transform.position,Vector3.up).normalized;
  }
  public static void Survey(){var m=Object.FindAnyObjectByType<RiftMatch>();Physics.SyncTransforms();var log=new StringBuilder();foreach(var l in m.lanes)for(int i=1;i<l.points.Length;i++){var a=l.points[i-1];var b=l.points[i];var tangent=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;for(int col=-1;col<=1;col++){var side=Vector3.Cross(Vector3.up,tangent)*col*1.15f;var start=a+side;var end=b+side;if(!m.Ground(start,out var g1)||!m.Ground(end,out var g2)){log.AppendLine(l.name+" "+i+" column "+col+" missing ground");continue;}if(Mathf.Abs(g2.y-g1.y)>.55f||Physics.Linecast(g1+Vector3.up*.4f,g2+Vector3.up*.4f,out var hit,m.player.worldMask,QueryTriggerInteraction.Ignore))log.AppendLine(l.name+" "+i+" column "+col+" "+g1.ToString("F2")+" -> "+g2.ToString("F2"));}}File.WriteAllText("Logs/POVOverhaul/RouteSurvey.txt",log.ToString());}
  static Vector3[] Route(RiftMatch m,Vector2[] anchors)
  {
   var points=new List<Vector3>();float prior=.2f;
   for(int i=1;i<anchors.Length;i++){int steps=Mathf.CeilToInt(Vector2.Distance(anchors[i-1],anchors[i])/1.6f);for(int n=0;n<steps;n++){var p=Vector2.Lerp(anchors[i-1],anchors[i],n/(float)steps);var at=new Vector3(p.x,prior,p.y);if(!m.Ground(at,out var ground))throw new Exception("Lane has no ground at "+at);prior=ground.y;points.Add(ground);}}
   var last=anchors.Last();if(!m.Ground(new Vector3(last.x,prior,last.y),out var end))throw new Exception("No ground at lane end");points.Add(end);return points.ToArray();
  }
  static Material PotionMaterial(int id,string group)
  {
   string folder=Art+"/Materials/LeaguePotions";Directory.CreateDirectory(folder);string path=folder+"/"+id+"-"+group+".mat";
   var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
   Color liquid=id==2003?new(.65f,.015f,.10f):id==2031?new(.05f,.69f,.48f):id==2138?new(.36f,.42f,.57f):id==2139?new(.14f,.23f,.84f):new(.64f,.025f,.13f);
   if(group=="Liquid"||group=="Gem")mat.shader=Shader.Find("Universal Render Pipeline/Unlit");else mat.shader=Shader.Find("Universal Render Pipeline/Lit");
   Color color=group switch{"Glass"=>new(.97f,.96f,.98f,.08f),"Liquid"=>liquid,"Gem"=>liquid*1.2f,"Gold"=>new(.68f,.42f,.10f),"Silver"=>new(.56f,.58f,.60f),"Iron"=>new(.17f,.18f,.22f),"Leather"=>new(.37f,.18f,.10f),_=>new(.28f,.13f,.06f)};
   mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",group=="Glass"?.90f:group=="Liquid"?.72f:.45f);mat.SetFloat("_Metallic",group=="Gold"||group=="Silver"||group=="Iron"?.75f:0);
   if(group=="Leather"||group=="Cork")mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/LOLVR-Potion-Leather.png"));
   if(group=="Liquid"||group=="Gem"){mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",liquid*.35f);}
   if(group=="Glass"){mat.SetFloat("_BlendModePreserveSpecular",0);mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;mat.SetOverrideTag("RenderType","Transparent");}
   EditorUtility.SetDirty(mat);return mat;
  }
  static void CapturePotions()
  {
   var root=new GameObject("Temporary potion preview"){hideFlags=HideFlags.HideAndDontSave};
   try
   {
    int[] ids={2003,2031,2138,2139,2140};for(int i=0;i<ids.Length;i++){var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Prefabs/"+ids[i]+".prefab"),root.transform);p.transform.position=new Vector3((i-2)*.20f,101,0);p.transform.rotation=Quaternion.Euler(0,-20,0);}
    var light=new GameObject("Preview light").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-20,0);light.intensity=2;
    var c=new GameObject("Preview camera").AddComponent<Camera>();c.transform.SetParent(root.transform);c.enabled=false;c.GetUniversalAdditionalCameraData().allowXRRendering=false;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.025f,.04f,.06f);c.transform.SetPositionAndRotation(new Vector3(0,101.12f,-.9f),Quaternion.Euler(7,0,0));c.fieldOfView=58;c.farClipPlane=3;
    Capture(c,"Potions",1600,640);
   }finally{Object.DestroyImmediate(root);}
  }
  public static void Capture(Camera c,string name,int w=1440,int h=900)
  {
   var rt=new RenderTexture(w,h,24);var previous=RenderTexture.active;var oldTarget=c.targetTexture;bool fog=RenderSettings.fog;RenderSettings.fog=false;
   try{c.targetTexture=rt;c.Render();RenderTexture.active=rt;var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes("Logs/POVOverhaul/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
   finally{c.targetTexture=oldTarget;RenderTexture.active=previous;RenderSettings.fog=fog;rt.Release();Object.DestroyImmediate(rt);}
  }
 }
}
