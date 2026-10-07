using System;
using System.Linq;
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
        public bool Running { get; private set; }
        public float Seconds { get; private set; }
        public int Wave { get; private set; }
        public float NextWave { get; private set; }
        public event Action<string> Announcement;
        float goldAccumulator;
        bool recalling;
        float recallUntil;
        Vector3 recallFrom;
        public bool IsRecalling => recalling;
        public float RecallRemaining => recalling ? Mathf.Max(0, recallUntil - Time.time) : 0;

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
            ui.OpenMenu();
        }

        void RecallDamaged(DamageHit hit, float amount)
        {
            CancelRecall();
        }

        void RecallCast(string ability, Vector3 position, Vector3 direction)
        {
            CancelRecall();
        }

        public void CancelRecall()
        {
            recalling = false;
        }

        public Material TeamMaterial(int team) => team == 0 ? blueMaterial : redMaterial;

        public bool InhibitorDown(int team, int lane = -1) => structures.Any(s => s.health.team == team && s.kind == StructureKind.Inhibitor && (lane < 0 || s.lane == lane) && !s.health.IsAlive);

        public bool NexusTurretsDown(int team) => structures.Where(s => s.health.team == team && s.kind == StructureKind.NexusTurret).All(s => !s.health.IsAlive);
        public bool AtShop => fountains.Any(f => f.team == player.Health.team && f.Contains(player.Feet));

        public bool Ground(Vector3 point, out Vector3 result)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, 25, point.z), Vector3.down, 45, player.worldMask, QueryTriggerInteraction.Ignore);
            var hit = hits.Where(h => h.normal.y > .65f && h.point.y > -12 && h.collider is MeshCollider).OrderBy(h => Mathf.Abs(h.point.y - point.y)).FirstOrDefault();
            result = hit.point;
            return hit.collider;
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
                Notify("Wave " + Wave + " • " + lanes.Length + " lanes");
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
            if (recalling)
            {
                if (Geo.FlatDistance(recallFrom, player.Feet) > .35f || !player.Health.IsAlive || economy.Stasis || ui.IsOpen)
                    recalling = false;
                else if (Time.time >= recallUntil)
                {
                    recalling = false;
                    MoveToFountain();
                    Notify("Returned to fountain");
                }
            }
        }

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
            var fountain = fountains.First(f => f.team == player.Health.team);
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

        public void Recall()
        {
            if (recalling)
            {
                CancelRecall();
                return;
            }
            if (!Running || !player.Health.IsAlive || economy.Stasis)
                return;
            recalling = true;
            recallFrom = player.Feet;
            recallUntil = Time.time + 8;
        }

        IEnumerator SpawnWave(int wave)
        {
            bool cannon = rules.CannonWave(wave, Seconds);
            int melee = cannon && Seconds >= rules.midWaveTime ? 2 : 3, casters = Seconds >= rules.lateWaveTime ? 2 : 3;
            for (int index = 0; index < melee + casters + (cannon ? 1 : 0); index++)
            {
                var kind = index < melee ? MinionKind.Melee : index == melee && cannon ? MinionKind.Cannon : MinionKind.Caster;
                for (int lane = 0; lane < lanes.Length; lane++)
                    for (int team = 0; team < 2; team++)
                    {
                        bool supers = InhibitorDown(1 - team, lane);
                        var spawnKind = supers && kind == MinionKind.Cannon ? MinionKind.Super : kind;
                        int member = kind == MinionKind.Melee ? index : kind == MinionKind.Caster ? index - melee - (cannon ? 1 : 0) : 1;
                        SpawnMinion(team, lane, spawnKind, member);
                        if (supers && index == 0 && !cannon)
                            SpawnMinion(team, lane, MinionKind.Super, 1);
                    }
                yield return new WaitForSeconds(rules.minionSpawnSpacing);
            }
        }

        public RiftMinion SpawnMinion(int team, int lane, MinionKind kind, int index = 1)
        {
            var route = team == 0 ? lanes[lane].points : lanes[lane].points.Reverse().ToArray();
            Vector3 tangent = Vector3.ProjectOnPlane(route[1] - route[0], Vector3.up).normalized;
            Vector3 at = route[0] + Vector3.Cross(Vector3.up, tangent) * ((Mathf.Clamp(index, 0, 2) - 1) * 1.15f);
            if (Ground(at, out var ground))
                at = ground;
            var instance = Instantiate(minions[team * 4 + (int)kind], at, Quaternion.LookRotation(route[1] - route[0]), spawnedRoot);
            instance.name = (team == 0 ? "Blue " : "Red ") + kind + " • " + lanes[lane].name;
            var unit = instance.GetComponent<RiftMinion>();
            unit.Initialize(this, team, lane, kind, route, index);
            return unit;
        }

        public void AwardUnit(Combatant target, DamageHit hit, float gold, float xp)
        {
            if (hit.source == player.Health)
            {
                economy.AddGold(Mathf.RoundToInt(gold), true);
                player.Defeated++;
            }
            if (target.team != player.Health.team && Geo.FlatDistance(target.transform.position, player.Feet) < 15)
                economy.AddExperience(xp);
        }

        public void Notify(string message)
        {
            Announcement?.Invoke(message);
        }

        public void Finish(bool victory)
        {
            Running = false;
            recalling = false;
            ui.OpenResult(victory);
            Notify(victory ? "Victory" : "Defeat");
        }
    }
}
