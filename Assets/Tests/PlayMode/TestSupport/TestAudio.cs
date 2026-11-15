using System;
using System.IO;
using UnityEngine;

namespace NoiseRun.Tests
{
    /// <summary>Synthetic, copyright-free test audio (spec 4.6).</summary>
    public static class TestAudio
    {
        const double LowHz = 60.0;
        const double MidHz = 800.0;
        const double HighHz = 5000.0;
        const double ClickLengthSec = 0.04;
        const double ClickDecaySec = 0.01;
        const float ClickAmplitude = 0.8f;

        /// <summary>
        /// Mono signal with a short decaying sine click on every beat (onBeat band)
        /// and, if given, on every half beat (offBeat band). Beats start at firstBeatSec.
        /// </summary>
        public static float[] ClickTrack(int sampleRate, double seconds, float bpm, double firstBeatSec, Band onBeat, Band? offBeat)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (bpm <= 0f) throw new ArgumentOutOfRangeException(nameof(bpm));

            var samples = new float[(int)Math.Round(seconds * sampleRate)];
            double period = 60.0 / bpm;
            for (int k = 0; ; k++)
            {
                double beat = firstBeatSec + k * period;
                if (beat >= seconds) break;
                AddClick(samples, sampleRate, beat, BandHz(onBeat));
                if (offBeat.HasValue)
                {
                    double half = beat + period * 0.5;
                    if (half < seconds) AddClick(samples, sampleRate, half, BandHz(offBeat.Value));
                }
            }
            return samples;
        }

        /// <summary>
        /// Interleaved 2-channel version of a mono signal. A channel that is switched off stays silent,
        /// so (true, true) is a dual-mono file and (true, false) a file with signal in the left channel only.
        /// </summary>
        public static float[] ToInterleavedStereo(float[] mono, bool left, bool right)
        {
            var stereo = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                if (left) stereo[2 * i] = mono[i];
                if (right) stereo[2 * i + 1] = mono[i];
            }
            return stereo;
        }

        public static AudioClip ToClip(float[] mono, int sampleRate)
        {
            var clip = AudioClip.Create("TestAudio", mono.Length, 1, sampleRate, false);
            clip.SetData(mono, 0);
            return clip;
        }

        /// <summary>Writes a 16-bit PCM mono RIFF wav. Samples are clamped to [-1, 1].</summary>
        public static void WriteWav(string path, float[] mono, int sampleRate)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            const short channels = 1;
            const short bitsPerSample = 16;
            int blockAlign = channels * bitsPerSample / 8;
            int dataBytes = mono.Length * blockAlign;

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write(channels);
                w.Write(sampleRate);
                w.Write(sampleRate * blockAlign);
                w.Write((short)blockAlign);
                w.Write(bitsPerSample);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                for (int i = 0; i < mono.Length; i++)
                {
                    float s = Mathf.Clamp(mono[i], -1f, 1f);
                    w.Write((short)Mathf.RoundToInt(s * short.MaxValue));
                }
            }
        }

        static double BandHz(Band band)
        {
            switch (band)
            {
                case Band.Low: return LowHz;
                case Band.Mid: return MidHz;
                case Band.High: return HighHz;
                default: throw new ArgumentOutOfRangeException(nameof(band));
            }
        }

        static void AddClick(float[] samples, int sampleRate, double startSec, double hz)
        {
            int start = (int)Math.Round(startSec * sampleRate);
            int length = (int)Math.Round(ClickLengthSec * sampleRate);
            for (int i = 0; i < length; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= samples.Length) break;
                double t = (double)i / sampleRate;
                samples[idx] += (float)(ClickAmplitude * Math.Sin(2.0 * Math.PI * hz * t) * Math.Exp(-t / ClickDecaySec));
            }
        }
    }
}
