using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Champion sounds. Champions with recorded League events in the sound bank (keys "Champion.Event", for example
    /// "Gwen.QCast") play those; the others (Zoe, Pantheon) play synthesized effects from <see cref="ProceduralSfx"/>.
    /// Casts play at the head, impacts in 3D where they land, and repeated impacts within a moment play once.
    /// </summary>
    [DefaultExecutionOrder(250)]
    [RequireComponent(typeof(PlayerChampion))]
    public class ChampionAudio : MonoBehaviour
    {
        public LeagueSoundBank originalSounds;
        [Range(0, 1)] public float effectsVolume = .8f;

        PlayerChampion player;
        AudioSource effects;
        bool mistPlaying, recorded;
        float lastAttack = -10, lastImpact = -10;
        readonly Dictionary<string, float> lastSynth = new();

        /// <summary>Synthesized cues per champion: signal -> (effect, volume, played where it happened).</summary>
        static readonly Dictionary<string, Dictionary<string, (string sound, float volume, bool spatial)>> Synth = new()
        {
            ["Zoe"] = new()
            {
                ["Attack1"] = ("zoe.attack", .45f, false),
                ["Hit"] = ("zoe.hit", .5f, true),
                ["Q"] = ("zoe.q", .7f, false),
                ["StarBurst"] = ("zoe.burst", .9f, true),
                ["W"] = ("zoe.w", .7f, false),
                ["Shard"] = ("zoe.shard", .6f, false),
                ["E"] = ("zoe.e", .6f, false),
                ["Pop"] = ("zoe.pop", .75f, true),
                ["Sleep"] = ("zoe.sleep", .55f, true),
                ["R"] = ("zoe.portal", .75f, false),
                ["PortalBack"] = ("zoe.portal", .5f, false),
            },
            ["Pantheon"] = new()
            {
                ["Attack1"] = ("pan.thrust", .5f, false),
                ["Hit"] = ("pan.hit", .65f, true),
                ["Q"] = ("pan.q", .75f, false),
                ["W"] = ("pan.w", .7f, false),
                ["Bash"] = ("pan.bash", .8f, true),
                ["E"] = ("pan.e", .7f, false),
                ["Block"] = ("pan.block", .7f, false),
                ["Slam"] = ("pan.slam", .85f, true),
                ["R"] = ("pan.r", .7f, false),
                ["Crash"] = ("pan.crash", 1f, true),
                ["Will"] = ("pan.will", .45f, false),
            },
        };

        static readonly Dictionary<string, (string sound, float volume, bool spatial)> Generic = new()
        {
            ["Attack1"] = ("generic.attack", .45f, false),
            ["Hit"] = ("generic.hit", .5f, true),
        };

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
            player.KitChanged += OnKitChanged;
            if (player.Kit != null)
                OnKitChanged(player.Kit);
        }

        void OnDisable()
        {
            if (!player)
                return;
            player.Cast -= OnCast;
            player.KitChanged -= OnKitChanged;
        }

        void OnKitChanged(ChampionKit kit)
        {
            recorded = originalSounds && originalSounds.Find(Prefix + "Attack") != null;
            mistPlaying = false;
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
        public void Cue(string evt, float volume = 1, float sustain = LeagueSoundBank.DefaultSustain)
        {
            if (originalSounds && effects)
                originalSounds.Play(effects, Prefix + evt, volume, sustain);
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
            if (signal == "Attack1")
                lastAttack = Time.time;
            if (signal == "Hit")
            {
                // Abilities that hit a whole wave (and play their own impacts) would otherwise stack a dozen hit sounds.
                if (Time.time - lastImpact < .12f)
                    return;
                lastImpact = Time.time;
            }
            if (recorded)
                RecordedCue(signal);
            else
                SynthCue(signal, position);
        }

        void RecordedCue(string signal)
        {
            bool empowered = player.Kit is GwenKit g && g.Empowered;
            switch (signal)
            {
                case "Attack1":
                    Cue(empowered ? "EmpoweredAttack" : "Attack", .7f);
                    break;
                case "Hit":
                    // The swing's own impact; spells play their own hit sounds.
                    if (Time.time - lastAttack < .8f)
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
                    // The empowered-blade loop lasts as long as the empowerment (4 s), not the 18 s of the recording.
                    Cue("E", 1, 4);
                    break;
                case "R":
                    if (player.Kit is GwenKit gwen)
                    {
                        int stage = gwen.RStage == 0 ? 3 : gwen.RStage;
                        Cue(stage == 1 ? "R" : "RRecast");
                        Cue("R" + stage, .65f, 8);
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

        void SynthCue(string signal, Vector3 position)
        {
            if (!Synth.TryGetValue(player.ChampionName, out var table))
                table = Generic;
            if (!table.TryGetValue(signal, out var cue))
                return;
            // The same effect never machine-guns: each cue waits a moment before it can play again.
            if (lastSynth.TryGetValue(cue.sound, out float last) && Time.time - last < .08f)
                return;
            lastSynth[cue.sound] = Time.time;
            var clip = ProceduralSfx.Get(cue.sound);
            if (!clip)
                return;
            if (!cue.spatial)
            {
                effects.PlayOneShot(clip, cue.volume);
                return;
            }
            var go = new GameObject("Champion impact audio");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            LeagueSoundBank.Configure(source);
            source.priority = 80;
            source.PlayOneShot(clip, cue.volume * effectsVolume);
            Destroy(go, clip.length + .1f);
        }
    }
}
