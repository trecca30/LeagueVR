using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRMapRebuild
    {
        const string Root = "Assets/_Game/SummonersRIft/VRRebuild";

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit play mode before rebuilding");
            var map = GameObject.Find("Summoner's Rift");
            if (!map)
                throw new InvalidOperationException("Open LeagueVR first");
            if (map.transform.Find("VR rebuilt map"))
                throw new InvalidOperationException("Map is already rebuilt");
            var scene = SceneManager.GetActiveScene();
            Directory.CreateDirectory("PrototypeBackups");
            EditorSceneManager.SaveScene(scene, "PrototypeBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-BeforeCompleteMapRebuild.unity", true);
            string preserved = LeagueVRMapWork.PreservedState();
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Materials");
            var originalRenderers = map.GetComponentsInChildren<Renderer>(true);
            var teleportSource = map.GetComponentsInChildren<TeleportationArea>().First();
            foreach (string p in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(p);
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.maxTextureSize = p.EndsWith("RiftGroundAtlas.png") ? 4096 : 2048;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var rebuilt = new GameObject("VR rebuilt map");
            rebuilt.transform.SetParent(map.transform, false);
            rebuilt.layer = LayerMask.NameToLayer("LeagueWorld");
            var materials = new Dictionary<string, Material>();
            var report = new StringBuilder("Original OBJ reconstructed with native material assignments and UV seams.\n");
            int triangles = 0;
            int count = 0;
            using (var reader = new BinaryReader(File.OpenRead(Root + "/map.bin")))
            {
                int total = reader.ReadInt32();
                for (int entry = 0; entry < total; entry++)
                {
                    string name = ReadString(reader), texture = ReadString(reader);
                    bool alpha = reader.ReadBoolean();
                    int vertices = reader.ReadInt32(), indexCount = reader.ReadInt32();
                    var points = new Vector3[vertices];
                    for (int i = 0; i < vertices; i++)
                        points[i] = map.transform.InverseTransformPoint(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                    var uv = new Vector2[vertices];
                    for (int i = 0; i < vertices; i++)
                        uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var indices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++)
                        indices[i] = reader.ReadInt32();
                    var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                    mesh.vertices = points;
                    mesh.uv = uv;
                    mesh.triangles = indices;
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, Root + "/Meshes/" + name + ".asset");
                    var go = new GameObject(name);
                    go.transform.SetParent(rebuilt.transform, false);
                    go.layer = rebuilt.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    if (texture != "")
                    {
                        if (!materials.TryGetValue(texture, out var mat))
                        {
                            bool foundation = texture == "RiftGroundAtlas.png";
                            mat = new Material(Shader.Find(foundation ? "LeagueVR/Rift Foundation Atlas" : "Universal Render Pipeline/Unlit")) { name = texture.Replace(".png", "") + " VR" };
                            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + texture));
                            mat.SetColor("_BaseColor", Color.white);
                            mat.SetFloat("_Cull", 0);
                            mat.enableInstancing = true;
                            if (alpha && !foundation)
                            {
                                mat.SetFloat("_AlphaClip", 1);
                                mat.SetFloat("_Cutoff", .25f);
                                mat.EnableKeyword("_ALPHATEST_ON");
                                mat.renderQueue = 2450;
                                mat.SetOverrideTag("RenderType", "TransparentCutout");
                            }
                            AssetDatabase.CreateAsset(mat, Root + "/Materials/" + mat.name + ".mat");
                            materials.Add(texture, mat);
                        }
                        if (name == "Boundary foundations")
                        {
                            var solid = new Material(mat) { name = "Sealed boundary stone" };
                            solid.SetFloat("_AlphaClip", 0);
                            solid.DisableKeyword("_ALPHATEST_ON");
                            solid.renderQueue = 2000;
                            solid.SetOverrideTag("RenderType", "Opaque");
                            solid.SetColor("_BaseColor", new Color(.55f, .62f, .59f));
                            AssetDatabase.CreateAsset(solid, Root + "/Materials/Sealed boundary stone.mat");
                            mat = solid;
                        }
                        var renderer = go.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = mat;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        triangles += indexCount / 3;
                        count++;
                    }
                    if (name == "Foundation collision")
                    {
                        var collider = go.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh;
                        var area = go.AddComponent<TeleportationArea>();
                        EditorUtility.CopySerialized(teleportSource, area);
                        area.colliders.Clear();
                        area.colliders.Add(collider);
                    }
                    report.AppendLine($"{name}: {vertices} vertices / {indexCount / 3} triangles / {texture}");
                }
            }
            foreach (var renderer in originalRenderers)
            {
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            if (LeagueVRMapWork.PreservedState() != preserved)
                throw new InvalidOperationException("Objects outside the map changed");
            Physics.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = rebuilt;
            SceneView.lastActiveSceneView?.FrameSelected();
            report.AppendLine($"Renderers {count}; visible triangles {triangles}; original map transforms, colliders, teleport areas, XR rig, Gwen and gameplay preserved.");
            File.WriteAllText("Logs/MapRebuild/rebuild.txt", report.ToString());
            Debug.Log("Map reconstruction saved: " + count + " renderers.");
        }

        static string ReadString(BinaryReader reader)
        {
            return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        }
    }
}
