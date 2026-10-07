using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRPresentationTest
    {
        static string pass;
        static double start;
        static int stage, errors, frames;
        static RiftMatch match;
        static readonly List<RiftMinion> probes = new();
        static StringBuilder log;
        static Vector3[] bones;
        static bool damageSeen;
        static int audioStart;
        static float deadAt;
        static Camera camera;
        static RenderTexture rt;
        static float audioPeak, listenerPeak;
        static int audibleFrames;
        static readonly float[] audioData = new float[1024];

        static LeagueVRPresentationTest()
        {
            EditorApplication.update += Tick;
        }

        public static void Begin(string name)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new Exception("Start from edit mode");
            Audit(name);
            SessionState.SetString("LeagueVR.PresentationTest", name);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            string pending = SessionState.GetString("LeagueVR.PresentationTest", "");
            if (pending.Length == 0)
                return;
            if (!EditorApplication.isPlaying)
            {
                if (pass != null)
                {
                    SessionState.SetString("LeagueVR.PresentationTest", "");
                    pass = null;
                }
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            try
            {
                if (pass == null)
                {
                    match = Object.FindAnyObjectByType<RiftMatch>();
                    if (!match || !match.player.Health)
                        return;
                    pass = pending;
                    start = EditorApplication.timeSinceStartup;
                    stage = 0;
                    errors = frames = audibleFrames = 0;
                    audioPeak = listenerPeak = 0;
                    log = new StringBuilder();
                    probes.Clear();
                    Application.logMessageReceived += Message;
                    audioStart = LeagueSoundBank.PlayedLayers;
                    camera = new GameObject("Temporary test capture") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                    camera.nearClipPlane = .05f;
                    camera.farClipPlane = 200;
                    camera.fieldOfView = 72;
                    rt = new RenderTexture(1280, 720, 24);
                    camera.targetTexture = rt;
                    log.AppendLine(pass + " live test " + DateTime.Now);
                    log.AppendLine("XR device active=" + XRSettings.isDeviceActive + " listener count=" + Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.enabled) + " listener volume=" + AudioListener.volume + " paused=" + AudioListener.pause + " editor muted=" + EditorUtility.audioMasterMute);
                }
                frames++;
                var fx = match.player.GetComponent<GwenAudio>().EffectsSource;
                if (fx)
                {
                    fx.GetOutputData(audioData, 0);
                    float peak = audioData.Max(v => Mathf.Abs(v));
                    audioPeak = Mathf.Max(audioPeak, peak);
                    if (peak > 1e-5f)
                        audibleFrames++;
                }
                AudioListener.GetOutputData(audioData, 0);
                listenerPeak = Mathf.Max(listenerPeak, audioData.Max(v => Mathf.Abs(v)));
                double elapsed = EditorApplication.timeSinceStartup - start;
                if (stage == 0 && elapsed > 2)
                {
                    match.Play();
                    stage++;
                }
                if (stage == 1 && match.Seconds > 3)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var p = new Vector3(-4.5f + (i % 4) * 3, 0, i < 4 ? 8 : 12);
                        match.Ground(p, out p);
                        var go = Object.Instantiate(match.minions[i], p, Quaternion.Euler(0, i < 4 ? 0 : 180, 0));
                        var m = go.GetComponent<RiftMinion>();
                        m.Initialize(match, i / 4, 1, (MinionKind)(i % 4), new[] { p, p + Vector3.forward * (i < 4 ? 4 : -4) });
                        m.health.maxHealth = 100000;
                        m.health.ResetHealth();
                        m.stats.damage = m.stats.minionOnHit = 0;
                        probes.Add(m);
                    }
                    stage++;
                }
                if (stage == 2 && match.Seconds > 6)
                {
                    bones = probes.Select(m => m.GetComponentInChildren<SkinnedMeshRenderer>().bones.Last().localPosition).ToArray();
                    stage++;
                }
                if (stage == 3 && match.Seconds > 7)
                {
                    int changed = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var m = probes[i];
                        if ((m.GetComponentInChildren<SkinnedMeshRenderer>().bones.Last().localPosition - bones[i]).sqrMagnitude > 1e-8)
                            changed++;
                        log.AppendLine(m.name + " live animation=" + m.GetComponentInChildren<RiftMinionMotion>().State);
                    }
                    log.AppendLine("Changed sampled bone translations=" + changed + "/8 (rotation-only tracks checked by edit-pose mesh deformation audit)");
                    Capture("minion-combat", new Vector3(1, 3, 3), new Vector3(1, .6f, 10));
                    stage++;
                }
                if (stage == 4 && match.Seconds > 12)
                {
                    deadAt = Time.time;
                    int deathIndex = 0;
                    foreach (var m in probes)
                    {
                        if (pass == "Test2")
                        {
                            var original = m.GetComponentInChildren<RiftMinionMotion>();
                            original.deaths = new[] { original.deaths.OrderByDescending(n => original.player[n].length).First() };
                        }
                        m.health.TakeDamage(new DamageHit(null, m.transform.position, 1e9f, pass == "Test3" && deathIndex++ < 2 ? DamageKind.Magic : DamageKind.True));
                        var motion = m.GetComponentInChildren<RiftMinionMotion>();
                        log.AppendLine(m.name + " death=" + motion.State + " cleanup=" + (motion.CorpseExpires - deadAt).ToString("F3") + "s colliders=" + m.GetComponentsInChildren<Collider>().Count(c => c.enabled));
                    }
                    stage++;
                }
                if (stage == 5 && match.Seconds > 13.4f)
                {
                    Capture("death-poses", new Vector3(1, 2.7f, 4), new Vector3(1, .3f, 10));
                    stage++;
                }
                if (stage == 6 && match.Seconds > 15.7f)
                {
                    foreach (var m in probes.Where(m => m))
                    {
                        var b = new MaterialPropertyBlock();
                        m.GetComponentInChildren<Renderer>().GetPropertyBlock(b);
                        log.AppendLine(m.name + " corpse at " + (Time.time - deadAt).ToString("F2") + "s alpha=" + b.GetColor("_BaseColor").a.ToString("F3") + " targetable=" + m.Targetable);
                    }
                    Capture("corpse-fade", new Vector3(1, 2.7f, 4), new Vector3(1, .3f, 10));
                    stage++;
                }
                if (stage == 7 && match.Seconds > 18)
                {
                    log.AppendLine("Corpses remaining after longest clip=" + probes.Count(m => m));
                    var tower = match.structures.First(s => s.health.team == 1 && s.kind == StructureKind.OuterTurret && s.lane == 1);
                    var p = tower.transform.position + tower.transform.forward * 5;
                    match.Ground(p, out p);
                    var cc = match.player.origin.GetComponent<CharacterController>();
                    bool was = cc && cc.enabled;
                    if (cc)
                        cc.enabled = false;
                    var offset = match.player.head.transform.position - match.player.origin.transform.position;
                    offset.y = 0;
                    match.player.origin.transform.position = p - offset;
                    if (cc)
                        cc.enabled = was;
                    match.player.DesktopMode = true;
                    match.player.head.transform.rotation = Quaternion.LookRotation(tower.health.AimPosition - match.player.head.transform.position);
                    match.player.Health.maxHealth = 100000;
                    match.player.Health.ResetHealth();
                    stage++;
                }
                if (stage == 8 && match.Seconds > 20)
                {
                    log.AppendLine("W cast=" + match.player.CastW());
                    stage++;
                }
                if (stage == 9 && match.Seconds > 21)
                {
                    log.AppendLine("Q cast=" + match.player.CastQ());
                    stage++;
                }
                if (stage == 10 && match.Seconds > 22)
                {
                    log.AppendLine("R1 cast=" + match.player.CastR());
                    stage++;
                }
                if (stage == 11 && match.Seconds > 22.8)
                {
                    log.AppendLine("R2 cast=" + match.player.CastR());
                    stage++;
                }
                if (stage == 12 && match.Seconds > 23.6)
                {
                    log.AppendLine("R3 cast=" + match.player.CastR());
                    stage++;
                }
                if (stage == 13 && match.Seconds > 24.5)
                {
                    log.AppendLine("E cast=" + match.player.CastE());
                    stage++;
                }
                if (stage == 14 && match.Seconds > 25.5)
                {
                    log.AppendLine("Basic attack=" + match.player.BasicAttack());
                    damageSeen = match.player.Health.Health < match.player.Health.maxHealth;
                    log.AppendLine("Tower damaged champion=" + damageSeen + " health=" + match.player.Health.Health);
                    float[] data = new float[1024];
                    match.player.GetComponent<GwenAudio>().EffectsSource.GetOutputData(data, 0);
                    log.AppendLine("Champion source output peak=" + data.Max(v => Mathf.Abs(v)));
                    Capture("enemy-turret", match.player.head.transform.position, match.structures.First(s => s.health.team == 1 && s.kind == StructureKind.OuterTurret && s.lane == 1).health.AimPosition);
                    stage++;
                }
                if (stage == 15 && match.Seconds > (pass == "Test3" ? 75 : 38))
                {
                    log.AppendLine("Champion measured output peak=" + audioPeak + " nonzero frames=" + audibleFrames + " listener peak=" + listenerPeak);
                    log.AppendLine("Wave=" + match.Wave + " living minions=" + Object.FindObjectsByType<RiftMinion>(FindObjectsSortMode.None).Count(m => m.health.IsAlive) + " original audio layers played=" + (LeagueSoundBank.PlayedLayers - audioStart));
                    int groundMiss = 0;
                    foreach (var m in Object.FindObjectsByType<RiftMinion>(FindObjectsSortMode.None))
                        if (m.health.IsAlive && !match.Ground(m.transform.position, out _))
                            groundMiss++;
                    log.AppendLine("Living minion ground misses=" + groundMiss);
                    log.AppendLine("Runtime errors=" + errors + " frames=" + frames + " XR active=" + XRSettings.isDeviceActive);
                    Capture("first-wave", new Vector3(-13, 5, -9), new Vector3(-17, 0, -3));
                    End();
                }
            }
            catch (Exception e)
            {
                File.WriteAllText("Logs/Presentation/" + pending + "-test-error.txt", e.ToString());
                End();
            }
        }

        static void Message(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                errors++;
                log?.AppendLine(type + ": " + text);
            }
        }

        static void Capture(string name, Vector3 eye, Vector3 target)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            LeagueVRMapWork.Capture(camera, rt, "Logs/Presentation/" + pass + "/" + name + ".png");
        }

        static void End()
        {
            Application.logMessageReceived -= Message;
            if (log != null)
                File.WriteAllText("Logs/Presentation/" + pass + "/live-test.txt", log.ToString());
            if (camera)
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject);
            }
            if (rt)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            SessionState.SetString("LeagueVR.PresentationTest", "");
            pass = null;
            EditorApplication.isPlaying = false;
        }

        public static void Audit(string name)
        {
            Directory.CreateDirectory("Logs/Presentation/" + name);
            var match = Object.FindAnyObjectByType<RiftMatch>();
            var b = new StringBuilder();
            var bank = match.player.GetComponent<GwenAudio>().originalSounds;
            b.AppendLine("Original audio output gain=" + bank.outputGain + " events=" + bank.events.Length + " missing clips=" + bank.events.Sum(e => e.layers.Sum(l => l.variants.Count(c => !c))));
            foreach (var s in match.structures)
            {
                b.AppendLine(s.name + " pos=" + s.transform.position + " yaw=" + s.transform.eulerAngles.y + " supported=" + match.Ground(s.transform.position, out _));
            }
            var go = new GameObject("Temporary unit preview camera") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            c.enabled = false;
            c.GetUniversalAdditionalCameraData().allowXRRendering = false;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(.10f, .14f, .18f);
            c.cullingMask = 1 << 31;
            c.nearClipPlane = .01f;
            c.farClipPlane = 20;
            c.fieldOfView = 44;
            var rt = new RenderTexture(512, 512, 24);
            c.targetTexture = rt;
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    var model = Object.Instantiate(match.minions[i]);
                    model.hideFlags = HideFlags.HideAndDontSave;
                    model.transform.position = Vector3.zero;
                    foreach (var t in model.GetComponentsInChildren<Transform>())
                        t.gameObject.layer = 31;
                    var minion = model.GetComponent<RiftMinion>();
                    minion.label.gameObject.SetActive(false);
                    var motion = model.GetComponentInChildren<RiftMinionMotion>();
                    var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                    var bake = new Mesh();
                    var clips = new[] { motion.idle, motion.run, motion.attacks[0], motion.deaths[0] };
                    Vector3[] first = null;
                    float delta = 0;
                    try
                    {
                        c.transform.SetPositionAndRotation(new Vector3(0, 1.1f, 3.6f), Quaternion.LookRotation(new Vector3(0, .65f, 0) - new Vector3(0, 1.1f, 3.6f)));
                        for (int k = 0; k < clips.Length; k++)
                        {
                            var clip = motion.player[clips[k]].clip;
                            clip.SampleAnimation(motion.player.gameObject, k == 3 ? Mathf.Min(clip.length, 1.8f) : clip.length * .35f);
                            skin.BakeMesh(bake);
                            var verts = bake.vertices;
                            if (k == 0)
                                first = verts;
                            if (k == 1 && first.Length == verts.Length)
                                delta = verts.Select((v, n) => (v - first[n]).magnitude).Max();
                            b.AppendLine(model.name + " " + clips[k] + " duration=" + clip.length.ToString("F3") + " curves=" + AnimationUtility.GetCurveBindings(clip).Length);
                            LeagueVRMapWork.Capture(c, rt, "Logs/Presentation/" + name + "/" + match.minions[i].name + "-" + k + ".png");
                        }
                        b.AppendLine(model.name + " pose vertex delta=" + delta + " bones=" + skin.bones.Length);
                    }
                    finally
                    {
                        Object.DestroyImmediate(bake);
                        Object.DestroyImmediate(model);
                    }
                }
                c.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
                c.fieldOfView = 65;
                foreach (var s in match.structures)
                {
                    string captureName = s.name + (s.kind == StructureKind.NexusTurret ? "-" + (Array.IndexOf(match.structures.Where(t => t.kind == StructureKind.NexusTurret && t.health.team == s.health.team).ToArray(), s) + 1) : "");
                    for (int d = 0; d < 4; d++)
                    {
                        float distance = s.kind == StructureKind.Inhibitor ? 3 : s.kind == StructureKind.Nexus ? 8 : 6;
                        Vector3 eye = s.transform.position + (d == 0 ? s.transform.forward : d == 1 ? -s.transform.forward : d == 2 ? s.transform.right : -s.transform.right) * distance + Vector3.up * (s.kind == StructureKind.Inhibitor ? 2.5f : 3);
                        Vector3 target = s.transform.position + Vector3.up * (s.kind != StructureKind.Inhibitor && s.kind != StructureKind.Nexus ? 3 : 1.2f);
                        c.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                        LeagueVRMapWork.Capture(c, rt, "Logs/Presentation/" + name + "/" + captureName + "-face-" + d + ".png");
                    }
                }
            }
            finally
            {
                c.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
            File.WriteAllText("Logs/Presentation/" + name + "/audit.txt", b.ToString());
        }
    }
}

