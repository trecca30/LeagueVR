using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace LeagueVR.Match
{
    public static class RiftUIVisuals
    {
        const int InterfaceQueue = 4000;
        static Material panel;
        static readonly Dictionary<Material, Material> fonts = new();

        static Shader OverlayShader(string resource, string name)
        {
            // Resources keeps both interface shaders available in player builds.
            var shader = Resources.Load<Shader>(resource);
            if (!shader)
                shader = Shader.Find(name);
            return shader && shader.isSupported ? shader : null;
        }

        public static void Graphic(Graphic graphic)
        {
            if (!panel)
            {
                var shader = OverlayShader("RiftPanelOverlay", "LeagueVR/UI Panel Overlay");
                if (!shader)
                    return;
                panel = new Material(shader) { name = "Rift interface panels", hideFlags = HideFlags.HideAndDontSave, renderQueue = InterfaceQueue };
            }
            graphic.material = panel;
        }

        public static void Text(TMP_Text text)
        {
            var original = text.fontSharedMaterial;
            if (!original)
                return;
            if (!fonts.TryGetValue(original, out var material) || !material)
            {
                var shader = OverlayShader("RiftTextOverlay", "LeagueVR/UI Text Overlay");
                // Preserve the working font atlas/material if an overlay shader cannot load.
                material = new Material(original) { name = original.name + " (Rift interface)", hideFlags = HideFlags.HideAndDontSave };
                if (shader)
                    material.shader = shader;
                material.renderQueue = InterfaceQueue;
                fonts[original] = material;
            }
            text.fontSharedMaterial = material;
        }
    }
}
