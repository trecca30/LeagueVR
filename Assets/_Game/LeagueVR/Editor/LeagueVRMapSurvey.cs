using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRMapSurvey
    {
        [MenuItem("Tools/League VR/Survey Whole Map")]
        public static void Survey()
        {
            var map = GameObject.Find("Summoner's Rift");
            if (!map)
                return;
            Directory.CreateDirectory("Logs/RiftSurvey");
            var g = Object.FindFirstObjectByType<GwenAbilities>();
            var b = new StringBuilder();
            var go = new GameObject("Temporary map survey camera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.GetUniversalAdditionalCameraData().allowXRRendering = false;
            cam.cullingMask = g.worldMask;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 800;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.18f, .08f, .2f);
            var rt = new RenderTexture(1400, 1400, 24);
            cam.targetTexture = rt;
            cam.orthographic = true;
            cam.orthographicSize = 100;
            cam.transform.SetPositionAndRotation(new Vector3(0, 200, 0), Quaternion.Euler(90, 0, 0));
            Capture(cam, rt, "overview");
            cam.orthographic = false;
            cam.fieldOfView = 80;
            foreach (float x in new[] { -60f, 0f, 60f })
                foreach (float z in new[] { -60f, 0f, 60f })
                {
                    var from = new Vector3(x, 30, z);
                    var hits = Physics.RaycastAll(from, Vector3.down, 200, g.worldMask).OrderBy(h => h.distance).ToArray();
                    b.AppendLine($"Region x={x} z={z}: " + string.Join("; ", hits.Take(5).Select(h => $"{h.collider.name} y={h.point.y:0.00} normal={h.normal.y:0.00}")));
                    float y = hits.Length > 0 && hits[0].point.y > -15 ? hits[0].point.y : 0;
                    cam.transform.SetPositionAndRotation(new Vector3(x, y + 1.65f, z), Quaternion.LookRotation(new Vector3(-x, 0, -z == 0 ? 1 : -z)));
                    Capture(cam, rt, $"region_{x}_{z}");
                }
            var ground = map.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>().Select(a => a.GetComponent<Collider>()).ToArray();
            int holes = 0, total = 0;
            for (float x = -70; x <= 60; x += 2)
                for (float z = -60; z <= 65; z += 2)
                {
                    total++;
                    if (!Physics.RaycastAll(new Vector3(x, 30, z), Vector3.down, 50, g.worldMask).Any(h => ground.Contains(h.collider) && h.normal.y > .6f))
                        holes++;
                }
            b.AppendLine($"Ground grid: {holes}/{total} samples have no upward-facing terrain within playable elevation.");
            b.AppendLine("Audio clips=" + AssetDatabase.FindAssets("t:AudioClip").Length);
            File.WriteAllText("Logs/RiftSurvey/report.txt", b.ToString());
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log("Rift survey complete.");
        }

        static void Capture(Camera cam, RenderTexture rt, string name)
        {
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            File.WriteAllBytes("Logs/RiftSurvey/" + name + ".png", t.EncodeToPNG());
            RenderTexture.active = previous;
            Object.DestroyImmediate(t);
        }
    }
}
