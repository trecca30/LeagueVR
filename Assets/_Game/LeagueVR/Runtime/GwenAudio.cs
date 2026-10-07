using UnityEngine;
namespace LeagueVR
{
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(GwenAbilities))]
    public class GwenAudio : MonoBehaviour
    {
        // Retained for older editor builders; the active scene clears these legacy generated clips.
        public AudioClip scissors, snip, mist, dash, needle, impact, hurt, footstep, ambience;
        public LeagueSoundBank originalSounds;
        [Range(0, 1)] public float effectsVolume = .8f;
        [Range(0, 1)] public float ambienceVolume = 0;
        GwenAbilities champion;
        AudioSource effects;
        bool wPlaying;
        public AudioSource EffectsSource => effects;

        void Awake()
        {
            champion = GetComponent<GwenAbilities>();
            var go = new GameObject("Gwen original League audio");
            go.transform.SetParent(champion.head.transform, false);
            effects = go.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0;
            effects.dopplerLevel = 0;
            effects.volume = effectsVolume;
            effects.priority = 60;
        }

        void OnEnable()
        {
            if (!champion)
                champion = GetComponent<GwenAbilities>();
            champion.Cast += OnCast;
        }

        void OnDisable()
        {
            if (champion)
                champion.Cast -= OnCast;
        }

        void Update()
        {
            if (effects)
                effects.volume = effectsVolume;
            if (wPlaying && !champion.MistActive)
            {
                Cue("Gwen.WEnd");
                wPlaying = false;
            }
        }

        public void Cue(string key, float volume = 1)
        {
            if (originalSounds && effects)
                originalSounds.Play(effects, key, volume);
        }

        public void Snip(int index, int count)
        {
            if (index == 0)
                Cue("Gwen.QCast");
            Cue(index == 0 ? "Gwen.QFirst" : index == count - 1 ? "Gwen.QLast" : "Gwen.QMiddle", .7f);
        }

        public void SnipHit(Vector3 point, bool final)
        {
            if (originalSounds)
                originalSounds.At(final ? "Gwen.QLastHit" : "Gwen.QHit", point, .45f);
        }

        public void NeedleHit(Combatant target)
        {
            if (originalSounds && target)
                originalSounds.At("Gwen.RHit", target.AimPosition, .5f);
        }

        void OnCast(string ability, Vector3 position, Vector3 direction)
        {
            if (champion.OtherActive)
                return;
            switch (ability)
            {
                case "Attack1":
                    Cue(champion.Empowered ? "Gwen.EmpoweredAttack" : "Gwen.Attack", .7f);
                    break;
                case "Hit":
                    Cue(champion.Empowered ? "Gwen.EmpoweredHit" : "Gwen.AttackHit", .6f);
                    break;
                case "W":
                    Cue(wPlaying ? "Gwen.WRecast" : "Gwen.W");
                    wPlaying = true;
                    break;
                case "E":
                    Cue("Gwen.E");
                    break;
                case "R":
                    int stage = champion.RStage == 0 ? 3 : champion.RStage;
                    Cue(stage == 1 ? "Gwen.R" : "Gwen.RRecast");
                    Cue("Gwen.R" + stage, .65f);
                    break;
                case "Death":
                    Cue("Gwen.Death");
                    break;
                case "Respawn":
                    Cue("Gwen.Respawn");
                    break;
            }
        }

        public void Play(AudioClip clip, float volume = 1)
        {
            if (originalSounds && effects && clip && clip.name.Contains("_sfx"))
                effects.PlayOneShot(clip, volume);
        }
    }
}
