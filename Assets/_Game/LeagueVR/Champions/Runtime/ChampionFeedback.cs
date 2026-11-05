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
            if (player.Kit is HweiKit && HweiSignal(signal))
                return;
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
                case "CritHit":
                    Haptic(XRNode.RightHand, .8f, .12f);
                    break;
                // Zeri: the gun kicks every round, the battery shot thumps, the left palm hums as the pulse charges.
                case "Round":
                    Haptic(XRNode.RightHand, .22f, .02f);
                    break;
                // Zeri's battery shot leaves her left hand (League's flick); chain lightning comes off the gun.
                case "Zap":
                    Haptic(player.Kit is ZeriKit ? XRNode.LeftHand : XRNode.RightHand, .3f, .04f);
                    break;
                case "Chain":
                    Haptic(XRNode.RightHand, .3f, .04f);
                    break;
                case "ChargedShot":
                    Haptic(player.Kit is ZeriKit ? XRNode.LeftHand : XRNode.RightHand, .85f, .12f);
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
                    Haptic(XRNode.LeftHand, .4f, .05f);
                    break;
                case "ChargedHit":
                    Haptic(XRNode.LeftHand, .7f, .1f);
                    break;
                case "Execute":
                    Haptic(XRNode.LeftHand, .9f, .15f);
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
                    // Gragas drinks from the keg in his right hand.
                    Haptic(player.Kit is GragasKit ? XRNode.RightHand : XRNode.LeftHand, .3f, .08f);
                    break;
                case "R":
                    Haptic(XRNode.LeftHand, .3f, .08f);
                    break;
                // Gragas: the keg hums when Drunken Rage empowers it, thumps on the splash; the barrel and the cask
                // burst in the hand that threw them; the slam shakes both arms; a swig is a soft pulse.
                case "Empowered":
                    Haptic(XRNode.RightHand, .3f, .1f);
                    break;
                case "WSplash":
                    Haptic(XRNode.RightHand, .8f, .14f);
                    break;
                case "QBurst":
                    Haptic(XRNode.RightHand, .45f, .1f);
                    break;
                case "RBurst":
                    Haptic(XRNode.LeftHand, .75f, .2f);
                    break;
                case "SlamHit":
                case "WSmash":
                    // The slam shakes both arms; so does the keg smashed down in both hands.
                    Haptic(XRNode.LeftHand, .9f, .18f);
                    Haptic(XRNode.RightHand, .9f, .18f);
                    break;
                case "Stride":
                    // Body Slam's strides thud through both hands while the charge carries him.
                    Haptic(XRNode.LeftHand, .25f, .04f);
                    Haptic(XRNode.RightHand, .25f, .04f);
                    break;
                case "HappyHour":
                    Haptic(XRNode.RightHand, .15f, .12f);
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

        /// <summary>
        /// Hwei: a tick in the hand that opened a subject (X is on the left controller), the brush hand feels each stroke
        /// leave it and the big impacts; the ultimate's fire shakes both hands, which hold the staff. True when handled.
        /// </summary>
        static bool HweiSignal(string signal)
        {
            switch (signal)
            {
                case "Q":
                case "W":
                case "E":
                    return true;
                case "SubjectQ":
                case "SubjectE":
                    Haptic(XRNode.RightHand, .2f, .05f);
                    return true;
                case "SubjectW":
                    Haptic(XRNode.LeftHand, .2f, .05f);
                    return true;
                case "WashBrush":
                    Haptic(XRNode.RightHand, .15f, .12f);
                    return true;
                case "QQRelease":
                case "EQRelease":
                case "WQRelease":
                case "QEStart":
                    Haptic(XRNode.RightHand, .35f, .07f);
                    return true;
                case "QW":
                case "WW":
                case "WE":
                case "EW":
                case "EE":
                    Haptic(XRNode.RightHand, .25f, .06f);
                    return true;
                case "QQHit":
                case "QWHit":
                case "EEHit":
                case "EQHit":
                case "EWHit":
                case "MarkBurst":
                    Haptic(XRNode.RightHand, .4f, .08f);
                    return true;
                case "WEHit":
                    Haptic(XRNode.RightHand, .3f, .04f);
                    return true;
                case "R":
                    Haptic(XRNode.LeftHand, .6f, .15f);
                    Haptic(XRNode.RightHand, .6f, .15f);
                    return true;
                case "RBurst":
                    Haptic(XRNode.LeftHand, .5f, .15f);
                    Haptic(XRNode.RightHand, .5f, .15f);
                    return true;
                case "WWShield":
                    Haptic(XRNode.LeftHand, .25f, .1f);
                    Haptic(XRNode.RightHand, .25f, .1f);
                    return true;
            }
            return false;
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
