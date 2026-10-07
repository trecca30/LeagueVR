using System;
using UnityEngine;
namespace LeagueVR.Match
{
    public enum MinionKind
    {
        Melee, Caster, Cannon, Super
    }

    public enum StructureKind
    {
        OuterTurret, InnerTurret, InhibitorTurret, NexusTurret, Inhibitor, Nexus
    }

    [Serializable]
    public struct MinionStats
    {
        public float health, damage, armor, magicResistance, attackInterval, range, gold, xp, minionOnHit;
    }

    [CreateAssetMenu(menuName = "League VR/Match Rules")]
    public class RiftRules : ScriptableObject
    {
        public string patch = "16.19.1 / 26.19", researchedOn = "2026-10-02";
        public float metresPerLeagueUnit = .01f, firstWave = 30, earlyWaveInterval = 30, midWaveTime = 840, midWaveInterval = 25, lateWaveTime = 1800, lateWaveInterval = 20;
        public float minionSpawnSpacing = .792f, minionSpeed = 350, inhibitorRespawn = 300, nexusTurretRespawn = 180, fountainRadius = 5.5f, fountainHealingPercentPerSecond = .084f, fountainHealingFlatPerSecond = 24;
        public int startingGold = 500;
        public float passiveGoldStart = 65, passiveGoldPerSecond = 2.04f;

        public float WaveInterval(float seconds) => seconds >= lateWaveTime ? lateWaveInterval : seconds >= midWaveTime ? midWaveInterval : earlyWaveInterval;

        public bool CannonWave(int wave, float seconds) => wave >= 3 && (seconds >= 1500 || wave % (seconds >= 840 ? 2 : 3) == 0);

        public MinionStats Stats(MinionKind kind, float seconds)
        {
            // The first upgrade is applied at match start; subsequent upgrades occur every 90 seconds.
            int n = 1 + Mathf.FloorToInt(Mathf.Min(seconds, 5400) / 90);
            if (kind == MinionKind.Melee)
                return new MinionStats { health = Mathf.Min(1500, 430 + 35 * n), damage = Mathf.Min(80, 11 + .5f * Mathf.Min(n - 1, 5) + 3 * Mathf.Max(0, n - 6)), armor = Mathf.Min(20, Mathf.Max(0, n - 5) * .75f), attackInterval = .8f, range = 110, gold = 20, xp = 62, minionOnHit = .02f };
            if (kind == MinionKind.Caster)
                return new MinionStats { health = Mathf.Min(600, 275 + 9 * n), damage = Mathf.Min(125, 19.5f + 1.5f * Mathf.Min(n, 10) + 4.5f * Mathf.Max(0, n - 10)), attackInterval = 1.5f, range = 550, gold = 14, xp = 31, minionOnHit = .035f };
            if (kind == MinionKind.Cannon)
                return new MinionStats { health = Mathf.Min(5850, 750 + 85 * n), damage = Mathf.Min(270, 36 + 1.5f * Mathf.Min(n, 5) + 4 * Mathf.Max(0, n - 5)), attackInterval = 1, range = 300, gold = Mathf.Min(90, 49 + n), xp = 75, minionOnHit = .05f };
            return new MinionStats { health = Mathf.Min(7500, 1500 + 100 * n), damage = Mathf.Min(510, 180 + 5 * n), armor = 100, magicResistance = -30, attackInterval = 1.176f, range = 170, gold = Mathf.Min(90, 49 + n), xp = 75 };
        }

        public float StructureHealth(StructureKind kind) => kind == StructureKind.OuterTurret ? 9000 : kind == StructureKind.InnerTurret ? 5000 : kind == StructureKind.InhibitorTurret ? 4750 : kind == StructureKind.NexusTurret ? 3500 : kind == StructureKind.Inhibitor ? 4000 : 5500;
    }
}
