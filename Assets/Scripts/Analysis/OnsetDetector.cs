using System;
using System.Collections.Generic;

namespace NoiseRun
{
    /// <summary>
    /// 모노 믹스다운, 대역별 스펙트럴 플럭스, 적응 임계값 피크 검출(구현명세 5.2–5.4).
    /// 워커 스레드에서 돌므로 Unity API를 부르지 않는다.
    /// </summary>
    public static class OnsetDetector
    {
        const int BandCount = 3;

        public const string FluxStage = "주파수 분석";
        public const string PeakStage = "피크 검출";

        /// <summary>5.2: 채널 평균. 1채널이면 입력 배열을 그대로 돌려준다(읽기 전용으로 쓸 것).</summary>
        public static float[] ToMono(float[] interleaved, int channels)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (channels == 1) return interleaved;

            int frames = interleaved.Length / channels;
            var mono = new float[frames];
            float inv = 1f / channels;
            for (int i = 0; i < frames; i++)
            {
                int baseIdx = i * channels;
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[baseIdx + c];
                mono[i] = sum * inv;
            }
            return mono;
        }

        /// <summary>5.3의 프레임 수 M = (샘플 수 − N) / hop + 1. 샘플이 N보다 적으면 0.</summary>
        public static int FrameCount(int sampleCount)
        {
            if (sampleCount < GameConfig.FftSize) return 0;
            return (sampleCount - GameConfig.FftSize) / GameConfig.HopSize + 1;
        }

        /// <summary>5.3의 프레임 m 시각(창 중심, 초).</summary>
        public static double FrameTime(int m, int sampleRate)
        {
            return ((long)m * GameConfig.HopSize + GameConfig.FftSize / 2) / (double)sampleRate;
        }

        /// <summary>
        /// 대역별 플럭스·임계값·온셋과 전체 강도 곡선(대역 정규화 합)을 만든다.
        /// 프레임이 없으면(M ≤ 0) 빈 결과를 돌려준다. progress는 null이어도 된다.
        /// </summary>
        public static OnsetResult Detect(float[] mono, int sampleRate, AnalysisProgress progress)
        {
            if (mono == null) throw new ArgumentNullException(nameof(mono));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int n = GameConfig.FftSize;
            int hop = GameConfig.HopSize;
            int half = n / 2;
            int m = FrameCount(mono.Length);
            double hopSec = (double)hop / sampleRate;

            var flux = new float[BandCount][];
            var thr = new float[BandCount][];
            for (int b = 0; b < BandCount; b++)
            {
                flux[b] = new float[m];
                thr[b] = new float[m];
            }
            var result = new OnsetResult
            {
                bandFlux = flux,
                bandThreshold = thr,
                strengthCurve = new float[m],
                onsets = new List<Onset>(),
                hopSec = hopSec,
            };
            if (m <= 0) return result;

            // ── 대역 bin 범위(5.3) ──
            var kLo = new int[BandCount];
            var kHi = new int[BandCount];
            // sr < 2·HighBandHzMax이면 SetBand가 kHi를 나이퀴스트 bin(half)으로 자른다(명세 5.3).
            SetBand(kLo, kHi, (int)Band.Low, GameConfig.LowBandHzMin, GameConfig.LowBandHzMax, sampleRate, half);
            SetBand(kLo, kHi, (int)Band.Mid, GameConfig.MidBandHzMin, GameConfig.MidBandHzMax, sampleRate, half);
            SetBand(kLo, kHi, (int)Band.High, GameConfig.HighBandHzMin, GameConfig.HighBandHzMax, sampleRate, half);

            // ── FFT·플럭스(0.05–0.80) ──
            if (progress != null)
            {
                progress.stage = FluxStage;
                progress.value = GameConfig.AnalysisProgressMonoEnd;
            }
            var fft = new Fft(n);
            float[] window = Fft.Hann(n);
            var re = new float[n];
            var im = new float[n];
            var mag = new float[half + 1];
            var prevMag = new float[half + 1];

            for (int f = 0; f < m; f++)
            {
                int offset = f * hop;
                for (int i = 0; i < n; i++)
                {
                    re[i] = mono[offset + i] * window[i];
                    im[i] = 0f;
                }
                fft.Forward(re, im);
                for (int k = 0; k <= half; k++)
                    mag[k] = (float)Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]);

                for (int b = 0; b < BandCount; b++)
                {
                    float sum = 0f;
                    for (int k = kLo[b]; k <= kHi[b]; k++)
                    {
                        float d = mag[k] - prevMag[k];
                        if (d > 0f) sum += d;
                    }
                    flux[b][f] = sum;
                }

                // 다음 프레임의 prevMag = 이번 mag(배열 교체)
                float[] swap = prevMag;
                prevMag = mag;
                mag = swap;

                if (progress != null && f % GameConfig.AnalysisProgressFrameInterval == 0)
                    progress.value = GameConfig.AnalysisProgressMonoEnd
                        + (GameConfig.AnalysisProgressFluxEnd - GameConfig.AnalysisProgressMonoEnd) * f / m;
            }

            // ── 적응 임계값·피크(0.80–0.85) + 강도 곡선 ──
            if (progress != null)
            {
                progress.stage = PeakStage;
                progress.value = GameConfig.AnalysisProgressFluxEnd;
            }
            int w = (int)Math.Round(GameConfig.ThresholdHalfWindowSec / hopSec);
            var prefix = new double[m + 1];
            var curve = result.strengthCurve;

            for (int b = 0; b < BandCount; b++)
            {
                float[] fl = flux[b];
                float[] th = thr[b];

                prefix[0] = 0.0;
                for (int i = 0; i < m; i++) prefix[i + 1] = prefix[i] + fl[i];
                double globalMean = prefix[m] / m;
                float floor = (float)(GameConfig.ThresholdFloorRatio * globalMean);

                for (int i = 0; i < m; i++)
                {
                    int lo = i - w < 0 ? 0 : i - w;
                    int hi = i + w > m - 1 ? m - 1 : i + w;
                    double mean = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
                    float t = (float)(mean * GameConfig.ThresholdMultiplier);
                    th[i] = t < floor ? floor : t;
                }

                for (int i = 0; i < m; i++)
                {
                    float v = fl[i];
                    if (!(v > th[i])) continue;
                    // 경계 프레임은 없는 이웃을 조건에서 뺀다
                    if (i > 0 && v < fl[i - 1]) continue;
                    if (i < m - 1 && v <= fl[i + 1]) continue;
                    result.onsets.Add(new Onset
                    {
                        time = FrameTime(i, sampleRate),
                        band = (Band)b,
                        strength = v / th[i],
                    });
                }

                // 5.5: 대역별 전체 평균으로 정규화해 합친다(평균 0인 무음 대역은 건너뜀)
                if (globalMean > 0.0)
                {
                    float inv = (float)(1.0 / globalMean);
                    for (int i = 0; i < m; i++) curve[i] += fl[i] * inv;
                }

                if (progress != null)
                    progress.value = GameConfig.AnalysisProgressFluxEnd
                        + (GameConfig.AnalysisProgressPeakEnd - GameConfig.AnalysisProgressFluxEnd) * (b + 1) / BandCount;
            }

            // 시간순(같은 시각이면 대역 순)
            result.onsets.Sort((x, y) =>
            {
                int c = x.time.CompareTo(y.time);
                return c != 0 ? c : ((int)x.band).CompareTo((int)y.band);
            });
            return result;
        }

        static void SetBand(int[] kLo, int[] kHi, int band, double fLo, double fHi, int sampleRate, int half)
        {
            int lo = (int)Math.Ceiling(fLo * GameConfig.FftSize / sampleRate);
            int hi = (int)Math.Floor(fHi * GameConfig.FftSize / sampleRate);
            kLo[band] = lo;
            kHi[band] = hi > half ? half : hi;
        }
    }
}
