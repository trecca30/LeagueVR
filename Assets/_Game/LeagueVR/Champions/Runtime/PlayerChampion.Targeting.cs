using System.Collections.Generic;
using UnityEngine;
using LeagueVR.Match;

namespace LeagueVR.Champions
{
    // Targeting, damage and movement services shared by every champion kit.
    public partial class PlayerChampion
    {
        static readonly Collider[] overlapBuffer = new Collider[256];
        static readonly RaycastHit[] castBuffer = new RaycastHit[128];
        static readonly Comparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        public bool IsEnemy(Combatant target) => target && target != Health && target.team != Health.team && target.IsTargetableBy(Health);

        public bool OwnCollider(Collider c) => c && (c.transform.IsChildOf(origin.transform) || (Economy && Economy.Rack && Economy.Rack.OwnsCollider(c)));

        static bool IsStructure(Combatant c) => c.GetComponent<RiftStructure>();

        /// <summary>Enemies with a collider inside the sphere. Structures are excluded unless requested.</summary>
        public List<Combatant> EnemiesAround(Vector3 position, float radius, bool includeStructures = false)
        {
            var found = new List<Combatant>();
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer, combatMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var t = overlapBuffer[i].GetComponentInParent<Combatant>();
                if (IsEnemy(t) && !found.Contains(t) && (includeStructures || !IsStructure(t)))
                    found.Add(t);
            }
            return found;
        }

        /// <summary>First enemy along a thick ray; terrain stops the search.</summary>
        public Combatant AimTarget(float range, Vector3 position, Vector3 direction, bool includeStructures = false, float radius = .35f)
        {
            int count = Physics.SphereCastNonAlloc(position, radius, direction.normalized, castBuffer, range, worldMask | combatMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(castBuffer, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
            {
                var h = castBuffer[i];
                if (OwnCollider(h.collider))
                    continue;
                var t = h.collider.GetComponentInParent<Combatant>();
                if (t)
                {
                    if (IsEnemy(t) && (includeStructures || !IsStructure(t)))
                        return t;
                }
                else if ((worldMask.value & (1 << h.collider.gameObject.layer)) != 0)
                    return null;
            }
            return null;
        }

        /// <summary>
        /// Melee sweep in front of a weapon. Ground-plane assistance lets a chest-height blade reach short minions.
        /// A cone narrows near the hand; otherwise the band has constant width. Results are sorted nearest-to-centreline first.
        /// </summary>
        public List<Combatant> SweepTargets(Vector3 start, Vector3 forward, float range, float halfWidth, bool cone = false)
        {
            var found = new List<Combatant>();
            forward = PlanarDirection(forward);
            float feetY = Feet.y;
            int count = Physics.OverlapSphereNonAlloc(start, range + 1.5f, overlapBuffer, combatMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var c = overlapBuffer[i];
                var t = c.GetComponentInParent<Combatant>();
                if (!IsEnemy(t) || found.Contains(t))
                    continue;
                Vector3 delta = Vector3.ProjectOnPlane(c.bounds.center - start, Vector3.up);
                float along = Vector3.Dot(delta, forward);
                if (along < 0 || c.bounds.min.y > start.y + 1.5f || c.bounds.max.y < feetY - .6f)
                    continue;
                Vector3 nearest = c.ClosestPoint(start + forward * Mathf.Clamp(along, 0, range));
                Vector3 edge = Vector3.ProjectOnPlane(nearest - start, Vector3.up);
                float width = cone ? Mathf.Lerp(.08f, halfWidth, Mathf.Clamp01(along / range)) : halfWidth;
                if (Vector3.Dot(edge, forward) > range || Vector3.Cross(edge, forward).magnitude > width || Geo.FlatDistance(c.ClosestPoint(start), start) > range)
                    continue;
                if (!ClearAttackLine(start, c.ClosestPoint(start), t))
                    continue;
                found.Add(t);
            }
            found.Sort((a, b) => SweepScore(a, start, forward).CompareTo(SweepScore(b, start, forward)));
            return found;
        }

        static float SweepScore(Combatant t, Vector3 start, Vector3 forward)
        {
            var d = Vector3.ProjectOnPlane(t.AimPosition - start, Vector3.up);
            return Vector3.Cross(d, forward).magnitude * 3 + d.magnitude * .2f;
        }

        /// <summary>
        /// First enemy crossed by a weapon edge that moved from (a0, b0) to (a1, b1) since last frame. Points along the
        /// outer 80% of the edge are swept, so the part inside the fist never counts.
        /// </summary>
        public bool WeaponSweep(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float radius, out Combatant target, out Vector3 point)
        {
            target = null;
            point = default;
            float best = float.MaxValue;
            for (int s = 0; s <= 4; s++)
            {
                float t = .2f + .8f * s / 4f;
                Vector3 p0 = Vector3.Lerp(a0, b0, t), p1 = Vector3.Lerp(a1, b1, t);
                Vector3 d = p1 - p0;
                float length = d.magnitude;
                if (length > 1e-4f)
                {
                    int count = Physics.SphereCastNonAlloc(p0, radius, d / length, castBuffer, length, combatMask, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < count; i++)
                    {
                        var c = castBuffer[i].collider.GetComponentInParent<Combatant>();
                        float when = castBuffer[i].distance / length;
                        if (IsEnemy(c) && when < best)
                        {
                            best = when;
                            target = c;
                            point = castBuffer[i].point == Vector3.zero ? p1 : castBuffer[i].point;
                        }
                    }
                }
                // Sphere casts miss colliders they start inside; the end position catches those.
                int overlaps = Physics.OverlapSphereNonAlloc(p1, radius, overlapBuffer, combatMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < overlaps; i++)
                {
                    var c = overlapBuffer[i].GetComponentInParent<Combatant>();
                    if (IsEnemy(c) && 1f < best)
                    {
                        best = 1f;
                        target = c;
                        point = overlapBuffer[i].ClosestPoint(p1);
                    }
                }
            }
            return target;
        }

        /// <summary>True when no terrain lies between the two points (the target's own colliders are ignored).</summary>
        public bool ClearAttackLine(Vector3 start, Vector3 end, Combatant target)
        {
            var delta = end - start;
            float length = delta.magnitude;
            if (length < 1e-4f)
                return true;
            int count = Physics.RaycastNonAlloc(start, delta / length, castBuffer, length, worldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = castBuffer[i].collider;
                if (!OwnCollider(c) && c.GetComponentInParent<Combatant>() != target)
                    return false;
            }
            return true;
        }

        /// <summary>Nearest terrain hit along a ray, ignoring the player's own colliders.</summary>
        public bool TerrainRay(Vector3 from, Vector3 direction, float range, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(from, direction.normalized, castBuffer, range, worldMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(castBuffer, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
                if (!OwnCollider(castBuffer[i].collider))
                {
                    hit = castBuffer[i];
                    return true;
                }
            hit = default;
            return false;
        }

        /// <summary>Ground point under an aim ray: the floor it hits, or the walkable ground at full range.</summary>
        public bool GroundAim(Vector3 from, Vector3 direction, float range, out Vector3 result)
        {
            if (TerrainRay(from, direction, range, out var hit) && hit.normal.y > .65f)
            {
                result = hit.point;
                return true;
            }
            result = default;
            var match = RiftMatch.Instance;
            return match && match.Ground(Feet + Geo.FlatDirection(direction, origin.transform.forward) * range, out result);
        }

        public Vector3 PlanarDirection(Vector3 direction)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < .01f)
                flat = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            return flat.sqrMagnitude > .01f ? flat.normalized : origin.transform.forward;
        }

        /// <summary>
        /// Deals ability or basic-attack damage from the player. Basic attacks roll critical strikes, use the structure
        /// formula and trigger on-attack item effects. Returns the damage actually dealt.
        /// </summary>
        public float Hit(Combatant target, float amount, DamageKind kind, string ability, bool basic = false, Vector3? from = null)
        {
            if (!IsEnemy(target))
                return 0;
            float damage = basic && Economy ? Economy.AttackDamage(amount, target) : amount;
            var hit = new DamageHit(Health, from ?? Health.AimPosition, damage, kind)
            {
                ability = ability,
                isBasicAttack = basic,
                isCritical = basic && Economy && Economy.LastAttackCritical,
            };
            float dealt = target.TakeDamage(hit);
            if (dealt > 0)
            {
                if (basic && Economy)
                    Economy.OnAttack(target, dealt);
                Kit?.OnDamageDealt(target, dealt, ability, basic);
            }
            return dealt;
        }

        // ---------- Movement ----------

        /// <summary>
        /// Direction the player is steering with the movement stick (or WASD on desktop), relative to where they look.
        /// <paramref name="walking"/> is false when the stick is centred; the head's facing is returned instead.
        /// </summary>
        public Vector3 LocomotionDirection(out bool walking)
        {
            Vector2 stick = Vector2.zero;
            if (DesktopMode)
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                if (k != null)
                    stick = new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            }
            else
            {
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
                if (device.isValid)
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stick);
            }
            walking = stick.magnitude > .35f;
            var yaw = Quaternion.Euler(0, head.transform.eulerAngles.y, 0);
            return walking ? (yaw * new Vector3(stick.x, 0, stick.y)).normalized : PlanarDirection(head.transform.forward);
        }

        CharacterController Controller => origin.GetComponent<CharacterController>();

        /// <summary>Instantly places the player's feet at a point (blinks, portals, respawns).</summary>
        public void MoveFeet(Vector3 point)
        {
            var cc = Controller;
            bool enabled = cc && cc.enabled;
            if (cc)
                cc.enabled = false;
            origin.transform.position += point - Feet + Vector3.up * .05f;
            if (cc)
                cc.enabled = enabled;
        }

        /// <summary>Capsule fits at the point without touching terrain.</summary>
        public bool ClearDestination(Vector3 at)
        {
            int count = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * .4f, at + Vector3.up * 1.4f, .22f, overlapBuffer, worldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!OwnCollider(overlapBuffer[i]))
                    return false;
            return true;
        }

        /// <summary>Furthest reachable walkable point toward <paramref name="desired"/>, stopping short of walls.</summary>
        public bool StepPoint(Vector3 desired, out Vector3 point)
        {
            point = default;
            var feet = Feet;
            var delta = Geo.Flat(desired - feet);
            float distance = delta.magnitude;
            if (distance < .01f)
                return false;
            var direction = delta / distance;
            int count = Physics.CapsuleCastNonAlloc(feet + Vector3.up * .38f, feet + Vector3.up * 1.4f, .22f, direction, castBuffer, distance, worldMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(castBuffer, 0, count, ByDistance);
            for (int i = 0; i < count; i++)
                if (!OwnCollider(castBuffer[i].collider))
                {
                    desired = feet + direction * Mathf.Max(0, castBuffer[i].distance - .15f);
                    break;
                }
            var match = RiftMatch.Instance;
            return Geo.FlatDistance(feet, desired) > .15f && match && match.Ground(desired + Vector3.up * .3f, out point) && Mathf.Abs(point.y - feet.y) < .65f && ClearDestination(point);
        }

        /// <summary>Collision-checked instant dash along the ground. Returns false if the way is blocked.</summary>
        public bool Dash(Vector3 direction, float distance)
        {
            direction = PlanarDirection(direction);
            if (Health.Rooted || !StepPoint(Feet + direction * distance, out var point))
                return false;
            var cc = Controller;
            Vector3 displacement = point - Feet + Vector3.up * .05f;
            if (cc && cc.enabled)
                cc.Move(displacement);
            else
                origin.transform.position += displacement;
            return true;
        }
    }
}
