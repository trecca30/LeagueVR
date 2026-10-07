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

        // ---------- Comfort blink ----------

        GameObject blinkQuad;
        Material blinkMaterial;
        float blinkStart, blinkDuration;

        /// <summary>
        /// A brief dark fade in front of the eyes for instant repositioning (dashes, blinks, leaps). Teleport-style moves
        /// with a short fade are far more comfortable in VR than visible camera motion.
        /// </summary>
        public void ComfortBlink(float seconds = .16f)
        {
            if (!blinkQuad)
            {
                blinkQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(blinkQuad.GetComponent<Collider>());
                blinkQuad.name = "Comfort blink";
                blinkQuad.transform.SetParent(head.transform, false);
                blinkQuad.transform.localPosition = new Vector3(0, 0, .08f);
                blinkQuad.transform.localScale = new Vector3(.5f, .4f, 1);
                var r = blinkQuad.GetComponent<Renderer>();
                blinkMaterial = new Material(AbilityFx.Glass(Color.black)) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 4500 };
                r.sharedMaterial = blinkMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            blinkStart = Time.time;
            blinkDuration = seconds;
            blinkQuad.SetActive(true);
            UpdateBlink();
        }

        void UpdateBlink()
        {
            if (!blinkQuad || !blinkQuad.activeSelf)
                return;
            float t = (Time.time - blinkStart) / Mathf.Max(.01f, blinkDuration);
            blinkMaterial.SetColor("_BaseColor", new Color(0, 0, 0, .9f * (1 - Mathf.Clamp01(t))));
            if (t >= 1)
                blinkQuad.SetActive(false);
        }

        void LateUpdate() => UpdateBlink();
    }
}
