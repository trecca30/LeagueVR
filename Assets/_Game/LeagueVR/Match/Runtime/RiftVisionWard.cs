using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

namespace LeagueVR.Match
{
    public class RiftVisionWard : MonoBehaviour
    {
        public static readonly HashSet<RiftVisionWard> All = new();
        public int ownerTeam, itemId;
        public RiftEconomy owner;
        public float expires, revealedUntil;
        Renderer[] visuals;
        Combatant health;
        public bool Revealed => itemId != 3340 && itemId != 3869 && itemId != 3870 && itemId != 3871 && itemId != 3876 && itemId != 3877 || !RiftMatch.Instance || ownerTeam == RiftMatch.Instance.player.Health.team || Time.time < revealedUntil;
        public static void Place(RiftEconomy economy, Vector3 at, int id, float duration)
        {
            if (id == 2055) foreach (var prior in All.Where(w => w && w.owner == economy && w.itemId == 2055).ToArray()) Destroy(prior.gameObject);
            var existing = All.Where(w => w && w.owner == economy && w.itemId != 2055 && w.itemId != 3363).OrderBy(w => w.expires).ToArray();
            if (id != 2055 && id != 3363 && existing.Length >= 3) Destroy(existing[0].gameObject);
            var go = new GameObject(economy.match.catalog.Find(id).name + " (placed)");
            go.transform.position = at; var ward = go.AddComponent<RiftVisionWard>();
            ward.owner = economy; ward.itemId = id; ward.ownerTeam = economy.match.player.Health.team; ward.expires = duration <= 0 ? float.PositiveInfinity : Time.time + duration;
            var prefab = economy.match.catalog.Find(id).physicalPrefab;
            if (prefab) Instantiate(prefab, go.transform);
            var collider = go.AddComponent<SphereCollider>(); collider.radius = .2f; collider.center = Vector3.up * .2f; collider.isTrigger = true;
            go.layer = 8; var unit = go.AddComponent<Combatant>(); unit.team = ward.ownerTeam; unit.countsAsChampion = false; unit.maxHealth = id == 3363 ? 1 : 3; unit.armor = unit.magicResistance = 0; unit.ResetHealth();
            ward.health = unit; unit.onDeath.AddListener(() => Destroy(go));
            go.AddComponent<RiftActor>().radius = .2f; ward.visuals = go.GetComponentsInChildren<Renderer>();
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.gameObject.layer = 8;
        }
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void Update()
        {
            if (!owner || !owner.match.Running || Time.time >= expires) { Destroy(gameObject); return; }
            if (itemId == 2055) Reveal(transform.position, ownerTeam, 9, .15f);
            if (itemId == 3363 && RiftActor.All.Any(a => a && a.health.team != ownerTeam && a.health.countsAsChampion && a.Targetable && Vector3.Distance(a.transform.position, transform.position) < 5)) Destroy(gameObject);
            if (visuals != null) foreach (var r in visuals) if (r) r.enabled = Revealed;
        }
        public static void Reveal(Vector3 at, int team, float range, float seconds)
        { foreach (var ward in All) if (ward && ward.ownerTeam != team && Vector3.Distance(ward.transform.position, at) < range) ward.revealedUntil = Mathf.Max(ward.revealedUntil, Time.time + seconds); }
    }
}