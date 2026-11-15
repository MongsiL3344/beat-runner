using System;

namespace NoiseRun
{
    /// <summary>
    /// 자기상관 BPM 추정과 박 위상 탐색(구현명세 5.5). 워커 스레드에서 돌므로 Unity API를 부르지 않는다.
    /// </summary>
    public static class BpmEstimator
    {
        /// <summary>
        /// strengthCurve = 전체 온셋 강도 곡선 o[m](OnsetDetector가 만든 대역 정규화 합, 프레임 간격 hopSec).
        /// 느린 변화 제거(o')는 여기서 한다. 그래서 상수·무음 곡선은 o'가 0이 되어 실패 경로로 간다.
        /// </summary>
        public static BpmResult Estimate(float[] strengthCurve, double hopSec, double songLength)
        {
            if (strengthCurve == null) throw new ArgumentNullException(nameof(strengthCurve));
            if (!(hopSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(hopSec));

            int m = strengthCurve.Length;
            if (m < 2) return Fallback(new double[m], hopSec, songLength, 0f);

            double[] op = Detrend(strengthCurve, hopSec);

            double r0 = 0.0;
            for (int i = 0; i < m; i++) r0 += op[i] * op[i];
            r0 /= m;
            if (!(r0 > 0.0)) return Fallback(op, hopSec, songLength, 0f);

            // 탐색 lag 범위. 보간에 r(L*±1)이 필요하므로 L ≤ M−2 안으로 자른다.
            int lMin = (int)Math.Floor(60.0 / (GameConfig.BpmMax * hopSec));
            int lMax = (int)Math.Ceiling(60.0 / (GameConfig.BpmMin * hopSec));
            if (lMin < 2) lMin = 2;
            if (lMax > m - 2) lMax = m - 2;
            if (lMax < lMin) return Fallback(op, hopSec, songLength, 0f);

            var r = new double[lMax + 2];
            for (int l = lMin - 1; l <= lMax + 1; l++) r[l] = Autocorr(op, l);

            int best = lMin;
            for (int l = lMin + 1; l <= lMax; l++)
                if (r[l] > r[best]) best = l;
            if (!(r[best] > 0.0)) return Fallback(op, hopSec, songLength, 0f);

            // 신뢰도는 반-lag 교체 전 L*로 판정한다. 교체 조건(r[half] ≥ BpmHalfLagRatio·r[L*])상
            // 교체 후 r는 최대 절반까지 낮아질 수 있어, 교체 뒤에 판정하면 멀쩡한 곡이 폴백으로 빠진다.
            float confidence = (float)(r[best] / r0);
            if (confidence < GameConfig.BpmConfidenceMin) return Fallback(op, hopSec, songLength, confidence);

            // 포물선 보간으로 lag의 소수 부분 δ를 구한다
            double delta = ParabolicDelta(r, best);

            // 옥타브 교정: 실제 박이 L̂/2에 있어도 2박 lag가 정수 격자에 더 잘 맞으면 L*가 2배로 잡힌다.
            // L̂/2 근처 정수 lag의 r가 충분히 크면(BpmHalfLagRatio) 그 lag를 L*로 바꾸고 다시 보간한다.
            double halfLag = (best + delta) * 0.5;
            int halfBest = -1;
            for (int cand = (int)Math.Floor(halfLag); cand <= (int)Math.Ceiling(halfLag); cand++)
            {
                if (cand < lMin || cand > lMax) continue;
                if (halfBest < 0 || r[cand] > r[halfBest]) halfBest = cand;
            }
            if (halfBest >= 0 && r[halfBest] >= GameConfig.BpmHalfLagRatio * r[best])
            {
                best = halfBest;
                delta = ParabolicDelta(r, best);
            }

            double bpm0 = 60.0 / ((best + delta) * hopSec);
            if (bpm0 < GameConfig.BpmMin) bpm0 = GameConfig.BpmMin;
            if (bpm0 > GameConfig.BpmMax) bpm0 = GameConfig.BpmMax;

            // 미세 보정: bpm0 ± BpmRefineRangePct를 BpmRefineStep 간격으로 훑으며 위상과 함께 최적점을 찾는다
            double lo = bpm0 * (1.0 - GameConfig.BpmRefineRangePct);
            double hi = bpm0 * (1.0 + GameConfig.BpmRefineRangePct);
            int steps = (int)Math.Floor((hi - lo) / GameConfig.BpmRefineStep) + 1;
            float bestBpm = (float)bpm0;
            double bestPhase = 0.0;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < steps; i++)
            {
                float candBpm = (float)(lo + i * (double)GameConfig.BpmRefineStep);
                double score = SearchPhase(op, hopSec, songLength, candBpm, out double phase);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBpm = candBpm;
                    bestPhase = phase;
                }
            }

            return new BpmResult { bpm = bestBpm, firstBeatTime = bestPhase, confidence = confidence, fallback = false };
        }

        /// <summary>BPM 추정 실패: FallbackBpm으로 고정하고 위상만 같은 방식으로 찾는다.</summary>
        static BpmResult Fallback(double[] op, double hopSec, double songLength, float confidence)
        {
            double phase = 0.0;
            if (op.Length >= 2) SearchPhase(op, hopSec, songLength, GameConfig.FallbackBpm, out phase);
            return new BpmResult
            {
                bpm = GameConfig.FallbackBpm,
                firstBeatTime = phase,
                confidence = confidence,
                fallback = true,
            };
        }

        /// <summary>o' = max(0, o − 이동평균(o, ±BpmDetrendHalfWindowSec)). 이동평균은 누적합으로 O(M).</summary>
        static double[] Detrend(float[] o, double hopSec)
        {
            int m = o.Length;
            int w = (int)Math.Round(GameConfig.BpmDetrendHalfWindowSec / hopSec);
            var prefix = new double[m + 1];
            for (int i = 0; i < m; i++) prefix[i + 1] = prefix[i] + o[i];

            var op = new double[m];
            for (int i = 0; i < m; i++)
            {
                int lo = i - w < 0 ? 0 : i - w;
                int hi = i + w > m - 1 ? m - 1 : i + w;
                double mean = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
                double v = o[i] - mean;
                op[i] = v > 0.0 ? v : 0.0;
            }
            return op;
        }

        /// <summary>r(L) = Σ o'[i]·o'[i+L] / (M − L).</summary>
        static double Autocorr(double[] op, int lag)
        {
            int count = op.Length - lag;
            if (count <= 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < count; i++) sum += op[i] * op[i + lag];
            return sum / count;
        }

        /// <summary>r(L*−1), r(L*), r(L*+1)을 지나는 포물선 꼭짓점의 lag 오프셋. 꼭짓점이 아니면 0.</summary>
        static double ParabolicDelta(double[] r, int best)
        {
            double a = r[best - 1], b = r[best], c = r[best + 1];
            // b가 극대가 아니면(탐색 경계의 비탈 등) 꼭짓점이 [best−1, best+1] 밖이라 보간하지 않는다. 이로써 |δ| ≤ 0.5.
            if (!(b >= a && b >= c)) return 0.0;
            double den = a - 2.0 * b + c;
            return den < 0.0 ? 0.5 * (a - c) / den : 0.0;
        }

        /// <summary>
        /// 박 간격 60/bpm에서 φ ∈ [0, T)를 hopSec/2 간격으로 훑어, 박 위치(φ + kT &lt; songLength)의
        /// o' 합(선형 보간)이 최대인 φ를 찾는다. 최대 합을 돌려준다.
        /// </summary>
        static double SearchPhase(double[] op, double hopSec, double songLength, float bpm, out double bestPhase)
        {
            double period = 60.0 / bpm;
            double phaseStep = hopSec * 0.5;
            int phaseCount = (int)Math.Ceiling(period / phaseStep);
            // 프레임 m의 시각 = (m + FftSize / (2·HopSize))·hopSec (창 중심)
            double frameOffset = (double)GameConfig.FftSize / (2.0 * GameConfig.HopSize);

            bestPhase = 0.0;
            double bestScore = double.NegativeInfinity;
            for (int p = 0; p < phaseCount; p++)
            {
                double phase = p * phaseStep;
                if (phase >= period) break;
                double score = 0.0;
                for (int k = 0; ; k++)
                {
                    double t = phase + k * period;
                    if (t >= songLength) break;
                    score += Interpolate(op, t / hopSec - frameOffset);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPhase = phase;
                }
            }
            return bestScore;
        }

        /// <summary>프레임 좌표 x(소수)에서 o'의 선형 보간. 범위 밖은 0.</summary>
        static double Interpolate(double[] op, double x)
        {
            if (x < 0.0) return 0.0;
            int i = (int)x;
            if (i >= op.Length) return 0.0;
            if (i == op.Length - 1) return op[i];
            double f = x - i;
            return op[i] * (1.0 - f) + op[i + 1] * f;
        }
    }
}
