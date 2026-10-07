using System.Collections.Generic;
using UnityEngine;
using TMPro;
using LeagueVR.Champions;
namespace LeagueVR.Match
{
    [RequireComponent(typeof(Combatant))]
    public class RiftActor : MonoBehaviour
    {
        public static readonly HashSet<RiftActor> All = new HashSet<RiftActor>();
        public Combatant health;
        public bool structure, neutral;
        public float radius = .45f;
        public TMP_Text label;

        protected virtual void Awake()
        {
            health = GetComponent<Combatant>();
        }

        protected virtual void OnEnable()
        {
            All.Add(this);
        }

        protected virtual void OnDisable()
        {
            All.Remove(this);
        }

        protected virtual void LateUpdate()
        {
            var bars = GetComponent<RiftWorldHealthBar>();
            if (!bars)
                gameObject.AddComponent<RiftWorldHealthBar>().Initialize(this);
        }
        public virtual bool Targetable => health && health.IsTargetable && (!structure || GetComponent<RiftStructure>().Vulnerable);
        public Vector3 Position => health.AimPosition;

        public static RiftActor Target(RiftActor source, float range, bool turret = false)
        {
            RiftActor best = null;
            float score = float.MaxValue;
            foreach (var actor in All)
            {
                if (!actor || actor == source || actor.neutral || actor.health.team == source.health.team || !actor.Targetable || !actor.health.IsTargetableBy(source.health))
                    continue;
                float d = Geo.FlatDistance(source.transform.position, actor.transform.position) - actor.radius;
                if (d > range)
                    continue;
                float s = d + (actor.structure ? 20 : actor.health.countsAsChampion ? 10 : 0);
                if (turret)
                {
                    var m = actor.GetComponent<RiftMinion>();
                    s = d + (m ? (m.kind == MinionKind.Super ? -30 : m.kind == MinionKind.Cannon ? -20 : 0) : 10);
                }
                if (s < score)
                {
                    score = s;
                    best = actor;
                }
            }
            return best;
        }
    }
}
