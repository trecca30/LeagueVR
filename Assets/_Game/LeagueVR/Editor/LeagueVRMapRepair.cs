using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Object=UnityEngine.Object;

namespace LeagueVR.Editor
{
    public static class LeagueVRMapRepair
    {
        const string Generated="Assets/_Game/LeagueVR/Generated";
        [MenuItem("Tools/League VR/Repair Map Surfaces")]
        public static void Repair()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var map=GameObject.Find("Summoner's Rift");
            var player=Object.FindFirstObjectByType<GwenAbilities>();
            if(!map || !player)throw new InvalidOperationException("Open the integrated LeagueVR scene first.");
            var report=PrepareSurfaces(map);
            Physics.SyncTransforms();LeagueVRBuilder.RefinePlacement();
            ApplyReadability(player);
            RepairGeneratedWeapons();
            report.AppendLine("Map rotation: "+map.transform.eulerAngles+"; scale: "+map.transform.localScale+"; spawn: "+player.spawn.position);
            report.AppendLine("Reversed collision meshes: "+map.GetComponentsInChildren<MeshCollider>().Length);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText("Logs/LeagueVR-map-repair.txt",report.ToString());
            Debug.Log("League VR map repair complete. Visual review required.");
        }
        public static StringBuilder PrepareSurfaces(GameObject map)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb");
            map.transform.rotation=source.transform.rotation;
            map.transform.localScale=Vector3.one*6;
            map.transform.position=new Vector3(map.transform.position.x,0,map.transform.position.z);
            Directory.CreateDirectory(Generated+"/Collision");
            var report=new StringBuilder("Rift repair: visual orientation restored; source GLB left intact.\n");
            foreach(var f in map.GetComponentsInChildren<MeshFilter>(true))
            {
                var collider=f.GetComponent<MeshCollider>();if(!collider)continue;
                string path=Generated+"/Collision/"+f.name+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh)
                {
                    mesh=new Mesh{name=f.name+" reversed collision",indexFormat=IndexFormat.UInt32};mesh.vertices=f.sharedMesh.vertices;
                    var indices=f.sharedMesh.triangles;
                    for(int i=0;i<indices.Length;i+=3){int v=indices[i];indices[i]=indices[i+2];indices[i+2]=v;}
                    mesh.triangles=indices;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
                }
                collider.sharedMesh=mesh;PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            var terrain=map.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterials.Any(m=>m && m.name.Contains("Terrain")));
            var texture=terrain.sharedMaterial.GetTexture("baseColorTexture");
            var fallback=AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Rift missing-texture fallback.mat");
            if(!fallback){fallback=new Material(Shader.Find("LeagueVR/Rift Ground Fallback"));AssetDatabase.CreateAsset(fallback,Generated+"/Rift missing-texture fallback.mat");}
            fallback.SetTexture("_BaseMap",texture);EditorUtility.SetDirty(fallback);
            foreach(var r in map.GetComponentsInChildren<Renderer>())
            {
                var materials=r.sharedMaterials;bool replaced=false;
                for(int i=0;i<materials.Length;i++)if(materials[i] && materials[i].name=="Merged_materials") {materials[i]=fallback;replaced=true;report.AppendLine("Fallback texture: "+r.name);}
                if(replaced){r.sharedMaterials=materials;PrefabUtility.RecordPrefabInstancePropertyModifications(r);}
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(map.transform);
            return report;
        }
        public static void ApplyReadability(GwenAbilities player)
        {
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");
            foreach(var t in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if(font)t.font=font;
                if(t.name=="Gwen wrist status") {t.fontSize=22;t.transform.localScale=Vector3.one*.015f;t.rectTransform.sizeDelta=new Vector2(26,23);}
                else if(t.name=="Sentinel health") {t.fontSize=12;t.rectTransform.sizeDelta=new Vector2(25,12);t.color=new Color(.95f,.65f,.75f);t.outlineWidth=.2f;t.outlineColor=Color.black;}
                else if(t.name=="Practice guide") {t.fontSize=18;t.transform.localScale=Vector3.one*.06f;t.rectTransform.sizeDelta=new Vector2(70,35);}
            }
            foreach(var ray in player.origin.GetComponentsInChildren<XRRayInteractor>(true)) {ray.raycastMask=ray.raycastMask.value|player.worldMask.value;PrefabUtility.RecordPrefabInstancePropertyModifications(ray);}
            player.origin.CameraYOffset=1.65f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.origin);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.origin.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.origin.CameraFloorOffsetObject.transform);

        }
        [MenuItem("Tools/League VR/Refresh Weapon Mesh Bounds")]
        public static void RepairGeneratedWeapons()
        {
            // glTF primitives share vertex buffers. Keep only each weapon's referenced vertices.
            foreach(string name in new[]{"Scissors A","Scissors B","GwenNeedle"})
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Generated+"/"+name+".asset");if(!mesh)continue;
                var oldVertices=mesh.vertices;var oldUV=mesh.uv;var triangles=mesh.triangles;
                var used=triangles.Distinct().ToArray();var lookup=used.Select((v,i)=>new{v,i}).ToDictionary(x=>x.v,x=>x.i);
                var vertices=used.Select(i=>oldVertices[i]).ToArray();
                if(name=="GwenNeedle")
                {
                    var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
                    for(int i=0;i<vertices.Length;i++)vertices[i]-=bounds.center;
                }
                mesh.Clear();mesh.vertices=vertices;if(oldUV.Length==oldVertices.Length)mesh.uv=used.Select(i=>oldUV[i]).ToArray();
                mesh.triangles=triangles.Select(i=>lookup[i]).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
