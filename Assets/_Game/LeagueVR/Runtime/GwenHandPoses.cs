using System;
using UnityEngine;
namespace LeagueVR
{
 [CreateAssetMenu(menuName="League VR/Gwen Hand Poses")]
 public class GwenHandPoses:ScriptableObject
 {
  [Serializable]public struct Joint{public string name;public Quaternion relaxed,holding;}
  public Joint[] joints;
  public Quaternion leftGripAlignment=Quaternion.identity,rightGripAlignment=Quaternion.identity;
  public Quaternion weaponGripAlignment=Quaternion.identity;
 }
}
