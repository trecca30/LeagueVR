using UnityEngine;
namespace LeagueVR.Match
{
    public class RiftFountain : MonoBehaviour
    {
        public int team;
        public Transform shopAnchor;

        public bool Contains(Vector3 point) => RiftMatch.Instance && Mathf.Abs(point.y - transform.position.y) < 3 && GwenAbilities.FlatDistance(point, transform.position) < RiftMatch.Instance.rules.fountainRadius;

        void Update()
        {
            var match = RiftMatch.Instance;
            if (!match || !match.Running)
                return;
            var health = match.player.Health;
            if (!health || !health.IsAlive || !Contains(match.player.Feet))
                return;
            if (health.team == team)
                health.Heal((health.maxHealth * match.rules.fountainHealingPercentPerSecond + match.rules.fountainHealingFlatPerSecond) * Time.deltaTime);
            else
                health.TakeDamage(new DamageHit(null, transform.position, 1000 * Time.deltaTime, DamageKind.True));
        }
    }
}
