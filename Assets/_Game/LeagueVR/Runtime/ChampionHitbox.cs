using UnityEngine;
namespace LeagueVR
{
    public class ChampionHitbox : MonoBehaviour
    {
        public GwenAbilities champion;
        public CapsuleCollider capsule;

        void LateUpdate()
        {
            transform.position = champion.Feet;
            float h = Mathf.Clamp(champion.head.transform.position.y - champion.Feet.y + .1f, 1, 2.3f);
            capsule.height = h;
            capsule.center = Vector3.up * h * .5f;
        }
    }
}
