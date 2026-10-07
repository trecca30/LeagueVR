using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    // Ability visuals in the champion's colour. Everything spawned here is cleaned up when the kit changes or the match resets.
    public partial class PlayerChampion
    {
        readonly List<GameObject> owned = new();

        Color Tint => Definition ? Definition.color : Color.white;

        public GameObject Track(GameObject go)
        {
            owned.RemoveAll(o => !o);
            owned.Add(go);
            return go;
        }

        public void ClearOwnedEffects()
        {
            foreach (var go in owned)
                if (go)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            owned.Clear();
        }

        public GameObject Ring(Vector3 position, float radius, float seconds) => Track(AbilityFx.Ring(position, radius, seconds, Tint));

        public GameObject Beam(Vector3 from, Vector3 to, float width, float seconds = .15f) => Track(AbilityFx.Beam(from, to, width, Tint, seconds));

        public GameObject Spark(Vector3 position, float radius) => Track(AbilityFx.Spark(position, radius, Tint));

        public AbilityProjectile Projectile(Vector3 start, Vector3 direction, float damage, DamageKind kind, string ability, float speed, float range, bool piercing)
            => AbilityProjectile.Launch(this, start, direction, damage, kind, ability, speed, range, piercing, Tint);
    }
}
