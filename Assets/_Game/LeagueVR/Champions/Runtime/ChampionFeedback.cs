using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace LeagueVR.Champions
{
    /// <summary>Controller haptics and weapon trails for attacks, hits, casts and incoming damage.</summary>
    public class ChampionFeedback : MonoBehaviour
    {
        [FormerlySerializedAs("champion")] public PlayerChampion player;
        [FormerlySerializedAs("cyanMaterial")] public Material trailMaterial;

        void OnEnable()
        {
            player.Cast += OnSignal;
            player.Health.Damaged += Hurt;
        }

        void OnDisable()
        {
            player.Cast -= OnSignal;
            player.Health.Damaged -= Hurt;
        }

        void OnSignal(string signal, Vector3 start, Vector3 direction)
        {
            switch (signal)
            {
                case "Attack1":
                case "Snip":
                    if (player.Kit is GwenKit)
                        SwingTrail(start, direction, signal == "Snip" ? 3.5f : player.AttackReach, signal == "Snip" ? .035f : .015f);
                    Haptic(XRNode.RightHand, .18f, .06f);
                    break;
                case "Hit":
                    Haptic(XRNode.RightHand, .5f, .08f);
                    break;
                case "Q":
                case "E":
                    Haptic(XRNode.RightHand, .3f, .08f);
                    break;
                case "W":
                case "R":
                    Haptic(XRNode.LeftHand, .3f, .08f);
                    break;
            }
        }

        void SwingTrail(Vector3 start, Vector3 direction, float range, float width)
        {
            var dir = Geo.FlatDirection(direction, player.origin.transform.forward);
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            var go = new GameObject("Swing trail");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = trailMaterial ? trailMaterial : AbilityFx.Material(player.Definition ? player.Definition.color : Color.cyan);
            line.startWidth = line.endWidth = width;
            line.positionCount = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.SetPosition(0, start + side * .35f);
            line.SetPosition(1, start + dir * range);
            line.SetPosition(2, start - side * .35f);
            Destroy(go, .13f);
        }

        void Hurt(DamageHit hit, float damage)
        {
            if (damage > 0)
                Haptic(XRNode.LeftHand, Mathf.Clamp(damage / Mathf.Max(1, player.Health.maxHealth) * 6, .15f, .6f), .12f);
        }

        static void Haptic(XRNode node, float amplitude, float duration)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            if (d.isValid)
                d.SendHapticImpulse(0, amplitude, duration);
        }
    }
}
