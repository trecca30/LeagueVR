using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR
{
    public class CombatProjectile : MonoBehaviour
    {
        public Combatant owner;
        public string ability;
        public float damage = 30, speed = 10, range = 20, radius = .1f;
        public float slowMultiplier = 1, slowDuration;
        public bool piercing;
        public bool ignoreStructures;
        public Func<Combatant, float> slowForTarget;
        public LayerMask worldMask, combatMask;
        public Vector3 direction;
        public Action<Combatant> onHit;
        readonly HashSet<Combatant> hitTargets = new HashSet<Combatant>();
        Vector3 launchOrigin;
        float travelled;

        void Start()
        {
            launchOrigin = owner ? owner.AimPosition : transform.position;
        }

        void Update()
        {
            if (!owner || !owner.IsAlive)
            {
                Destroy(gameObject);
                return;
            }
            float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
            var hits = Physics.SphereCastAll(transform.position, radius, direction, step, worldMask | combatMask, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var target = hit.collider.GetComponentInParent<Combatant>();
                if (target)
                {
                    if (target == owner || target.team == owner.team || !target.IsTargetableBy(owner) || (ignoreStructures && (target.GetComponent<LeagueVR.Match.RiftStructure>() || target.GetComponent<LeagueVR.Match.RiftVisionWard>())) || !hitTargets.Add(target))
                        continue;
                    // Use the source's current position: walking into the mist makes its attacks eligible.
                    float dealt = target.TakeDamage(new DamageHit(owner, owner ? owner.AimPosition : launchOrigin, damage, DamageKind.Magic) { ability = ability });
                    if (dealt > 0)
                    {
                        target.ApplySlow(slowForTarget != null ? slowForTarget(target) : slowMultiplier, slowDuration);
                        onHit?.Invoke(target);
                    }
                    if (!piercing)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
                else if ((worldMask.value & (1 << hit.collider.gameObject.layer)) != 0)
                {
                    if (owner.GetComponent<GwenAbilities>()?.OwnCollider(hit.collider) == true)
                        continue;
                    Destroy(gameObject);
                    return;
                }
            }
            transform.position += direction * step;
            travelled += step;
            if (travelled >= range)
                Destroy(gameObject);
        }
    }
}
