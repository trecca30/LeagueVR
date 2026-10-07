using UnityEngine;
using UnityEngine.InputSystem.XR;
namespace LeagueVR
{
 // XRI's controller transform is the pointer/aim pose. The visible palm belongs on the grip pose.
 // Read the existing OpenXR controls; never change the rig, tracking origin or camera transform.
 public static class GwenTracking
 {
  public static bool Tracked(bool left)
  {
   var device=left?XRController.leftHand:XRController.rightHand;
   if(device==null)return !UnityEngine.XR.XRSettings.isDeviceActive;
   return device.isTracked.isPressed && (device.trackingState.ReadValue()&3)==3;
  }
  public static Pose Grip(GwenAbilities player,bool left)
  {
   Transform hand=left?player.leftHand:player.rightHand;
   var device=left?XRController.leftHand:XRController.rightHand;
   if(!player.DesktopMode&&device!=null&&Tracked(left))
   {
    Transform space=hand.parent;
    return new Pose(space.TransformPoint(device.devicePosition.ReadValue()),space.rotation*device.deviceRotation.ReadValue());
   }
   return new Pose(hand.position,hand.rotation);
  }
  public static Vector3 Aim(GwenAbilities player,bool left)=>player.DesktopMode?player.head.transform.forward:(left?player.leftHand:player.rightHand).forward;
 }
}
