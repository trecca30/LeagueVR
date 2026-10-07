using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRMatchTests
    {
        static readonly List<string> report = new List<string>();
        static RiftMatch match;

        static void Check(bool value, string text)
        {
            if (!value)
                throw new InvalidOperationException(text);
            report.Add("PASS " + text);
        }

        [MenuItem("Tools/League VR/Test League Match")]
        public static void Run()
        {
            report.Clear();
            match = Object.FindFirstObjectByType<RiftMatch>();
            Check(match, "Match exists");
            Check(match.catalog.items.Length > 180, "Current item catalog imported");
            Check(match.catalog.items.All(i => i.icon), "Every catalog item has its original icon");
            Check(match.structures.Length == 30, "Both teams have 11 turrets, 3 inhibitors and a Nexus");
            Check(match.minions.Length == 8, "All four minion types have both team prefabs");
            Check(!Object.FindObjectsByType<TrainingSentinel>(FindObjectsSortMode.None).Any(), "No practice dummies remain");
            Check(match.player.origin && match.player.head && match.player.rightHand && match.player.leftHand, "Original XR/Gwen references remain connected");
            Check(Mathf.Approximately(match.rules.Stats(MinionKind.Melee, 0).health, 465), "Initial melee HP is 465");
            Check(Mathf.Approximately(match.rules.Stats(MinionKind.Caster, 0).health, 284), "Initial caster HP is 284");
            Check(Mathf.Approximately(match.rules.Stats(MinionKind.Cannon, 90).health, 920), "First cannon HP is 920");
            Check(match.rules.firstWave == 30 && match.rules.WaveInterval(839) == 30 && match.rules.WaveInterval(840) == 25 && match.rules.WaveInterval(1800) == 20, "Wave timer boundaries match current rules");
            Check(!match.rules.CannonWave(2, 60) && match.rules.CannonWave(3, 90), "First cannon is wave 3");
            int misses = 0, total = 0;
            foreach (var lane in match.lanes)
                foreach (var point in lane.points)
                {
                    total++;
                    if (!match.Ground(point, out var p) || Mathf.Abs(p.y - point.y) > .4f)
                        misses++;
                }
            Check(misses == 0, $"All {total} lane samples rest on existing ground");
            if (EditorApplication.isPlaying)
            {
                match.Play();
                Check(match.Running && match.AtShop, "Play starts at a healing/shop fountain");
                match.economy.Gold = 10000;
                Check(match.economy.Buy(1043), "Recurve Bow purchase succeeds");
                Check(match.economy.Buy(1026), "Blasting Wand purchase succeeds");
                Check(match.economy.Buy(3108), "Fiendish Codex purchase succeeds");
                var nashor = match.catalog.Find(3115);
                int remaining = match.economy.Cost(nashor, out var used);
                Check(used.Count == 3 && remaining == nashor.price - match.catalog.Find(1043).price - match.catalog.Find(1026).price - match.catalog.Find(3108).price, "Upgrade uses owned components without charging twice");
                int gold = match.economy.Gold;
                Check(match.economy.Buy(3115) && match.economy.inventory.Count == 1 && match.economy.Gold == gold - remaining, "Crafting consumes components and correct gold");
                Check(match.economy.AbilityPower >= 80, "Purchased stats affect Gwen");
                Check(match.economy.Sell(0) && match.economy.inventory.Count == 0, "Selling removes item stats");
                var minion = match.SpawnMinion(1, 1, MinionKind.Melee);
                Check(minion.health.IsAlive && minion.health.Health == 465, "Spawned enemy minion has correct health");
                int before = match.economy.Gold;
                minion.health.TakeDamage(new DamageHit(match.player.Health, minion.transform.position, 99999, DamageKind.True));
                Check(!minion.health.IsAlive && match.economy.Gold == before + 20, "Minion death awards last-hit gold");
                var tower = match.structures.First(s => s.health.team == 1 && s.kind == StructureKind.OuterTurret);
                var cc = match.player.origin.GetComponent<CharacterController>();
                bool on = cc && cc.enabled;
                if (cc)
                    cc.enabled = false;
                match.player.origin.transform.position = tower.transform.position + Vector3.right * 4 + Vector3.up * .1f;
                if (cc)
                    cc.enabled = on;
                Physics.SyncTransforms();
                float health = match.player.Health.Health;
                RiftMissile.Launch(tower.health, match.player.Health, match.player.Health.AimPosition, 152, 18, match.redMaterial);
                match.ui.Close();
                EditorApplication.delayCall += () =>
{
    File.WriteAllText("Logs/LeagueMatch/combat-check.txt", $"Turret target test initial Gwen health={health}; after={match.player.Health.Health}");
};
                Check(!match.economy.Buy(1056), "Buying outside fountain is rejected");
                match.Play();
            }
            File.WriteAllText("Logs/LeagueMatch/tests.txt", string.Join("\n", report));
            Debug.Log(string.Join("\n", report));
            Capture();
        }

        [MenuItem("Tools/League VR/Capture League Match")]
        public static void Capture()
        {
            match = Object.FindFirstObjectByType<RiftMatch>();
            if (!match)
                return;
            Directory.CreateDirectory("Logs/LeagueMatch");
            var go = new GameObject("Temporary verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.farClipPlane = 600;
            camera.nearClipPlane = .05f;
            var rt = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = rt;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var prior = RenderTexture.active;
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = 85;
                camera.transform.SetPositionAndRotation(new Vector3(3, 180, 3), Quaternion.Euler(90, 0, 0));
                Shot("overview");
                camera.orthographic = false;
                camera.fieldOfView = 72;
                camera.transform.SetPositionAndRotation(match.player.head.transform.position, match.player.head.transform.rotation);
                Shot("player");
                void Shot(string name)
                {
                    camera.Render();
                    RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                    image.Apply();
                    File.WriteAllBytes("Logs/LeagueMatch/" + name + ".png", image.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = prior;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
