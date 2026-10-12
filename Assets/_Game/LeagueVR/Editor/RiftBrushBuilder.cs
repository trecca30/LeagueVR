using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LeagueVR.EditorTools
{
    /// <summary>
    /// Grows Summoner's Rift's brush. The patches come from League's own navigation grid (its brush cells, mapped onto
    /// this map's 25 cm grid in Generated/RiftNav/LeagueBrush.bytes), kept only where this map's floor is walkable.
    /// Each patch gets an id in <see cref="RiftNavGrid.brush"/> (League's hiding rule) and a mesh of curved blades,
    /// shorter at the patch edges for a rounded tuft. Menu: League VR > Build Rift Brush (collision builds call it too).
    /// </summary>
    public static class RiftBrushBuilder
    {
        const string GridPath = "Assets/_Game/LeagueVR/Generated/RiftNav/RiftNavGrid.asset";
        const string MaskPath = "Assets/_Game/LeagueVR/Generated/RiftNav/LeagueBrush.bytes";
        const string MeshFolder = "Assets/_Game/LeagueVR/Generated/RiftNav/Brush";
        const string MaterialPath = "Assets/_Game/SummonersRIft/VRRebuild/Rift Brush.mat";
        const string RootName = "Rift Brush";
        const int MinCells = 10, BladesPerCell = 4, Segments = 4;

        static readonly Color[] Palette =
        {
            new(.03f, .13f, .11f), new(.04f, .17f, .12f), new(.03f, .12f, .14f), new(.06f, .2f, .11f), new(.04f, .15f, .15f),
        };

        [MenuItem("League VR/Build Rift Brush")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var grid = AssetDatabase.LoadAssetAtPath<RiftNavGrid>(GridPath);
            if (!grid || !File.Exists(MaskPath))
                return "Brush: no grid or brush mask.";
            var mask = File.ReadAllBytes(MaskPath);
            int w = grid.width, h = grid.height;
            if (mask.Length != w * h)
                return $"Brush: mask is {mask.Length} cells, grid {w * h}.";

            // ---------- Patches: 8-connected brush cells on walkable floor ----------
            var patch = new short[w * h];
            var cells = new List<List<int>>();
            var queue = new Queue<int>();
            for (int start = 0; start < patch.Length; start++)
            {
                if (patch[start] != 0 || mask[start] == 0 || grid.walkable[start] == 0)
                    continue;
                var members = new List<int>();
                patch[start] = -1;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    members.Add(c);
                    int cx = c % w, cz = c / w;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = cx + dx, z = cz + dz;
                            if (x < 0 || z < 0 || x >= w || z >= h)
                                continue;
                            int n = z * w + x;
                            if (patch[n] == 0 && mask[n] != 0 && grid.walkable[n] != 0)
                            {
                                patch[n] = -1;
                                queue.Enqueue(n);
                            }
                        }
                }
                if (members.Count >= MinCells)
                {
                    cells.Add(members);
                    foreach (int c in members)
                        patch[c] = (short)cells.Count;
                }
            }
            for (int i = 0; i < patch.Length; i++)
                if (patch[i] < 0)
                    patch[i] = 0;
            grid.brush = patch;
            EditorUtility.SetDirty(grid);

            // ---------- Blades ----------
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(Shader.Find("LeagueVR/Rift Brush")) { name = "Rift Brush" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            // League's brush: a dense dark blue-green mass with muted yellow-green tips.
            material.SetColor("_TipColor", new Color(.36f, .48f, .17f));
            material.SetFloat("_Light", .4f);
            EditorUtility.SetDirty(material);
            if (AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.DeleteAsset(MeshFolder);
            AssetDatabase.CreateFolder(Path.GetDirectoryName(MeshFolder).Replace('\\', '/'), Path.GetFileName(MeshFolder));
            var root = GameObject.Find(RootName) ?? new GameObject(RootName);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            if (!root.GetComponent<RiftBrushField>())
                root.AddComponent<RiftBrushField>();
            var edge = EdgeDistance(patch, w, h);
            int blades = 0;
            for (int p = 0; p < cells.Count; p++)
            {
                var mesh = PatchMesh(grid, cells[p], edge, p + 1, ref blades);
                AssetDatabase.CreateAsset(mesh, $"{MeshFolder}/Brush {p + 1}.asset");
                var go = new GameObject($"Brush {p + 1}");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();
            return $"brush: {cells.Count} patches, {blades} blades";
        }

        /// <summary>Cells from each brush cell to the nearest cell outside its patch (for rounded tufts).</summary>
        static int[] EdgeDistance(short[] patch, int w, int h)
        {
            var distance = new int[patch.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < patch.Length; i++)
            {
                if (patch[i] == 0)
                    continue;
                int x = i % w, z = i / w;
                bool border = x == 0 || z == 0 || x == w - 1 || z == h - 1 || patch[i - 1] != patch[i] || patch[i + 1] != patch[i] || patch[i - w] != patch[i] || patch[i + w] != patch[i];
                distance[i] = border ? 1 : int.MaxValue;
                if (border)
                    queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                foreach (int n in new[] { c - 1, c + 1, c - w, c + w })
                    if (n >= 0 && n < patch.Length && patch[n] == patch[c] && distance[n] > distance[c] + 1)
                    {
                        distance[n] = distance[c] + 1;
                        queue.Enqueue(n);
                    }
            }
            return distance;
        }

        static float Hash(int a, int b) => Mathf.Abs(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f) % 1f;

        static Mesh PatchMesh(RiftNavGrid grid, List<int> patchCells, int[] edge, int id, ref int blades)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            int w = grid.width;
            var centre = Vector3.zero;
            foreach (int c in patchCells)
                centre += new Vector3(grid.origin.x + (c % w + .5f) * grid.cellSize, 0, grid.origin.y + (c / w + .5f) * grid.cellSize);
            centre /= patchCells.Count;
            foreach (int c in patchCells)
            {
                float floor = grid.floor[c] * .01f;
                for (int b = 0; b < BladesPerCell; b++)
                {
                    float r1 = Hash(c, b), r2 = Hash(c + 7, b + 3), r3 = Hash(c + 13, b + 11), r4 = Hash(c + 29, b + 17);
                    var root = new Vector3(grid.origin.x + (c % w + r1) * grid.cellSize, floor - .03f, grid.origin.y + (c / w + r2) * grid.cellSize);
                    // Full height about a metre in from the edge; a little shorter and leaning out at the rim.
                    float inner = Mathf.Clamp01(edge[c] * grid.cellSize / 1f);
                    float tall = Mathf.Lerp(.6f, 1.45f, Mathf.SmoothStep(0, 1, inner)) * Mathf.Lerp(.85f, 1.12f, r3);
                    float yaw = r4 * 360;
                    var facing = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                    var side = Vector3.Cross(Vector3.up, facing);
                    var outward = root - centre;
                    outward.y = 0;
                    var lean = (outward.sqrMagnitude > 1e-4f ? outward.normalized * (1 - inner) * .25f : Vector3.zero) + facing * Mathf.Lerp(.08f, .3f, r3);
                    float width = Mathf.Lerp(.09f, .15f, r1);
                    var color = Palette[(int)(r2 * Palette.Length) % Palette.Length];
                    int first = vertices.Count;
                    for (int k = 0; k <= Segments; k++)
                    {
                        float t = (float)k / Segments;
                        var spine = root + Vector3.up * tall * t + lean * t * t;
                        float half = width * .5f * Mathf.Pow(1 - t, .8f) + .002f;
                        var normal = (facing + Vector3.up * .4f).normalized;
                        vertices.Add(spine - side * half);
                        vertices.Add(spine + side * half);
                        normals.Add(normal);
                        normals.Add(normal);
                        color.a = t;
                        colors.Add(color);
                        colors.Add(color);
                        if (k > 0)
                        {
                            int a = first + (k - 1) * 2;
                            triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                        }
                    }
                    blades++;
                }
            }
            var mesh = new Mesh { name = $"Brush {id}" };
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
