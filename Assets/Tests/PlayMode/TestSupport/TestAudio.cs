using System;
using System.Collections.Generic;
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

        // ── 드럼·지속음 합성(HPSS 테스트용). 값은 모두 고정이라 같은 인자면 같은 신호가 나온다 ──
        /// <summary>드럼 한 번이 차지하는 길이(초). 가장 긴 감쇠(킥)의 몇 배.</summary>
        const double DrumHitLengthSec = 0.25;
        const double KickStartHz = 160.0;
        const double KickEndHz = 50.0;
        const double KickSweepSec = 0.03;
        const double KickDecaySec = 0.04;
        const double KickGain = 1.0;
        const double KickClickGain = 0.3;
        const double KickClickDecaySec = 0.002;
        const double SnareBodyHz = 200.0;
        const double SnareBodyGain = 0.3;
        const double SnareBodyDecaySec = 0.03;
        const double SnareNoiseHz = 1000.0;
        const double SnareNoiseQ = 0.7;
        const double SnareNoiseGain = 1.5;
        const double SnareNoiseDecaySec = 0.025;
        const double HiHatNoiseHz = 6000.0;
        const double HiHatNoiseQ = 0.8;
        const double HiHatGain = 1.2;
        const double HiHatDecaySec = 0.02;
        const double MixPeak = 0.9;

        /// <summary>지속음: 음 길이는 NoteMinSec ~ NoteMinSec+NoteSpanSec 사이 무작위(박 격자와 무관), 음 사이는 NoteRampSec 동안 겹쳐 바뀐다.</summary>
        const double NoteMinSec = 0.4;
        const double NoteSpanSec = 0.8;
        const double NoteRampSec = 0.025;
        /// <summary>배음 상한. 2kHz 위(고음 대역)까지 배음이 있다.</summary>
        const double HarmonicMaxHz = 6000.0;
        const double VibratoHz = 5.5;
        const double VibratoDepth = 0.006;
        const double TremoloHz = 0.4;
        const double TremoloDepth = 0.15;
        /// <summary>음마다 기본음 + 완전5도(기본음 × 1.5) 두 음을 쌓는다(파워코드).</summary>
        static readonly double[] VoiceRatios = { 1.0, 1.5 };
        /// <summary>음높이 후보(Hz): G3 A3 B3 D4 E4.</summary>
        static readonly double[] NotePitchesHz = { 196.00, 220.00, 246.94, 293.66, 329.63 };

        public enum DrumKind { Kick, Snare, HiHat }

        public readonly struct DrumHit
        {
            /// <summary>히트 시작 시각(초). 이 칸의 반박자 격자 시각과 같다.</summary>
            public readonly double time;
            public readonly DrumKind kind;

            public DrumHit(double time, DrumKind kind)
            {
                this.time = time;
                this.kind = kind;
            }
        }

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
        /// 반박자 칸마다 드럼 히트 하나: 정박 칸은 번갈아 킥(1·3박)과 스네어(2·4박), 엇박 칸은 하이햇.
        /// 히트 번호 = 반박자 칸 번호(firstBeatSec가 0번)라서 BeatMap의 slotIndex로 그대로 찾을 수 있다.
        /// </summary>
        public static List<DrumHit> DrumHits(double seconds, float bpm, double firstBeatSec)
        {
            if (bpm <= 0f) throw new ArgumentOutOfRangeException(nameof(bpm));

            var hits = new List<DrumHit>();
            double halfBeat = 30.0 / bpm;
            for (int slot = 0; ; slot++)
            {
                double time = firstBeatSec + slot * halfBeat;
                if (time >= seconds) break;
                DrumKind kind;
                if ((slot & 1) == 1) kind = DrumKind.HiHat;
                else kind = ((slot >> 1) & 1) == 0 ? DrumKind.Kick : DrumKind.Snare;
                hits.Add(new DrumHit(time, kind));
            }
            return hits;
        }

        /// <summary>
        /// DrumHits 그대로 드럼을 합성한 모노 신호(최대 진폭 1). 킥 = 피치가 내려가는 사인 + 짧은 광대역 클릭(저음),
        /// 스네어 = 200Hz 몸통 + 1kHz 중심 대역 노이즈(중음), 하이햇 = 6kHz 중심 대역 노이즈(고음). 전부 지수 감쇠 수십 ms.
        /// 노이즈 버스트는 주파수 방향으로 넓게 퍼져 HPSS가 타악기로 잡는다. 노이즈는 seed로 고정한다.
        /// </summary>
        public static float[] DrumTrack(int sampleRate, double seconds, float bpm, double firstBeatSec, uint seed)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            var samples = new double[(int)Math.Round(seconds * sampleRate)];
            var burst = new double[(int)Math.Round(DrumHitLengthSec * sampleRate)];
            uint state = seed;
            foreach (DrumHit hit in DrumHits(seconds, bpm, firstBeatSec))
            {
                RenderDrum(burst, sampleRate, hit.kind, ref state);
                int start = (int)Math.Round(hit.time * sampleRate);
                for (int i = 0; i < burst.Length; i++)
                {
                    int idx = start + i;
                    if (idx >= samples.Length) break;
                    samples[idx] += burst[i];
                }
            }
            return NormalizePeak(samples, 1.0);
        }

        /// <summary>
        /// 디스토션 기타·보컬을 흉내 낸 지속음(박과 무관): 0.4~1.2초마다 무작위로 바뀌는 음 하나(기본음 196~330Hz + 완전5도,
        /// 배음 6kHz까지, 진폭 1/k²), 5.5Hz 비브라토, 0.4Hz 트레몰로. drive &gt; 0이면 tanh로 부드럽게 클리핑한다. 최대 진폭은 약 1.
        /// 원음 플럭스로 보면 음 전환·비브라토가 박과 무관한 온셋을 많이 만든다.
        /// </summary>
        public static float[] SustainedTrack(int sampleRate, double seconds, uint seed, float drive)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (drive < 0f) throw new ArgumentOutOfRangeException(nameof(drive));

            int length = (int)Math.Round(seconds * sampleRate);
            var samples = new double[length];

            // 음 시작 시각(맨 끝 값은 곡 길이 이후)과 음높이를 이 순서대로 뽑는다
            uint state = seed ^ 0x5bd1e995u;
            var starts = new List<double> { 0.0 };
            double t = 0.0;
            while (t < seconds)
            {
                t += NoteMinSec + NoteSpanSec * NextUnit(ref state);
                starts.Add(t);
            }
            var pitches = new double[starts.Count];
            for (int i = 0; i < pitches.Length; i++)
                pitches[i] = NotePitchesHz[(int)(NextUnit(ref state) * NotePitchesHz.Length) % NotePitchesHz.Length];

            double ramp = NoteRampSec;
            for (int note = 0; note + 1 < starts.Count; note++)
            {
                double start = starts[note];
                double end = starts[note + 1];
                int from = Math.Max(0, (int)Math.Round((start - ramp / 2) * sampleRate));
                int to = Math.Min(length, (int)Math.Round((end + ramp / 2) * sampleRate));
                for (int i = from; i < to; i++)
                {
                    double time = (double)i / sampleRate;
                    double fadeIn = Math.Min(1.0, Math.Max(0.0, (time - (start - ramp / 2)) / ramp));
                    double fadeOut = Math.Min(1.0, Math.Max(0.0, ((end + ramp / 2) - time) / ramp));
                    double weight = 0.5 - 0.5 * Math.Cos(Math.PI * fadeIn * fadeOut);

                    double sum = 0.0;
                    for (int v = 0; v < VoiceRatios.Length; v++)
                    {
                        double f0 = pitches[note] * VoiceRatios[v];
                        // FM 비브라토: 순간 주파수 f0·(1 + depth·sin(2π·vibHz·t))를 적분한 위상
                        double phase = f0 * (time - VibratoDepth / (2.0 * Math.PI * VibratoHz) * Math.Cos(2.0 * Math.PI * VibratoHz * time));
                        sum += HarmonicSum(2.0 * Math.PI * phase, (int)(HarmonicMaxHz / f0));
                    }
                    samples[i] += sum * weight;
                }
            }

            double[] shaped = NormalizePeak(samples, 1.0, out double _);
            var result = new float[length];
            for (int i = 0; i < length; i++)
            {
                double x = shaped[i];
                if (drive > 0f) x = Math.Tanh(drive * x) / Math.Tanh(drive);
                double time = (double)i / sampleRate;
                result[i] = (float)(x * (1.0 + TremoloDepth * Math.Sin(2.0 * Math.PI * TremoloHz * time)));
            }
            return result;
        }

        /// <summary>a·gainA + b·gainB를 최대 진폭 0.9로 줄인다(클리핑 방지). 두 신호 길이가 같아야 한다.</summary>
        public static float[] Mix(float[] a, float gainA, float[] b, float gainB)
        {
            if (a.Length != b.Length) throw new ArgumentException("signals must have the same length");

            var mixed = new float[a.Length];
            float peak = 0f;
            for (int i = 0; i < mixed.Length; i++)
            {
                mixed[i] = a[i] * gainA + b[i] * gainB;
                peak = Math.Max(peak, Math.Abs(mixed[i]));
            }
            if (peak <= 0f) return mixed;
            float scale = (float)MixPeak / peak;
            for (int i = 0; i < mixed.Length; i++) mixed[i] *= scale;
            return mixed;
        }

        /// <summary>시드 상태를 한 걸음 옮겨 [0, 1) 값을 돌려준다(AnalysisTests의 NextUnit과 같은 선형 합동식).</summary>
        static double NextUnit(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return (state >> 8) / 16777216.0;
        }

        /// <summary>[-1, 1) 백색 노이즈 한 샘플.</summary>
        static double NextNoise(ref uint state)
        {
            return NextUnit(ref state) * 2.0 - 1.0;
        }

        /// <summary>Σ_{k=1..count} sin(k·theta) / k². sin 한 번과 점화식 sin((k+1)θ) = 2cosθ·sin(kθ) − sin((k−1)θ)로 계산한다.</summary>
        static double HarmonicSum(double theta, int count)
        {
            if (count < 1) return 0.0;
            double twoCos = 2.0 * Math.Cos(theta);
            double previous = 0.0;
            double current = Math.Sin(theta);
            double sum = current;
            for (int k = 2; k <= count; k++)
            {
                double next = twoCos * current - previous;
                previous = current;
                current = next;
                sum += current / ((double)k * k);
            }
            return sum;
        }

        static double[] NormalizePeak(double[] samples, double peak, out double original)
        {
            original = 0.0;
            for (int i = 0; i < samples.Length; i++) original = Math.Max(original, Math.Abs(samples[i]));
            if (original <= 0.0) return samples;
            double scale = peak / original;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
            return samples;
        }

        static float[] NormalizePeak(double[] samples, double peak)
        {
            double[] scaled = NormalizePeak(samples, peak, out double _);
            var result = new float[scaled.Length];
            for (int i = 0; i < result.Length; i++) result[i] = (float)scaled[i];
            return result;
        }

        /// <summary>burst 전체를 종류별 드럼 소리로 채운다(덮어쓴다).</summary>
        static void RenderDrum(double[] burst, int sampleRate, DrumKind kind, ref uint state)
        {
            switch (kind)
            {
                case DrumKind.Kick:
                    for (int i = 0; i < burst.Length; i++)
                    {
                        double t = (double)i / sampleRate;
                        // 순간 주파수 End + (Start − End)·exp(−t/sweep)를 적분한 위상
                        double phase = 2.0 * Math.PI * (KickEndHz * t + (KickStartHz - KickEndHz) * KickSweepSec * (1.0 - Math.Exp(-t / KickSweepSec)));
                        burst[i] = KickGain * Math.Sin(phase) * Math.Exp(-t / KickDecaySec)
                            + KickClickGain * NextNoise(ref state) * Math.Exp(-t / KickClickDecaySec);
                    }
                    break;
                case DrumKind.Snare:
                    for (int i = 0; i < burst.Length; i++) burst[i] = NextNoise(ref state);
                    BandPass(burst, sampleRate, SnareNoiseHz, SnareNoiseQ);
                    for (int i = 0; i < burst.Length; i++)
                    {
                        double t = (double)i / sampleRate;
                        burst[i] = SnareNoiseGain * burst[i] * Math.Exp(-t / SnareNoiseDecaySec)
                            + SnareBodyGain * Math.Sin(2.0 * Math.PI * SnareBodyHz * t) * Math.Exp(-t / SnareBodyDecaySec);
                    }
                    break;
                case DrumKind.HiHat:
                    for (int i = 0; i < burst.Length; i++) burst[i] = NextNoise(ref state);
                    BandPass(burst, sampleRate, HiHatNoiseHz, HiHatNoiseQ);
                    for (int i = 0; i < burst.Length; i++)
                        burst[i] = HiHatGain * burst[i] * Math.Exp(-((double)i / sampleRate) / HiHatDecaySec);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>2차 대역통과(RBJ, 중심에서 이득 q 무관 1). 제자리에서 걸고 상태는 0에서 시작한다.</summary>
        static void BandPass(double[] x, int sampleRate, double hz, double q)
        {
            double w0 = 2.0 * Math.PI * hz / sampleRate;
            double alpha = Math.Sin(w0) / (2.0 * q);
            double a0 = 1.0 + alpha;
            double b0 = alpha / a0;
            double a1 = -2.0 * Math.Cos(w0) / a0;
            double a2 = (1.0 - alpha) / a0;
            double x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                double input = x[i];
                double y = b0 * input - b0 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = input;
                y2 = y1; y1 = y;
                x[i] = y;
            }
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
