using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRInspection
    {
        public const string Report = @"Logs/LeagueVRInspection.txt";

        static LeagueVRInspection()
        {
            if (!SessionState.GetBool("LeagueVR.Inspected", false))
                EditorApplication.delayCall += Inspect;
        }

        [MenuItem("Tools/League VR/Inspect Current Setup")]
        public static void Inspect()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Inspect;
                return;
            }
            var b = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            b.AppendLine($"SCENE {scene.path}, dirty={scene.isDirty}");
            foreach (var root in scene.GetRootGameObjects())
            {
                b.AppendLine($"ROOT {root.name}, active={root.activeSelf}, pos={root.transform.position}, rotation={root.transform.eulerAngles}, scale={root.transform.lossyScale}");
                if (root.name.Contains("XR") || root.name.Contains("rift"))
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        b.AppendLine($"  {PathOf(t)} active={t.gameObject.activeSelf} pos={t.localPosition} rot={t.localEulerAngles} components={string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "MISSING"))}");
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers)
                        bounds.Encapsulate(r.bounds);
                    b.AppendLine($"  BOUNDS {bounds} renderers={renderers.Length}");
                }
            }
            foreach (var path in new[] { "Assets/_Game/Gwen/gwen.glb", "Assets/_Game/SummonersRIft/summoner_rift_3d_export.glb" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                b.AppendLine($"ASSET {path} importer={AssetImporter.GetAtPath(path)?.GetType().FullName}, prefab={prefab}");
                if (!prefab)
                    continue;
                b.AppendLine($"  root rot={prefab.transform.localEulerAngles} scale={prefab.transform.localScale}");
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    b.AppendLine($"  RENDERER {r.name}, bounds={r.bounds}, materials={string.Join(",", r.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "NULL"))}");
                foreach (var a in prefab.GetComponentsInChildren<Animator>(true))
                    b.AppendLine($"  Animator {a.name} avatar={a.avatar} humanoid={a.isHuman} controller={a.runtimeAnimatorController}");
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>();
                foreach (var clip in clips)
                    b.AppendLine($"  CLIP {clip.name} duration={clip.length} legacy={clip.legacy}");
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Report));
            File.WriteAllText(Report, b.ToString());
            SessionState.SetBool("LeagueVR.Inspected", true);
            Debug.Log("League VR inspection complete: " + Report);
        }

        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    }
}

