using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    [InitializeOnLoad]
    public static class LeagueVRPOVTest
    {
        static LeagueVRPOVTest()
        {
            EditorApplication.playModeStateChanged += State;
        }

        public static void Begin(int run)
        {
            if (EditorApplication.isPlaying)
                throw new Exception("Start from edit mode");
            if (SessionState.GetBool("POV.Run" + run, false))
                throw new Exception("This run was already used");
            SessionState.SetBool("POV.Run" + run, true);
            SessionState.SetInt("POV.Pending", run);
            EditorApplication.isPlaying = true;
        }

        static void State(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                int run = SessionState.GetInt("POV.Pending", 0);
                if (run > 0)
                {
                    SessionState.SetInt("POV.Pending", 0);
                    new GameObject("POV test run " + run).AddComponent<LeagueVRPOVProbe>().run = run;
                }
            }
        }
    }

    public class LeagueVRPOVProbe : MonoBehaviour
    {
        public int run;
        RiftMatch m;
        GwenAbilities p;
        GwenAvatar avatar;
        RiftVRHUD hud;
        RiftItemRack rack;
        Camera camera;
        List<string> passes = new(), failures = new(), errors = new(), observations = new();
        bool finished;

        class March
        {
            public RiftMinion unit;
            public int team, lane, col;
            public float lastMoved;
            public Vector3 last;
            public bool arrived;
            public float maxCorridor, minSeparation = 100;
        }

        void Check(bool good, string label)
        {
            (good ? passes : failures).Add(label);
            File.WriteAllText("Logs/POVOverhaul/Run" + run + "-progress.txt", label + "\n" + passes.Count + " passed / " + failures.Count + " failed");
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += Log;
            yield return null;
            yield return null;
            var suite = run == 3 ? MinionRepairSuite() : Suite();
            while (true)
            {
                bool more;
                object value = null;
                try
                {
                    more = suite.MoveNext();
                    if (more)
                        value = suite.Current;
                }
                catch (Exception e)
                {
                    failures.Add("Unhandled test exception: " + e);
                    break;
                }
                if (!more)
                    break;
                yield return value;
            }
            Finish();
        }

        void Log(string text, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(text + "\n" + trace);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            if (!finished)
            {
                Time.timeScale = 1;
            }
        }

        void Pose(Vector3 head, Quaternion rotation, Vector3 left, Quaternion lr, Vector3 right, Quaternion rr)
        {
            p.head.transform.SetPositionAndRotation(head, rotation);
            p.leftHand.SetPositionAndRotation(left, lr);
            p.rightHand.SetPositionAndRotation(right, rr);
            avatar.UpdateTrackedVisuals();
        }

        void Capture(string name)
        {
            camera.transform.SetPositionAndRotation(p.head.transform.position, p.head.transform.rotation);
            LeagueVRPOVBuild.Capture(camera, "Run" + run + "-" + name);
        }

        Combatant Enemy(string name)
        {
            var go = new GameObject(name);
            go.transform.position = p.Feet + Vector3.forward * 3;
            var h = go.AddComponent<Combatant>();
            h.team = 1 - p.Health.team;
            h.maxHealth = 10000;
            h.ResetHealth();
            var actor = go.AddComponent<RiftActor>();
            actor.neutral = true;
            return h;
        }

        IEnumerator Suite()
        {
            m = Object.FindAnyObjectByType<RiftMatch>();
            p = m.player;
            avatar = p.GetComponent<GwenAvatar>();
            hud = m.GetComponent<RiftVRHUD>();
            if (!hud)
                hud = Object.FindAnyObjectByType<RiftVRHUD>();
            rack = m.economy.GetComponent<RiftItemRack>();
            Check(m && p && avatar && hud && rack, "Scene player, avatar, HUD and physical rack initialise");
            var poses = p.origin.GetComponentsInChildren<TrackedPoseDriver>(true);
            Check(poses.Length >= 3 && poses.All(t => t.updateType == TrackedPoseDriver.UpdateType.UpdateAndBeforeRender), "Existing XR pose drivers use update and before-render tracking");
            var view = p.GetComponent<RiftPlayerView>();
            Check(view && view.HiddenTemplateMeshCount > 10, "Controller and template hand meshes are found and suppressed");
            var controllerMeshes = p.leftHand.GetComponentsInChildren<Renderer>(true).Concat(p.rightHand.GetComponentsInChildren<Renderer>(true)).Where(r => r is not LineRenderer).ToArray();
            Check(controllerMeshes.All(r => r.forceRenderingOff), "Template controller mesh renderers remain hidden");
            Check(p.origin.GetComponent<CharacterController>() && p.leftHand.GetComponent<TrackedPoseDriver>().enabled && p.rightHand.GetComponent<TrackedPoseDriver>().enabled, "Controller tracking and character controller remain intact");
            Check(Mathf.Abs(p.head.nearClipPlane - .025f) < .0001f, "Head camera near plane is 2.5 cm");
            foreach (var provider in p.origin.GetComponentsInChildren<MonoBehaviour>(true))
                if (provider && provider.GetType().Namespace != null && provider.GetType().Namespace.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion"))
                    provider.enabled = false;
            foreach (var pose in poses)
                pose.enabled = false;
            p.GetComponent<GwenVRInput>().enabled = false;
            p.DesktopMode = false;
            m.Play();
            typeof(RiftMatch).GetProperty("NextWave").SetValue(m, 100000f);
            foreach (var s in m.structures)
                s.enabled = false;
            foreach (var o in m.objectives)
                o.enabled = false;
            foreach (var f in m.fountains)
                f.enabled = false;
            m.economy.enabled = false;
            var cc = p.origin.GetComponent<CharacterController>();
            cc.enabled = false;
            rack.enabled = false;
            camera = new GameObject("POV audit camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.nearClipPlane = .025f;
            camera.fieldOfView = 80;
            camera.farClipPlane = 350;
            var head = p.origin.transform.position + Vector3.up * 1.65f;
            var left = head + new Vector3(-.26f, -.35f, .45f);
            var right = head + new Vector3(.26f, -.35f, .45f);
            var face = Quaternion.Euler(run == 1 ? 15 : 25, run == 1 ? 0 : 25, 0);
            Pose(head, face, left, Quaternion.identity, right, Quaternion.identity);
            yield return null;
            Capture("Hands");
            float worst = 0;
            var fixedHead = p.head.transform.position;
            for (int i = 0; i < 600; i++)
            {
                avatar.UpdateTrackedVisuals();
                worst = Mathf.Max(worst, Vector3.Distance(avatar.leftPalm.position, p.leftHand.TransformPoint(avatar.leftGripOffset)), Vector3.Distance(avatar.rightPalm.position, p.rightHand.TransformPoint(avatar.rightGripOffset)));
            }
            Check(worst < .001f, "Repeated hand solve: endpoint error " + worst.ToString("F6") + " m");
            Check(Vector3.Distance(fixedHead, p.head.transform.position) < .00001f, "Avatar never changes head tracking position");
            var oldPalm = avatar.leftPalm.rotation;
            var oldGrip = p.leftHand.rotation;
            var roll = Quaternion.AngleAxis(70, p.leftHand.forward);
            p.leftHand.rotation = roll * oldGrip;
            avatar.UpdateTrackedVisuals();
            Check(Quaternion.Angle(avatar.leftPalm.rotation, roll * oldPalm) < .1f, "Palm rotation follows controller roll");
            Pose(head - Vector3.up * .45f, face, left - Vector3.up * .45f, Quaternion.identity, right - Vector3.up * .45f, Quaternion.identity);
            Check(Vector3.Distance(avatar.rightPalm.position, p.rightHand.TransformPoint(avatar.rightGripOffset)) < .001f, "Crouched hand endpoint remains aligned");
            Pose(head, Quaternion.identity, left, Quaternion.identity, right, Quaternion.identity);
            Vector3 watch = head + new Vector3(-.08f, -.10f, .44f);
            Quaternion handRotation = Quaternion.LookRotation(Vector3.up, (head - watch).normalized);
            Vector3 handPosition = watch - handRotation * new Vector3(-.015f, .04f, -.095f);
            Pose(head, Quaternion.LookRotation(watch - head), handPosition, handRotation, right, Quaternion.identity);
            yield return new WaitForSecondsRealtime(.3f);
            Check(hud.WristVisible, "Raised wrist facing user appears after gaze dwell");
            Capture("Wrist");
            p.head.transform.rotation = Quaternion.Euler(0, 120, 0);
            yield return new WaitForSecondsRealtime(.2f);
            Check(!hud.WristVisible, "Looking away hides wrist display");
            p.head.transform.rotation = Quaternion.LookRotation(watch - head);
            Check(!RiftVRHUD.LookingAtWatch(p.head.transform, watch, -(head - watch)), "Back of wrist does not activate display");
            Check(!RiftVRHUD.LookingAtWatch(p.head.transform, head + Vector3.forward * 1.3f, Vector3.back), "Distant wrist does not activate display");
            m.ui.OpenMenu();
            yield return null;
            Check(!hud.WristVisible && !hud.TargetVisible && !hud.RecallVisible, "Menus hide wrist, target health and recall HUD");
            m.ui.Close();
            Pose(head, Quaternion.identity, left, Quaternion.identity, right, Quaternion.identity);
            var enemy = Enemy("Attack HUD test enemy");
            var targetMinion = m.SpawnMinion(1 - p.Health.team, 1, MinionKind.Caster);
            targetMinion.enabled = false;
            foreach (var target in new[] { targetMinion.health, enemy, m.structures.First(s => s.health.team != p.Health.team && s.kind == StructureKind.OuterTurret).health, m.structures.First(s => s.health.team != p.Health.team && s.kind == StructureKind.Inhibitor).health, m.structures.First(s => s.health.team != p.Health.team && s.kind == StructureKind.Nexus).health }.Concat(m.objectives.Select(o => o.health)))
            {
                target.TakeDamage(new DamageHit(p.Health, p.Feet, 10, DamageKind.Physical) { isBasicAttack = true });
                yield return new WaitForSeconds(.1f);
                Check(hud.TargetVisible && hud.AttackTarget == target, "Compact attacked target HP appears for " + target.name);
            }
            Object.Destroy(targetMinion.gameObject);
            Capture("Target-health");
            yield return new WaitForSeconds(1.2f);
            Check(!hud.TargetVisible, "Target health hides after attacks stop");
            enemy.TakeDamage(new DamageHit(p.Health, p.Feet, 10, DamageKind.Magic) { isItemEffect = true });
            yield return null;
            Check(!hud.TargetVisible, "Lingering item damage does not pin target HUD open");
            foreach (var s in m.structures)
            {
                var bar = s.GetComponent<RiftWorldHealthBar>();
                Check(bar && bar.BarPosition.y > bar.StructureTop + .6f, s.name + ": health bar clears highest visible mesh");
                if (s.kind != StructureKind.Inhibitor && s.kind != StructureKind.Nexus)
                    Check(Vector3.Dot(s.transform.forward, LeagueVRPOVBuild.Facing(m, s)) > .99f, s.name + ": statue faces approach along enemy lane");
            }
            var tower = m.structures.First(s => s.kind == StructureKind.OuterTurret && s.health.team == p.Health.team);
            var top = tower.GetComponent<RiftWorldHealthBar>().StructureTop;
            p.head.transform.SetPositionAndRotation(tower.transform.position + new Vector3(-5, 1.65f, -7), Quaternion.LookRotation(new Vector3(5, top - tower.transform.position.y - 1.0f, 7)));
            yield return null;
            Capture("Structure-health");
            // Recall checks exercise the running match update, rather than calling completion directly.
            Time.timeScale = 3;
            p.origin.transform.position += Vector3.forward * 12;
            head = p.origin.transform.position + Vector3.up * 1.65f;
            Pose(head, Quaternion.identity, head + new Vector3(-.2f, -.3f, .4f), Quaternion.identity, head + new Vector3(.2f, -.3f, .4f), Quaternion.identity);
            m.Recall();
            yield return new WaitForSeconds(.4f);
            Check(m.IsRecalling && m.RecallRemaining > 7 && hud.RecallVisible, "Recall starts eight-second bar and numeric timer");
            Capture("Recall");
            m.Recall();
            Check(!m.IsRecalling, "Second recall press cancels channel");
            m.Recall();
            p.origin.transform.position += Vector3.right * .5f;
            yield return null;
            Check(!m.IsRecalling, "Movement cancels recall");
            m.Recall();
            p.Health.TakeDamage(new DamageHit(enemy, enemy.transform.position, 10, DamageKind.True));
            Check(!m.IsRecalling, "Incoming damage cancels recall");
            p.ResetPractice();
            m.Recall();
            p.CastQ();
            Check(!m.IsRecalling, "Ability cast cancels recall");
            yield return new WaitForSeconds(.5f);
            m.Recall();
            p.Health.SetHealth(0);
            yield return null;
            Check(!m.IsRecalling, "Death cancels recall");
            p.Health.ResetHealth();
            m.economy.inventory.Clear();
            m.economy.inventory.Add(new InventorySlot(3157));
            m.economy.Recalculate();
            m.economy.Effects.ResetEffects();
            m.economy.Effects.Activate(3157, p.Feet, p.head.transform.forward);
            m.Recall();
            Check(!m.IsRecalling, "Stasis prevents recall channel");
            m.economy.Effects.ResetEffects();
            m.economy.inventory.Clear();
            m.economy.inventory.Add(new InventorySlot(1056));
            m.economy.Recalculate();
            m.Recall();
            yield return new WaitForSeconds(8.2f);
            Check(!m.IsRecalling && m.AtShop && m.economy.Owns(1056), "Recall completes at fountain and retains inventory");
            head = p.origin.transform.position + Vector3.up * 1.65f;
            Pose(head, Quaternion.identity, head + new Vector3(-.2f, -.3f, .4f), Quaternion.identity, head + new Vector3(.2f, -.3f, .4f), Quaternion.identity);
            Time.timeScale = 1;
            var meshSignatures = new HashSet<string>();
            foreach (int id in new[] { 2003, 2031, 2138, 2139, 2140 })
            {
                var prefab = m.catalog.Find(id).physicalPrefab;
                var meshes = prefab.GetComponentsInChildren<MeshFilter>();
                meshSignatures.Add(string.Join("/", meshes.Select(f => f.sharedMesh.name + f.sharedMesh.vertexCount)));
                Check(meshes.Length >= 4 && prefab.transform.localScale == Vector3.one && prefab.transform.Find("Drink tip"), id + ": complete hand-sized 3D flask with drink lip");
                Check(prefab.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial.GetTexture("_BaseMap")), id + ": existing texture data assigned");
                m.economy.Effects.ResetEffects();
                m.economy.inventory.Clear();
                m.economy.inventory.Add(new InventorySlot(id));
                m.economy.Recalculate();
                p.Health.SetHealth(p.Health.maxHealth * .5f);
                rack.SendMessage("Sync");
                int hand = run == 1 ? 1 : 0;
                Check(rack.EquipSlot(0, hand), id + ": equips from physical rack");
                rack.SendMessage("PoseHeld");
                var flask = Object.FindObjectsByType<Transform>().First(t => t.name == "Drink tip" && t.parent.parent == null).parent;
                var source = hand == 0 ? p.leftHand : p.rightHand;
                Check(Vector3.Distance(flask.position, source.TransformPoint(new Vector3(0, -.025f, .035f))) < .001f, id + ": held position follows controller grip");
                var upright = flask.rotation;
                flask.rotation = Quaternion.Euler(100, 0, 0);
                var tip = flask.Find("Drink tip");
                flask.position = p.head.transform.position - Vector3.up * .09f - flask.rotation * tip.localPosition;
                Check(RiftItemRack.CanSip(flask.gameObject, p.head.transform), id + ": tipped lip at mouth activates sip gesture");
                flask.rotation = Quaternion.identity;
                flask.position = p.head.transform.position - Vector3.up * .09f - tip.localPosition;
                Check(!RiftItemRack.CanSip(flask.gameObject, p.head.transform), id + ": upright bottle does not auto-drink");
                rack.SendMessage("PoseHeld");
                m.Recall();
                Check(rack.UseHeld(hand), id + ": actual potion/elixir use succeeds");
                Check(!m.IsRecalling, id + ": item use cancels recall");
                rack.ReturnHeld(hand);
            }
            Check(meshSignatures.Count >= 3, "Health potion, refillable and elixir families have distinct geometry");
            m.economy.Effects.ResetEffects();
            m.economy.inventory.Clear();
            m.economy.Recalculate();
            rack.SendMessage("Sync");
            yield return MarchCheck();
            Time.timeScale = 1;
            observations.Add("Hand tests use controlled tracking poses in the live XR scene; physical headset comfort and controller motion were not measured.");
            observations.Add("The full march test isolates targeting while traversing all routes, then restores enemy combat for a separate encounter in the same run.");
        }

        IEnumerator MinionRepairSuite()
        {
            m = Object.FindAnyObjectByType<RiftMatch>();
            p = m.player;
            p.GetComponent<GwenVRInput>().enabled = false;
            m.Play();
            typeof(RiftMatch).GetProperty("NextWave").SetValue(m, 100000f);
            foreach (var s in m.structures)
                s.enabled = false;
            foreach (var o in m.objectives)
                o.enabled = false;
            foreach (var f in m.fountains)
                f.enabled = false;
            yield return MarchCheck();
            observations.Add("Focused minion repair verification after the two complete feature runs; no other features were rerun.");
        }

        IEnumerator MarchCheck()
        {
            // Full route traversal for both teams, every lane and all three columns. Combat is isolated
            // by temporarily making actors neutral; spacing still uses the real actor collection.
            Time.timeScale = 4;
            var actors = RiftActor.All.ToDictionary(a => a, a => a.neutral);
            foreach (var a in actors.Keys)
                a.neutral = true;
            var march = new List<March>();
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                {
                    for (int lane = 0; lane < 3; lane++)
                        for (int team = 0; team < 2; team++)
                        {
                            var unit = m.SpawnMinion(team, lane, row == 0 ? MinionKind.Melee : MinionKind.Caster, col);
                            unit.neutral = true;
                            march.Add(new March { unit = unit, team = team, lane = lane, col = col, last = unit.transform.position, lastMoved = Time.time });
                        }
                    yield return new WaitForSeconds(m.rules.minionSpawnSpacing);
                }
            float until = Time.time + 120;
            int frame = 0;
            while (Time.time < until && march.Any(s => !s.arrived))
            {
                foreach (var s in march.Where(s => !s.arrived))
                {
                    var u = s.unit;
                    s.maxCorridor = Mathf.Max(s.maxCorridor, RiftMinion.CorridorDistance(u.transform.position, u.route));
                    if (Vector3.Distance(s.last, u.transform.position) > .1f)
                    {
                        s.last = u.transform.position;
                        s.lastMoved = Time.time;
                    }
                    foreach (var o in march.Where(o => !o.arrived && o.team == s.team && o.lane == s.lane && o.unit.kind == u.kind && o != s))
                    {
                        s.minSeparation = Mathf.Min(s.minSeparation, GwenAbilities.FlatDistance(u.transform.position, o.unit.transform.position));
                    }
                    if (u.Waypoint >= u.route.Length - 2 && GwenAbilities.FlatDistance(u.transform.position, u.route.Last()) < 2.8f)
                    {
                        s.arrived = true;
                        u.gameObject.SetActive(false);
                    }
                }
                if (frame++ % 100 == 0)
                    File.WriteAllText("Logs/POVOverhaul/Run" + run + "-march.txt", string.Join("\n", march.Select(s => $"{s.team}/{s.lane}/{s.unit.kind}/{s.col} waypoint {s.unit.Waypoint}/{s.unit.route.Length} at {s.unit.transform.position:F2}; stalled {Time.time - s.lastMoved:F1}s")));
                yield return null;
            }
            foreach (var s in march)
            {
                Check(s.arrived, $"Route {s.team}/{s.lane}/{s.unit.kind}/column {s.col}: arrived {s.arrived}, waypoint {s.unit.Waypoint}/{s.unit.route.Length}, stopped at {s.unit.transform.position:F2}");
                Check(s.maxCorridor < 3.2f, $"Route {s.team}/{s.lane}/{s.unit.kind}/column {s.col}: stays in lane corridor ({s.maxCorridor:F2}m)");
                Check(s.minSeparation > .55f, $"Route {s.team}/{s.lane}/{s.unit.kind}/column {s.col}: avoids body overlap ({s.minSeparation:F2}m minimum)");
                Object.Destroy(s.unit.gameObject);
            }
            foreach (var a in actors)
                if (a.Key)
                    a.Key.neutral = a.Value;
            yield return null;
            var fighters = new List<RiftMinion>();
            for (int col = 0; col < 3; col++)
                for (int team = 0; team < 2; team++)
                {
                    var u = m.SpawnMinion(team, 1, col == 1 ? MinionKind.Caster : MinionKind.Melee, col);
                    var centre = m.lanes[1].points[m.lanes[1].points.Length / 2];
                    u.transform.position = centre + new Vector3((col - 1) * 1.3f, 0, team == 0 ? -3 : 3);
                    fighters.Add(u);
                }
            yield return new WaitForSeconds(12);
            Check(fighters.Any(u => !u || !u.health.IsAlive || u.health.Health < u.health.maxHealth), "Minions resume enemy targeting and combat after route traversal");
            foreach (var u in fighters.Where(u => u && u.health.IsAlive))
                Check(u.GetComponentInChildren<RiftMinionMotion>().player != null, "Original minion animation controller remains attached");
            Time.timeScale = 1;
        }

        void Finish()
        {
            finished = true;
            Time.timeScale = 1;
            Application.logMessageReceived -= Log;
            if (errors.Count > 0)
                failures.Add("Runtime console errors: " + errors.Count);
            var report = new StringBuilder();
            report.AppendLine("# POV overhaul — test run " + run);
            report.AppendLine("\nDate: " + DateTime.Now.ToString("O"));
            report.AppendLine($"\n{passes.Count} checks passed; {failures.Count} failed. Controlled Unity Play Mode test.");
            report.AppendLine("\n## Failures\n");
            foreach (var s in failures)
                report.AppendLine("- " + s);
            report.AppendLine("\n## Passed checks\n");
            foreach (var s in passes)
                report.AppendLine("- " + s);
            report.AppendLine("\n## Scope and limitations\n");
            foreach (var s in observations)
                report.AppendLine("- " + s);
            File.WriteAllText("Logs/POVOverhaul/Run" + run + ".md", report.ToString());
            File.WriteAllText("Logs/POVOverhaul/Run" + run + "-errors.txt", string.Join("\n", errors));
            Debug.Log("POV test " + run + " finished: " + passes.Count + " passed, " + failures.Count + " failed");
            EditorApplication.isPlaying = false;
        }
    }
}
