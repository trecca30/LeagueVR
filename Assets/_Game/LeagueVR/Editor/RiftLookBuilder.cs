using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeagueVR.EditorTools
{
    /// <summary>
    /// Gives Summoner's Rift its VR look. Every map material moves from flat unlit to LeagueVR/Rift Stylized (sunlight
    /// bands, relief from the painting, cliff stretch cleanup, sky rim). A distance haze (RiftAtmosphere) is added, and
    /// a horizon ground fills the black voids beyond the map's edge. Menu: League VR > Apply Rift Look; the switch back restores Unlit.
    /// </summary>
    public static class RiftLookBuilder
    {
        const string MapRoot = "Summoner's Rift", Pieces = "VR rebuilt map", Horizon = "Rift horizon ground";
        static readonly Color FogColor = new(.7f, .79f, .86f);

        /// <summary>The look's settings for every map material, in one place.</summary>
        static void Tune(Material material, bool cliffs)
        {
            material.SetFloat("_Light", .5f);
            material.SetFloat("_Bands", 3);
            material.SetFloat("_Relief", 1.2f);
            material.SetFloat("_Steep", cliffs ? .4f : 0);
            material.SetFloat("_Grain", .25f);
            material.SetFloat("_Rim", .12f);
        }

        [MenuItem("League VR/Apply Rift Look")]
        public static void ApplyMenu() => Debug.Log(Apply(true));

        [MenuItem("League VR/Revert Rift Look (unlit)")]
        public static void RevertMenu() => Debug.Log(Apply(false));

        public static string Apply(bool stylized)
        {
            var pieces = GameObject.Find(MapRoot)?.transform.Find(Pieces);
            if (!pieces)
                return "No map in the open scene.";
            var lit = Shader.Find("LeagueVR/Rift Stylized");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (!lit || !unlit)
                return "Shaders missing.";
            int changed = 0, skipped = 0;
            var materials = pieces.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct();
            foreach (var material in materials)
            {
                bool transparent = material.renderQueue >= 2500 || (material.HasProperty("_Surface") && material.GetFloat("_Surface") > .5f);
                if (transparent || (material.shader != unlit && material.shader != lit) || (!stylized && material.shader != lit))
                {
                    skipped++;
                    continue;
                }
                var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
                var tiling = material.HasProperty("_BaseMap") ? material.GetTextureScale("_BaseMap") : Vector2.one;
                var offset = material.HasProperty("_BaseMap") ? material.GetTextureOffset("_BaseMap") : Vector2.zero;
                var tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                bool clip = material.IsKeywordEnabled("_ALPHATEST_ON") || (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f);
                float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : .5f;
                Undo.RecordObject(material, "Rift look");
                material.shader = stylized ? lit : unlit;
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling);
                material.SetTextureOffset("_BaseMap", offset);
                material.SetColor("_BaseColor", tint);
                material.SetFloat("_Cutoff", cutoff);
                material.SetFloat("_AlphaClip", clip ? 1 : 0);
                if (clip)
                    material.EnableKeyword("_ALPHATEST_ON");
                else
                    material.DisableKeyword("_ALPHATEST_ON");
                if (stylized)
                    Tune(material, !material.name.StartsWith("grnd_terrain"));
                else
                    material.SetFloat("_Cull", 0);
                EditorUtility.SetDirty(material);
                changed++;
            }

            // Haze comes from RiftAtmosphere (true distance per pixel); Unity's fog stays off.
            RenderSettings.fog = false;

            var horizon = GameObject.Find(Horizon);
            if (stylized && !horizon)
            {
                horizon = GameObject.CreatePrimitive(PrimitiveType.Plane);
                horizon.name = Horizon;
                Object.DestroyImmediate(horizon.GetComponent<Collider>());
                // Just under the lowest floor (the river), wide enough to reach the fog.
                horizon.transform.SetPositionAndRotation(new Vector3(0, -2.2f, 8), Quaternion.identity);
                horizon.transform.localScale = new Vector3(60, 1, 60);
                var material = new Material(lit) { name = "Rift horizon ground" };
                material.SetColor("_BaseColor", new Color(.2f, .27f, .22f));
                material.SetFloat("_Light", .25f);
                material.SetFloat("_Relief", 0);
                material.SetFloat("_Rim", 0);
                AssetDatabase.CreateAsset(material, "Assets/_Game/SummonersRIft/VRRebuild/Rift horizon ground.mat");
                var renderer = horizon.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (horizon)
            {
                horizon.SetActive(stylized);
                if (!horizon.TryGetComponent<RiftAtmosphere>(out var atmosphere))
                    atmosphere = horizon.AddComponent<RiftAtmosphere>();
                atmosphere.color = FogColor;
            }
            var scene = pieces.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return $"{(stylized ? "stylized" : "unlit")}: {changed} materials changed, {skipped} left as they were; haze {(stylized ? "on" : "off")}";
        }
    }
}
