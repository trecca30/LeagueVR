using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRShopWork
    {
        const string Root = "Assets/_Game/LeagueVR/Match", Art = Root + "/Art/Items3D";
        static double next;

        static LeagueVRShopWork()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next)
                return;
            next = EditorApplication.timeSinceStartup + 1;
            const string request = "Temp/LeagueVRShop.request";
            if (!File.Exists(request))
                return;
            var action = File.ReadAllText(request).Trim();
            File.Delete(request);
            Directory.CreateDirectory("Logs/ShopOverhaul");
            try
            {
                if (action == "Apply")
                    Apply();
                else if (action == "Test")
                    LeagueVRShopTest.Begin();
                else
                    throw new Exception("Unknown shop action: " + action);
            }
            catch (Exception e)
            {
                File.WriteAllText("Logs/ShopOverhaul/error.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        [Serializable]
        class Models
        {
            public Model[] models;
        }

        [Serializable]
        class Model
        {
            public string name;
            public string[] materials;
            public int triangles;
        }

        static void Apply()
        {
            if (EditorApplication.isPlaying)
                throw new Exception("Apply requires edit mode.");
            var match = Object.FindFirstObjectByType<RiftMatch>();
            if (!match || !EditorSceneManager.GetActiveScene().path.EndsWith("LeagueVR.unity"))
                throw new Exception("Open LeagueVR.unity first.");
            var evidence = new StringBuilder();
            evidence.AppendLine("SHOP INTEGRATION " + DateTime.Now.ToString("O"));
            evidence.AppendLine("Scene and existing map/XR objects preserved. Item data, UI and combat item hooks updated.");
            Directory.CreateDirectory(Art + "/Materials");
            Directory.CreateDirectory(Art + "/Prefabs");
            foreach (string path in Directory.GetFiles(Art, "*.png", SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(Root + "/Art/ItemIcons", "*.png")))
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.mipmapEnabled = true;
                    ti.maxTextureSize = path.Contains("Leather") ? 1024 : 256;
                    ti.textureCompression = TextureImporterCompression.Compressed;
                    ti.SaveAndReimport();
                }
            }
            var models = JsonUtility.FromJson<Models>(File.ReadAllText(Art + "/Models/manifest.json"));
            foreach (var model in models.models)
            {
                string path = Art + "/Models/" + model.name + ".obj";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                importer.globalScale = 1;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.importNormals = ModelImporterNormals.Import;
                importer.SaveAndReimport();
            }
            var imported = JsonUtility.FromJson<CatalogImport>(File.ReadAllText(Root + "/Data/catalog.json"));
            var catalog = AssetDatabase.LoadAssetAtPath<LeagueCatalog>(Root + "/Data/League Items.asset");
            catalog.patch = imported.patch;
            catalog.researchedOn = imported.researchedOn;
            catalog.source = imported.source;
            catalog.items = imported.items;
            catalog.Refresh();
            foreach (var item in catalog.items)
            {
                item.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/ItemIcons/" + item.id + ".png");
                if (item.active == ItemActive.None)
                    continue;
                string family = Family(item.active);
                var model = models.models.First(m => m.name == family);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/" + family + ".obj");
                if (!source)
                    throw new Exception("Missing model: " + family);
                var root = new GameObject(item.name + " — physical active");
                var visual = Object.Instantiate(source, root.transform);
                visual.name = family + " mesh";
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    int count = mesh ? mesh.subMeshCount : renderer.sharedMaterials.Length;
                    string group = model.materials.FirstOrDefault(m => renderer.name.Contains(m)) ?? model.materials.FirstOrDefault(m => mesh && mesh.name.Contains(m));
                    renderer.sharedMaterials = Enumerable.Range(0, count).Select(n => MaterialFor(group ?? model.materials[Mathf.Min(n, model.materials.Length - 1)], item)).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                // A textured, double-sided inset badge keeps related item variants recognisable.
                var badgeMat = SaveMaterial("Badge-" + item.id, new Color(1, 1, 1), item.icon, false, 0);
                for (int side = 0; side < 2; side++)
                {
                    var badge = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    badge.name = "Original Riot icon inset";
                    Object.DestroyImmediate(badge.GetComponent<Collider>());
                    badge.transform.SetParent(root.transform, false);
                    badge.transform.localPosition = new Vector3(0, .17f, side == 0 ? -.087f : .087f);
                    badge.transform.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0);
                    badge.transform.localScale = Vector3.one * .065f;
                    badge.GetComponent<Renderer>().sharedMaterial = badgeMat;
                }
                root.transform.localScale = Vector3.one * .75f;
                if (family == "Potion")
                {
                    var tip = new GameObject("Drink tip");
                    tip.transform.SetParent(root.transform, false);
                    tip.transform.localPosition = Vector3.up * .32f;
                }
                var box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(0, .17f, 0);
                box.size = new Vector3(.19f, .34f, .20f);
                root.layer = 8;
                item.physicalPrefab = PrefabUtility.SaveAsPrefabAsset(root, Art + "/Prefabs/" + item.id + ".prefab");
                Object.DestroyImmediate(root);
                evidence.AppendLine($"{item.id} {item.name}: {family}; {item.physicalPrefab.GetComponentsInChildren<MeshRenderer>().Length} mesh parts; {model.triangles} source triangles.");
            }
            match.catalog = catalog;
            match.ui.gwenPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/GwenPortrait.jpg");
            var feedback = match.player.GetComponent<GwenFeedback>();
            if (feedback && feedback.status)
            {
                feedback.status.gameObject.SetActive(false);
                EditorUtility.SetDirty(feedback.status);
            }
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(match.ui);
            EditorUtility.SetDirty(match);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            evidence.AppendLine($"Riot patch {catalog.patch}: {catalog.items.Count(i => i.showInShop)} shop items; {catalog.items.Count(i => !i.showInShop)} automatic forms; {catalog.items.Count(i => i.active != ItemActive.None)} physical actives.");
            File.WriteAllText("Logs/ShopOverhaul/Integration.txt", evidence.ToString());
            Debug.Log("League VR shop/item/UI integration saved.");
        }

        static string Family(ItemActive active) => active switch
        {
            ItemActive.Potion or ItemActive.Refillable or ItemActive.ElixirIron or ItemActive.ElixirSorcery or ItemActive.ElixirWrath => "Potion",
            ItemActive.Stasis => "Hourglass",
            ItemActive.SingleStasis => "Armguard",
            ItemActive.Actualizer => "Tome",
            ItemActive.Shurelya => "Scepter",
            ItemActive.Hydra or ItemActive.Ravenous or ItemActive.Titanic or ItemActive.Stridebreaker or ItemActive.Profane => "Axe",
            ItemActive.Ghostblade or ItemActive.Mercurial => "Sword",
            ItemActive.Quicksilver => "Sash",
            ItemActive.Randuin => "Shield",
            ItemActive.Gunblade => "Gunblade",
            ItemActive.Rocketbelt => "Rocketbelt",
            ItemActive.Redemption => "Relic",
            ItemActive.Mikael => "Chalice",
            ItemActive.Vow or ItemActive.Locket => "Pendant",
            ItemActive.Oracle => "Oracle",
            ItemActive.BlackSpear => "Spear",
            _ => "Ward"
        };

        static Material MaterialFor(string name, LeagueItem item)
        {
            var gem = item.id == 2055 || item.id == 2140 ? new Color(.8f, .06f, .15f) : item.id == 2138 ? new Color(.67f, .66f, .8f) : item.id == 2139 ? new Color(.25f, .12f, .8f) : new Color(.05f, .68f, .78f);
            return name switch
            {
                "Gold" or "Metal" => SaveMaterial(name, new Color(.75f, .53f, .19f), null, false, .8f),
                "Steel" => SaveMaterial(name, new Color(.20f, .27f, .34f), null, false, .75f),
                "Leather" => SaveMaterial(name, new Color(.37f, .17f, .12f), AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/LOLVR-Potion-Leather.png"), false, 0),
                "Glass" => SaveMaterial(name, new Color(.90f, .95f, 1, .10f), null, true, .1f),
                "Potion" => SaveMaterial("Liquid-" + item.id, item.id == 2031 ? new Color(.11f, .67f, .34f) : item.id == 2003 ? new Color(.85f, .04f, .15f) : gem, null, false, .15f),
                "Gem" => SaveMaterial("Gem-" + item.id, gem, null, false, .2f),
                "Sand" => SaveMaterial(name, new Color(.97f, .82f, .40f), null, false, 0),
                "Bork" => SaveMaterial(name, new Color(.44f, .25f, .10f), null, false, 0),
                _ => SaveMaterial(name, new Color(.3f, .45f, .53f), null, false, .1f)
            };
        }

        static Material SaveMaterial(string name, Color color, Texture texture, bool transparent, float metal)
        {
            string path = Art + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", metal > 0 ? .62f : .35f);
            m.SetTexture("_BaseMap", texture);
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
