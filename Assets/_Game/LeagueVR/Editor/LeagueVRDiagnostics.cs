using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRDiagnostics
    {
        static double next;

        static LeagueVRDiagnostics()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next)
                return;
            next = EditorApplication.timeSinceStartup + 3;
            Capture();
        }

        [MenuItem("Tools/League VR/Capture Runtime Diagnostics")]
        public static void Capture()
        {
            var g = Object.FindFirstObjectByType<GwenAbilities>();
            if (!g)
                return;
            var b = new StringBuilder();
            var camera = g.head;
            var map = GameObject.Find("Summoner's Rift");
            b.AppendLine($"Scene={SceneManager.GetActiveScene().path} play={EditorApplication.isPlaying} desktop={g.DesktopMode} XR={XRSettings.isDeviceActive} stereo={camera.stereoEnabled} mode={XRSettings.stereoRenderingMode}");
            b.AppendLine($"Rig={g.origin.transform.position} head={camera.transform.position} localHead={camera.transform.localPosition} headEuler={camera.transform.eulerAngles} feet={g.Feet} spawn={g.spawn.position}");
            b.AppendLine($"Camera enabled={camera.enabled} mask={camera.cullingMask} near={camera.nearClipPlane} far={camera.farClipPlane} target={camera.targetTexture} offset={g.origin.CameraYOffset} trackingOrigin={g.origin.CurrentTrackingOriginMode}");
            b.AppendLine($"R controller={g.rightHand.position} active={g.rightHand.gameObject.activeInHierarchy}; L controller={g.leftHand.position} active={g.leftHand.gameObject.activeInHierarchy}");
            b.AppendLine($"Audio volume={AudioListener.volume} pause={AudioListener.pause} listeners={Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length} sources={Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length}");
            if (map)
            {
                var renderers = map.GetComponentsInChildren<Renderer>(true);
                b.AppendLine($"Map position={map.transform.position} scale={map.transform.lossyScale} rotation={map.transform.eulerAngles} renderers={renderers.Length} enabled={renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy)} visible={renderers.Count(r => r.isVisible)}");
                foreach (var r in renderers.Take(8))
                    b.AppendLine($"{r.name} layer={r.gameObject.layer} enabled={r.enabled} bounds={r.bounds} material={r.sharedMaterial?.name} shader={r.sharedMaterial?.shader.name} queue={r.sharedMaterial?.renderQueue}");
            }
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                b.AppendLine($"Camera {c.name} enabled={c.enabled} depth={c.depth} mask={c.cullingMask}");
            if (Physics.Raycast(camera.transform.position, Vector3.down, out var hit, 1000, g.worldMask))
                b.AppendLine($"Ground below head={hit.point} normal={hit.normal} collider={hit.collider.name}");
            else
                b.AppendLine("NO GROUND BELOW HEAD");
            File.WriteAllText("Logs/LeagueVR-runtime-diagnostics.txt", b.ToString());
        }
    }
}
