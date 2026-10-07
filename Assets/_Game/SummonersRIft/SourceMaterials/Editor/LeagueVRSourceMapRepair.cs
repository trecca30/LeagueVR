using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LeagueVR.Editor
{
    public static class LeagueVRSourceMapRepair
    {
        const string Root = "Assets/_Game/SummonersRIft/SourceMaterials";

        [Serializable]
        class Data
        {
            public Record[] records;
        }

        [Serializable]
        class Record
        {
            public int mesh;
            public Vertex[] vertices;
            public int[] missing;
        }

        [Serializable]
        class Vertex
        {
            public float[] position, uv, sourceUV;
            public string texture;
            public bool alpha;
        }

        static string Key(Vector3 p) => $"{Mathf.RoundToInt(p.x * 10000)},{Mathf.RoundToInt(p.y * 10000)},{Mathf.RoundToInt(p.z * 10000)}";

        static string SceneSnapshot(GameObject map) => string.Join("\n", map.GetComponentsInChildren<Transform>(true).Select(t => $"{Path(t)}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|" + string.Join(",", t.GetComponents<Component>().Where(c => c).Select(c => c.GetType().FullName))));

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

        [MenuItem("Tools/League VR/Restore Map Source Textures")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit play mode before material repair.");
            var map = GameObject.Find("Summoner's Rift");
            if (!map)
                throw new InvalidOperationException("Map is not in this scene.");
            Directory.CreateDirectory("Logs/SourceMapRepair");
            Directory.CreateDirectory("PrototypeBackups");
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, "PrototypeBackups/Before-source-material-repair.unity", true);
            string before = SceneSnapshot(map);
            File.WriteAllText("Logs/SourceMapRepair/before-transforms.txt", before);
            Capture(map, "before");
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(Root + "/map-source-matching.json"));
            var textures = data.records.SelectMany(r => r.vertices).Where(v => !string.IsNullOrEmpty(v.texture)).Select(v => v.texture).Distinct().OrderBy(n => n).ToArray();
            if (textures.Length != 17)
                throw new InvalidOperationException("Unexpected source texture count: " + textures.Length);
            var array = new Texture2DArray(2048, 2048, textures.Length, TextureFormat.DXT5, true, false) { name = "Original Rift source textures", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            try
            {
                for (int slice = 0; slice < textures.Length; slice++)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + textures[slice]);
                    if (!source)
                        throw new InvalidOperationException("Texture not imported: " + textures[slice]);
                    var rt = RenderTexture.GetTemporary(2048, 2048, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var prior = RenderTexture.active;
                    var copy = new Texture2D(2048, 2048, TextureFormat.RGBA32, true, false);
                    try
                    {
                        Graphics.Blit(source, rt);
                        RenderTexture.active = rt;
                        copy.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0);
                        copy.Apply(true);
                        copy.Compress(true);
                        if (copy.format != TextureFormat.DXT5)
                            throw new InvalidOperationException("Expected DXT5");
                        for (int mip = 0; mip < copy.mipmapCount; mip++)
                            Graphics.CopyTexture(copy, 0, mip, array, slice, mip);
                    }
                    finally
                    {
                        RenderTexture.active = prior;
                        RenderTexture.ReleaseTemporary(rt);
                        Object.DestroyImmediate(copy);
                    }
                }
                AssetDatabase.CreateAsset(array, Root + "/Original source textures.asset");
                var shader = Shader.Find("LeagueVR/Rift Source Materials");
                if (!shader || !shader.isSupported)
                    throw new InvalidOperationException("Source material shader is unsupported.");
                var report = new System.Text.StringBuilder();
                foreach (var record in data.records)
                {
                    var renderer = map.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Object_" + (record.mesh + 2));
                    if (renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial.name != "Merged_materials")
                        throw new InvalidOperationException("Unexpected material on " + renderer.name);
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var points = mesh.vertices;
                    var uv = mesh.uv;
                    var lookup = record.vertices.Where(v => !string.IsNullOrEmpty(v.texture)).GroupBy(v => Key(new Vector3(v.position[0], v.position[1], v.position[2]))).ToDictionary(g => g.Key, g => g.ToArray());
                    // glTF converts handedness when importing positions. Discover and verify the actual conversion.
                    int[] bestPerm = null;
                    Vector3 bestSigns = Vector3.one;
                    int bestCount = -1;
                    foreach (var perm in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
                        foreach (int sx in new[] { -1, 1 })
                            foreach (int sy in new[] { -1, 1 })
                                foreach (int sz in new[] { -1, 1 })
                                {
                                    int count = points.Count(p => lookup.ContainsKey(Key(new Vector3(p[perm[0]] * sx, p[perm[1]] * sy, p[perm[2]] * sz))));
                                    if (count > bestCount)
                                    {
                                        bestCount = count;
                                        bestPerm = perm;
                                        bestSigns = new Vector3(sx, sy, sz);
                                    }
                                }
                    var used = new HashSet<int>(mesh.triangles);
                    var colors = new Color[256 * Mathf.CeilToInt(points.Length / 256f)];
                    int restored = 0;
                    for (int vi = 0; vi < points.Length; vi++)
                    {
                        var p = points[vi];
                        string key = Key(new Vector3(p[bestPerm[0]] * bestSigns.x, p[bestPerm[1]] * bestSigns.y, p[bestPerm[2]] * bestSigns.z));
                        if (!lookup.TryGetValue(key, out var choices))
                        {
                            if (used.Contains(vi))
                                throw new InvalidOperationException($"Unmatched used vertex {renderer.name}:{vi}");
                            continue;
                        }
                        // Source triangle records preserve UV seams. Compare both glTF V conventions.
                        var v = choices.OrderBy(c => Mathf.Min(Vector2.SqrMagnitude(uv[vi] - new Vector2(c.uv[0], c.uv[1])), Vector2.SqrMagnitude(uv[vi] - new Vector2(c.uv[0], 1 - c.uv[1])))).First();
                        colors[vi] = new Color(v.sourceUV[0], v.sourceUV[1], Array.IndexOf(textures, v.texture), v.alpha ? 1 : 0);
                        restored++;
                    }
                    var table = new Texture2D(256, colors.Length / 256, TextureFormat.RGBAFloat, false, true) { name = renderer.name + " source material lookup", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    table.SetPixels(colors);
                    table.Apply(false, false);
                    AssetDatabase.CreateAsset(table, Root + "/" + renderer.name + " material lookup.asset");
                    var material = new Material(shader) { name = renderer.name + " restored original materials" };
                    material.SetTexture("_SourceTextures", array);
                    material.SetTexture("_VertexMaterials", table);
                    material.SetFloat("_LookupWidth", 256);
                    material.SetFloat("_LookupHeight", table.height);
                    AssetDatabase.CreateAsset(material, Root + "/" + renderer.name + " restored.mat");
                    Undo.RecordObject(renderer, "Restore original Rift textures");
                    renderer.sharedMaterial = material;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    report.AppendLine($"{renderer.name}: {restored}/{points.Length} vertices mapped; source textures assigned; mesh={AssetDatabase.GetAssetPath(mesh)}");
                }
                if (SceneSnapshot(map) != before)
                    throw new InvalidOperationException("Map transforms/hierarchy/components changed during material repair.");
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Capture(map, "after");
                report.AppendLine("All map transforms, hierarchy, mesh references and components preserved. Only three renderer materials changed.");
                File.WriteAllText("Logs/SourceMapRepair/result.txt", report.ToString());
                Debug.Log("Original Rift materials restored: " + report);
            }
            catch (Exception ex)
            {
                File.WriteAllText("Logs/SourceMapRepair/error.txt", ex.ToString());
                Debug.LogException(ex);
                throw;
            }
        }

        public static void Capture(GameObject map, string name)
        {
            var cameraObject = new GameObject("Temporary Rift material verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.cullingMask = 1 << LayerMask.NameToLayer("LeagueWorld");
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .09f, .12f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 600;
            var rt = new RenderTexture(1600, 1600, 24);
            camera.targetTexture = rt;
            camera.orthographic = true;
            camera.orthographicSize = 85;
            camera.transform.SetPositionAndRotation(new Vector3(3, 180, 3), Quaternion.Euler(90, 0, 0));
            var prior = RenderTexture.active;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                File.WriteAllBytes("Logs/SourceMapRepair/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prior;
                camera.targetTexture = null;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
