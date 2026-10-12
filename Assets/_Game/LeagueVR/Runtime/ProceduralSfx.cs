using System;
using System.Collections.Generic;
using UnityEngine;

namespace LeagueVR
{
    /// <summary>
    /// Synthesized sound effects for champions without recorded League sounds (Zoe, Pantheon): chimes, whooshes,
    /// pops, clangs and booms built from sines, filtered noise and envelopes. Each clip is generated once and cached.
    /// </summary>
    public static class ProceduralSfx
    {
        const int Rate = 44100;
        const float Tau = Mathf.PI * 2;
        static readonly Dictionary<string, AudioClip> cache = new();

        /// <summary>The named effect, or null for an unknown name.</summary>
        public static AudioClip Get(string name)
        {
            if (cache.TryGetValue(name, out var clip) && clip)
                return clip;
            clip = Build(name);
            if (clip)
                cache[name] = clip;
            return clip;
        }

        static AudioClip Build(string name) => name switch
        {
            // Zoe: bright, playful, cosmic.
            "zoe.attack" => Make(name, .28f, Pew(1700, 900, .18f) + Chime(2600, .12f, .35f)),
            "zoe.hit" => Make(name, .35f, Chime(2093, .18f) + Chime(3136, .12f, .5f), .55f),
            "zoe.q" => Make(name, .7f, Whoosh(.45f, 600, 3200, .6f) + Arpeggio(new[] { 1047f, 1319, 1568, 2093 }, .06f, .35f)),
            "zoe.burst" => Make(name, .9f, Sparkles(14, .55f, 1800, 4200, 3) + Boom(140, 60, .35f, .5f)),
            "zoe.w" => Make(name, .9f, Arpeggio(new[] { 1319f, 1568, 1976, 2349, 2637, 3136 }, .07f, .4f) + Whoosh(.6f, 900, 4000, .4f)),
            "zoe.shard" => Make(name, .5f, Chime(1568, .3f) + Delay(.09f, Chime(2349, .3f)), .6f),
            "zoe.e" => Make(name, .6f, Bubble(.5f)),
            "zoe.pop" => Make(name, .35f, Pop(900, 160, .05f) + Chime(1760, .1f, .4f)),
            "zoe.sleep" => Make(name, 1.1f, Arpeggio(new[] { 1568f, 1319, 1047, 880 }, .16f, .5f), .45f),
            "zoe.portal" => Make(name, .9f, Sweep(260, 1250, .75f) + Whoosh(.8f, 400, 2400, .5f) + Sparkles(8, .8f, 2000, 4000, 2)),
            // Pantheon: metal, weight and fire.
            "pan.thrust" => Make(name, .3f, Whoosh(.24f, 500, 2600, 1f)),
            "pan.hit" => Make(name, .45f, Thud(120, 55, .1f) + Clang(.12f, .35f) + Noise(.03f, .6f)),
            "pan.q" => Make(name, .55f, Whoosh(.45f, 300, 2200, 1f) + Thud(90, 45, .2f, .4f)),
            "pan.w" => Make(name, .5f, Whoosh(.3f, 400, 2000, .8f) + Delay(.22f, Clang(.5f, .9f))),
            "pan.bash" => Make(name, .7f, Clang(.6f, 1f) + Thud(100, 50, .18f, .7f)),
            "pan.e" => Make(name, .9f, Clang(.8f, .7f) + Hum(110, .8f, .25f)),
            "pan.block" => Make(name, .5f, Clang(.4f, 1f) + Noise(.02f, .5f), .6f),
            "pan.slam" => Make(name, 1.2f, Boom(85, 40, .7f) + Clang(.9f, .8f) + Noise(.12f, .5f)),
            "pan.r" => Make(name, 2.1f, Rumble(2f)),
            "pan.crash" => Make(name, 2.4f, Boom(70, 30, 1.6f) + Noise(.5f, .8f) + Delay(.05f, Clang(1.2f, .5f))),
            "pan.will" => Make(name, .9f, Clang(.8f, .5f) + Chime(660, .6f, .5f), .5f),
            // Zeri: crackling electricity. A burst is baked into one clip (rounds 35 ms apart) so no round is throttled away.
            "zeri.volley" => Make(name, .32f, Round(0, 2600) + Round(.035f, 2400) + Round(.07f, 2800) + Round(.105f, 2500) + Round(.14f, 2700) + Round(.175f, 2300) + Round(.21f, 2900), .6f),
            "zeri.volley3" => Make(name, .3f, Round(0, 3200) + Round(.03f, 3000) + Round(.06f, 3400) + Sparkles(8, .25f, 2400, 6400, 11), .65f),
            "zeri.hit" => Make(name, .18f, Noise(.005f, .9f) + Chime(2349, .05f, .6f) + Boom(240, 80, .04f, .5f) + Sparkles(4, .12f, 2400, 5200, 7), .6f),
            "zeri.zap" => Make(name, .2f, Pew(1900, 700, .1f) + Noise(.006f, .6f) + Sparkles(3, .12f, 2600, 5000, 5), .6f),
            "zeri.charged" => Make(name, .55f, Boom(220, 50, .2f, 1f) + Pew(2600, 400, .2f) + Clang(.1f, .4f) + Noise(.012f, .8f) + Whoosh(.3f, 400, 3600, .5f), .85f),
            "zeri.ready" => Make(name, .5f, Chime(1568, .2f, .7f) + Delay(.08f, Chime(2349, .25f, .6f)) + Sparkles(6, .4f, 2600, 6000, 13), .55f),
            "zeri.charge" => Make(name, .74f, Sweep(200, 1400, .72f) + Rumble(.72f) + Delay(.16f, Noise(.005f, .4f)) + Delay(.3f, Noise(.005f, .5f)) + Delay(.42f, Noise(.005f, .6f)) + Delay(.52f, Noise(.005f, .75f)) + Delay(.6f, Noise(.005f, .9f)), .6f),
            "zeri.w" => Make(name, .32f, Pew(600, 2800, .16f) + Whoosh(.16f, 900, 5000, .5f) + Delay(.16f, Chime(1976, .14f, .5f)) + Boom(260, 120, .05f, .4f), .75f),
            "zeri.telegraph" => Make(name, .85f, Hum(440, .85f, .3f) + Sweep(500, 1800, .8f) + Sparkles(6, .8f, 2200, 5200, 17), .45f),
            "zeri.laser" => Make(name, 1f, Hum(330, 1f, .5f) + Hum(660, 1f, .25f) + Sweep(1400, 1600, 1f) + Sparkles(12, .9f, 2400, 5200, 9) + Boom(180, 60, .3f, .7f), .65f),
            "zeri.e" => Make(name, .36f, Pew(300, 2600, .3f) + Whoosh(.34f, 400, 6000, .7f) + Sparkles(6, .3f, 2200, 5200, 4), .7f),
            "zeri.crashcharge" => Make(name, .6f, Sweep(150, 700, .55f) + Rumble(.55f) + Sparkles(8, .5f, 1800, 5000, 19), .5f),
            "zeri.r" => Make(name, 1.8f, Noise(.02f, 1.2f) + Boom(120, 26, 1.2f, 1.1f) + Whoosh(1f, 300, 7000, .9f) + Delay(.02f, Clang(.9f, .5f)) + Sparkles(16, 1.3f, 1400, 6500, 21), .9f),
            "zeri.chain" => Make(name, .22f, Pew(3200, 1400, .09f) + Delay(.09f, Pew(1400, 2800, .09f)) + Noise(.012f, .7f) + Sparkles(6, .2f, 2000, 6000, 33), .6f),
            // Anyone else.
            "generic.attack" => Make(name, .25f, Whoosh(.2f, 600, 2400, .8f), .5f),
            "generic.hit" => Make(name, .3f, Thud(140, 70, .08f) + Noise(.02f, .4f), .55f),
            _ => null,
        };

        // ---------- Voices: functions of the sample index, combined with + ----------

        public sealed class Voice
        {
            readonly Func<int, float> sample;

            public Voice(Func<int, float> sample) => this.sample = sample;

            public float this[int i] => sample(i);

            public static Voice operator +(Voice a, Voice b) => new(i => a[i] + b[i]);
        }

        static float T(int i) => i / (float)Rate;

        /// <summary>A bell-like tone: a few inharmonic partials that die away.</summary>
        static Voice Chime(float frequency, float decay, float level = 1) => new(i =>
        {
            float t = T(i);
            return level * (Mathf.Sin(Tau * frequency * t) * Mathf.Exp(-t / decay)
                + .45f * Mathf.Sin(Tau * frequency * 2.76f * t) * Mathf.Exp(-t / (decay * .6f))
                + .2f * Mathf.Sin(Tau * frequency * 5.4f * t) * Mathf.Exp(-t / (decay * .35f)));
        });

        static Voice Delay(float seconds, Voice voice)
        {
            int offset = Mathf.RoundToInt(seconds * Rate);
            return new(i => i < offset ? 0 : voice[i - offset]);
        }

        /// <summary>One electric round of Zeri's Burst Fire: a short falling zap with a crack, starting at <paramref name="at"/>.</summary>
        static Voice Round(float at, float pitch) => Delay(at, Pew(pitch, pitch * .42f, .035f) + Noise(.004f, .9f));

        static Voice Arpeggio(float[] notes, float step, float decay)
        {
            Voice v = new(_ => 0);
            for (int n = 0; n < notes.Length; n++)
                v += Delay(n * step, Chime(notes[n], decay, .8f));
            return v;
        }

        /// <summary>A quick falling "pew".</summary>
        static Voice Pew(float from, float to, float seconds) => new(i =>
        {
            float t = T(i);
            if (t > seconds)
                return 0;
            float k = t / seconds;
            float phase = Tau * (from * t + (to - from) * t * k * .5f);
            return Mathf.Sin(phase) * (1 - k) * (1 - k);
        });

        /// <summary>A rising glide with vibrato (portals).</summary>
        static Voice Sweep(float from, float to, float seconds)
        {
            float phase = 0;
            return new(i =>
            {
                float t = T(i);
                if (t > seconds)
                    return 0;
                float k = t / seconds;
                float f = Mathf.Lerp(from, to, k * k) * (1 + .03f * Mathf.Sin(Tau * 9 * t));
                phase += Tau * f / Rate;
                return Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * k) * .8f;
            });
        }

        /// <summary>Noise through a low-pass filter whose cutoff sweeps up and down: air rushing past.</summary>
        static Voice Whoosh(float seconds, float low, float high, float level = 1)
        {
            float state = 0;
            uint seed = 0x9E3779B9;
            return new(i =>
            {
                float t = T(i);
                if (t > seconds)
                    return 0;
                float k = t / seconds;
                float cutoff = Mathf.Lerp(low, high, Mathf.Sin(Mathf.PI * k));
                float a = 1 - Mathf.Exp(-Tau * cutoff / Rate);
                state += a * (White(ref seed) - state);
                float envelope = Mathf.Sin(Mathf.PI * k);
                return level * state * envelope * envelope * 2.2f;
            });
        }

        static Voice Noise(float decay, float level)
        {
            uint seed = 0x2545F491;
            return new(i => level * White(ref seed) * Mathf.Exp(-T(i) / decay));
        }

        /// <summary>A low sine that drops in pitch: impacts and landings.</summary>
        static Voice Boom(float from, float to, float decay, float level = 1)
        {
            float phase = 0;
            return new(i =>
            {
                float t = T(i);
                float f = to + (from - to) * Mathf.Exp(-t * 6);
                phase += Tau * f / Rate;
                return level * Mathf.Sin(phase) * Mathf.Exp(-t / decay);
            });
        }

        static Voice Thud(float from, float to, float decay, float level = 1) => Boom(from, to, decay, level);

        /// <summary>Struck metal: inharmonic partials with different decays (shield, spear).</summary>
        static Voice Clang(float decay, float level)
        {
            float[] partials = { 431, 1157, 1789, 2893, 4127 };
            float[] decays = { 1f, .55f, .4f, .28f, .16f };
            return new(i =>
            {
                float t = T(i), sum = 0;
                for (int p = 0; p < partials.Length; p++)
                    sum += Mathf.Sin(Tau * partials[p] * t + p) * Mathf.Exp(-t / (decay * decays[p])) / (1 + p * .6f);
                return level * sum;
            });
        }

        static Voice Hum(float frequency, float seconds, float level) => new(i =>
        {
            float t = T(i);
            return t > seconds ? 0 : level * Mathf.Sin(Tau * frequency * t) * Mathf.Sin(Mathf.PI * t / seconds);
        });

        /// <summary>A building low rumble with rising crackle (gathering power).</summary>
        static Voice Rumble(float seconds)
        {
            float state = 0, phase = 0;
            uint seed = 0x68E31DA4;
            return new(i =>
            {
                float t = T(i);
                if (t > seconds)
                    return 0;
                float k = t / seconds;
                state += .02f * (White(ref seed) - state);
                phase += Tau * (45 + 30 * k) / Rate;
                return (state * 3 + Mathf.Sin(phase) * .6f) * k * (1 - Mathf.Pow(k, 8));
            });
        }

        /// <summary>A wobbling, gurgling blow (Zoe's bubble).</summary>
        static Voice Bubble(float seconds)
        {
            float phase = 0;
            return new(i =>
            {
                float t = T(i);
                if (t > seconds)
                    return 0;
                float k = t / seconds;
                float f = 320 + 220 * k + 90 * Mathf.Sin(Tau * 11 * t);
                phase += Tau * f / Rate;
                return Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * k) * (.6f + .4f * Mathf.Sin(Tau * 23 * t));
            });
        }

        static Voice Pop(float from, float to, float decay) => new(i =>
        {
            float t = T(i);
            float f = to + (from - to) * Mathf.Exp(-t / .02f);
            return Mathf.Sin(Tau * f * t) * Mathf.Exp(-t / decay);
        });

        /// <summary>Little random high chimes scattered over a duration.</summary>
        static Voice Sparkles(int count, float seconds, float low, float high, int seed)
        {
            var random = new System.Random(seed);
            Voice v = new(_ => 0);
            for (int n = 0; n < count; n++)
                v += Delay((float)random.NextDouble() * seconds * .8f, Chime(Mathf.Lerp(low, high, (float)random.NextDouble()), .07f, .5f));
            return v;
        }

        static float White(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 8388608f - 1;
        }

        /// <summary>Renders a voice, normalizes it and adds short fades so nothing clicks.</summary>
        static AudioClip Make(string name, float seconds, Voice voice, float gain = .8f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            float peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                data[i] = voice[i];
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            int fade = Mathf.Min(n / 4, Rate / 200);
            for (int i = 0; i < n; i++)
            {
                float edge = Mathf.Min(1, Mathf.Min(i, n - 1 - i) / (float)Mathf.Max(1, fade));
                data[i] = data[i] / peak * gain * edge;
            }
            var clip = AudioClip.Create("Synth " + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
