using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRPOVWork
    {
        static double next;

        static LeagueVRPOVWork()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next)
                return;
            next = EditorApplication.timeSinceStartup + .5;
            const string p = "Temp/LeagueVRPOV.request";
            if (!File.Exists(p))
                return;
            string action = File.ReadAllText(p).Trim();
            File.Delete(p);
            Directory.CreateDirectory("Logs/POVOverhaul");
            try
            {
                if (action == "Inspect")
                    Inspect();
                else if (action == "Apply")
                    LeagueVRPOVBuild.Apply();
                else if (action == "Survey")
                    LeagueVRPOVBuild.Survey();
                else if (action == "Test1")
                    LeagueVRPOVTest.Begin(1);
                else if (action == "Test2")
                    LeagueVRPOVTest.Begin(2);
                else if (action == "MinionRepair")
                    LeagueVRPOVTest.Begin(3);
                else
                    Debug.LogError("Unknown POV action " + action);
            }
            catch (Exception e)
            {
                File.WriteAllText("Logs/POVOverhaul/error.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        static void Inspect()
        {
            var m = Object.FindAnyObjectByType<RiftMatch>();
            var a = m.player.GetComponent<GwenAvatar>();
            var b = new StringBuilder();
            b.AppendLine("Player origin=" + m.player.origin.transform.position + " originMode=" + m.player.origin.RequestedTrackingOriginMode + " currentMode=" + m.player.origin.CurrentTrackingOriginMode + " yOffset=" + m.player.origin.CameraYOffset + " headLocal=" + m.player.head.transform.localPosition + " near=" + m.player.head.nearClipPlane + " bodyScale=" + a.visualRoot.localScale);
            foreach (var t in m.player.origin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("Controller") || t.name.Contains("Hand") || t.GetComponent<Renderer>() || t.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>())
                    b.AppendLine("Rig " + PathFor(t) + " local=" + t.localPosition + " rot=" + t.localEulerAngles + " active=" + t.gameObject.activeSelf + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "missing")));
            }
            foreach (var t in new[] { a.leftUpper, a.leftLower, a.leftPalm, a.rightUpper, a.rightLower, a.rightPalm })
            {
                b.AppendLine("Arm " + t.name + " position=" + t.position + " rot=" + t.eulerAngles);
                foreach (Transform c in t)
                    b.AppendLine(" child " + c.name + " local=" + c.localPosition);
            }
            foreach (var l in m.lanes)
            {
                b.AppendLine("Lane " + l.name + " points=" + l.points.Length);
                for (int i = 0; i < l.points.Length; i++)
                    b.AppendLine(i + ":" + l.points[i].ToString("F3"));
            }
            foreach (var s in m.structures)
            {
                b.AppendLine("Structure " + s.name + " position=" + s.transform.position + " yaw=" + s.transform.eulerAngles.y + " modelYaw=" + s.visual.localEulerAngles.y);
                foreach (var r in s.visual.GetComponentsInChildren<Renderer>())
                    b.AppendLine(" bounds=" + r.bounds + " model=" + r.name);
            }
            File.WriteAllText("Logs/POVOverhaul/Before.txt", b.ToString());
            CaptureRoutes(m, "Before-routes");
        }

        static string PathFor(Transform t) => t.parent ? PathFor(t.parent) + "/" + t.name : t.name;

        public static void CaptureRoutes(RiftMatch m, string name)
        {
            var lines = new GameObject("Temporary route survey") { hideFlags = HideFlags.HideAndDontSave };
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                Color[] colors = { Color.cyan, Color.yellow, Color.magenta };
                for (int i = 0; i < m.lanes.Length; i++)
                {
                    var lr = new GameObject(m.lanes[i].name).AddComponent<LineRenderer>();
                    lr.transform.SetParent(lines.transform);
                    lr.sharedMaterial = new Material(mat);
                    lr.sharedMaterial.SetColor("_BaseColor", colors[i]);
                    lr.startWidth = lr.endWidth = .4f;
                    lr.positionCount = m.lanes[i].points.Length;
                    lr.SetPositions(m.lanes[i].points.Select(p => p + Vector3.up * 1).ToArray());
                }
                var camera = new GameObject("Temporary lane camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.orthographic = true;
                camera.orthographicSize = 87;
                camera.farClipPlane = 500;
                camera.transform.SetPositionAndRotation(new Vector3(0, 200, 8), Quaternion.Euler(90, 0, 0));
                var rt = new RenderTexture(2048, 2048, 24);
                camera.targetTexture = rt;
                var previous = RenderTexture.active;
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(2048, 2048, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0);
                image.Apply();
                File.WriteAllBytes("Logs/POVOverhaul/" + name + ".png", image.EncodeToPNG());
                RenderTexture.active = previous;
                RenderSettings.fog = fog;
                camera.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(camera.gameObject);
            }
            finally
            {
                foreach (var lr in lines.GetComponentsInChildren<LineRenderer>())
                    Object.DestroyImmediate(lr.sharedMaterial);
                Object.DestroyImmediate(lines);
                Object.DestroyImmediate(mat);
            }
        }
    }
}
