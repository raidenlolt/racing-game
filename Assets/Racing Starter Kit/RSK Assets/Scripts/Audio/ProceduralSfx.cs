using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// synthesised stand-in sound effects. the project ships engine, skid and countdown audio and
    /// nothing else, so the impact, nitro and finish features would otherwise be silent until an
    /// audio pass. each clip is built once from noise and a few sine partials, cached, and can be
    /// swapped for a recorded clip in the inspector without touching the code that plays it.
    ///
    /// everything is mono at 22 kHz. the clips are short, so the memory cost is a few hundred
    /// kilobytes in total.
    /// </summary>
    public static class ProceduralSfx
    {
        private const int Rate = 22050;
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public enum Impact { Light, Medium, Heavy }

        /// <summary>a body-on-body thump: a low sine knock under a burst of falling-pitch noise</summary>
        public static AudioClip ImpactClip(Impact severity)
        {
            var key = "impact_" + severity;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float seconds, thud, noiseGain, cutoff;
            switch (severity)
            {
                case Impact.Light:  seconds = 0.18f; thud = 0.25f; noiseGain = 0.5f; cutoff = 2500f; break;
                case Impact.Medium: seconds = 0.32f; thud = 0.5f;  noiseGain = 0.7f; cutoff = 1800f; break;
                default:            seconds = 0.5f;  thud = 0.8f;  noiseGain = 0.9f; cutoff = 1200f; break;
            }

            var n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(severity.GetHashCode());
            var lp = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var u = t / seconds;
                // noise, low-passed with a cutoff that drops as the hit dies away
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                var fc = Mathf.Lerp(cutoff, 200f, u);
                var alpha = OnePoleAlpha(fc);
                lp += alpha * (white - lp);
                var noise = lp * noiseGain * Mathf.Exp(-u * 7f);
                // the knock: 70 Hz pitch-bending down, fast decay
                var knock = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 45f, u) * t) * thud * Mathf.Exp(-u * 10f);
                data[i] = Mathf.Clamp(noise + knock, -1f, 1f);
            }
            return Cache[key] = Make(key, data);
        }

        /// <summary>the nitro ignition: a rising whoosh, band-passed noise sweeping up over half a second</summary>
        public static AudioClip NitroIgniteClip()
        {
            const string key = "nitro_ignite";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const float seconds = 0.6f;
            var n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(7);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var u = t / seconds;
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                var fc = Mathf.Lerp(300f, 3200f, u * u);
                var a = OnePoleAlpha(fc);
                lp += a * (white - lp);
                lp2 += a * (lp - lp2);          // two poles for a rounder band
                var env = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);   // swell in, swell out
                data[i] = Mathf.Clamp(lp2 * 1.8f * env, -1f, 1f);
            }
            return Cache[key] = Make(key, data);
        }

        /// <summary>seamless one-second loop of hiss and a low hum for the running boost</summary>
        public static AudioClip NitroLoopClip()
        {
            const string key = "nitro_loop";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const float seconds = 1f;
            var n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(11);
            var lp = 0f;
            var a = OnePoleAlpha(1400f);
            for (int i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += a * (white - lp);
                var hum = Mathf.Sin(2f * Mathf.PI * 82f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 164f * t) * 0.1f;
                data[i] = Mathf.Clamp(lp * 0.9f + hum, -1f, 1f);
            }
            // crossfade the last 40 ms into the first so the loop point does not click
            var fade = Mathf.CeilToInt(0.04f * Rate);
            for (int i = 0; i < fade; i++)
            {
                var w = i / (float)fade;
                data[n - fade + i] = data[n - fade + i] * (1f - w) + data[i] * w;
            }
            return Cache[key] = Make(key, data);
        }

        /// <summary>a short three-note rise and a held top note, for crossing the line</summary>
        public static AudioClip FinishFanfareClip()
        {
            const string key = "finish_fanfare";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var notes = new[] { 523.25f, 659.25f, 783.99f, 1046.5f };   // C5 E5 G5 C6
            var starts = new[] { 0f, 0.14f, 0.28f, 0.42f };
            var lengths = new[] { 0.16f, 0.16f, 0.16f, 0.9f };
            const float seconds = 1.4f;
            var n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                var from = Mathf.FloorToInt(starts[k] * Rate);
                var count = Mathf.CeilToInt(lengths[k] * Rate);
                for (int i = 0; i < count && from + i < n; i++)
                {
                    var t = i / (float)Rate;
                    var u = t / lengths[k];
                    var env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-u * 3f);
                    // a fundamental with a couple of soft partials reads as a brass-ish stab
                    var s = Mathf.Sin(2f * Mathf.PI * notes[k] * t)
                          + 0.4f * Mathf.Sin(2f * Mathf.PI * notes[k] * 2f * t)
                          + 0.15f * Mathf.Sin(2f * Mathf.PI * notes[k] * 3f * t);
                    data[from + i] += s * 0.35f * env;
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            return Cache[key] = Make(key, data);
        }

        private static float OnePoleAlpha(float cutoffHz)
        {
            var dt = 1f / Rate;
            var rc = 1f / (2f * Mathf.PI * cutoffHz);
            return dt / (rc + dt);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
