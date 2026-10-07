using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using LeagueVR.Match;
using LeagueVR.Champions;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class GwenFocusWork
    {
        public const string Folder = "Logs/GwenFocus";
        static double next;

        static GwenFocusWork()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += State;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next)
                return;
            next = EditorApplication.timeSinceStartup + .5;
            const string path = "Temp/GwenFocus.request";
            if (!File.Exists(path))
                return;
            string action = File.ReadAllText(path).Trim();
            File.Delete(path);
            Directory.CreateDirectory(Folder);
            try
            {
                if (action == "Inspect")
                    Inspect();
                else if (action == "Apply")
                    GwenFocusVisuals.Apply();
                else if (action == "Baseline" || action.StartsWith("Recheck") || action.StartsWith("Edges"))
                {
                    if (EditorApplication.isPlaying)
                        throw new Exception("Start in edit mode");
                    SessionState.SetString("GwenFocus.Pending", action);
                    EditorApplication.isPaused = false;
                    EditorApplication.isPlaying = true;
                }
                else
                    throw new Exception("Unknown Gwen focus action: " + action);
            }
            catch (Exception e)
            {
                File.WriteAllText(Folder + "/Error.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        static void State(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode)
                return;
            string pending = SessionState.GetString("GwenFocus.Pending", "");
            if (pending == "")
                return;
            SessionState.SetString("GwenFocus.Pending", "");
            new GameObject("Temporary Gwen focus probe").AddComponent<GwenFocusProbe>().run = pending;
        }

        static string PathFor(Transform t) => t.parent ? PathFor(t.parent) + "/" + t.name : t.name;

        static void Inspect()
        {
            var m = Object.FindAnyObjectByType<RiftMatch>();
            if (!m)
                throw new Exception("Open LeagueVR scene");
            var p = m.player;
            var a = p.GetComponent<GwenAvatar>();
            var b = new StringBuilder();
            b.AppendLine("Scene: " + EditorSceneManager.GetActiveScene().path + "; champions=" + p.GetComponent<ChampionRoster>().champions.Length + "; startingGold=" + m.rules.startingGold);
            b.AppendLine("Origin=" + p.origin.transform.position + " mode=" + p.origin.RequestedTrackingOriginMode + " offset=" + p.origin.CameraYOffset + "; head=" + p.head.transform.localPosition + "; world=" + p.worldMask.value + " combat=" + p.combatMask.value);
            foreach (var t in p.origin.GetComponentsInChildren<Transform>(true))
            {
                var pose = t.GetComponent<TrackedPoseDriver>();
                if (pose)
                    b.AppendLine("Tracking: " + PathFor(t) + " update=" + pose.updateType + " position=" + pose.positionInput.action?.bindings.FirstOrDefault().path + " rotation=" + pose.rotationInput.action?.bindings.FirstOrDefault().path);
                if (t == p.leftHand || t == p.rightHand || t.name.Contains("Teleport") || t.GetComponent<Renderer>())
                    b.AppendLine("Rig: " + PathFor(t) + " active=" + t.gameObject.activeInHierarchy + " local=" + t.localPosition + " rotation=" + t.localEulerAngles + " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "MISSING")));
            }
            foreach (var t in new[] { a.visualRoot, a.leftUpper, a.leftLower, a.leftPalm, a.rightUpper, a.rightLower, a.rightPalm, a.scissorsRoot, a.bladeA, a.bladeB })
            {
                b.AppendLine("Gwen: " + PathFor(t) + " world=" + t.position.ToString("F4") + " local=" + t.localPosition.ToString("F4") + " rotation=" + t.localEulerAngles + " scale=" + t.localScale);
                foreach (Transform c in t)
                    if (t == a.leftPalm || t == a.rightPalm)
                        b.AppendLine("Finger " + c.name + " " + c.localPosition + " rotation=" + c.localEulerAngles);
            }
            foreach (var r in a.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                b.AppendLine("Skin " + r.name + " mesh=" + AssetDatabase.GetAssetPath(r.sharedMesh) + " bounds=" + r.bounds + " bones=" + r.bones.Length + " mats=" + string.Join(",", r.sharedMaterials.Select(v => v.name)));
            foreach (var r in a.scissorsRoot.GetComponentsInChildren<MeshRenderer>())
                b.AppendLine("Blade " + r.name + " bounds=" + r.GetComponent<MeshFilter>().sharedMesh.bounds + " scale=" + r.transform.lossyScale);
            if (a.animationPlayer)
                foreach (AnimationState s in a.animationPlayer)
                    b.AppendLine("Original animation " + s.name + " duration=" + s.length);
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gwen/gwen.glb"));
            try
            {
                source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = source.GetComponentInChildren<Animation>();
                foreach (string clip in new[] { "Idle.anm", "Attack1.anm" })
                {
                    if (anim[clip] == null)
                        continue;
                    anim.Play(clip);
                    anim[clip].time = clip == "Idle.anm" ? 0 : .1f;
                    anim.Sample();
                    foreach (var t in source.GetComponentsInChildren<Transform>())
                        if (new[] { "R_Hand", "L_Hand", "Scissors_A", "Scissors_B", "Buffbone_Scissors_A_Tip", "Buffbone_Scissors_B_Tip" }.Contains(t.name))
                            b.AppendLine("Source " + clip + " " + t.name + " position=" + t.position.ToString("F4") + " rotation=" + t.eulerAngles);
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
            File.WriteAllText(Folder + "/LiveInspection.txt", b.ToString());
            Debug.Log("Gwen live inspection saved");
        }
    }

    public partial class GwenFocusProbe : MonoBehaviour
    {
        public string run;
        RiftMatch m;
        GwenAbilities p;
        GwenAvatar avatar;
        RiftEconomy economy;
        Camera eye;
        Vector3 arena;
        int layer;
        float began;
        bool finished;
        readonly List<string> results = new(), errors = new();
        readonly List<GameObject> fixtures = new();

        void Record(string scenario, string observed, bool? okay = null)
        {
            results.Add((okay.HasValue ? (okay.Value ? "PASS " : "FAIL ") : "OBSERVED ") + scenario + ": " + observed);
            File.WriteAllText(GwenFocusWork.Folder + "/" + run + "-progress.txt", string.Join("\n", results));
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            yield return null;
            yield return null;
            began = Time.realtimeSinceStartup;
            var suite = Suite();
            while (true)
            {
                bool more;
                object current = null;
                try
                {
                    more = suite.MoveNext();
                    if (more)
                        current = suite.Current;
                }
                catch (Exception e)
                {
                    errors.Add(e.ToString());
                    break;
                }
                if (!more)
                    break;
                yield return current;
            }
            Finish();
        }

        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                errors.Add(message + "\n" + stack);
        }

        void Place(float height = 1.65f)
        {
            p.origin.transform.position = arena;
            p.head.transform.SetPositionAndRotation(arena + Vector3.up * height, Quaternion.identity);
            p.leftHand.SetPositionAndRotation(arena + new Vector3(-.24f, height - .35f, .45f), Quaternion.identity);
            p.rightHand.SetPositionAndRotation(arena + new Vector3(.24f, height - .35f, .45f), Quaternion.identity);
            Physics.SyncTransforms();
        }

        Combatant Enemy(Vector3 at, float height = 1.2f)
        {
            var go = new GameObject("Temporary combat target");
            fixtures.Add(go);
            go.layer = layer;
            go.transform.position = at;
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true;
            c.height = height;
            c.radius = .24f;
            c.center = Vector3.up * (height * .5f);
            var h = go.AddComponent<Combatant>();
            h.team = 1 - p.Health.team;
            h.maxHealth = 5000;
            h.armor = h.magicResistance = 0;
            h.ResetHealth();
            var aim = new GameObject("Aim").transform;
            aim.SetParent(go.transform, false);
            aim.localPosition = Vector3.up * (height * .5f);
            h.aimPoint = aim;
            Physics.SyncTransforms();
            return h;
        }

        void Clear()
        {
            foreach (var f in fixtures)
                if (f)
                {
                    f.SetActive(false);
                    Destroy(f);
                }
            fixtures.Clear();
            p.ResetPractice();
            economy.RestoreMana(economy.MaxMana);
        }

        void Capture(string name)
        {
            avatar.UpdateTrackedVisuals();
            var rt = new RenderTexture(1100, 900, 24);
            var previous = RenderTexture.active;
            try
            {
                eye.targetTexture = rt;
                eye.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1100, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1100, 900), 0, 0);
                tex.Apply();
                File.WriteAllBytes(GwenFocusWork.Folder + "/" + run + "-" + name + ".png", tex.EncodeToPNG());
                Destroy(tex);
            }
            finally
            {
                eye.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                Destroy(rt);
            }
        }

        IEnumerator Suite()
        {
            m = Object.FindAnyObjectByType<RiftMatch>();
            p = m.player;
            avatar = p.GetComponent<GwenAvatar>();
            economy = m.economy;
            var poses = p.origin.GetComponentsInChildren<TrackedPoseDriver>(true);
            Record("Initial tracking", poses.Length + " pose drivers enabled; headset active=" + UnityEngine.XR.XRSettings.isDeviceActive, poses.Length >= 3 && poses.All(t => t.enabled));
            Record("Preserved content", "champions=" + p.GetComponent<ChampionRoster>().champions.Length + " startingGold=" + m.rules.startingGold, p.GetComponent<ChampionRoster>().champions.Length == 7 && m.rules.startingGold == 10000);
            // Controlled editor simulation only. Changes to tracking components exist only during this play session.
            foreach (var t in poses)
                t.enabled = false;
            foreach (var c in p.origin.GetComponentsInChildren<MonoBehaviour>(true))
                if (c && c.GetType().Namespace?.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion") == true)
                    c.enabled = false;
            p.GetComponent<GwenVRInput>().enabled = false;
            p.GetComponent<ChampionRoster>().Select(0);
            m.Play();
            p.DesktopMode = false;
            economy.enabled = false;
            p.origin.GetComponent<CharacterController>().enabled = false;
            layer = Enumerable.Range(0, 32).First(i => (p.combatMask.value & (1 << i)) != 0);
            arena = m.lanes[1].points.OrderBy(v => GwenAbilities.FlatDistance(v, new Vector3(-25, 0, -18))).First();
            Place();
            eye = new GameObject("Temporary simulated eye").AddComponent<Camera>();
            eye.enabled = false;
            eye.nearClipPlane = p.head.nearClipPlane;
            eye.farClipPlane = p.head.farClipPlane;
            eye.fieldOfView = 90;
            eye.GetUniversalAdditionalCameraData().allowXRRendering = false;
            eye.transform.SetPositionAndRotation(p.head.transform.position - p.head.transform.right * .032f, Quaternion.Euler(20, 0, 0));
            Capture("standing-left-eye");
            eye.transform.position += p.head.transform.right * .064f;
            Capture("standing-right-eye");
            Vector3 headBefore = p.head.transform.position;
            Quaternion rotationBefore = p.head.transform.rotation;
            avatar.UpdateTrackedVisuals();
            var palms = new[] { avatar.leftPalm.position, avatar.rightPalm.position };
            for (int i = 0; i < 120; i++)
                avatar.UpdateTrackedVisuals();
            Record("Stationary pose repeated 120 times", "palm drift=" + Mathf.Max(Vector3.Distance(palms[0], avatar.leftPalm.position), Vector3.Distance(palms[1], avatar.rightPalm.position)).ToString("F6"), Vector3.Distance(palms[0], avatar.leftPalm.position) < .0001f && Vector3.Distance(palms[1], avatar.rightPalm.position) < .0001f);
            Record("Head tracking ownership", "position delta=" + Vector3.Distance(headBefore, p.head.transform.position) + " rotation delta=" + Quaternion.Angle(rotationBefore, p.head.transform.rotation), p.head.transform.position == headBefore && Quaternion.Angle(rotationBefore, p.head.transform.rotation) < .001f);
            Place(.95f);
            eye.transform.SetPositionAndRotation(p.head.transform.position, Quaternion.Euler(30, 0, 0));
            Capture("crouched");
            Place();
            var low = Enemy(arena + new Vector3(.24f, 0, 1.8f), .7f);
            float hp = low.Health;
            bool attack = p.BasicAttack();
            Record("Horizontal scissors at chest height vs 0.7m minion", "attack=" + attack + " HP lost=" + (hp - low.Health), low.Health < hp);
            Clear();
            var center = Enemy(arena + new Vector3(.24f, 0, 2.1f), 1.2f);
            hp = center.Health;
            int snips = 0;
            center.Damaged += (hit, n) =>
            {
                if (hit.ability == "Q")
                    snips++;
            };
            p.CastQ();
            yield return new WaitForSeconds(.8f);
            Record("Q centre against lower enemy", "Q packets=" + snips + " HP lost=" + (hp - center.Health), center.Health < hp);
            Clear();
            center = Enemy(arena + new Vector3(.24f, 0, 2.1f), 2.4f);
            hp = center.Health;
            p.CastQ();
            p.rightHand.rotation = Quaternion.Euler(0, 90, 0);
            yield return new WaitForSeconds(.8f);
            Record("Rotate controller away immediately after Q", "HP lost=" + (hp - center.Health));
            Clear();
            Place();
            var rtarget = Enemy(arena + new Vector3(-.24f, 0, 5), 2.4f);
            hp = rtarget.Health;
            int rHits = 0;
            rtarget.Damaged += (hit, n) =>
            {
                if (hit.ability == "R")
                    rHits++;
            };
            bool r1 = p.CastR();
            yield return new WaitForSeconds(1.05f);
            bool r2 = p.CastR();
            yield return new WaitForSeconds(1.05f);
            bool r3 = p.CastR();
            yield return new WaitForSeconds(1);
            Record("R 1/3/5 needle sequence", "casts=" + r1 + "," + r2 + "," + r3 + " packets=" + rHits + " HP lost=" + (hp - rtarget.Health), r1 && r2 && r3 && rHits == 9);
            Clear();
            var far = Enemy(arena + Vector3.forward * 5);
            p.Health.SetHealth(300);
            p.CastW();
            hp = p.Health.Health;
            float blocked = p.Health.TakeDamage(new DamageHit(far, far.AimPosition, 100, DamageKind.Magic));
            Record("W outsider damage", "dealt=" + blocked + " HP=" + p.Health.Health, blocked == 0 && p.Health.Health == hp);
            far.transform.position = arena + Vector3.forward;
            float allowed = p.Health.TakeDamage(new DamageHit(far, far.AimPosition, 100, DamageKind.Magic));
            Record("W attacker enters mist", "dealt=" + allowed, allowed > 0);
            Clear();
            Place();
            Vector3 feet = p.Feet;
            rotationBefore = p.head.transform.rotation;
            bool dash = p.CastE();
            Record("E on actual mid lane", "cast=" + dash + " displacement=" + Vector3.Distance(feet, p.Feet).ToString("F3") + " rotation delta=" + Quaternion.Angle(rotationBefore, p.head.transform.rotation), dash && Vector3.Distance(feet, p.Feet) > .15f && Quaternion.Angle(rotationBefore, p.head.transform.rotation) < .001f);
            if (run.StartsWith("Edges"))
            {
                Clear();
                Place();
                var edges = Edges();
                while (edges.MoveNext())
                    yield return edges.Current;
            }
            yield return null;
        }

        void Finish()
        {
            if (finished)
                return;
            finished = true;
            Application.logMessageReceived -= Log;
            CleanupControllers();
            File.WriteAllText(GwenFocusWork.Folder + "/" + run + ".md", "# Gwen " + run + "\n\nUnity play-mode scenarios using controlled controller/head poses in the saved LeagueVR map. No physical headset input. Simulated eye cameras use +/-32mm offsets and 90 degree FOV; these are not actual headset screenshots or a comfort/performance benchmark.\n\n" + string.Join("\n\n", results) + "\n\n## Runtime errors\n" + (errors.Count == 0 ? "None observed" : string.Join("\n", errors)) + "\n\nDuration: " + (Time.realtimeSinceStartup - began).ToString("F1") + "s\n");
            EditorApplication.isPlaying = false;
        }
    }
}
