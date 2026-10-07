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
        float blinkStart, blinkIn, blinkHold, blinkOut, blinkAlpha;
        Color blinkColor;

        /// <summary>
        /// A brief dark fade in front of the eyes for instant repositioning (dashes, blinks, leaps). Teleport-style moves
        /// with a short fade are far more comfortable in VR than visible camera motion.
        /// </summary>
        public void ComfortBlink(float seconds = .16f) => ScreenFade(Color.black, 0, 0, seconds, .9f);

        /// <summary>
        /// Covers the eyes with a colour: fades in, holds, then fades out (all in seconds). Used to hide cuts such as
        /// Pantheon's leap into the sky; nothing about the view ever moves smoothly behind it.
        /// </summary>
        public void ScreenFade(Color color, float fadeIn, float hold, float fadeOut, float alpha = 1)
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
            blinkIn = fadeIn;
            blinkHold = hold;
            blinkOut = fadeOut;
            blinkColor = color;
            blinkAlpha = alpha;
            blinkQuad.SetActive(true);
            UpdateBlink();
        }

        /// <summary>Seconds until a running screen fade is fully covering the view (0 once covered).</summary>
        public float FadeCoveredIn => blinkQuad && blinkQuad.activeSelf ? Mathf.Max(0, blinkStart + blinkIn - Time.time) : 0;

        void UpdateBlink()
        {
            if (!blinkQuad || !blinkQuad.activeSelf)
                return;
            float t = Time.time - blinkStart;
            float amount = t < blinkIn ? t / Mathf.Max(.01f, blinkIn) : t < blinkIn + blinkHold ? 1 : 1 - (t - blinkIn - blinkHold) / Mathf.Max(.01f, blinkOut);
            var c = blinkColor;
            c.a = blinkAlpha * Mathf.Clamp01(amount);
            blinkMaterial.SetColor("_BaseColor", c);
            if (t >= blinkIn + blinkHold + blinkOut)
                blinkQuad.SetActive(false);
        }

        void LateUpdate() => UpdateBlink();
    }
}
