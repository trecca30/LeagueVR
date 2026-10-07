using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LeagueVR.Match;
using Object = UnityEngine.Object;
namespace LeagueVR.Editor
{
    public static class LeagueVRMapNavigation
    {
        const float Step = .75f;
        const int Width = 211, Depth = 211;
        static Vector3[] positions;
        static bool[] valid;
        static int sequence;

        struct Node
        {
            public int id, sequence;
            public float cost, priority;
        }

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode");
            var map = GameObject.Find("Summoner's Rift");
            var match = Object.FindAnyObjectByType<RiftMatch>();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, "PrototypeBackups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-BeforeMapRouteAlignment.unity", true);
            positions = new Vector3[Width * Depth];
            valid = new bool[positions.Length];
            Physics.SyncTransforms();
            int free = 0;
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    int i = z * Width + x;
                    var at = new Vector3(-78 + x * Step, 0, -68 + z * Step);
                    if (!LeagueVRMapWork.Ground(at, out var hit))
                        continue;
                    positions[i] = hit.point;
                    var overlaps = Physics.OverlapCapsule(hit.point + Vector3.up * .55f, hit.point + Vector3.up * 1.4f, .32f, match.player.worldMask, QueryTriggerInteraction.Ignore);
                    if (overlaps.Any(c => c.transform.IsChildOf(map.transform) && c.name != "Foundation collision"))
                        continue;
                    valid[i] = true;
                    free++;
                }
            Vector2[][] controls = { new[] { new Vector2(-59, -45), new Vector2(-63, -27), new Vector2(-63, -4), new Vector2(-63, 26), new Vector2(-57, 50), new Vector2(-38, 62), new Vector2(-13, 65), new Vector2(13, 65), new Vector2(36, 64), new Vector2(51, 61) }, new[] { new Vector2(-59, -45), new Vector2(-44, -30), new Vector2(-28, -17), new Vector2(-12, -2), new Vector2(1, 11), new Vector2(14, 25), new Vector2(28, 40), new Vector2(40, 52), new Vector2(51, 61) }, new[] { new Vector2(-59, -45), new Vector2(-43, -51), new Vector2(-17, -54), new Vector2(10, -54), new Vector2(37, -51), new Vector2(56, -40), new Vector2(59, -18), new Vector2(59, 10), new Vector2(59, 36), new Vector2(51, 61) } };
            var report = new StringBuilder("Lane routes aligned to walkable map surfaces; gameplay timing and combat unchanged.\n");
            report.AppendLine("Walkable grid cells: " + free);
            for (int lane = 0; lane < 3; lane++)
            {
                var path = new List<int>();
                for (int p = 1; p < controls[lane].Length; p++)
                {
                    int a = Closest(controls[lane][p - 1]), b = Closest(controls[lane][p]);
                    var part = Path(a, b);
                    if (path.Count > 0)
                        part.RemoveAt(0);
                    path.AddRange(part);
                }
                var reduced = new List<Vector3>();
                for (int i = 0; i < path.Count; i++)
                {
                    if (i == 0 || i == path.Count - 1 || path[i] - path[i - 1] != path[i + 1] - path[i])
                        reduced.Add(positions[path[i]]);
                }
                match.lanes[lane].points = reduced.ToArray();
                report.AppendLine(match.lanes[lane].name + ": " + path.Count + " checked grid steps, " + reduced.Count + " turns/endpoints");
            }
            int fixedMaterials = 0;
            foreach (var p in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Game/LeagueVR/Match/Generated" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!mat.HasProperty("_BaseMap") || !mat.GetTexture("_BaseMap"))
                    continue;
                mat.SetTextureScale("_BaseMap", new Vector2(1, -1));
                mat.SetTextureOffset("_BaseMap", new Vector2(0, 1));
                EditorUtility.SetDirty(mat);
                fixedMaterials++;
            }
            report.AppendLine("Original League structure/minion/monster texture V origin corrected in editable materials: " + fixedMaterials);
            EditorUtility.SetDirty(match);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Logs/MapRebuild/navigation.txt", report.ToString());
            Frame();
        }

        static int Closest(Vector2 p)
        {
            int best = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < positions.Length; i++)
                if (valid[i])
                {
                    float d = Vector2.SqrMagnitude(new Vector2(positions[i].x, positions[i].z) - p);
                    if (d < distance)
                    {
                        best = i;
                        distance = d;
                    }
                }
            if (best < 0 || distance > 100)
                throw new InvalidOperationException("No safe path point near " + p);
            return best;
        }

        static List<int> Path(int start, int goal)
        {
            var frontier = new SortedSet<Node>(Comparer<Node>.Create((a, b) =>
         {
             int c = a.priority.CompareTo(b.priority);
             return c != 0 ? c : a.sequence.CompareTo(b.sequence);
         }));
            var score = new float[positions.Length];
            for (int i = 0; i < score.Length; i++)
                score[i] = float.PositiveInfinity;
            var prior = new int[score.Length];
            Array.Fill(prior, -1);
            score[start] = 0;
            frontier.Add(new Node { id = start, cost = 0, priority = Heuristic(start, goal), sequence = sequence++ });
            while (frontier.Count > 0)
            {
                var node = frontier.Min;
                frontier.Remove(node);
                if (node.cost > score[node.id] + .0001f)
                    continue;
                if (node.id == goal)
                {
                    var path = new List<int>();
                    for (int i = goal; i != -1; i = prior[i])
                        path.Add(i);
                    path.Reverse();
                    return path;
                }
                int x = node.id % Width, z = node.id / Width;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= Width || nz >= Depth)
                            continue;
                        int id = nz * Width + nx;
                        if (!valid[id])
                            continue;
                        if (dx != 0 && dz != 0 && (!valid[z * Width + nx] || !valid[nz * Width + x]))
                            continue;
                        if (Mathf.Abs(positions[id].y - positions[node.id].y) > .5f)
                            continue;
                        float cost = node.cost + Vector3.Distance(positions[node.id], positions[id]);
                        if (cost >= score[id])
                            continue;
                        score[id] = cost;
                        prior[id] = node.id;
                        frontier.Add(new Node { id = id, cost = cost, priority = cost + Heuristic(id, goal), sequence = sequence++ });
                    }
            }
            throw new InvalidOperationException("Cannot connect map route: " + positions[start] + " to " + positions[goal]);
        }

        static float Heuristic(int a, int b)
        {
            return Vector2.Distance(new Vector2(positions[a].x, positions[a].z), new Vector2(positions[b].x, positions[b].z));
        }

        public static void Frame()
        {
            var view = SceneView.lastActiveSceneView;
            if (view)
            {
                view.sceneViewState.showFog = false;
                view.LookAt(new Vector3(3, 0, 3), Quaternion.Euler(75, 0, 0), 88, true);
                SceneView.RepaintAll();
            }
        }
    }
}

