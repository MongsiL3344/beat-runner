using System;
using System.Collections.Generic;

namespace NoiseRun
{
    /// <summary>
    /// 모노 믹스다운, 대역별 스펙트럴 플럭스(원음·HPSS 타악기), 적응 임계값 피크 검출(구현명세 5.2–5.4).
    /// 곡 전체 타악기 비중이 PercussiveRatioMin 이상이면 타악기 플럭스를, 미만이면 원음 플럭스를 쓴다.
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

            var mixFlux = new float[BandCount][];
            var percFlux = new float[BandCount][];
            var thr = new float[BandCount][];
            for (int b = 0; b < BandCount; b++)
            {
                mixFlux[b] = new float[m];
                percFlux[b] = new float[m];
                thr[b] = new float[m];
            }
            var result = new OnsetResult
            {
                bandFlux = mixFlux,
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

            // ── FFT·원음 플럭스·HPSS 타악기 플럭스(0.05–0.80) ──
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
            var hpss = new Hpss(m, half, kLo, kHi, percFlux);

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
                    mixFlux[b][f] = sum;
                }
                hpss.Push(mag);

                // 다음 프레임의 prevMag = 이번 mag(배열 교체)
                float[] swap = prevMag;
                prevMag = mag;
                mag = swap;

                if (progress != null && f % GameConfig.AnalysisProgressFrameInterval == 0)
                    progress.value = GameConfig.AnalysisProgressMonoEnd
                        + (GameConfig.AnalysisProgressFluxEnd - GameConfig.AnalysisProgressMonoEnd) * f / m;
            }
            hpss.Flush();

            // 드럼이 거의 없는 곡(타악기 비중 < PercussiveRatioMin)은 원음 플럭스로 되돌린다
            result.percussiveRatio = (float)hpss.Ratio;
            result.percussive = result.percussiveRatio >= GameConfig.PercussiveRatioMin;
            float[][] flux = result.percussive ? percFlux : mixFlux;
            result.bandFlux = flux;

            // ── 적응 임계값·피크(0.80–0.85) + 강도 곡선 ──
            if (progress != null)
            {
                progress.stage = PeakStage;
                progress.value = GameConfig.AnalysisProgressFluxEnd;
            }
            int w = (int)Math.Round(GameConfig.ThresholdHalfWindowSec / hopSec);
            // 타악기 플럭스는 지속음을 걷어 내도 잔여 출렁임이 상대 기준 피크로 잡히므로 배수를 따로 둔다
            double multiplier = result.percussive ? GameConfig.PercussiveThresholdMultiplier : GameConfig.ThresholdMultiplier;
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
                    float t = (float)(mean * multiplier);
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

        /// <summary>
        /// HPSS(5.3): 화성 H = 시간 방향 미디언(HpssHarmonicFrames), 타악기 P = 주파수 방향 미디언(HpssPercussiveBins),
        /// 타악기 크기 = mag × P²/(P²+H²)(소프트 마스크, P²+H² = 0이면 0). 그 크기로 대역 플럭스를 만든다.
        /// 시간 미디언은 중심 프레임 뒤 반폭이 필요해서 반폭 프레임만큼 늦게 확정한다. 링 버퍼만 두고 전체 스펙트로그램은 저장하지 않는다.
        /// 미디언 창은 배열 경계(프레임 0..M−1, bin 0..N/2)에서 자른다.
        /// </summary>
        sealed class Hpss
        {
            readonly int frameCount;
            readonly int half;
            readonly int binCount;
            readonly int timeLen;
            readonly int timeHalf;
            readonly int freqHalf;
            readonly int[] kLo;
            readonly int[] kHi;
            readonly int ratioLo;
            readonly float[][] flux;
            readonly float[][] ringMag;
            readonly float[][] ringPerc;
            readonly SortedWindow[] timeWindow;
            readonly SortedWindow freqWindow;
            float[] percMag;
            float[] prevPercMag;
            int pushed;
            int finalized;
            double sumPerc;
            double sumAll;

            /// <summary>flux[b]는 길이 frameCount. 타악기 비중은 bin kLo[Low]..max(kHi)에서 잰다.</summary>
            public Hpss(int frameCount, int half, int[] kLo, int[] kHi, float[][] flux)
            {
                this.frameCount = frameCount;
                this.half = half;
                this.kLo = kLo;
                this.kHi = kHi;
                this.flux = flux;
                int top = 0;
                for (int b = 0; b < kHi.Length; b++) if (kHi[b] > top) top = kHi[b];
                binCount = top + 1;
                ratioLo = kLo[(int)Band.Low];
                timeLen = GameConfig.HpssHarmonicFrames;
                timeHalf = timeLen / 2;
                freqHalf = GameConfig.HpssPercussiveBins / 2;

                ringMag = new float[timeLen][];
                ringPerc = new float[timeLen][];
                for (int i = 0; i < timeLen; i++)
                {
                    ringMag[i] = new float[binCount];
                    ringPerc[i] = new float[binCount];
                }
                timeWindow = new SortedWindow[binCount];
                for (int k = 0; k < binCount; k++) timeWindow[k] = new SortedWindow(timeLen);
                freqWindow = new SortedWindow(2 * freqHalf + 1);
                percMag = new float[binCount];
                prevPercMag = new float[binCount];
            }

            /// <summary>곡 전체 ΣP²/Σ(P²+H²). Flush 뒤에 읽는다.</summary>
            public double Ratio => sumAll > 0.0 ? sumPerc / sumAll : 0.0;

            /// <summary>프레임 하나의 크기 스펙트럼(0..half)을 넣는다. 반폭 전 프레임이 확정된다. mag는 복사하므로 호출자가 다시 써도 된다.</summary>
            public void Push(float[] mag)
            {
                int f = pushed++;
                int slot = f % timeLen;
                float[] rm = ringMag[slot];
                float[] rp = ringPerc[slot];

                // 링에서 밀려나는 프레임 f − timeLen을 시간 창에서 뺀다
                if (f >= timeLen)
                    for (int k = 0; k < binCount; k++) timeWindow[k].Remove(rm[k]);

                // 주파수 방향 미디언: bin k 창 = [k − freqHalf, k + freqHalf] ∩ [0, half]
                freqWindow.Clear();
                int first = Math.Min(half, freqHalf);
                for (int k = 0; k <= first; k++) freqWindow.Insert(mag[k]);
                for (int k = 0; k < binCount; k++)
                {
                    rp[k] = freqWindow.Median();
                    if (k + 1 == binCount) break;
                    int add = k + 1 + freqHalf;
                    int drop = k - freqHalf;
                    if (drop >= 0) freqWindow.Remove(mag[drop]); // 먼저 빼야 용량(2·freqHalf+1)을 넘지 않는다
                    if (add <= half) freqWindow.Insert(mag[add]);
                }

                for (int k = 0; k < binCount; k++)
                {
                    rm[k] = mag[k];
                    timeWindow[k].Insert(mag[k]);
                }

                // 시간 창이 [f − 2·timeHalf, f]이면 중심 프레임 f − timeHalf가 확정된다
                if (f >= timeHalf) Finalize(f - timeHalf);
            }

            /// <summary>모든 프레임을 넣은 뒤 남은 끝 프레임(창이 뒤쪽에서 잘림)을 확정한다.</summary>
            public void Flush()
            {
                for (int c = finalized; c < frameCount; c++)
                {
                    // 이 프레임 창에서 빠지는 c − timeHalf − 1. Push 단계에서는 아직 빠지지 않았고 링에 남아 있다
                    int drop = c - timeHalf - 1;
                    if (drop >= 0)
                    {
                        float[] rm = ringMag[drop % timeLen];
                        for (int k = 0; k < binCount; k++) timeWindow[k].Remove(rm[k]);
                    }
                    Finalize(c);
                }
            }

            void Finalize(int c)
            {
                int slot = c % timeLen;
                float[] rm = ringMag[slot];
                float[] rp = ringPerc[slot];
                for (int k = 0; k < binCount; k++)
                {
                    double p = rp[k];
                    double h = timeWindow[k].Median();
                    double p2 = p * p;
                    double total = p2 + h * h;
                    if (k >= ratioLo)
                    {
                        sumPerc += p2;
                        sumAll += total;
                    }
                    percMag[k] = total > 0.0 ? (float)(rm[k] * p2 / total) : 0f;
                }

                for (int b = 0; b < flux.Length; b++)
                {
                    float sum = 0f;
                    for (int k = kLo[b]; k <= kHi[b]; k++)
                    {
                        float d = percMag[k] - prevPercMag[k];
                        if (d > 0f) sum += d;
                    }
                    flux[b][c] = sum;
                }

                float[] swap = prevPercMag;
                prevPercMag = percMag;
                percMag = swap;
                finalized = c + 1;
            }
        }

        /// <summary>정렬된 고정 용량 창(미디언용). 삽입·삭제 O(용량). 순서는 float.CompareTo(NaN이 가장 작음)로 BinarySearch와 맞춘다.</summary>
        sealed class SortedWindow
        {
            readonly float[] values;
            int count;

            public SortedWindow(int capacity)
            {
                values = new float[capacity];
            }

            public void Clear()
            {
                count = 0;
            }

            public void Insert(float x)
            {
                int i = count;
                while (i > 0 && values[i - 1].CompareTo(x) > 0)
                {
                    values[i] = values[i - 1];
                    i--;
                }
                values[i] = x;
                count++;
            }

            /// <summary>같은 값 하나를 뺀다. 넣은 적 없는 값이면 InvalidOperationException(호출 순서 버그).</summary>
            public void Remove(float x)
            {
                int i = Array.BinarySearch(values, 0, count, x);
                if (i < 0) throw new InvalidOperationException("SortedWindow: 없는 값 제거");
                Array.Copy(values, i + 1, values, i, count - i - 1);
                count--;
            }

            /// <summary>개수가 짝수면 가운데 두 값의 평균. 비어 있으면 호출하지 않는다.</summary>
            public float Median()
            {
                int mid = count >> 1;
                return (count & 1) == 1 ? values[mid] : 0.5f * (values[mid - 1] + values[mid]);
            }
        }
    }
}
