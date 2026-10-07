using UnityEngine;
using UnityEngine.Serialization;

namespace LeagueVR.Champions
{
    /// <summary>Keeps the player's hurtbox capsule under the headset and as tall as the player's eye height.</summary>
    public class ChampionHitbox : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        public CapsuleCollider capsule;

        void LateUpdate()
        {
            transform.position = player.Feet;
            float h = Mathf.Clamp(player.head.transform.position.y - player.Feet.y + .1f, 1, 2.3f);
            capsule.height = h;
            capsule.center = Vector3.up * h * .5f;
        }
    }
}
