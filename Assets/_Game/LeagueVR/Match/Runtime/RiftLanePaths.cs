using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Match
{
    /// <summary>
    /// Precomputed marching paths: for each lane, team and formation column (left, centre, right) a grounded
    /// polyline offset from the lane centre. Columns narrow automatically at chokepoints instead of clipping walls.
    /// Built once per match so minions never raycast the terrain while marching.
    /// </summary>
    public class RiftLanePaths
    {
        public const int Columns = 3;
        static readonly float[] Widths = { 1.15f, .9f, .65f, .45f, 0f };

        readonly Vector3[][][][] paths; // [lane][team][column] -> points
        public int LaneCount => paths.Length;

        public RiftLanePaths(RiftMatch match)
        {
            var lanes = match.lanes;
            paths = new Vector3[lanes.Length][][][];
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                paths[lane] = new Vector3[2][][];
                for (int team = 0; team < 2; team++)
                {
                    var route = Route(lanes[lane].points, team);
                    paths[lane][team] = new Vector3[Columns][];
                    for (int column = 0; column < Columns; column++)
                        paths[lane][team][column] = Build(match, route, column);
                }
            }
        }

        /// <summary>Lane centre line in travel order for a team (blue walks the authored order, red walks it backwards).</summary>
        public static Vector3[] Route(Vector3[] points, int team)
        {
            var route = (Vector3[])points.Clone();
            if (team == 1)
                System.Array.Reverse(route);
            return route;
        }

        public Vector3[] Path(int lane, int team, int column) => paths[lane][team][Mathf.Clamp(column, 0, Columns - 1)];

        static Vector3[] Build(RiftMatch match, Vector3[] route, int column)
        {
            var result = new Vector3[route.Length];
            int side = column - 1;
            for (int i = 0; i < route.Length; i++)
            {
                Vector3 tangent = Geo.Flat(route[Mathf.Min(i + 1, route.Length - 1)] - route[Mathf.Max(0, i - 1)]);
                Vector3 lateral = Vector3.Cross(Vector3.up, tangent.sqrMagnitude > 1e-4f ? tangent.normalized : Vector3.forward);
                Vector3 chosen = route[i];
                if (side != 0)
                {
                    foreach (float width in Widths)
                    {
                        var candidate = route[i] + lateral * side * width;
                        if (match.Ground(candidate, out var ground) && Mathf.Abs(ground.y - route[i].y) < .5f && !Physics.CheckSphere(ground + Vector3.up * .55f, .22f, match.WorldMask, QueryTriggerInteraction.Ignore))
                        {
                            chosen = ground;
                            break;
                        }
                    }
                }
                else if (match.Ground(route[i], out var centre))
                    chosen = centre;
                result[i] = chosen;
            }
            Smooth(result);
            // Bend around turret and inhibitor bases; a second pass after smoothing keeps the detour round.
            AvoidStructures(match, result, side);
            Smooth(result);
            AvoidStructures(match, result, side);
            return result;
        }

        /// <summary>
        /// Moves path points out of every turret and inhibitor base (plus a minion's width). Each column keeps to its
        /// own side of the structure so a wave flows past it; the centre column takes the side with room.
        /// </summary>
        static void AvoidStructures(RiftMatch match, Vector3[] points, int side)
        {
            const float Clearance = .55f;
            foreach (var structure in match.structures)
            {
                if (!structure || structure.kind == StructureKind.Nexus)
                    continue;
                Vector3 centre = structure.transform.position;
                float reach = structure.Footprint + Clearance;
                int pass = side;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 away = Geo.Flat(points[i] - centre);
                    if (away.sqrMagnitude >= reach * reach)
                        continue;
                    Vector3 tangent = Geo.Flat(points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(0, i - 1)]);
                    Vector3 lateral = Vector3.Cross(Vector3.up, tangent.sqrMagnitude > 1e-4f ? tangent.normalized : Vector3.forward);
                    if (pass == 0)
                        pass = RoomierSide(match, centre, lateral, reach, Vector3.Dot(away, lateral));
                    // Mirror points that sit on the wrong side of the structure, then push them out to its edge.
                    float offset = Vector3.Dot(away, lateral);
                    if (offset * pass < 0)
                        away -= 2 * offset * lateral;
                    if (Mathf.Abs(Vector3.Dot(away, lateral)) < .05f)
                        away += lateral * pass * .05f;
                    Vector3 pushed = centre + away.normalized * reach;
                    pushed.y = points[i].y;
                    if (match.Ground(pushed + Vector3.up * .3f, out var ground) && Mathf.Abs(ground.y - points[i].y) < .6f)
                        pushed = ground;
                    if (!Physics.CheckSphere(pushed + Vector3.up * .55f, .22f, match.WorldMask, QueryTriggerInteraction.Ignore))
                        points[i] = pushed;
                }
            }
        }

        /// <summary>1 or -1: the side of a structure (along <paramref name="lateral"/>) with walkable ground clear of walls.</summary>
        static int RoomierSide(RiftMatch match, Vector3 centre, Vector3 lateral, float reach, float current)
        {
            bool Clear(int s)
            {
                Vector3 probe = centre + lateral * s * (reach + .3f);
                return match.Ground(probe + Vector3.up * .3f, out var ground) && Mathf.Abs(ground.y - centre.y) < .8f
                    && !Physics.CheckSphere(ground + Vector3.up * .55f, .4f, match.WorldMask, QueryTriggerInteraction.Ignore);
            }
            int preferred = current >= 0 ? 1 : -1;
            if (Clear(preferred))
                return preferred;
            return Clear(-preferred) ? -preferred : preferred;
        }

        /// <summary>Removes single-point zigzags where one sample had to narrow more than its neighbours.</summary>
        static void Smooth(Vector3[] points)
        {
            if (points.Length < 3)
                return;
            var copy = (Vector3[])points.Clone();
            for (int i = 1; i < points.Length - 1; i++)
            {
                var mid = (copy[i - 1] + copy[i + 1]) * .5f;
                var blended = Vector3.Lerp(copy[i], mid, .5f);
                blended.y = copy[i].y;
                points[i] = blended;
            }
        }

        /// <summary>Index of the path point closest to <paramref name="position"/>, searching forward from <paramref name="from"/>.</summary>
        public static int NearestAhead(Vector3[] path, Vector3 position, int from, int window = 12)
        {
            int best = Mathf.Clamp(from, 0, path.Length - 1);
            float bestDistance = float.MaxValue;
            for (int i = best; i < Mathf.Min(path.Length, from + window); i++)
            {
                float d = Geo.FlatDistanceSqr(path[i], position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Ground-plane distance from a point to the nearest segment of a path.</summary>
        public static float CorridorDistance(Vector3 point, IReadOnlyList<Vector3> path)
        {
            float best = float.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
                best = Mathf.Min(best, Geo.FlatDistanceToSegment(point, path[i - 1], path[i]));
            return best;
        }
    }
}
