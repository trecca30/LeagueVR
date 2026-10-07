using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Controller poses for the avatar and abilities. XRI's controller transforms carry the pointer/aim pose;
    /// the visible palm belongs on the grip pose, read here from the same OpenXR device without touching the rig.
    /// </summary>
    public static class XRPoses
    {
        public static bool Tracked(bool left)
        {
            var device = left ? XRController.leftHand : XRController.rightHand;
            if (device == null)
                return !UnityEngine.XR.XRSettings.isDeviceActive;
            return device.isTracked.isPressed && (device.trackingState.ReadValue() & 3) == 3;
        }

        /// <summary>
        /// Approximate grip orientation for a pointing (aim) orientation on Touch-style controllers: the handle leans
        /// about 55 degrees up from the pointing direction. Used for the desktop fallback and simulated poses.
        /// </summary>
        public static Quaternion GripFromAim(Quaternion aim) => aim * Quaternion.Euler(-55, 0, 0);

        /// <summary>World-space grip pose, or the aim transform when the device is unavailable.</summary>
        public static Pose Grip(PlayerChampion player, bool left)
        {
            Transform hand = left ? player.leftHand : player.rightHand;
            var device = left ? XRController.leftHand : XRController.rightHand;
            if (!player.DesktopMode && device != null && Tracked(left))
            {
                Transform space = hand.parent;
                return new Pose(space.TransformPoint(device.devicePosition.ReadValue()), space.rotation * device.deviceRotation.ReadValue());
            }
            return new Pose(hand.position, hand.rotation);
        }
    }
}
