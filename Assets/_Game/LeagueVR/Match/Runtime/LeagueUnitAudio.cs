using UnityEngine;
namespace LeagueVR.Match
{
    [RequireComponent(typeof(RiftActor))]
    public class LeagueUnitAudio : MonoBehaviour
    {
        public LeagueSoundBank bank;
        [Range(0, 1)] public float volume = .4f;
        AudioSource source;
        RiftActor actor;
        public string ImpactKey { get; private set; }

        void Awake()
        {
            actor = GetComponent<RiftActor>();
            source = gameObject.AddComponent<AudioSource>();
            LeagueSoundBank.Configure(source);
            source.volume = volume;
        }

        public void Attack(bool champion, int variant = 0)
        {
            if (!bank)
                return;
            string key;
            var minion = actor as RiftMinion;
            if (minion)
            {
                string kind = new[] { "Melee", "Ranged", "Siege", "Super" }[(int)minion.kind];
                key = $"Minion.{actor.health.team}.{kind}.OnCast.{variant}";
                ImpactKey = $"Minion.{actor.health.team}.{kind}.OnHit.{variant}";
                if (bank.Find(key) == null)
                {
                    key = $"Minion.{actor.health.team}.{kind}.OnCast.0";
                    ImpactKey = $"Minion.{actor.health.team}.{kind}.OnHit.0";
                }
            }
            else
            {
                string type = champion ? "Champion" : "Minion";
                key = $"Turret.{actor.health.team}.{type}.Cast";
                ImpactKey = $"Turret.{actor.health.team}.{type}.Hit";
            }
            bank.Play(source, key, 1);
        }

        public void Hit(Vector3 point, string key = null)
        {
            if (bank)
                bank.At(key ?? ImpactKey, point, volume);
        }

        public void Death()
        {
            if (!bank || actor is RiftMinion)
                return;
            var s = actor as RiftStructure;
            string kind = s.kind == StructureKind.Inhibitor ? "Inhibitor" : s.kind == StructureKind.Nexus ? "Nexus" : "Turret";
            bank.Play(source, $"{kind}.{actor.health.team}.Death");
        }

        public void Respawn()
        {
            var s = actor as RiftStructure;
            if (bank && s && s.kind == StructureKind.Inhibitor)
                bank.Play(source, $"Inhibitor.{actor.health.team}.Respawn");
        }
    }
}
