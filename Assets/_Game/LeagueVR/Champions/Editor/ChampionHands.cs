using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem.XR;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Champions.Editor
{
    [InitializeOnLoad]
    public static class ChampionHandsWork
    {
        static ChampionHandsWork()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += State;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || !File.Exists("Temp/LeagueChampions.hands"))
                return;
            File.Delete("Temp/LeagueChampions.hands");
            SessionState.SetBool("Champions.HandsPending", true);
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        static void State(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Champions.HandsPending", false))
            {
                SessionState.SetBool("Champions.HandsPending", false);
                new GameObject("Champion wrist visual check").AddComponent<ChampionHandsProbe>();
            }
        }
    }

    public class ChampionHandsProbe : MonoBehaviour
    {
        readonly ConcurrentQueue<string> renderErrors = new();

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += Collect;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= Collect;
        }

        void Collect(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || message.Contains("Invalid AABB"))
                renderErrors.Enqueue(message);
        }

        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

        IEnumerator Start()
        {
            yield return null;
            yield return null;
            var m = Object.FindAnyObjectByType<RiftMatch>();
            var p = m.player;
            var roster = p.GetComponent<ChampionRoster>();
            var results = new List<string>();
            foreach (var t in p.origin.GetComponentsInChildren<TrackedPoseDriver>(true))
                t.enabled = false;
            foreach (var c in p.origin.GetComponentsInChildren<MonoBehaviour>(true))
                if (c && c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion"))
                    c.enabled = false;
            p.GetComponent<GwenVRInput>().enabled = false;
            p.origin.GetComponent<CharacterController>().enabled = false;
            m.economy.enabled = false;
            foreach (var t in m.structures)
                t.enabled = false;
            foreach (var t in m.objectives)
                t.enabled = false;
            foreach (var t in m.fountains)
                t.enabled = false;
            var camera = new GameObject("Final champion hand camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = .025f;
            camera.fieldOfView = 80;
            camera.farClipPlane = 350;
            for (int i = 1; i < roster.champions.Length; i++)
            {
                roster.Select(i);
                m.Play();
                typeof(RiftMatch).GetProperty("NextWave").SetValue(m, 100000f);
                p.DesktopMode = false;
                var arena = new Vector3(-5.6f, 0, 1.4f);
                p.origin.transform.position = arena;
                p.head.transform.SetPositionAndRotation(arena + Vector3.up * 1.65f, Quaternion.identity);
                p.leftHand.SetPositionAndRotation(p.head.transform.position + new Vector3(-.24f, -.35f, .45f), Quaternion.identity);
                p.rightHand.SetPositionAndRotation(p.head.transform.position + new Vector3(.24f, -.35f, .45f), Quaternion.identity);
                roster.avatar.UpdatePose();
                yield return null;
                camera.transform.SetPositionAndRotation(p.head.transform.position, Quaternion.Euler(12, 0, 0));
                ChampionBuild.Capture(camera, "Final-" + roster.Active.name + "-hands");
                if (roster.Active.id == ChampionId.Brand || roster.Active.id == ChampionId.Yunara)
                {
                    foreach (var palm in new[] { roster.avatar.LeftPalm, roster.avatar.RightPalm })
                    {
                        foreach (var bone in palm.GetComponentsInChildren<Transform>().Where(t => new[] { "middle", "index", "pinky" }.Any(n => t.name.ToLowerInvariant().Contains(n))))
                            results.Add(roster.Active.name + " runtime " + bone.name + " forward vector=" + (bone.position - palm.position).ToString("F4") + " local scale=" + bone.lossyScale);
                        var filter = palm.GetComponentInChildren<MeshFilter>();
                        results.Add(roster.Active.name + " mesh rotation=" + filter.transform.rotation.eulerAngles + " local=" + filter.transform.localRotation.eulerAngles + " palm=" + palm.rotation.eulerAngles + " scale=" + filter.transform.lossyScale);
                        results.Add(roster.Active.name + " runtime mesh " + filter.name + " bounds=" + filter.sharedMesh.bounds + " forward min=" + filter.sharedMesh.vertices.Min(v => Vector3.Dot(filter.transform.TransformPoint(v) - palm.position, Vector3.forward)) + " max=" + filter.sharedMesh.vertices.Max(v => Vector3.Dot(filter.transform.TransformPoint(v) - palm.position, Vector3.forward)));
                    }
                    camera.transform.SetPositionAndRotation(p.head.transform.position + Vector3.right * .8f, Quaternion.Euler(20, -55, 0));
                    ChampionBuild.Capture(camera, "Side-" + roster.Active.name + "-hands");
                    camera.transform.SetPositionAndRotation(p.head.transform.position, Quaternion.identity);
                    var full = Instantiate(roster.Active.model);
                    full.transform.SetPositionAndRotation(arena + Vector3.forward * 2.5f, Quaternion.Euler(0, 180, 0));
                    roster.Active.idle.SampleAnimation(full, 0);
                    foreach (var renderer in roster.avatar.Instance.GetComponentsInChildren<Renderer>())
                        renderer.forceRenderingOff = true;
                    camera.transform.rotation = Quaternion.identity;
                    ChampionBuild.Capture(camera, "Source-" + roster.Active.name + "-anatomy");
                    Destroy(full);
                    foreach (var bone in roster.avatar.Instance.GetComponentsInChildren<Transform>())
                        if (new[] { "l_forearm", "r_forearm", "l_elbow", "r_elbow", "l_hand", "r_hand", "l_hand_twist", "r_hand_twist", "l_arm_twist", "r_arm_twist" }.Contains(bone.name.ToLowerInvariant()))
                            results.Add(roster.Active.name + " " + bone.name + " position=" + bone.position + " local=" + bone.localPosition + " parent=" + bone.parent.name);
                }
                foreach (var renderer in roster.avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled))
                {
                    var mesh = renderer.sharedMesh;
                    bool skin = mesh.bindposes.Length == renderer.bones.Length && mesh.boneWeights.Length == mesh.vertexCount && mesh.vertices.All(Finite);
                    var baked = new Mesh();
                    renderer.BakeMesh(baked);
                    bool valid = baked.vertexCount == mesh.vertexCount && baked.vertices.All(Finite);
                    Destroy(baked);
                    results.Add(roster.Active.name + ": " + (skin && valid ? "PASS" : "FAIL") + " persisted skin weights, bind poses and finite rendered vertices");
                }
                float error = 0;
                var headRotation = p.head.transform.rotation;
                for (int pose = 0; pose < 120; pose++)
                {
                    float t = pose * .08f;
                    p.leftHand.position = p.head.transform.position + new Vector3(-.3f, -.24f + Mathf.Sin(t) * .15f, .4f);
                    p.rightHand.position = p.head.transform.position + new Vector3(.3f, -.24f + Mathf.Cos(t) * .15f, .4f);
                    p.leftHand.rotation = Quaternion.Euler(0, 0, pose * 2);
                    roster.avatar.UpdatePose();
                    error = Mathf.Max(error, Vector3.Distance(roster.avatar.LeftPalm.position, p.leftHand.TransformPoint(new Vector3(0, -.025f, -.015f))), Vector3.Distance(roster.avatar.RightPalm.position, p.rightHand.TransformPoint(new Vector3(0, -.025f, -.015f))));
                }
                results.Add(roster.Active.name + ": " + (error < .001f ? "PASS" : "FAIL") + " 120 hand poses, max palm error " + error.ToString("F6") + " m; camera rotation change " + Quaternion.Angle(headRotation, p.head.transform.rotation));
            }
            yield return null;
            results.Add((renderErrors.IsEmpty ? "PASS" : "FAIL") + " no native rendering errors or invalid bounds during all six hand views");
            File.WriteAllText("Logs/ChampionExpansion/Hands-errors.txt", string.Join("\n", renderErrors));
            File.WriteAllText("Logs/ChampionExpansion/Hands.md", "# Final wrist mesh visual verification\n\n" + string.Join("\n\n", results));
            EditorApplication.isPlaying = false;
        }
    }
}
