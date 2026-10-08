using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LeagueVR.EditorTools
{
    /// <summary>
    /// Builds the PC VR game (Windows, OpenXR) into Builds/ and zips it for sharing.
    /// Menu: League VR > Build Windows. Command line (editor closed):
    /// Unity.exe -batchmode -quit -buildTarget Win64 -projectPath . -executeMethod LeagueVR.EditorTools.LeagueVRBuild.BuildWindowsBatch -logFile Logs/build.log
    /// </summary>
    public static class LeagueVRBuild
    {
        public const string Version = "0.9.0";
        const string Product = "League VR";
        const string Folder = "Builds/LeagueVR-Windows";
        const string Exe = "LeagueVR.exe";

        [MenuItem("League VR/Build Windows")]
        public static void BuildWindowsMenu()
        {
            var report = BuildWindows(out string zip);
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(zip);
        }

        /// <summary>Entry point for batch mode: exits with code 1 when the build fails.</summary>
        public static void BuildWindowsBatch()
        {
            var report = BuildWindows(out _);
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        public static BuildReport BuildWindows(out string zip)
        {
            PlayerSettings.productName = Product;
            PlayerSettings.bundleVersion = Version;
            // Mono ships with Unity's Windows support; IL2CPP needs an extra Hub module this machine does not have.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                scenes = new[] { "Assets/Scenes/LeagueVR.unity" };
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, true);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(Folder, Exe),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[LeagueVRBuild] {summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalSize / (1024f * 1024f):0} MB, {summary.totalTime.TotalMinutes:0.0} min -> {summary.outputPath}");
            zip = null;
            if (summary.result != BuildResult.Succeeded)
                return report;
            File.WriteAllText(Path.Combine(Folder, "HOW TO PLAY.txt"), HowToPlay);
            zip = Path.GetFullPath($"Builds/LeagueVR-Windows-{Version}.zip");
            Zip(Folder, zip);
            Debug.Log($"[LeagueVRBuild] Zipped to {zip} ({new FileInfo(zip).Length / (1024f * 1024f):0} MB)");
            return report;
        }

        /// <summary>Zips the build, leaving out Unity's debug-symbol folders that are not meant to ship.</summary>
        static void Zip(string folder, string zip)
        {
            if (File.Exists(zip))
                File.Delete(zip);
            var root = Path.GetFullPath(folder);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length + 1).Replace('\\', '/');
                if (relative.Split('/').Any(part => part.Contains("BackUpThisFolder") || part.EndsWith("_DoNotShip", StringComparison.Ordinal)))
                    continue;
                archive.CreateEntryFromFile(file, "LeagueVR/" + relative, System.IO.Compression.CompressionLevel.Optimal);
            }
        }

        const string HowToPlay = @"LEAGUE VR " + Version + @" - fan-made, non-commercial. Not endorsed by Riot Games.

Requirements
- Windows 10/11, a VR-ready GPU, and a PC VR headset with an OpenXR runtime:
  Quest via Meta Quest Link / Air Link, SteamVR headsets, or Windows Mixed Reality.
- Set your headset's software as the active OpenXR runtime (Meta Quest app: Settings > General > OpenXR Runtime;
  SteamVR: Settings > OpenXR).

Play
1. Connect the headset (for Quest: start Link or Air Link first).
2. Run LeagueVR.exe. Without a headset the game starts in desktop mode (WASD, right mouse to look).
3. Choose a champion in the menu and press PLAY.

Streaming to friends
- Menu > Comfort & audio > STREAM VIEW, or F8 in game: SMOOTH or SHOULDER makes the desktop window
  a viewer-friendly camera. Share the game window on Discord or capture it in OBS.

Controls: open the menu (right stick press) > CONTROLS.
";
    }
}
