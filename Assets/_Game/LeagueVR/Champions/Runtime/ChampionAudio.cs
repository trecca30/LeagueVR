using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Plays the champion's original League sound events. Keys follow "Champion.Event" in the sound bank
    /// (for example "Gwen.QCast"); champions without recorded events stay silent.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(PlayerChampion))]
    public class ChampionAudio : MonoBehaviour
    {
        public LeagueSoundBank originalSounds;
        [Range(0, 1)] public float effectsVolume = .8f;

        PlayerChampion player;
        AudioSource effects;
        bool mistPlaying;

        public AudioSource EffectsSource => effects;
        string Prefix => player.ChampionName + ".";

        void Awake()
        {
            player = GetComponent<PlayerChampion>();
            var go = new GameObject("Champion original League audio");
            go.transform.SetParent(player.head.transform, false);
            effects = go.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0;
            effects.dopplerLevel = 0;
            effects.volume = effectsVolume;
            effects.priority = 60;
        }

        void OnEnable()
        {
            if (!player)
                player = GetComponent<PlayerChampion>();
            player.Cast += OnCast;
        }

        void OnDisable()
        {
            if (player)
                player.Cast -= OnCast;
        }

        void Update()
        {
            if (effects)
                effects.volume = effectsVolume;
            if (mistPlaying && !(player.Kit is GwenKit gwen && gwen.MistActive))
            {
                Cue("WEnd");
                mistPlaying = false;
            }
        }

        /// <summary>Plays "Champion.event" through the head-locked source if the bank has it.</summary>
        public void Cue(string evt, float volume = 1)
        {
            if (originalSounds && effects)
                originalSounds.Play(effects, Prefix + evt, volume);
        }

        public void CueAt(string evt, Vector3 position, float volume = .5f)
        {
            if (originalSounds)
                originalSounds.At(Prefix + evt, position, volume);
        }

        public void Snip(int index, int count)
        {
            if (index == 0)
                Cue("QCast");
            Cue(index == 0 ? "QFirst" : index == count - 1 ? "QLast" : "QMiddle", .7f);
        }

        public void SnipHit(Vector3 point, bool final) => CueAt(final ? "QLastHit" : "QHit", point, .45f);

        public void NeedleHit(Combatant target)
        {
            if (target)
                CueAt("RHit", target.AimPosition, .5f);
        }

        void OnCast(string signal, Vector3 position, Vector3 direction)
        {
            bool empowered = player.Kit is GwenKit g && g.Empowered;
            switch (signal)
            {
                case "Attack1":
                    Cue(empowered ? "EmpoweredAttack" : "Attack", .7f);
                    break;
                case "Hit":
                    Cue(empowered ? "EmpoweredHit" : "AttackHit", .6f);
                    break;
                case "Q":
                    if (!(player.Kit is GwenKit))
                        Cue("Q");
                    break;
                case "W":
                    Cue(mistPlaying ? "WRecast" : "W");
                    mistPlaying = player.Kit is GwenKit;
                    break;
                case "E":
                    Cue("E");
                    break;
                case "R":
                    if (player.Kit is GwenKit gwen)
                    {
                        int stage = gwen.RStage == 0 ? 3 : gwen.RStage;
                        Cue(stage == 1 ? "R" : "RRecast");
                        Cue("R" + stage, .65f);
                    }
                    else
                        Cue("R");
                    break;
                case "Death":
                    Cue("Death");
                    break;
                case "Respawn":
                    Cue("Respawn");
                    break;
            }
        }
    }
}
