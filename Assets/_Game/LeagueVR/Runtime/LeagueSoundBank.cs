using System;
using System.Collections.Generic;
using UnityEngine;
namespace LeagueVR
{
    [CreateAssetMenu(menuName = "League VR/Original Sound Bank")]
    public class LeagueSoundBank : ScriptableObject
    {
        [Serializable]
        public class Layer
        {
            public AudioClip[] variants;
        }

        [Serializable]
        public class Event
        {
            public string key, eventName, sourceBank;
            public Layer[] layers;
        }
        [Range(0, 1)] public float outputGain = .6f;
        public Event[] events;
        public static int PlayedLayers;
        public static string LastEvent;

        /// <summary>Layers longer than this (buff loops, ambiences) fade out after it unless the caller asks for longer.</summary>
        public const float DefaultSustain = 6;

        /// <summary>
        /// World sounds fade to silence over 26 m. Unity's logarithmic rolloff never reaches zero, which made every
        /// minion fight on the map audible everywhere at once.
        /// </summary>
        static readonly AnimationCurve Rolloff = new(new Keyframe(0, 1), new Keyframe(.08f, 1), new Keyframe(.25f, .45f), new Keyframe(.55f, .14f), new Keyframe(1, 0));

        Dictionary<string, Event> index;

        public Event Find(string key)
        {
            if (events == null || key == null)
                return null;
            if (index == null || index.Count == 0)
            {
                index = new Dictionary<string, Event>();
                foreach (var e in events)
                    if (e != null && e.key != null)
                        index[e.key] = e;
            }
            return index.TryGetValue(key, out var found) ? found : null;
        }

        void OnValidate() => index = null;

        /// <summary>
        /// Plays an event: one random variant of each layer. Layers are separate Wwise actions; repeats of the same sound
        /// in one event (Gwen's four-stack snip) play once, and long buff loops fade out after
        /// <paramref name="sustain"/> seconds instead of running for twenty.
        /// </summary>
        public float Play(AudioSource source, string key, float volume = 1, float sustain = DefaultSustain)
        {
            var e = Find(key);
            if (e == null || !source || e.layers == null)
                return 0;
            float duration = 0;
            var played = new List<AudioClip[]>();
            foreach (var layer in e.layers)
            {
                if (layer.variants == null || layer.variants.Length == 0 || played.Exists(p => SameClips(p, layer.variants)))
                    continue;
                played.Add(layer.variants);
                var clip = layer.variants[UnityEngine.Random.Range(0, layer.variants.Length)];
                if (!clip)
                    continue;
                float gain = volume * outputGain;
                if (clip.length > sustain + 1)
                {
                    PlayTrimmed(source, clip, gain, sustain);
                    duration = Mathf.Max(duration, sustain + .6f);
                }
                else
                {
                    source.PlayOneShot(clip, gain);
                    duration = Mathf.Max(duration, clip.length);
                }
                PlayedLayers++;
            }
            LastEvent = e.eventName;
            return duration;
        }

        static bool SameClips(AudioClip[] a, AudioClip[] b)
        {
            if (a.Length != b.Length)
                return false;
            foreach (var clip in a)
                if (Array.IndexOf(b, clip) < 0)
                    return false;
            return true;
        }

        /// <summary>A long clip on its own source next to <paramref name="like"/>, faded out after a while.</summary>
        static void PlayTrimmed(AudioSource like, AudioClip clip, float volume, float sustain)
        {
            var go = new GameObject("League sound (trimmed)");
            go.transform.SetParent(like.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = like.spatialBlend;
            source.dopplerLevel = like.dopplerLevel;
            source.rolloffMode = like.rolloffMode;
            source.minDistance = like.minDistance;
            source.maxDistance = like.maxDistance;
            if (like.rolloffMode == AudioRolloffMode.Custom)
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, like.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            source.priority = like.priority;
            source.clip = clip;
            source.volume = volume * like.volume;
            source.Play();
            go.AddComponent<SoundFade>().Begin(sustain, .6f);
        }

        public void At(string key, Vector3 position, float volume = .5f)
        {
            if (Find(key) == null)
                return;
            var go = new GameObject("Original League impact audio");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            Configure(source);
            float duration = Play(source, key, volume);
            Destroy(go, duration + .1f);
        }

        public static void Configure(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = 1;
            s.dopplerLevel = 0;
            s.rolloffMode = AudioRolloffMode.Custom;
            s.minDistance = 2;
            s.maxDistance = 26;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            s.priority = 160;
        }
    }

    /// <summary>Fades an audio source out after a delay, then removes it.</summary>
    public class SoundFade : MonoBehaviour
    {
        AudioSource source;
        float start, fadeAt, fade, volume;

        public void Begin(float delay, float fadeSeconds)
        {
            source = GetComponent<AudioSource>();
            volume = source ? source.volume : 1;
            start = Time.time;
            fadeAt = start + delay;
            fade = Mathf.Max(.05f, fadeSeconds);
        }

        void Update()
        {
            if (!source)
            {
                Destroy(gameObject);
                return;
            }
            if (Time.time >= fadeAt)
                source.volume = volume * Mathf.Clamp01(1 - (Time.time - fadeAt) / fade);
            if (Time.time >= fadeAt + fade || !source.isPlaying)
                Destroy(gameObject);
        }
    }
}
