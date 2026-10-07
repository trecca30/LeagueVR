using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRWorldRecovery
    {
        const string Root = "Assets/_Game/LeagueVR";

        [MenuItem("Tools/League VR/Recover Whole Map and Enable Audio")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode first.");
            var map = GameObject.Find("Summoner's Rift");
            var g = Object.FindFirstObjectByType<GwenAbilities>();
            if (!map || !g)
                throw new InvalidOperationException("Open LeagueVR first.");
            Directory.CreateDirectory("PrototypeBackups");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.Copy(SceneManager.GetActiveScene().path, "PrototypeBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-BeforeWorldRecovery.unity", true);
            var array = BuildTextureArray();
            string path = Root + "/Generated/Rift recovered terrain.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("LeagueVR/Rift Recovered Terrain"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_Tiles", array);
            material.SetVector("_MapOrigin", map.transform.position);
            material.SetFloat("_MapScale", map.transform.lossyScale.x);
            EditorUtility.SetDirty(material);
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && (mats[i].name == "Merged_materials" || mats[i].name == "Rift missing-texture fallback"))
                    {
                        mats[i] = material;
                        changed = true;
                    }
                if (changed)
                {
                    r.sharedMaterials = mats;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
            }
            BuildContinuousGround(map, g, material);
            AddAudio(g);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Whole-map terrain textures, continuous ground and champion audio saved.");
        }

        static Texture2DArray BuildTextureArray()
        {
            const string path = Root + "/Generated/RiftTerrainTiles.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (existing)
                return existing;
            var textures = new List<Texture2D>();
            foreach (char letter in "abcdefghijklmnopqrstuvwxy")
            {
                string p = Root + "/Art/RiftTerrain/grnd_terrain_" + letter + ".png";
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!importer)
                    throw new InvalidOperationException("Missing terrain tile " + p);
                importer.isReadable = true;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.maxTextureSize = 1024;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                textures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(p));
            }
            var first = textures[0];
            var array = new Texture2DArray(first.width, first.height, 25, first.format, true, false) { name = "Rift terrain A-Y", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int i = 0; i < 25; i++)
                for (int mip = 0; mip < first.mipmapCount; mip++)
                    array.SetPixelData(textures[i].GetPixelData<byte>(mip), mip, i);
            array.Apply(false, false);
            AssetDatabase.CreateAsset(array, path);
            return array;
        }

        static void BuildContinuousGround(GameObject map, GwenAbilities g, Material material)
        {
            var old = map.transform.Find("Recovered river lanes and foundations");
            if (old)
                Object.DestroyImmediate(old.gameObject);
            var terrain = map.GetComponentsInChildren<TeleportationArea>().Select(a => a.GetComponent<Collider>()).ToHashSet();
            Physics.SyncTransforms();
            float scale = map.transform.lossyScale.x;
            var origin = map.transform.position;
            float minX = origin.x - 12.441667f * scale, minZ = origin.z - 10.416912f * scale, size = 4.577887f * 5 * scale;
            int cells = Mathf.CeilToInt(size / .65f), side = cells + 1;
            float step = size / cells;
            var heights = new float[side * side];
            var known = new bool[heights.Length];
            int holes = 0;
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    var hits = Physics.RaycastAll(new Vector3(minX + x * step, 25, minZ + z * step), Vector3.down, 45, g.worldMask, QueryTriggerInteraction.Ignore);
                    var valid = hits.Where(h => terrain.Contains(h.collider) && h.normal.y > .6f && h.point.y > -14 && h.point.y < 5).OrderByDescending(h => h.point.y).ToArray();
                    if (valid.Length > 0)
                    {
                        known[i] = true;
                        heights[i] = valid[0].point.y;
                    }
                    else
                        holes++;
                }
            // Interpolate across missing faces from nearby real terrain. Keep the intact GLB geometry above it.
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    if (known[i])
                        continue;
                    float sum = 0, weight = 0;
                    for (int radius = 1; radius <= 18 && weight == 0; radius++)
                        for (int dz = -radius; dz <= radius; dz++)
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius)
                                    continue;
                                int nx = x + dx, nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= side || nz >= side || !known[nz * side + nx])
                                    continue;
                                float w = 1f / (dx * dx + dz * dz);
                                sum += heights[nz * side + nx] * w;
                                weight += w;
                            }
                    heights[i] = weight > 0 ? sum / weight : 0;
                }
            var vertices = new Vector3[heights.Length];
            var indices = new int[cells * cells * 6];
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    vertices[i] = map.transform.InverseTransformPoint(new Vector3(minX + x * step, heights[i] - .06f, minZ + z * step));
                }
            int at = 0;
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int a = z * side + x;
                    indices[at++] = a;
                    indices[at++] = a + side;
                    indices[at++] = a + 1;
                    indices[at++] = a + 1;
                    indices[at++] = a + side;
                    indices[at++] = a + side + 1;
                }
            string path = Root + "/Generated/RiftContinuousGround.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = new Mesh { name = "Recovered river lanes and foundations", indexFormat = IndexFormat.UInt32 };
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
                mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            EditorUtility.SetDirty(mesh);
            var go = new GameObject("Recovered river lanes and foundations");
            go.transform.SetParent(map.transform, false);
            go.layer = LayerMask.NameToLayer("LeagueWorld");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var area = go.AddComponent<TeleportationArea>();
            area.teleportationProvider = g.origin.GetComponentInChildren<TeleportationProvider>(true);
            area.interactionLayers = unchecked((int)0x80000000);
            area.matchOrientation = MatchOrientation.WorldSpaceUp;
            area.filterSelectionByHitNormal = true;
            area.upNormalToleranceDegrees = 35;
            area.colliders.Add(collider);
            Physics.SyncTransforms();
            File.WriteAllText("Logs/LeagueVR-world-recovery.txt", $"25 matching terrain tiles restored.\nGround samples: {heights.Length}; missing samples filled: {holes}.\nGround bounds: {collider.bounds}\nOriginal GLB geometry retained.\n");
        }

        public static void AddAudio(GwenAbilities g)
        {
            string folder = Root + "/Audio";
            Directory.CreateDirectory(folder);
            foreach (string name in new[] { "Scissors", "Snip", "Mist", "Dash", "Needle", "Impact", "Hurt", "Footstep" })
            {
                string p = folder + "/" + name + ".wav";
                if (!File.Exists(p))
                    MakeSound(p, name);
                AssetDatabase.ImportAsset(p);
            }
            var audio = g.GetComponent<GwenAudio>();
            if (!audio)
                audio = g.gameObject.AddComponent<GwenAudio>();
            AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(folder + "/" + name + ".wav");
            audio.scissors = Clip("Scissors");
            audio.snip = Clip("Snip");
            audio.mist = Clip("Mist");
            audio.dash = Clip("Dash");
            audio.needle = Clip("Needle");
            audio.impact = Clip("Impact");
            audio.hurt = Clip("Hurt");
            audio.footstep = Clip("Footstep");
            audio.ambience = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/DemoAssets/Audio/AmbientBackgroundNoise.mp3");
            var listener = g.head.GetComponent<AudioListener>();
            if (!listener)
                listener = g.head.gameObject.AddComponent<AudioListener>();
            listener.enabled = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
        }

        static void MakeSound(string path, string kind)
        {
            const int rate = 44100;
            float duration = kind == "Mist" ? 1.4f : kind == "Dash" ? .45f : kind == "Needle" ? .4f : .2f;
            int count = (int)(rate * duration);
            var random = new System.Random(137 + kind.Length);
            float filtered = 0;
            var samples = new short[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate, u = t / duration, noise = (float)random.NextDouble() * 2 - 1;
                filtered = Mathf.Lerp(filtered, noise, .16f);
                float envelope = Mathf.Min(1, t / .012f) * Mathf.Pow(1 - u, 2);
                float sample;
                if (kind == "Mist")
                    sample = (filtered * .5f + Mathf.Sin(2 * Mathf.PI * 330 * t) * .13f + Mathf.Sin(2 * Mathf.PI * 495 * t) * .1f) * Mathf.Sin(Mathf.PI * u);
                else if (kind == "Needle")
                    sample = Mathf.Sin(2 * Mathf.PI * (650 * t + 600 * t * t)) * .3f + filtered * .15f;
                else if (kind == "Dash")
                    sample = filtered * .8f + Mathf.Sin(2 * Mathf.PI * (180 * t - 120 * t * t)) * .15f;
                else if (kind == "Scissors" || kind == "Snip")
                    sample = noise * .18f + Mathf.Sin(2 * Mathf.PI * 1800 * t) * .18f * Mathf.Exp(-25 * t) + Mathf.Sin(2 * Mathf.PI * 2711 * t) * .13f * Mathf.Exp(-35 * t);
                else
                    sample = filtered * .6f + Mathf.Sin(2 * Mathf.PI * (kind == "Hurt" ? 95 : 140) * t) * .3f;
                samples[i] = (short)(Mathf.Clamp(sample * envelope, -.8f, .8f) * short.MaxValue);
            }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + count * 2);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(count * 2);
                foreach (short s in samples)
                    w.Write(s);
            }
        }
    }
}
