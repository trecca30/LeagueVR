using System;
using System.Collections;
using UnityEngine;
using LeagueVR.Champions;

namespace LeagueVR.Match
{
    [Serializable]
    public class RiftLane
    {
        public string name;
        public Vector3[] points;
    }

    /// <summary>
    /// Runs a match on Summoner's Rift: game clock, minion waves, passive gold, experience, recall and victory.
    /// </summary>
    public class RiftMatch : MonoBehaviour
    {
        public static RiftMatch Instance { get; private set; }
        public RiftRules rules;
        public LeagueCatalog catalog;
        public PlayerChampion player;
        public RiftEconomy economy;
        public RiftUI ui;
        public RiftLane[] lanes;
        public GameObject[] minions;
        public RiftStructure[] structures;
        public RiftObjective[] objectives;
        public RiftFountain[] fountains;
        public Material blueMaterial, redMaterial, neutralMaterial;
        public Transform spawnedRoot;

        public const float RecallDuration = 8;

        public bool Running { get; private set; }
        public float Seconds { get; private set; }
        public int Wave { get; private set; }
        public float NextWave { get; private set; }
        public RiftLanePaths LanePaths { get; private set; }
        public LayerMask WorldMask => player.worldMask;
        public event Action<string> Announcement;
        public bool IsRecalling => recalling;
        public float RecallRemaining => recalling ? Mathf.Max(0, recallUntil - Time.time) : 0;
        public float RecallProgress => recalling ? 1 - RecallRemaining / RecallDuration : 0;

        float goldAccumulator;
        bool recalling;
        float recallUntil;
        Vector3 recallOrigin, recallHead;
        static readonly RaycastHit[] groundHits = new RaycastHit[16];

        void Awake()
        {
            Instance = this;
            NextWave = rules.firstWave;
        }

        void OnDestroy()
        {
            if (player)
            {
                player.Health.Damaged -= RecallDamaged;
                player.Cast -= RecallCast;
            }
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            player.Health.Damaged += RecallDamaged;
            player.Cast += RecallCast;
            LanePaths = new RiftLanePaths(this);
            ui.OpenMenu();
        }

        void RecallDamaged(DamageHit hit, float amount) => CancelRecall();

        void RecallCast(string signal, Vector3 position, Vector3 direction)
        {
            // Attacking or casting interrupts a recall; passive signals (hits, respawn) do not.
            if (signal == "Attack1" || signal.Length == 1)
                CancelRecall();
        }

        public void CancelRecall() => recalling = false;

        public Material TeamMaterial(int team) => team == 0 ? blueMaterial : redMaterial;

        public bool InhibitorDown(int team, int lane = -1)
        {
            foreach (var s in structures)
                if (s.health.team == team && s.kind == StructureKind.Inhibitor && (lane < 0 || s.lane == lane) && !s.health.IsAlive)
                    return true;
            return false;
        }

        public bool AllInhibitorsDown(int team)
        {
            foreach (var s in structures)
                if (s.health.team == team && s.kind == StructureKind.Inhibitor && s.health.IsAlive)
                    return false;
            return true;
        }

        public bool NexusTurretsDown(int team)
        {
            foreach (var s in structures)
                if (s.health.team == team && s.kind == StructureKind.NexusTurret && s.health.IsAlive)
                    return false;
            return true;
        }

        public bool AtShop
        {
            get
            {
                foreach (var f in fountains)
                    if (f.team == player.Health.team && f.Contains(player.Feet))
                        return true;
                return false;
            }
        }

        /// <summary>Walkable ground under a point: the upward-facing surface closest in height to the query point.</summary>
        public bool Ground(Vector3 point, out Vector3 result)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, 25, point.z), Vector3.down, groundHits, 45, player.worldMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            result = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = groundHits[i];
                if (h.normal.y <= .65f || h.point.y <= -12 || !(h.collider is MeshCollider))
                    continue;
                float d = Mathf.Abs(h.point.y - point.y);
                if (d < best)
                {
                    best = d;
                    result = h.point;
                    found = true;
                }
            }
            return found;
        }

        void Update()
        {
            if (!Running)
                return;
            Seconds += Time.deltaTime;
            if (Seconds >= NextWave)
            {
                Wave++;
                StartCoroutine(SpawnWave(Wave));
                NextWave += rules.WaveInterval(Seconds);
            }
            if (Seconds >= rules.passiveGoldStart)
            {
                goldAccumulator += Time.deltaTime * rules.passiveGoldPerSecond;
                int gain = Mathf.FloorToInt(goldAccumulator);
                if (gain > 0)
                {
                    economy.Gold += gain;
                    goldAccumulator -= gain;
                }
            }
            UpdateRecall();
        }

        void UpdateRecall()
        {
            if (!recalling)
                return;
            // Locomotion (stick, teleport, dashes) moves the rig and cancels; leaning or a small step in the room does not.
            bool rigMoved = Geo.FlatDistance(recallOrigin, player.origin.transform.position) > .2f;
            bool walkedAway = Geo.FlatDistance(recallHead, player.head.transform.position) > 1f;
            if (rigMoved || walkedAway || !player.Health.IsAlive || economy.Stasis || ui.IsOpen)
            {
                recalling = false;
                Notify("Recall cancelled");
            }
            else if (Time.time >= recallUntil)
            {
                recalling = false;
                MoveToFountain();
                Notify("Returned to fountain");
            }
        }

        /// <summary>Starts a new match with the selected champion.</summary>
        public void Play()
        {
            player.GetComponent<ChampionRoster>()?.ApplySelection();
            StopAllCoroutines();
            foreach (Transform child in spawnedRoot)
                Destroy(child.gameObject);
            Seconds = 0;
            Wave = 0;
            NextWave = rules.firstWave;
            goldAccumulator = 0;
            recalling = false;
            Running = true;
            foreach (var structure in structures)
                structure.ResetStructure();
            foreach (var objective in objectives)
                objective.ResetObjective();
            economy.ResetMatch();
            player.ResetPractice();
            MoveToFountain();
            ui.Close();
            Notify("Welcome to Summoner's Rift");
        }

        public void MoveToFountain()
        {
            RiftFountain fountain = null;
            foreach (var f in fountains)
                if (f.team == player.Health.team)
                    fountain = f;
            if (!fountain)
                return;
            player.spawn.position = fountain.transform.position + Vector3.up * .08f;
            var cc = player.origin.GetComponent<CharacterController>();
            bool enabled = cc && cc.enabled;
            if (cc)
                cc.enabled = false;
            var offset = player.head.transform.position - player.origin.transform.position;
            offset.y = 0;
            player.origin.transform.position = player.spawn.position - offset;
            if (cc)
                cc.enabled = enabled;
        }

        /// <summary>Starts recalling, or cancels a recall in progress.</summary>
        public void Recall()
        {
            if (recalling)
            {
                CancelRecall();
                Notify("Recall cancelled");
                return;
            }
            if (!Running || !player.Health.IsAlive || economy.Stasis)
                return;
            recalling = true;
            recallOrigin = player.origin.transform.position;
            recallHead = player.head.transform.position;
            recallUntil = Time.time + RecallDuration;
        }

        IEnumerator SpawnWave(int wave)
        {
            bool cannon = rules.CannonWave(wave, Seconds);
            // After 14:00 siege waves have one fewer melee minion; after 30:00 every wave has one fewer caster.
            int melee = cannon && Seconds >= rules.midWaveTime ? 2 : 3, casters = Seconds >= rules.lateWaveTime ? 2 : 3;
            for (int index = 0; index < melee + casters + 1; index++)
            {
                bool isMelee = index < melee, isSiegeSlot = index == melee;
                for (int lane = 0; lane < lanes.Length; lane++)
                    for (int team = 0; team < 2; team++)
                    {
                        if (isMelee)
                        {
                            SpawnMinion(team, lane, MinionKind.Melee, index);
                            continue;
                        }
                        if (isSiegeSlot)
                        {
                            // Super minions replace the siege minion: one when this lane's enemy inhibitor is down, two when all are.
                            int supers = AllInhibitorsDown(1 - team) ? 2 : InhibitorDown(1 - team, lane) ? 1 : 0;
                            for (int s = 0; s < supers; s++)
                                SpawnMinion(team, lane, MinionKind.Super, s == 0 ? 1 : 0);
                            if (supers == 0 && cannon)
                                SpawnMinion(team, lane, MinionKind.Cannon, 1);
                            continue;
                        }
                        SpawnMinion(team, lane, MinionKind.Caster, index - melee - 1);
                    }
                if (!isSiegeSlot || cannon || AnySupers())
                    yield return new WaitForSeconds(rules.minionSpawnSpacing);
            }
        }

        bool AnySupers()
        {
            for (int lane = 0; lane < lanes.Length; lane++)
                if (InhibitorDown(0, lane) || InhibitorDown(1, lane))
                    return true;
            return false;
        }

        public RiftMinion SpawnMinion(int team, int lane, MinionKind kind, int index = 1)
        {
            int column = Mathf.Clamp(index, 0, 2);
            var path = LanePaths != null ? LanePaths.Path(lane, team, column) : RiftLanePaths.Route(lanes[lane].points, team);
            Vector3 at = path[0];
            var instance = Instantiate(minions[team * 4 + (int)kind], at, Quaternion.LookRotation(Geo.FlatDirection(path[1] - path[0], Vector3.forward)), spawnedRoot);
            instance.name = (team == 0 ? "Blue " : "Red ") + kind + " • " + lanes[lane].name;
            var unit = instance.GetComponent<RiftMinion>();
            unit.Initialize(this, team, lane, kind, RiftLanePaths.Route(lanes[lane].points, team), column);
            return unit;
        }

        /// <summary>Gold goes to the champion who landed the killing blow; experience is shared within 16 m.</summary>
        public void AwardUnit(Combatant target, DamageHit hit, float gold, float xp)
        {
            if (hit.source == player.Health)
            {
                economy.AddGold(Mathf.RoundToInt(gold), true);
                player.Defeated++;
            }
            if (target.team != player.Health.team && Geo.FlatDistance(target.transform.position, player.Feet) < 16)
                economy.AddExperience(xp);
        }

        public void Notify(string message) => Announcement?.Invoke(message);

        public void Finish(bool victory)
        {
            Running = false;
            recalling = false;
            ui.OpenResult(victory);
            Notify(victory ? "Victory" : "Defeat");
        }
    }
}
