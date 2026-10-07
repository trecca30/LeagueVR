using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRMapPlayTest
    {
        static double start;
        static int stage;
        static RiftMatch match;
        static StringBuilder report;
        static List<string> errors = new List<string>();
        static int frames;
        static double frameSeconds;

        static LeagueVRMapPlayTest()
        {
            EditorApplication.update += Tick;
        }

        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start map play test from edit mode");
            SessionState.SetBool("LeagueVR.MapPlayTest", true);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool("LeagueVR.MapPlayTest", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling)
                return;
            try
            {
                if (stage == 0)
                {
                    start = EditorApplication.timeSinceStartup;
                    stage = 1;
                    report = new StringBuilder();
                    errors.Clear();
                    Application.logMessageReceived += Log;
                    match = Object.FindAnyObjectByType<RiftMatch>();
                    if (!match)
                        throw new InvalidOperationException("League Match missing");
                    report.AppendLine("Map test 3: live Play Mode, game cameras and physics. " + DateTime.Now);
                    report.AppendLine("XR active=" + XRSettings.isDeviceActive + "; device=" + XRSettings.loadedDeviceName);
                    report.AppendLine("XR input devices=" + string.Join(",", InputSystem.devices.Where(d => d.layout.Contains("XR") || d.layout.Contains("OpenXR")).Select(d => d.layout)));
                    report.AppendLine("Original Gwen/XR references valid=" + (match.player.origin && match.player.head && match.player.leftHand && match.player.rightHand));
                    report.AppendLine("Camera enabled=" + match.player.head.GetComponent<Camera>().enabled + "; XR rendering allowed=" + match.player.head.GetComponent<Camera>().GetUniversalAdditionalCameraData().allowXRRendering);
                }
                double elapsed = EditorApplication.timeSinceStartup - start;
                if (stage == 1 && elapsed > 3)
                {
                    CaptureGame("menu");
                    match.Play();
                    stage = 2;
                    start = EditorApplication.timeSinceStartup;
                    var map = GameObject.Find("Summoner's Rift");
                    var renderers = map.GetComponentsInChildren<MeshRenderer>();
                    int missing = renderers.Count(r => !r.sharedMaterial || !r.sharedMaterial.shader.isSupported || !r.sharedMaterial.GetTexture("_BaseMap"));
                    report.AppendLine("Map materials missing texture/unsupported shader=" + missing + " / " + renderers.Length);
                    report.AppendLine("Play starts at shop/fountain=" + match.AtShop + "; running=" + match.Running);
                    report.AppendLine("Map colliders=" + map.GetComponentsInChildren<Collider>().Length + "; teleport surfaces=" + map.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>().Length);
                    LeagueVRMapWork.Survey("Pass3");
                    StereoProbe();
                    Traverse();
                    CaptureGame("fountain");
                }
                if (stage == 2)
                {
                    frames++;
                    frameSeconds += Time.unscaledDeltaTime;
                    if (match.Seconds >= 40)
                    {
                        report.AppendLine("Simulation time=" + match.Seconds.ToString("0.0") + " sec; wave=" + match.Wave + "; spawned minions=" + match.spawnedRoot.childCount);
                        report.AppendLine("Player health=" + match.player.Health.Health + "; shop accessible=" + match.AtShop);
                        report.AppendLine("Editor frame rate (not a headset benchmark)=" + (frames / Math.Max(frameSeconds, .001)).ToString("0.0"));
                        CaptureGame("after-wave");
                        Finish();
                    }
                }
            }
            catch (Exception e)
            {
                if (report == null)
                    report = new StringBuilder();
                report.AppendLine("TEST FAILURE " + e);
                Finish();
            }
        }

        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(message.Split('\n')[0]);
        }

        static void Finish()
        {
            Application.logMessageReceived -= Log;
            report.AppendLine("Runtime errors: " + errors.Count);
            foreach (var e in errors.Distinct())
                report.AppendLine(e);
            Directory.CreateDirectory("Logs/MapRebuild/Pass3");
            File.WriteAllText("Logs/MapRebuild/Pass3/play-test.txt", report.ToString());
            SessionState.SetBool("LeagueVR.MapPlayTest", false);
            stage = 0;
            EditorApplication.isPlaying = false;
        }

        static void CaptureGame(string name)
        {
            var source = match.player.head.GetComponent<Camera>();
            var go = new GameObject("Temporary game verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(source);
            cam.enabled = false;
            cam.GetUniversalAdditionalCameraData().allowXRRendering = false;
            cam.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            Directory.CreateDirectory("Logs/MapRebuild/Pass3");
            try
            {
                LeagueVRMapWork.Capture(cam, rt, "Logs/MapRebuild/Pass3/game-" + name + ".png");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void StereoProbe()
        {
            var go = new GameObject("Temporary stereo texture probe") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.GetUniversalAdditionalCameraData().allowXRRendering = false;
            cam.fieldOfView = 85;
            cam.nearClipPlane = .05f;
            cam.farClipPlane = 500;
            cam.clearFlags = CameraClearFlags.Skybox;
            var rt = new RenderTexture(960, 720, 24);
            cam.targetTexture = rt;
            try
            {
                var points = new[] { new Vector3(-12, 0, -2), new Vector3(-22, -1.23f, 28), new Vector3(37, 0, -53), new Vector3(45, .5f, 55) };
                for (int i = 0; i < points.Length; i++)
                    for (int side = 0; side < 2; side++)
                    {
                        cam.transform.SetPositionAndRotation(points[i] + Vector3.up * 1.65f + Vector3.right * (side == 0 ? -.032f : .032f), Quaternion.Euler(12, 45, 0));
                        LeagueVRMapWork.Capture(cam, rt, $"Logs/MapRebuild/Pass3/stereo-{i}-{(side == 0 ? "left" : "right")}.png");
                    }
                report.AppendLine("Four pairs of 64 mm eye-offset renders captured; actual headset comfort is not tested by these renders.");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void Traverse()
        {
            int total = 0, miss = 0, obstructed = 0;
            var details = new StringBuilder();
            foreach (var lane in match.lanes)
                for (int i = 1; i < lane.points.Length; i++)
                {
                    var a = lane.points[i - 1];
                    var b = lane.points[i];
                    int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .5f);
                    for (int k = 0; k <= count; k++)
                    {
                        var p = Vector3.Lerp(a, b, k / (float)count);
                        total++;
                        if (!LeagueVRMapWork.Ground(p, out var hit))
                        {
                            miss++;
                            continue;
                        }
                        var bottom = hit.point + Vector3.up * .4f;
                        var top = hit.point + Vector3.up * 1.45f;
                        var overlaps = Physics.OverlapCapsule(bottom, top, .18f, match.player.worldMask, QueryTriggerInteraction.Ignore).Where(c => c.transform.IsChildOf(GameObject.Find("Summoner's Rift").transform)).ToArray();
                        if (overlaps.Any(c =>
                   {
                       if (c is MeshCollider mesh)
                           return mesh.Raycast(new Ray(p + Vector3.up * .85f, Vector3.right), out _, .18f) || mesh.Raycast(new Ray(p + Vector3.up * .85f, Vector3.left), out _, .18f) || mesh.Raycast(new Ray(p + Vector3.up * .85f, Vector3.forward), out _, .18f) || mesh.Raycast(new Ray(p + Vector3.up * .85f, Vector3.back), out _, .18f);
                       return false;
                   }))
                        {
                            obstructed++;
                            details.AppendLine($"{lane.name}: {p.x:0.00},{p.z:0.00}");
                        }
                    }
                }
            report.AppendLine($"Lane traversal probes: {total} samples, {miss} floor misses, {obstructed} possible wall conflicts.");
            File.WriteAllText("Logs/MapRebuild/Pass3/wall-conflicts.txt", details.ToString());
        }
    }
}


