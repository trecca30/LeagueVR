using System.Collections.Generic;
using System.IO;
using System.Linq;
using LeagueVR.Match;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace LeagueVR.EditorTools
{
    /// <summary>
    /// Builds Summoner's Rift's collision from the map's own meshes, so walls stand exactly where this version of the map
    /// draws them. The meshes are voxelized into a navigation mesh (Recast, the voxel approach behind navigation grids
    /// such as League's); only floor connected to the fountains and lanes is kept, so wall tops and sealed pockets count
    /// as walls. The outline of that floor becomes invisible double-sided walls, each as tall as the map wall beside it,
    /// and the floor is saved as a 25 cm <see cref="RiftNavGrid"/> for abilities. Towers, inhibitors and Nexuses get
    /// solid blockers that switch off when they fall. Menu: League VR > Build Rift Collision.
    /// </summary>
    public static class RiftCollisionBuilder
    {
        const string MapRoot = "Summoner's Rift", Pieces = "VR rebuilt map", Folder = "Assets/_Game/LeagueVR/Generated/RiftNav";
        const string CollisionName = "Rift Collision", BlockerName = "Movement blocker";
        const float Cell = .25f, MinWall = 1.4f, MaxWall = 7f;
        static readonly string[] Ignored = { "bk_distant_horizon_a", "Boundary foundations", "Foundation collision" };

        // League's own piece names say what is floor; everything else (jungle islands, perimeter, base walls, shops,
        // arches) is wall: solid, but never walkable, even where its rocks slope gently enough to climb.
        static readonly string[] FloorPrefixes = { "grnd_terrain", "Foundation", "mage_spawn_pad", "pally_platform", "_chunk_base_north_platform" };

        // Floor that must be reachable: both fountains, the middle of the map and the three lanes.
        static readonly Vector3[] Seeds =
        {
            new(-70, 0, -59), new(66, 0, 72), new(-2, 0, 7),
            new(-63, 0, 18), new(-12, 0, 70), new(-46, 0, 70),
            new(10, 0, -55), new(60, 0, -2), new(-6, 0, 2),
        };

        [MenuItem("League VR/Build Rift Collision")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var root = GameObject.Find(MapRoot);
            var pieces = root ? root.transform.Find(Pieces) : null;
            if (!pieces)
                return "No '" + MapRoot + "/" + Pieces + "' in the open scene.";
            int layer = LayerMask.NameToLayer("LeagueWorld");

            // ---------- Voxelize the map ----------
            var meshes = new List<(Mesh mesh, Matrix4x4 matrix)>();
            var floorPieces = new List<(Mesh mesh, Matrix4x4 matrix)>();
            var wallPieces = new List<(Mesh mesh, Matrix4x4 matrix)>();
            foreach (var filter in pieces.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!filter.sharedMesh || Ignored.Contains(filter.name) || !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled)
                    continue;
                var piece = (filter.sharedMesh, filter.transform.localToWorldMatrix);
                meshes.Add(piece);
                (FloorPrefixes.Any(prefix => filter.name.StartsWith(prefix)) ? floorPieces : wallPieces).Add(piece);
            }
            var bounds = new Bounds(new Vector3(0, -4, 8), new Vector3(180, 44, 184));
            var min = new Vector2(bounds.min.x, bounds.min.z);
            int width = Mathf.CeilToInt(bounds.size.x / Cell), height = Mathf.CeilToInt(bounds.size.z / Cell);
            // The floor pieces' height per cell, to tell a wall piece's stairs and ground skirts from its walls.
            var floorHeight = Enumerable.Repeat(float.NegativeInfinity, width * height).ToArray();
            foreach (var (mesh, matrix) in floorPieces)
                ForEachTriangle(mesh, matrix, (a, b, c) => Rasterize(a, b, c, min, width, height, (cell, y) =>
                {
                    if (y > floorHeight[cell])
                        floorHeight[cell] = y;
                }));
            // Floor pieces are walkable. Wall pieces are walls, except their flat parts within 0.55 m of the floor below
            // (base exit stairs, ramps, grass skirts) and short risers between them. Low base walls (0.6 m and more)
            // stay walls. The export is mirrored, so most triangles face down in the world
            // (they render double-sided); walkable ones are turned to face up, since voxelizing treats downward faces
            // as ceilings.
            var walkable = new List<Vector3>();
            var blocked = new List<Vector3>();
            foreach (var (mesh, matrix) in floorPieces)
                ForEachTriangle(mesh, matrix, (a, b, c) => AddUp(walkable, a, b, c));
            foreach (var (mesh, matrix) in wallPieces)
                ForEachTriangle(mesh, matrix, (a, b, c) =>
                {
                    var normal = Vector3.Cross(b - a, c - a);
                    float area = normal.magnitude;
                    Vector3 centre = (a + b + c) / 3;
                    int x = Mathf.FloorToInt((centre.x - min.x) / Cell), z = Mathf.FloorToInt((centre.z - min.y) / Cell);
                    float ground = x >= 0 && z >= 0 && x < width && z < height ? floorHeight[z * width + x] : float.NegativeInfinity;
                    bool flat = area > 1e-8f && Mathf.Abs(normal.y) / area > .8f;
                    float above = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - ground;
                    // Stair risers are ordinary ground the voxelizer can climb; marking them unwalkable would wall off
                    // every flight of stairs.
                    float rise = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    if (flat && above < .55f)
                        AddUp(walkable, a, b, c);
                    else if (above < .5f && rise < .3f)
                        walkable.AddRange(new[] { a, b, c });
                    else
                        blocked.AddRange(new[] { a, b, c });
                });
            var sources = new List<NavMeshBuildSource>
            {
                new() { shape = NavMeshBuildSourceShape.Mesh, sourceObject = WorldMesh("Rift floor", walkable), transform = Matrix4x4.identity, area = 0 },
                new() { shape = NavMeshBuildSourceShape.Mesh, sourceObject = WorldMesh("Rift walls", blocked), transform = Matrix4x4.identity, area = 1 },
            };
            var settings = NavMesh.GetSettingsByIndex(0);
            // The player's CharacterController: 0.1 m radius, 1.36 m tall, 0.5 m steps, 45° slopes (fountain stairs included).
            settings.agentRadius = .1f;
            settings.agentHeight = 1.4f;
            settings.agentSlope = 45;
            settings.agentClimb = .5f;
            settings.minRegionArea = 1;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .075f;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (!data)
                return "NavMesh build failed.";
            NavMesh.RemoveAllNavMeshData();
            var instance = NavMesh.AddNavMeshData(data);
            var triangulation = NavMesh.CalculateTriangulation();

            // ---------- Weld tiles, keep the floor connected to the lanes ----------
            var weld = new Dictionary<(int, int, int), int>();
            var points = new List<Vector3>();
            var vertexOf = new int[triangulation.vertices.Length];
            for (int i = 0; i < triangulation.vertices.Length; i++)
            {
                var v = triangulation.vertices[i];
                var key = (Mathf.RoundToInt(v.x * 100), Mathf.RoundToInt(v.z * 100), Mathf.RoundToInt(v.y * 2));
                if (!weld.TryGetValue(key, out int index))
                {
                    index = points.Count;
                    points.Add(v);
                    weld[key] = index;
                }
                vertexOf[i] = index;
            }
            int triangleCount = triangulation.indices.Length / 3;
            var tris = new int[triangleCount * 3];
            for (int i = 0; i < tris.Length; i++)
                tris[i] = vertexOf[triangulation.indices[i]];
            var edges = new Dictionary<long, List<int>>();
            long EdgeKey(int a, int b) => a < b ? (long)a * 10_000_000 + b : (long)b * 10_000_000 + a;
            for (int t = 0; t < triangleCount; t++)
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]);
                    if (!edges.TryGetValue(key, out var list))
                        edges[key] = list = new List<int>(2);
                    list.Add(t);
                }
            var kept = new bool[triangleCount];
            var queue = new Queue<int>();
            int seedsFound = 0;
            var seedReport = new List<string>();
            foreach (var seed in Seeds)
            {
                int t = TriangleAt(seed, points, tris);
                seedReport.Add(t < 0 ? "none" : kept[t] ? "joined" : "new");
                if (t < 0 || kept[t])
                    continue;
                seedsFound++;
                kept[t] = true;
                queue.Enqueue(t);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    for (int e = 0; e < 3; e++)
                        foreach (int next in edges[EdgeKey(tris[current * 3 + e], tris[current * 3 + (e + 1) % 3])])
                            if (!kept[next])
                            {
                                kept[next] = true;
                                queue.Enqueue(next);
                            }
                }
            }
            int keptCount = kept.Count(k => k);
            instance.Remove();

            // ---------- Navigation grid ----------
            var open = new byte[width * height];
            var floor = new short[width * height];
            for (int t = 0; t < triangleCount; t++)
                if (kept[t])
                    Rasterize(points[tris[t * 3]], points[tris[t * 3 + 1]], points[tris[t * 3 + 2]], min, width, height, (cell, y) =>
                    {
                        open[cell] = 1;
                        floor[cell] = (short)Mathf.Clamp(Mathf.RoundToInt(y * 100), short.MinValue, short.MaxValue);
                    });
            // Tallest map surface per cell, for wall heights.
            var top = Enumerable.Repeat(float.NegativeInfinity, width * height).ToArray();
            foreach (var (mesh, matrix) in meshes)
                ForEachTriangle(mesh, matrix, (a, b, c) => Rasterize(a, b, c, min, width, height, (cell, y) =>
                {
                    if (y > top[cell])
                        top[cell] = y;
                }));

            // ---------- Walls along the floor's outline ----------
            var wallVertices = new List<Vector3>();
            var wallTriangles = new List<int>();
            int boundary = 0;
            foreach (var pair in edges)
            {
                var owners = pair.Value.Where(t => kept[t]).ToList();
                if (owners.Count != 1)
                    continue;
                int owner = owners[0];
                int a = (int)(pair.Key / 10_000_000), b = (int)(pair.Key % 10_000_000);
                int c = tris[owner * 3] != a && tris[owner * 3] != b ? tris[owner * 3] : tris[owner * 3 + 1] != a && tris[owner * 3 + 1] != b ? tris[owner * 3 + 1] : tris[owner * 3 + 2];
                Vector3 pa = points[a], pb = points[b];
                Vector3 mid = (pa + pb) * .5f;
                Vector3 outward = Vector3.Cross(Vector3.up, pb - pa);
                outward.y = 0;
                if (outward.sqrMagnitude < 1e-8f)
                    continue;
                outward.Normalize();
                Vector3 inside = points[c] - mid;
                if (outward.x * inside.x + outward.z * inside.z > 0)
                    outward = -outward;
                float floorY = Mathf.Max(pa.y, pb.y);
                float wallTop = float.NegativeInfinity;
                foreach (float reach in new[] { .3f, .6f, 1f })
                {
                    var probe = mid + outward * reach;
                    int x = Mathf.FloorToInt((probe.x - min.x) / Cell), z = Mathf.FloorToInt((probe.z - min.y) / Cell);
                    if (x >= 0 && z >= 0 && x < width && z < height)
                        wallTop = Mathf.Max(wallTop, top[z * width + x]);
                }
                float tall = float.IsNegativeInfinity(wallTop) ? 3 : Mathf.Clamp(wallTop - floorY, MinWall, MaxWall);
                AddWall(wallVertices, wallTriangles, pa, pb, Mathf.Min(pa.y, pb.y) - 1, floorY + tall);
                boundary++;
            }

            // ---------- Save ----------
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
            var wallMesh = new Mesh { name = "Rift walls", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            wallMesh.SetVertices(wallVertices);
            wallMesh.SetTriangles(wallTriangles, 0);
            wallMesh.RecalculateBounds();
            wallMesh.RecalculateNormals();
            wallMesh = Save(wallMesh, Folder + "/RiftWalls.asset");
            var grid = ScriptableObject.CreateInstance<RiftNavGrid>();
            grid.origin = min;
            grid.cellSize = Cell;
            grid.width = width;
            grid.height = height;
            grid.walkable = open;
            grid.floor = floor;
            grid = Save(grid, Folder + "/RiftNavGrid.asset");
            Save(data, Folder + "/RiftNavMesh.asset");

            var collision = GameObject.Find(CollisionName) ?? new GameObject(CollisionName);
            collision.layer = layer;
            collision.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            collision.transform.localScale = Vector3.one;
            if (!collision.TryGetComponent<MeshCollider>(out var meshCollider))
                meshCollider = collision.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = wallMesh;
            if (!collision.TryGetComponent<RiftCollision>(out var component))
                component = collision.AddComponent<RiftCollision>();
            component.grid = grid;
            int blockers = AddStructureBlockers(layer);

            WritePreview(grid, top);
            // A new grid has no brush patches yet.
            string brush = RiftBrushBuilder.Build();
            EditorSceneManager.MarkSceneDirty(collision.scene);
            EditorSceneManager.SaveScene(collision.scene);
            AssetDatabase.SaveAssets();
            int openCells = open.Count(w => w != 0);
            return $"navmesh {triangleCount} triangles, kept {keptCount} in {seedsFound} regions (seeds: {string.Join(",", seedReport)}); grid {width}x{height}, walkable {openCells * Cell * Cell:0} m², floor triangles {walkable.Count / 3}, wall triangles {blocked.Count / 3}; walls {boundary} segments ({wallTriangles.Count / 3} triangles); structure blockers {blockers}; {brush}";
        }

        static void ForEachTriangle(Mesh mesh, Matrix4x4 matrix, System.Action<Vector3, Vector3, Vector3> visit)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                visit(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
        }

        /// <summary>Adds a triangle wound to face up (Unity's clockwise winding seen from above).</summary>
        static void AddUp(List<Vector3> list, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).y < 0)
                (b, c) = (c, b);
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        static Mesh WorldMesh(string name, List<Vector3> triangleCorners)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(triangleCorners);
            mesh.SetTriangles(Enumerable.Range(0, triangleCorners.Count).ToArray(), 0);
            return mesh;
        }

        static int TriangleAt(Vector3 point, List<Vector3> points, int[] tris)
        {
            if (!NavMesh.SamplePosition(point + Vector3.up * 2, out var hit, 6, NavMesh.AllAreas))
                return -1;
            int best = -1;
            float bestHeight = float.MaxValue;
            for (int t = 0; t < tris.Length / 3; t++)
            {
                Vector3 a = points[tris[t * 3]], b = points[tris[t * 3 + 1]], c = points[tris[t * 3 + 2]];
                if (!Barycentric(hit.position.x, hit.position.z, a, b, c, out float u, out float v, out float w))
                    continue;
                float gap = Mathf.Abs(a.y * u + b.y * v + c.y * w - hit.position.y);
                if (gap < bestHeight)
                {
                    bestHeight = gap;
                    best = t;
                }
            }
            return best;
        }

        static bool Barycentric(float x, float z, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
        {
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            u = v = w = 0;
            if (Mathf.Abs(d) < 1e-9f)
                return false;
            u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            w = 1 - u - v;
            return u >= -1e-4f && v >= -1e-4f && w >= -1e-4f;
        }

        /// <summary>Calls <paramref name="write"/> for every grid cell whose centre lies under the triangle, with its height there.</summary>
        static void Rasterize(Vector3 a, Vector3 b, Vector3 c, Vector2 min, int width, int height, System.Action<int, float> write)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - min.x) / Cell));
            int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - min.x) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - min.y) / Cell));
            int z1 = Mathf.Min(height - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - min.y) / Cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float cx = min.x + (x + .5f) * Cell, cz = min.y + (z + .5f) * Cell;
                    if (Barycentric(cx, cz, a, b, c, out float u, out float v, out float w))
                        write(z * width + x, a.y * u + b.y * v + c.y * w);
                }
        }

        /// <summary>A vertical quad from <paramref name="bottom"/> to <paramref name="top"/>, facing both ways.</summary>
        static void AddWall(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, float bottom, float top)
        {
            int i = vertices.Count;
            vertices.Add(new Vector3(a.x, bottom, a.z));
            vertices.Add(new Vector3(b.x, bottom, b.z));
            vertices.Add(new Vector3(b.x, top, b.z));
            vertices.Add(new Vector3(a.x, top, a.z));
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        /// <summary>Capsules on the movement layer around every structure, sized to its footprint (they fall with it).</summary>
        static int AddStructureBlockers(int layer)
        {
            int count = 0;
            foreach (var structure in Object.FindObjectsByType<RiftStructure>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var existing = structure.transform.Find(BlockerName);
                var blocker = existing ? existing.gameObject : new GameObject(BlockerName);
                blocker.transform.SetParent(structure.transform, false);
                blocker.transform.localPosition = Vector3.zero;
                blocker.transform.localRotation = Quaternion.identity;
                blocker.layer = layer;
                if (!blocker.TryGetComponent<CapsuleCollider>(out var capsule))
                    capsule = blocker.AddComponent<CapsuleCollider>();
                float scale = Mathf.Max(.01f, structure.transform.lossyScale.x);
                capsule.radius = structure.Footprint * .75f / scale;
                capsule.height = 7 / scale;
                capsule.center = Vector3.up * 3 / scale;
                capsule.direction = 1;
                count++;
            }
            return count;
        }

        static T Save<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Top-down picture of the grid for reviews: walkable floor green (shaded by height), walls by height.</summary>
        static void WritePreview(RiftNavGrid grid, float[] top)
        {
            var texture = new Texture2D(grid.width, grid.height, TextureFormat.RGB24, false);
            var pixels = new Color32[grid.width * grid.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                if (grid.walkable[i] != 0)
                {
                    float y = Mathf.InverseLerp(-2, 1.5f, grid.floor[i] * .01f);
                    pixels[i] = Color32.Lerp(new Color32(40, 110, 60, 255), new Color32(150, 220, 140, 255), y);
                }
                else
                {
                    float t = float.IsNegativeInfinity(top[i]) ? 0 : Mathf.InverseLerp(-2, 8, top[i]);
                    pixels[i] = Color32.Lerp(new Color32(20, 20, 30, 255), new Color32(130, 110, 150, 255), t);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Directory.CreateDirectory("Temp/claude-pov/rift_nav");
            File.WriteAllBytes("Temp/claude-pov/rift_nav/navgrid.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
