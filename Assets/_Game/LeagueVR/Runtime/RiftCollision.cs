using UnityEngine;

namespace LeagueVR
{
    /// <summary>
    /// The Rift's generated walls (a collision mesh on this object) and its navigation grid. Built by Tools > League VR >
    /// Build Rift Collision; at runtime this only publishes the grid for abilities.
    /// </summary>
    public class RiftCollision : MonoBehaviour
    {
        public RiftNavGrid grid;

        void OnEnable() => RiftNavGrid.Active = grid;

        void OnDisable()
        {
            if (RiftNavGrid.Active == grid)
                RiftNavGrid.Active = null;
        }
    }
}
