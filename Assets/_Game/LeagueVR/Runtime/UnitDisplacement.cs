using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR
{
    /// <summary>
    /// Knock-ups and knockbacks (Gragas's Body Slam and Explosive Cask): carries a unit along the ground in an arc over a
    /// short time, stopping at walls of the Rift's navigation grid (terrain blocks knockbacks in League), and stuns it
    /// meanwhile so its own movement does not fight the push. Structures never move. Runs after the units' own updates.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class UnitDisplacement : MonoBehaviour
    {
        Combatant unit;
        Vector3 from, to;
        float start, duration, height;

        /// <summary>Where the unit is on the ground under its arc right now.</summary>
        Vector3 Ground => Vector3.Lerp(from, to, Mathf.Clamp01((Time.time - start) / duration));

        /// <summary>
        /// Pushes <paramref name="target"/> <paramref name="distance"/> metres along <paramref name="direction"/> (zero for
        /// a knock-up on the spot), arcing <paramref name="height"/> metres high over <paramref name="seconds"/>.
        /// </summary>
        public static void Push(Combatant target, Vector3 direction, float distance, float seconds, float height)
        {
            // The player moves through the XR rig, never by its transform; no kit pushes the player today.
            if (!target || !target.IsAlive || target.GetComponent<RiftStructure>() || target.GetComponent<LeagueVR.Champions.PlayerChampion>())
                return;
            direction.y = 0;
            // A unit already in the air starts the new push from the ground under it, not from the top of its arc. The
            // component stays on the unit between pushes (switched off), so a push in the frame another ends is kept.
            if (!target.TryGetComponent<UnitDisplacement>(out var push))
                push = target.gameObject.AddComponent<UnitDisplacement>();
            else if (push.enabled)
            {
                push.from = push.Ground;
                push.unit = target;
            }
            if (!push.enabled || push.unit != target)
                push.from = target.transform.position;
            push.enabled = true;
            push.unit = target;
            push.to = Reach(push.from, direction.normalized, distance);
            push.start = Time.time;
            push.duration = Mathf.Max(.05f, seconds);
            push.height = height;
            target.ApplyStun(seconds);
        }

        /// <summary>The furthest walkable point along a push (its end included), stopping before walls and ledges.</summary>
        static Vector3 Reach(Vector3 from, Vector3 direction, float distance)
        {
            var grid = RiftNavGrid.Active;
            if (!grid)
                return from;
            // Start on the floor under the unit, so a push never leaves it standing in the air.
            Vector3 last = grid.Floor(from, out float floor) ? new Vector3(from.x, floor, from.z) : from;
            if (distance <= 0 || direction.sqrMagnitude < 1e-6f)
                return last;
            float lastY = last.y;
            int steps = Mathf.CeilToInt(distance / .25f);
            for (int i = 1; i <= steps; i++)
            {
                var p = from + direction * Mathf.Min(i * .25f, distance);
                if (!grid.Floor(p, out float y) || Mathf.Abs(y - lastY) > .6f)
                    break;
                last = new Vector3(p.x, y, p.z);
                lastY = y;
            }
            return last;
        }

        void LateUpdate()
        {
            float t = Mathf.Clamp01((Time.time - start) / duration);
            // A unit that dies mid-arc drops where it is instead of sliding on.
            if (unit && !unit.IsAlive)
            {
                transform.position = Ground;
                enabled = false;
                return;
            }
            transform.position = Vector3.Lerp(from, to, t) + Vector3.up * (height * 4 * t * (1 - t));
            if (t >= 1)
                enabled = false;
        }
    }
}
