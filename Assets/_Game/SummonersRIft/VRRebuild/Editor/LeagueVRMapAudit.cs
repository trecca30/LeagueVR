using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace LeagueVR.Editor {
 public static class LeagueVRMapAudit {
  public static void Run(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Audit saved scene outside Play mode");
   string root="Assets/_Game/SummonersRIft/VRRebuild";var map=GameObject.Find("Summoner's Rift");var report=new StringBuilder();
   // This original PNG contains a cutout mask even though its original MTL omitted map_d.
   var material=AssetDatabase.LoadAssetAtPath<Material>(root+"/Materials/_chunk_base_north_walls_g_alpha_3dcuv_atlas_color VR.mat");material.SetFloat("_AlphaClip",1);material.SetFloat("_Cutoff",.25f);material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;material.SetOverrideTag("RenderType","TransparentCutout");EditorUtility.SetDirty(material);
   report.AppendLine("Restored alpha cutout on original north base wall G texture, omitted by source MTL.");
   string original=File.ReadAllText("Logs/MapRebuild/Pass1/preserved-state.txt");string current=LeagueVRMapWork.PreservedState();File.WriteAllText("Logs/MapRebuild/final-edit-state.txt",current);report.AppendLine("Non-map edit-mode transforms and component types unchanged: "+(original==current));
   var renderers=map.GetComponentsInChildren<MeshRenderer>();report.AppendLine("Map renderers: "+renderers.Length);report.AppendLine("Unassigned map textures/materials: "+renderers.Count(r=>!r.sharedMaterial||!r.sharedMaterial.GetTexture("_BaseMap")));report.AppendLine("Unsupported shaders: "+renderers.Count(r=>!r.sharedMaterial.shader.isSupported));report.AppendLine("Visible map triangles: "+map.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<Renderer>()).Sum(f=>f.sharedMesh.triangles.Length/3));
   report.AppendLine("Map colliders retain GLB mesh dependencies: "+map.GetComponentsInChildren<MeshCollider>().Count(c=>AssetDatabase.GetAssetPath(c.sharedMesh).EndsWith(".glb")));
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());LeagueVRMapNavigation.Frame();
   File.WriteAllText("Logs/MapRebuild/final-audit.txt",report.ToString());
   // Capture the final atmosphere and exterior closure after correcting the one omitted alpha mask.
   LeagueVRMapWork.Survey("FinalVisual");
  }
 }
}
