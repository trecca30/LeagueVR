using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace LeagueVR
{
    /// <summary>Player comfort options, saved between sessions.</summary>
    public static class ComfortSettings
    {
        const string VignetteKey = "LeagueVR.VignetteLevel";
        const string SkyViewKey = "LeagueVR.SkyView";

        /// <summary>
        /// Big leaps (Pantheon's Grand Starfall) show the landing from high above instead of fading out.
        /// The view itself never moves smoothly: it cuts behind a short fade both ways.
        /// </summary>
        public static bool SkyView
        {
            get => PlayerPrefs.GetInt(SkyViewKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SkyViewKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static readonly string[] VignetteNames = { "OFF", "VERY LIGHT", "LIGHT", "MEDIUM", "STRONG" };
        // Aperture is the diameter of the clear circle (bigger = less black); "STRONG" is the XR template's default.
        static readonly float[] Aperture = { 2f, 1.35f, 1.1f, .9f, .7f };
        static readonly float[] Feathering = { 0f, .4f, .32f, .26f, .2f };
        public const int DefaultVignetteLevel = 4;

        /// <summary>How strongly the comfort vignette darkens the edges of the view while moving or turning.</summary>
        public static int VignetteLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VignetteKey, DefaultVignetteLevel), 0, VignetteNames.Length - 1);
            set
            {
                PlayerPrefs.SetInt(VignetteKey, Mathf.Clamp(value, 0, VignetteNames.Length - 1));
                PlayerPrefs.Save();
                ApplyVignette();
            }
        }

        public static string VignetteName => VignetteNames[VignetteLevel];

        /// <summary>Pushes the saved strength to every tunneling vignette in the scene.</summary>
        public static void ApplyVignette()
        {
            int level = VignetteLevel;
            foreach (var controller in Object.FindObjectsByType<TunnelingVignetteController>(FindObjectsInactive.Include))
            {
                Configure(controller.defaultParameters, level);
                foreach (var provider in controller.locomotionVignetteProviders)
                {
                    provider.enabled = level > 0;
                    if (provider.overrideDefaultParameters)
                        Configure(provider.overrideParameters, level);
                }
            }
        }

        static void Configure(VignetteParameters parameters, int level)
        {
            if (parameters == null)
                return;
            parameters.apertureSize = Aperture[level];
            parameters.featheringEffect = Feathering[level];
        }
    }
}
