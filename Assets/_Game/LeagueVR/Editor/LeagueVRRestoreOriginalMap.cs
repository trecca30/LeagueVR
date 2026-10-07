using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Object=UnityEngine.Object;

namespace LeagueVR.Editor
{
    public static class LeagueVRRestoreOriginalMap
    {
        [MenuItem("Tools/League VR/Replace Map With Original GLB")]
        public static void Restore()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var scene=SceneManager.GetActiveScene();
            var old=scene.GetRootGameObjects().Single(g=>g.name=="Summoner's Rift");
            const string asset="Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb";
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if(!source)throw new InvalidOperationException("Original map has not imported.");
            Directory.CreateDirectory("PrototypeBackups");
            EditorSceneManager.SaveScene(scene);
            File.Copy(scene.path,"PrototypeBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-BeforeOriginalMapRestore.unity");
            Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Replace map with original GLB");
            var fresh=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);
            Undo.RegisterCreatedObjectUndo(fresh,"Restore original map");
            fresh.name=old.name;
            fresh.transform.SetPositionAndRotation(old.transform.position,source.transform.rotation);
            fresh.transform.localScale=old.transform.localScale;
            int colliders=0,areas=0;
            // Retain existing VR collision and teleport support, never copy altered renderers or generated scenery.
            foreach(var f in fresh.GetComponentsInChildren<MeshFilter>(true))
            {
                var relative=AnimationUtility.CalculateTransformPath(f.transform,fresh.transform);
                var previous=old.transform.Find(relative);if(!previous)continue;
                f.gameObject.layer=previous.gameObject.layer;
                var priorCollider=previous.GetComponent<MeshCollider>();
                if(!priorCollider)continue;
                var collider=f.GetComponent<MeshCollider>();if(!collider)collider=f.gameObject.AddComponent<MeshCollider>();
                EditorUtility.CopySerialized(priorCollider,collider);colliders++;
                var priorArea=previous.GetComponent<TeleportationArea>();
                if(!priorArea)continue;
                var area=f.GetComponent<TeleportationArea>();if(!area)area=f.gameObject.AddComponent<TeleportationArea>();
                EditorUtility.CopySerialized(priorArea,area);area.colliders.Clear();area.colliders.Add(collider);areas++;
            }
            var renderers=fresh.GetComponentsInChildren<Renderer>(true);
            bool original=renderers.All(r=>{
                var s=PrefabUtility.GetCorrespondingObjectFromSource(r);
                return s && r.sharedMaterials.SequenceEqual(s.sharedMaterials);
            });
            bool meshes=fresh.GetComponentsInChildren<MeshFilter>(true).All(f=>f.sharedMesh==PrefabUtility.GetCorrespondingObjectFromSource(f).sharedMesh);
            if(!original || !meshes){Undo.DestroyObjectImmediate(fresh);throw new InvalidOperationException("Original asset verification failed; previous map retained.");}
            Undo.DestroyObjectImmediate(old);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fresh.transform);
            Physics.SyncTransforms();EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject=fresh;
            SceneView.lastActiveSceneView?.FrameSelected();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/LeagueVR-original-map-restore.txt",$"Original GLB map restored in {scene.path}\nRenderers: {renderers.Length}\nAll renderer materials match original GLB: {original}\nAll visual meshes match original GLB: {meshes}\nExisting VR colliders retained: {colliders}\nExisting teleport surfaces retained: {areas}\nReplacement terrain/fallback materials are not applied.\nXR rig and Gwen objects retained.\n");
            Debug.Log("Original GLB map restored and saved. Original meshes and materials verified.");
        }
    }
}
