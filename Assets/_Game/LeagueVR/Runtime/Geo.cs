using UnityEngine;

namespace LeagueVR
{
    /// <summary>Ground-plane helpers shared by combat, AI and abilities.</summary>
    public static class Geo
    {
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        public static float FlatDistanceSqr(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v;
        }

        /// <summary>Normalized horizontal direction, or <paramref name="fallback"/> when the vector is (nearly) vertical.</summary>
        public static Vector3 FlatDirection(Vector3 v, Vector3 fallback)
        {
            v.y = 0;
            return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
        }

        /// <summary>Distance from <paramref name="point"/> to the segment a-b on the ground plane.</summary>
        public static float FlatDistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0;
            Vector3 d = b - a;
            float t = d.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + d * t);
        }
    }
}
