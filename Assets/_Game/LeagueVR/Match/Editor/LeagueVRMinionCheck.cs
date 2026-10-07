using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using LeagueVR.Match;
using Object = UnityEngine.Object;

namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRMinionCheck
    {
        const string Output = "Logs/MinionFaceFix";
        const string Pending = "LeagueVR.MinionFocused";
        static RiftMatch match;
        static readonly List<RiftMinion> units = new();
        static StringBuilder log;
        static int stage, errors;
        static Camera camera;
        static RenderTexture texture;
        static float deathTime;
        static bool started;
        static readonly FieldInfo TargetField = typeof(RiftMinion).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);

        static LeagueVRMinionCheck()
        {
            EditorApplication.update += Tick;
        }

        public static void ApplyAndBegin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new Exception("Start the minion check in edit mode.");
            Directory.CreateDirectory(Output);
            var current = Object.FindAnyObjectByType<RiftMatch>();
            Portraits(current, "Before");
            string backup = "PrototypeBackups/MinionFaceFix";
            Directory.CreateDirectory(backup);
            foreach (var material in current.minions.SelectMany(p => p.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                         .SelectMany(r => r.sharedMaterials).Distinct())
            {
                string path = AssetDatabase.GetAssetPath(material);
                string copy = backup + "/" + Path.GetFileName(path);
                if (!File.Exists(copy))
                    File.Copy(path, copy);
                material.SetFloat("_Surface", 0);
                material.SetFloat("_SrcBlend", 1);
                material.SetFloat("_DstBlend", 0);
                material.SetFloat("_SrcBlendAlpha", 1);
                material.SetFloat("_DstBlendAlpha", 0);
                material.SetFloat("_ZWrite", 1);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.SetShaderPassEnabled("DepthOnly", true);
                material.renderQueue = 2000;
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            Portraits(current, "After");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        static void Portraits(RiftMatch current, string version)
        {
            var go = new GameObject("Temporary minion portrait camera") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            c.enabled = false;
            c.GetUniversalAdditionalCameraData().allowXRRendering = false;
            c.cullingMask = 1 << 31;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(.10f, .14f, .18f);
            c.nearClipPlane = .01f;
            c.farClipPlane = 10;
            c.fieldOfView = 38;
            var rt = new RenderTexture(768, 768, 24);
            c.targetTexture = rt;
            try
            {
                foreach (var prefab in current.minions)
                {
                    var clone = Object.Instantiate(prefab);
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                            t.gameObject.layer = 31;
                        clone.GetComponent<RiftMinion>().label.gameObject.SetActive(false);
                        var motion = clone.GetComponentInChildren<RiftMinionMotion>();
                        motion.player[motion.idle].clip.SampleAnimation(motion.gameObject, .2f);
                        for (int d = 0; d < 4; d++)
                        {
                            Vector3 direction = d == 0 ? Vector3.forward : d == 1 ? Vector3.back : d == 2 ? Vector3.right : Vector3.left;
                            Vector3 eye = direction * 3.2f + Vector3.up * 1.1f;
                            c.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.up * .7f - eye));
                            LeagueVRMapWork.Capture(c, rt, Output + "/" + version + "-" + prefab.name + "-" + d + ".png");
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(clone);
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
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Pending, false) || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!EditorApplication.isPlaying)
                return;
            try
            {
                if (!started)
                {
                    match = Object.FindAnyObjectByType<RiftMatch>();
                    if (!match || !match.player.Health)
                        return;
                    started = true;
                    stage = errors = 0;
                    units.Clear();
                    log = new StringBuilder("One focused minion test " + DateTime.Now + "\n");
                    Application.logMessageReceived += Message;
                    foreach (var s in match.structures)
                        s.enabled = false;
                    foreach (var o in match.objectives)
                        o.enabled = false;
                    var cc = match.player.origin.GetComponent<CharacterController>();
                    if (cc)
                        cc.enabled = false;
                    match.Play();
                    camera = new GameObject("Temporary focused minion camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                    camera.cullingMask = 1 << 31;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.10f, .14f, .18f);
                    camera.nearClipPlane = .01f;
                    camera.farClipPlane = 80;
                    camera.fieldOfView = 70;
                    texture = new RenderTexture(1280, 720, 24);
                    camera.targetTexture = texture;
                    log.AppendLine("Structures/objectives temporarily disabled; no abilities tested. XR active=" + XRSettings.isDeviceActive);
                }
                float t = match.Seconds;
                if (stage == 0 && t > 1)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 at = new Vector3(-6 + (i % 4) * 4, 0, i < 4 ? 4 : 20);
                        match.Ground(at, out at);
                        var go = Object.Instantiate(match.minions[i], at, Quaternion.Euler(0, i < 4 ? 0 : 180, 0));
                        var m = go.GetComponent<RiftMinion>();
                        m.Initialize(match, i / 4, 1, (MinionKind)(i % 4), new[] { at, at + Vector3.forward * (i < 4 ? 8 : -8) });
                        m.health.maxHealth = 100000;
                        m.health.ResetHealth();
                        m.stats.damage = m.stats.minionOnHit = 0;
                        m.label.gameObject.SetActive(false);
                        foreach (var child in go.GetComponentsInChildren<Transform>(true))
                            child.gameObject.layer = 31;
                        m.label.gameObject.layer = 30;
                        units.Add(m);
                    }
                    stage++;
                }
                if (stage == 1 && t > 1.4f)
                {
                    foreach (var u in units)
                        log.AppendLine(u.name + " walking=" + u.GetComponentInChildren<RiftMinionMotion>().State);
                    stage++;
                }
                if (stage == 2 && t > 3)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        Vector3 at = new Vector3(-6 + (i % 4) * 4, 0, i < 4 ? 8 : 11);
                        match.Ground(at, out at);
                        units[i].transform.position = at;
                        units[i].route = new[] { at, at + Vector3.forward * (i < 4 ? 4 : -4) };
                    }
                    stage++;
                }
                if (stage == 3 && t >= 5 && t < 11)
                {
                    float angle = (t - 5) / 6 * Mathf.PI * 2;
                    MovePlayer(new Vector3(Mathf.Cos(angle) * 22, 0, 9.5f + Mathf.Sin(angle) * 22));
                }
                if (stage == 3 && t > 11)
                {
                    foreach (var u in units)
                    {
                        var target = TargetField.GetValue(u) as RiftActor;
                        Vector3 direction = target ? Vector3.ProjectOnPlane(target.transform.position - u.transform.position, Vector3.up).normalized : Vector3.zero;
                        float dot = Vector3.Dot(u.transform.forward, direction);
                        var material = u.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                        log.AppendLine(u.name + " target=" + (target ? target.name : "none") + " facing target dot=" + dot.ToString("F4") + " animation=" + u.GetComponentInChildren<RiftMinionMotion>().State + " surface=" + material.GetFloat("_Surface") + " depth=" + material.GetFloat("_ZWrite"));
                        if (!target || target.health.countsAsChampion || dot < .97f)
                            log.AppendLine("CHECK FAILED: unit did not retain enemy-minion facing while viewer orbited.");
                    }
                    Capture("combat", new Vector3(0, 3, 3), new Vector3(0, .7f, 9.5f));
                    foreach (var u in units.Take(4))
                        u.gameObject.SetActive(false);
                    MovePlayer(units[5].transform.position + Vector3.back * 3);
                    stage++;
                }
                if (stage == 4 && t > 13)
                {
                    var caster = units[5];
                    var target = TargetField.GetValue(caster) as RiftActor;
                    log.AppendLine("Enemy caster attacks nearby champion=" + (target && target.health == match.player.Health));
                    MovePlayer(new Vector3(0, 0, -20));
                    stage++;
                }
                if (stage == 5 && t > 16)
                {
                    var caster = units[5];
                    log.AppendLine("Caster releases departed champion=" + (TargetField.GetValue(caster) == null) + " resumes=" + caster.GetComponentInChildren<RiftMinionMotion>().State);
                    foreach (var u in units.Take(4))
                        u.gameObject.SetActive(true);
                    deathTime = Time.time;
                    foreach (var u in units)
                        u.health.TakeDamage(new DamageHit(null, u.transform.position, 1e9f, DamageKind.True));
                    stage++;
                }
                if (stage == 6 && t > 17.4f)
                {
                    Capture("death-opaque", new Vector3(0, 3, 3), new Vector3(0, .3f, 9.5f));
                    foreach (var u in units)
                        log.AppendLine(u.name + " death animation surface=" + u.GetComponentInChildren<Renderer>().sharedMaterial.GetFloat("_Surface"));
                    stage++;
                }
                if (stage == 7 && t > 19.7f)
                {
                    foreach (var u in units.Where(u => u))
                    {
                        var renderer = u.GetComponentInChildren<Renderer>();
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        log.AppendLine(u.name + " corpse age=" + (Time.time - deathTime).ToString("F2") + " alpha=" + block.GetColor("_BaseColor").a.ToString("F3") + " surface=" + renderer.sharedMaterial.GetFloat("_Surface") + " depth=" + renderer.sharedMaterial.GetFloat("_ZWrite"));
                    }
                    Capture("corpse-fade", new Vector3(0, 3, 3), new Vector3(0, .3f, 9.5f));
                    stage++;
                }
                if (stage == 8 && t > 23)
                {
                    log.AppendLine("Corpses remaining=" + units.Count(u => u));
                    log.AppendLine("Runtime errors=" + errors);
                    log.AppendLine("Focused test complete; no scene changes saved from Play mode.");
                    End();
                }
            }
            catch (Exception e)
            {
                log?.AppendLine(e.ToString());
                File.WriteAllText(Output + "/error.txt", e.ToString());
                End();
            }
        }

        static void MovePlayer(Vector3 at)
        {
            if (match.Ground(at, out var ground))
                at = ground;
            at.y += .08f;
            Vector3 offset = match.player.head.transform.position - match.player.origin.transform.position;
            offset.y = 0;
            match.player.origin.transform.position = at - offset;
        }

        static void Capture(string name, Vector3 eye, Vector3 target)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            LeagueVRMapWork.Capture(camera, texture, Output + "/" + name + ".png");
        }

        static void Message(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                errors++;
                log?.AppendLine(text);
            }
        }

        static void End()
        {
            Application.logMessageReceived -= Message;
            if (log != null)
                File.WriteAllText(Output + "/focused-test.txt", log.ToString());
            if (camera)
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject);
            }
            if (texture)
            {
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            SessionState.SetBool(Pending, false);
            started = false;
            EditorApplication.isPlaying = false;
        }
    }
}
