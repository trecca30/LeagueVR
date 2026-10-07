using UnityEngine;
namespace LeagueVR
{
    [CreateAssetMenu(menuName = "League VR/Gwen Tuning")]
    public class GwenTuning : ScriptableObject
    {
        [Header("PC 16.19.1 rank 1 baseline; distances adapted to VR metres")]
        public float attackDamage = 63, attackInterval = 1/.69f, attackReach = 2.2f;
        public float passiveMaxHealthFraction = .01f, passiveChampionHealFraction = .67f;
        [Header("Q / Snip Snip!")]
        public float qCooldown = 6.5f, qRange = 3.5f, qHalfWidth = .8f, qCenterHalfWidth = .22f;
        public float qSnipDamage = 10, qFinalDamage = 60, qInterval = .1f, qStackLifetime = 6;
        public float qDuration=.5f,qMinionModifier=.8f,qExecuteThreshold=.2f;
        [Header("W / Hallowed Mist")]
        public float wCooldown = 22, wDuration = 4, wRadius = 3, wResistance = 22;
        [Header("E / Skip 'n Slash")]
        public float eCooldown = 13, eDistance = 2.4f, eDuration = 4, eBonusDamage = 15, eAttackIntervalMultiplier = 1/1.3f;
        [Range(0, 1)] public float eCooldownRefundFraction = .25f;
        [Header("R / Needlework")]
        public float rCooldown = 120, rWindow = 6, rRecastDelay = 1, rDamage = 30, rSpeed = 16, rRange = 24;
        public float rSlowMultiplier = .6f, rSlowDuration = 1.5f;
    }
}
