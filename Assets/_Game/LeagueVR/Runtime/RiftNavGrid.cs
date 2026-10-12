using UnityEngine;

namespace LeagueVR
{
    /// <summary>
    /// Summoner's Rift's walkable floor as a grid, like League's navigation grid: one cell per 25 cm square, walkable or
    /// not, with the floor height of walkable cells. Built from the map's own meshes by Tools > League VR > Build Rift
    /// Collision (only floor connected to the lanes counts, so wall tops and sealed pockets are blocked). Abilities use
    /// it to measure walls (Zeri's vault) and to find landing spots.
    /// </summary>
    public class RiftNavGrid : ScriptableObject
    {
        public Vector2 origin;
        public float cellSize = .25f;
        public int width, height;
        [HideInInspector] public byte[] walkable;
        /// <summary>Floor height of walkable cells, in centimetres.</summary>
        [HideInInspector] public short[] floor;
        /// <summary>Brush patch of each cell (0 for none), from League's own navigation grid.</summary>
        [HideInInspector] public short[] brush;

        /// <summary>The grid of the loaded Rift (set by <see cref="RiftCollision"/>).</summary>
        public static RiftNavGrid Active { get; set; }

        public bool Cell(Vector3 position, out int x, out int z)
        {
            x = Mathf.FloorToInt((position.x - origin.x) / cellSize);
            z = Mathf.FloorToInt((position.z - origin.y) / cellSize);
            return x >= 0 && z >= 0 && x < width && z < height;
        }

        public bool Walkable(Vector3 position) => Cell(position, out int x, out int z) && walkable[z * width + x] != 0;

        /// <summary>The brush patch a point stands in, or 0 outside brush.</summary>
        public int BrushAt(Vector3 position) => brush != null && brush.Length == width * height && Cell(position, out int x, out int z) ? brush[z * width + x] : 0;

        /// <summary>Floor height at a walkable point.</summary>
        public bool Floor(Vector3 position, out float y)
        {
            y = 0;
            if (!Cell(position, out int x, out int z) || walkable[z * width + x] == 0)
                return false;
            y = floor[z * width + x] * .01f;
            return true;
        }

        /// <summary>
        /// Walks a straight line across the floor from <paramref name="from"/>: the distance to the first blocked cell
        /// (<paramref name="wallStart"/>) and to the first walkable floor beyond it that is at least
        /// <paramref name="clearance"/> metres deep (<paramref name="wallEnd"/>). False when the line never meets a wall
        /// within <paramref name="maxDistance"/>, or meets one with no floor beyond it in range.
        /// </summary>
        public bool CrossWall(Vector3 from, Vector3 direction, float maxDistance, float clearance, out float wallStart, out float wallEnd)
        {
            wallStart = wallEnd = 0;
            direction.y = 0;
            if (direction.sqrMagnitude < 1e-6f)
                return false;
            direction.Normalize();
            float step = cellSize * .5f;
            bool inWall = false;
            float clearFrom = 0;
            for (float d = 0; d <= maxDistance; d += step)
            {
                bool open = Walkable(from + direction * d);
                if (!inWall)
                {
                    if (!open)
                    {
                        inWall = true;
                        wallStart = d;
                    }
                    continue;
                }
                if (!open)
                {
                    clearFrom = 0;
                    continue;
                }
                if (clearFrom == 0)
                    clearFrom = d;
                if (d - clearFrom >= clearance)
                {
                    wallEnd = clearFrom;
                    return true;
                }
            }
            return false;
        }
    }
}
