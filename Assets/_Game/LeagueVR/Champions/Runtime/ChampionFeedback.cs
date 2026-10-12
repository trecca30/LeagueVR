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
                    // Zeri's rounds land seven at a time: a lighter tick each, not one long buzz.
                    if (player.Kit is ZeriKit)
                        Haptic(XRNode.RightHand, .35f, .03f);
                    else
                        Haptic(XRNode.RightHand, .5f, .08f);
                    break;
                // Zeri: the gun kicks every round, the battery shot thumps, the left palm hums as the pulse charges.
                case "Round":
                    Haptic(XRNode.RightHand, .22f, .02f);
                    break;
                case "Zap":
                case "Chain":
                    Haptic(XRNode.RightHand, .3f, .04f);
                    break;
                case "ChargedShot":
                    Haptic(XRNode.RightHand, .85f, .12f);
                    break;
                case "Charged":
                    Haptic(XRNode.RightHand, .35f, .04f);
                    break;
                case "PulseCharge":
                case "CrashCharge":
                    Haptic(XRNode.LeftHand, .2f, .15f);
                    break;
                // Held charges hum in the left hand and tick faster once ready to release.
                case "HoldHum":
                    Haptic(XRNode.LeftHand, .12f, .04f);
                    break;
                case "HoldReady":
                    Haptic(XRNode.LeftHand, .3f, .03f);
                    break;
                case "PulseHit":
                    Haptic(XRNode.LeftHand, .35f, .05f);
                    break;
                case "LaserCharge":
                    Haptic(XRNode.LeftHand, .2f, .1f);
                    break;
                case "BatteryHit":
                    Haptic(XRNode.RightHand, .4f, .05f);
                    break;
                case "ChargedHit":
                    Haptic(XRNode.RightHand, .7f, .1f);
                    break;
                case "Execute":
                    Haptic(XRNode.RightHand, .9f, .15f);
                    break;
                case "Overcharge":
                    Haptic(XRNode.LeftHand, .6f, .2f);
                    Haptic(XRNode.RightHand, .6f, .2f);
                    break;
                case "Laser":
                case "Surge":
                    Haptic(XRNode.LeftHand, .5f, .12f);
                    Haptic(XRNode.RightHand, .5f, .12f);
                    break;
                case "Q":
                case "E":
                    Haptic(XRNode.RightHand, .3f, .08f);
                    break;
                case "W":
                case "R":
                    Haptic(XRNode.LeftHand, .3f, .08f);
                    break;
                case "Block":
                case "Bash":
                case "Slam":
                    // The shield arm feels what the shield takes.
                    Haptic(XRNode.LeftHand, signal == "Block" ? .6f : .8f, signal == "Block" ? .06f : .12f);
                    break;
                case "Crash":
                    Haptic(XRNode.LeftHand, 1, .25f);
                    Haptic(XRNode.RightHand, 1, .25f);
                    break;
                case "StarBurst":
                case "Pop":
                    Haptic(XRNode.RightHand, .3f, .05f);
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
