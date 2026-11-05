using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Champion sounds. Champions with recorded League events in the sound bank (keys "Champion.Event", for example
    /// "Gwen.QCast" or "Zeri.Q") play those, with their voice lines on a chance and a cooldown like League (Zeri through
    /// her own signal table, Gwen through the recorded cues); the others (Zoe, Pantheon) play synthesized effects from
    /// <see cref="ProceduralSfx"/>.
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
        bool mistPlaying, recorded, haveFeet, wasRecalling;
        float lastAttack = -10, lastImpact = -10, stillSince = -1;
        Vector3 lastFeet;
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
            ["Zeri"] = new()
            {
                ["Volley"] = ("zeri.volley", .5f, false),
                ["Volley3"] = ("zeri.volley3", .55f, false),
                ["VolleyPierce"] = ("zeri.volley", .5f, false),
                ["Hit"] = ("zeri.hit", .5f, true),
                ["Zap"] = ("zeri.zap", .5f, false),
                ["ChargedShot"] = ("zeri.charged", .85f, false),
                ["Charged"] = ("zeri.ready", .45f, false),
                ["PulseCharge"] = ("zeri.charge", .5f, false),
                ["W"] = ("zeri.w", .7f, false),
                ["PulseHit"] = ("zeri.hit", .5f, true),
                ["LaserCharge"] = ("zeri.telegraph", .6f, true),
                ["Laser"] = ("zeri.laser", .8f, true),
                ["E"] = ("zeri.e", .65f, false),
                ["CrashCharge"] = ("zeri.crashcharge", .55f, false),
                ["Crash"] = ("zeri.r", 1f, false),
                ["Chain"] = ("zeri.chain", .5f, true),
                ["Execute"] = ("zeri.charged", .7f, true),
            },
        };

        /// <summary>
        /// Original League recordings per champion: signal -> bank events (without the champion prefix), each with a
        /// volume, whether it plays where it happened, a chance and a cooldown (voice lines). A signal listed here with no
        /// events is silent; signals not listed fall back to the synthesized table.
        /// </summary>
        static readonly Dictionary<string, Dictionary<string, (string key, float volume, bool spatial, float chance, float cooldown)[]>> Original = new()
        {
            ["Zeri"] = new()
            {
                // Levels from the clips' loudness: her effects are mastered hot, her voice lines quieter.
                ["Volley"] = new[] { ("Q", .5f, false, 1f, 0f), ("VOQ", 1f, false, .1f, 12f) },
                ["VolleyPierce"] = new[] { ("QPierce", .6f, false, 1f, 0f), ("VOQ", 1f, false, .1f, 12f) },
                ["Volley3"] = new[] { ("QOvercharged", .75f, false, 1f, 0f), ("VOQ", 1f, false, .12f, 12f) },
                ["Hit"] = new[] { ("QHit", .5f, true, 1f, .08f) },
                ["Chain"] = new[] { ("RChainHit", .55f, true, 1f, .06f) },
                ["Zap"] = new[] { ("BatteryShot", .75f, false, 1f, 0f) },
                ["BatteryHit"] = new[] { ("BatteryHit", .45f, true, 1f, 0f) },
                ["ChargedShot"] = new[] { ("ChargedShot", .6f, false, 1f, 0f), ("VOCharged", 1f, false, .35f, 10f) },
                ["ChargedHit"] = new[] { ("ChargedHit", .6f, true, 1f, 0f) },
                ["Execute"] = new[] { ("ChargedHit", .65f, true, 1f, 0f), ("VOKill", 1f, false, .35f, 12f) },
                ["Charged"] = new[] { ("PassiveReady", .55f, false, 1f, 0f), ("VOPassiveReady", 1f, false, .4f, 15f) },
                ["PulseCharge"] = new[] { ("WCast", .4f, false, 1f, 0f) },
                ["W"] = new[] { ("WLaunch", .75f, false, 1f, 0f), ("VOW", 1f, false, .3f, 10f) },
                ["PulseHit"] = new[] { ("WHit", .5f, true, 1f, .15f) },
                // The pulse meets the wall, then the beam fires 0.85 s later.
                ["LaserCharge"] = new[] { ("WHit", .5f, true, 1f, .15f) },
                ["Laser"] = new[] { ("WLaser", .6f, false, 1f, 0f) },
                ["E"] = new[] { ("E", .55f, false, 1f, 0f), ("VOE", 1f, false, .3f, 10f) },
                ["Surge"] = new[] { ("ERounds", .45f, false, 1f, 0f) },
                ["Crash"] = new[] { ("R", .8f, false, 1f, 0f), ("VOR", 1f, false, 1f, 0f) },
                ["Overcharge"] = new[] { ("ROvercharged", .8f, false, 1f, 0f) },
                ["OverchargeEnd"] = new[] { ("REnd", .4f, false, 1f, 0f), ("VOREnd", 1f, false, .5f, 10f) },
                ["Moving"] = new[] { ("VOMove", 1f, false, .3f, 30f) },
                ["Death"] = new[] { ("Death", .55f, false, 1f, 0f), ("VODeath", 1f, false, 1f, 0f) },
                ["Respawn"] = new[] { ("Respawn", .5f, false, 1f, 0f), ("VORespawn", 1f, false, .7f, 0f) },
            },
            ["Gragas"] = new()
            {
                ["Attack1"] = new[] { ("Attack", .6f, false, 1f, 0f), ("VOBasicAttack", 1f, false, .12f, 6f), ("VOAttack", 1f, false, .04f, 30f) },
                ["Hit"] = new[] { ("AttackHit", .55f, true, 1f, .08f) },
                // A critical strike: the wound-up swing, then League's heavier hit.
                ["CritSwing"] = new[] { ("Crit", .6f, false, 1f, 0f) },
                ["CritHit"] = new[] { ("CritHit", .7f, true, 1f, .08f) },
                ["Q"] = new[] { ("QCast", .6f, false, 1f, 0f), ("VOQ", 1f, false, .35f, 8f) },
                // The barrel foams where it stops while it ferments.
                ["QLand"] = new[] { ("QMissile", .6f, true, 1f, 0f) },
                ["QBurst"] = new[] { ("QExplode", .75f, true, 1f, 0f) },
                ["W"] = new[] { ("WCast", .7f, false, 1f, 0f), ("VOW", 1f, false, .35f, 8f) },
                ["Empowered"] = new[] { ("WBuffActivate", .5f, false, 1f, 0f) },
                ["WSplash"] = new[] { ("WEmpoweredHit", .75f, true, 1f, 0f) },
                // Both hands take the keg for the empowered smash.
                ["WSmashCast"] = new[] { ("WEmpoweredCast", .7f, false, 1f, 0f) },
                // The keg smashed down in both hands thuds even when it lands on nobody.
                ["WSmash"] = new[] { ("AttackHit", .6f, true, 1f, .08f) },
                ["E"] = new[] { ("ECast", .7f, false, 1f, 0f), ("VOE", 1f, false, .35f, 8f) },
                ["SlamHit"] = new[] { ("EHit", .8f, true, 1f, 0f) },
                ["R"] = new[] { ("RCast", .8f, false, 1f, 0f), ("VOR", 1f, false, 1f, 0f) },
                ["RBurst"] = new[] { ("RExplode", 1f, true, 1f, 0f) },
                ["Death"] = new[] { ("VODeath", 1f, false, 1f, 0f) },
            },
            // Hwei: the subject spells carry their own sounds (the plain Q/W/E signals of the cast are silent).
            ["Hwei"] = new()
            {
                ["Attack1"] = new[] { ("Attack", .55f, false, 1f, 0f), ("VOAttack", 1f, false, .1f, 8f) },
                ["AttackRelease"] = new[] { ("AttackMissile", .45f, false, 1f, 0f) },
                ["Hit"] = new[] { ("AttackHit", .5f, true, 1f, .08f) },
                ["SubjectQ"] = new[] { ("SubjectQ", .6f, false, 1f, 0f) },
                ["SubjectW"] = new[] { ("SubjectW", .6f, false, 1f, 0f) },
                ["SubjectE"] = new[] { ("SubjectE", .6f, false, 1f, 0f) },
                ["WashBrush"] = new[] { ("WashBrush", .6f, false, 1f, 0f) },
                ["Q"] = new (string, float, bool, float, float)[0],
                ["W"] = new (string, float, bool, float, float)[0],
                ["E"] = new (string, float, bool, float, float)[0],
                ["QQ"] = new[] { ("QQCast", .6f, false, 1f, 0f), ("VOQQ", 1f, false, .3f, 8f) },
                ["QQRelease"] = new[] { ("QQMissile", .55f, false, 1f, 0f) },
                ["QQHit"] = new[] { ("QQExplode", .7f, true, 1f, 0f) },
                ["QW"] = new[] { ("QWCast", .6f, false, 1f, 0f) },
                ["QWWarn"] = new[] { ("QWZone", .55f, true, 1f, 0f) },
                ["QWHit"] = new[] { ("QWExplode", .8f, true, 1f, 0f) },
                ["QE"] = new[] { ("QECast", .6f, false, 1f, 0f), ("VOQE", 1f, false, .3f, 8f) },
                ["QEStart"] = new[] { ("QEMissile", .5f, true, 1f, 0f) },
                ["QEErupt"] = new[] { ("QEExplode", .5f, true, 1f, .15f) },
                ["WQ"] = new[] { ("WQCast", .6f, false, 1f, 0f), ("VOWQ", 1f, false, .3f, 8f) },
                ["WQRelease"] = new[] { ("WQMissile", .5f, false, 1f, 0f) },
                ["WQZone"] = new[] { ("WQZone", .45f, true, 1f, 0f) },
                ["WQSpeed"] = new[] { ("WQMoveSpeed", .5f, false, 1f, 1.5f) },
                ["WW"] = new[] { ("WWCast", .6f, false, 1f, 0f), ("VOWW", 1f, false, .3f, 8f) },
                ["WWZone"] = new[] { ("WWZone", .5f, true, 1f, 0f) },
                ["WWShield"] = new[] { ("WWShieldOn", .55f, false, 1f, 1f) },
                ["WWShieldEnd"] = new[] { ("WWShieldOff", .45f, false, 1f, 0f) },
                ["WE"] = new[] { ("WECast", .6f, false, 1f, 0f) },
                ["WELights"] = new[] { ("WEBuffStart", .5f, false, 1f, 0f) },
                ["WEHit"] = new[] { ("WEHit", .55f, true, 1f, .05f) },
                ["WEEnd"] = new[] { ("WEBuffEnd", .45f, false, 1f, 0f) },
                ["EQ"] = new[] { ("EQCast", .6f, false, 1f, 0f), ("VOEQ", 1f, false, .3f, 8f) },
                ["EQRelease"] = new[] { ("EQMissile", .55f, false, 1f, 0f) },
                ["EQHit"] = new[] { ("EQHit", .7f, true, 1f, 0f) },
                ["EW"] = new[] { ("EWCast", .6f, false, 1f, 0f) },
                ["EWZone"] = new[] { ("EWZone", .5f, true, 1f, 0f) },
                ["EWWarn"] = new[] { ("EWTargetWarning", .5f, true, 1f, 0f) },
                ["EWStrike"] = new[] { ("EWMissile", .55f, true, 1f, 0f) },
                ["EWHit"] = new[] { ("EWHit", .7f, true, 1f, 0f), ("VOEWRoot", 1f, false, .5f, 8f) },
                ["EWEnd"] = new[] { ("EWZoneEnd", .4f, true, 1f, 0f) },
                ["EE"] = new[] { ("EECast", .6f, false, 1f, 0f), ("VOEE", 1f, false, .3f, 8f) },
                ["EEZone"] = new[] { ("EEZone", .55f, true, 1f, 0f) },
                ["EEHit"] = new[] { ("EEHit", .75f, true, 1f, 0f) },
                ["R"] = new[] { ("RCast", .7f, false, 1f, 0f), ("RMissile", .6f, false, 1f, 0f), ("VOR", 1f, false, 1f, 0f) },
                ["RAttach"] = new[] { ("RAttach", .7f, true, 1f, 0f) },
                ["RGrow"] = new[] { ("RGrow", .55f, true, 1f, 0f) },
                ["RBurst"] = new[] { ("RExplode", .9f, true, 1f, 0f) },
                ["Mark"] = new[] { ("PMark", .45f, true, 1f, .1f) },
                ["MarkWarn"] = new[] { ("PWarning", .5f, true, 1f, .1f) },
                ["MarkBurst"] = new[] { ("PDetonate", .7f, true, 1f, .05f) },
                ["Death"] = new[] { ("Death", .55f, false, 1f, 0f), ("VODeath", 1f, false, 1f, 0f) },
                ["Respawn"] = new[] { ("Respawn", .5f, false, 1f, 0f), ("VORespawn", 1f, false, .7f, 0f) },
            },
        };

        readonly Dictionary<string, float> lastOriginal = new();

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
            Combatant.Defeated += OnDefeated;
            if (player.Kit != null)
                OnKitChanged(player.Kit);
        }

        void OnDisable()
        {
            Combatant.Defeated -= OnDefeated;
            if (!player)
                return;
            player.Cast -= OnCast;
            player.KitChanged -= OnKitChanged;
        }

        /// <summary>A kill line when the player takes down a champion (bots included).</summary>
        void OnDefeated(Combatant victim, DamageHit hit)
        {
            if (player && hit.source == player.Health && victim && victim != player.Health && victim.countsAsChampion)
                Voice("VOKill", .8f, 6);
        }

        /// <summary>
        /// A voice line from the bank, on a chance and a cooldown like League's, so lines stay occasional. Does nothing for
        /// champions whose bank has no such line.
        /// </summary>
        void Voice(string key, float chance, float cooldown)
        {
            string full = Prefix + key;
            if (!originalSounds || !effects || originalSounds.Find(full) == null)
                return;
            if (cooldown > 0 && lastOriginal.TryGetValue(full, out float last) && Time.time - last < cooldown)
                return;
            if (chance < 1 && Random.value > chance)
                return;
            lastOriginal[full] = Time.time;
            originalSounds.Play(effects, full, 1);
        }

        /// <summary>For recorded champions whose kits send no move signal: a move line when walking off after standing still.</summary>
        void MoveVoice()
        {
            Vector3 feet = player.Feet;
            float speed = haveFeet && Time.deltaTime > 0 ? Geo.FlatDistance(feet, lastFeet) / Time.deltaTime : 0;
            lastFeet = feet;
            haveFeet = true;
            if (speed < .4f)
            {
                if (stillSince < 0)
                    stillSince = Time.time;
                return;
            }
            if (stillSince >= 0 && Time.time - stillSince > 2.5f)
                Voice("VOMove", .3f, 30);
            stillSince = -1;
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
            if (recorded && player.Health && player.Health.IsAlive)
                MoveVoice();
            // The recall channel and a recall line as the recall begins (any champion whose bank has them).
            var match = LeagueVR.Match.RiftMatch.Instance;
            bool recalling = match && match.IsRecalling;
            if (recalling && !wasRecalling)
            {
                Cue("Recall", .6f, LeagueVR.Match.RiftMatch.RecallDuration);
                Voice("VORecall", 1, 0);
            }
            wasRecalling = recalling;
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

        /// <summary>A snip landing: League's middle or final hit, or its centre-line version where the true damage lands.</summary>
        public void SnipHit(Vector3 point, bool final, bool centre)
        {
            string key = final ? (centre ? "QLastHitCenter" : "QLastHit") : (centre ? "QHitCenter" : "QMiddleHit");
            if (originalSounds && originalSounds.Find(Prefix + key) == null)
                key = final ? "QLastHit" : "QHit";
            CueAt(key, point, .45f);
        }

        /// <summary>The needles leaving the hand: League's missile whoosh, the heavier one for the five-needle third cast.</summary>
        public void NeedlesThrown(int count) => Cue(count >= 5 ? "R3Missile" : "RMissile", .55f);

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
            if (OriginalCue(signal, position))
                return;
            if (recorded)
                RecordedCue(signal);
            else
                SynthCue(signal, position);
        }

        /// <summary>Plays a champion's original recordings for a signal; false if the champion has none for it.</summary>
        bool OriginalCue(string signal, Vector3 position)
        {
            if (!originalSounds || !Original.TryGetValue(player.ChampionName, out var table) || !table.TryGetValue(signal, out var cues))
                return false;
            foreach (var (key, volume, spatial, chance, cooldown) in cues)
            {
                string full = Prefix + key;
                if (originalSounds.Find(full) == null)
                    continue;
                if (cooldown > 0 && lastOriginal.TryGetValue(full, out float last) && Time.time - last < cooldown)
                    continue;
                if (chance < 1 && Random.value > chance)
                    continue;
                lastOriginal[full] = Time.time;
                if (spatial)
                    originalSounds.At(full, position, volume * effectsVolume);
                else if (effects)
                    originalSounds.Play(effects, full, volume);
            }
            return true;
        }

        void RecordedCue(string signal)
        {
            bool empowered = player.Kit is GwenKit g && g.Empowered;
            switch (signal)
            {
                case "Attack1":
                    Cue(empowered ? "EmpoweredAttack" : "Attack", .7f);
                    Voice("VOAttack", .03f, 30);
                    break;
                case "Hit":
                    // The swing's own impact; spells play their own hit sounds.
                    if (Time.time - lastAttack < .8f)
                        Cue(empowered ? "EmpoweredHit" : "AttackHit", .6f);
                    break;
                case "Q":
                    if (!(player.Kit is GwenKit))
                        Cue("Q");
                    Voice("VOQ", .35f, 8);
                    break;
                case "W":
                    if (!mistPlaying)
                        Voice("VOW", .35f, 8);
                    Cue(mistPlaying ? "WRecast" : "W");
                    mistPlaying = player.Kit is GwenKit;
                    break;
                case "E":
                    // The empowered-blade loop lasts as long as the empowerment (4 s), not the 18 s of the recording.
                    Cue("E", 1, 4);
                    Voice("VOE", .35f, 8);
                    break;
                case "R":
                    if (player.Kit is GwenKit gwen)
                    {
                        // The cast signal comes before the kit counts the cast, so this is cast RStage + 1 of 3.
                        int stage = gwen.RStage + 1;
                        Cue(stage == 1 ? "R" : "RRecast");
                        Cue("R" + stage, .65f, 8);
                        // League voices the first cast every time and the recasts now and then.
                        Voice(stage == 1 ? "VOR" : stage == 2 ? "VOR2" : "VOR3", stage == 1 ? 1 : .6f, 0);
                    }
                    else
                        Cue("R");
                    break;
                case "Death":
                    Cue("Death");
                    Voice("VODeath", 1, 0);
                    break;
                case "Respawn":
                    Cue("Respawn");
                    Voice("VORespawn", .7f, 0);
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
