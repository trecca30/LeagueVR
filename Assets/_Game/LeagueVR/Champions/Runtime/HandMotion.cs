using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Short history of a hand's position in rig space, used to read throw and swing velocities.
    /// Rig space excludes locomotion, snap turns and dashes, so only the arm's own motion counts.
    /// </summary>
    public class HandMotion
    {
        const int Size = 8;
        readonly Vector3[] positions = new Vector3[Size];
        readonly float[] times = new float[Size];
        int count, head;

        public void Clear() => count = 0;

        public void Sample(Vector3 rigLocalPosition, float time)
        {
            head = (head + 1) % Size;
            positions[head] = rigLocalPosition;
            times[head] = time;
            count = Mathf.Min(Size, count + 1);
        }

        /// <summary>Average velocity (rig space, m/s) over roughly the last <paramref name="window"/> seconds.</summary>
        public Vector3 Velocity(float window = .07f)
        {
            if (count < 2)
                return Vector3.zero;
            int newest = head;
            float now = times[newest];
            int oldest = newest;
            for (int i = 1; i < count; i++)
            {
                int index = (head - i + Size) % Size;
                oldest = index;
                if (now - times[index] >= window)
                    break;
            }
            float dt = now - times[oldest];
            return dt > 1e-4f ? (positions[newest] - positions[oldest]) / dt : Vector3.zero;
        }

        /// <summary>Peak speed over the history, for detecting the snap of a throw just before release.</summary>
        public Vector3 PeakVelocity()
        {
            Vector3 best = Vector3.zero;
            for (int i = 1; i < count; i++)
            {
                int a = (head - i + Size) % Size, b = (head - i + 1 + Size) % Size;
                float dt = times[b] - times[a];
                if (dt <= 1e-4f)
                    continue;
                var v = (positions[b] - positions[a]) / dt;
                if (v.sqrMagnitude > best.sqrMagnitude)
                    best = v;
            }
            return best;
        }
    }
}
