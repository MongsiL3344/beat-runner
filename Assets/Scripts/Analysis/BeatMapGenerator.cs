using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NoiseRun
{
    /// <summary>
    /// 분석 전체 조립(모노 → 온셋 → BPM·위상 → 반박자 정렬·장애물 매핑, 구현명세 5절).
    /// 분석 불가는 AnalysisFailedException으로 Task를 실패시킨다(Task.Exception.InnerException).
    /// 워커 스레드에서 돌므로 Unity API를 부르지 않는다.
    /// </summary>
    public static class BeatMapGenerator
    {
        /// <summary>분석 불가 때 화면에 그대로 띄우는 문구(구현명세 5.5).</summary>
        public const string NoRhythmMessage = "리듬을 찾을 수 없는 곡입니다. 다른 곡을 선택하세요";

        const string MonoStage = "모노 변환";
        const string BpmStage = "BPM 계산";
        const string MapStage = "맵 생성";

        /// <summary>BeatMap만 필요한 호출자용(명세 4.2). 자세한 결과는 AnalyzeDetailedAsync.</summary>
        public static Task<BeatMap> AnalyzeAsync(float[] interleaved, int channels, int sampleRate, double songLength, AnalysisProgress progress, Difficulty difficulty)
        {
            return Task.Run(() => Run(interleaved, channels, sampleRate, songLength, progress, difficulty).map);
        }

        /// <summary>
        /// 워커 스레드에서 분석한다. 성공하면 progress.value = 1.
        /// interleaved는 분석이 끝날 때까지 바꾸지 말 것. 난이도는 마지막 노트 선별(5.7)에만 쓴다.
        /// </summary>
        public static Task<AnalysisOutput> AnalyzeDetailedAsync(float[] interleaved, int channels, int sampleRate, double songLength, AnalysisProgress progress, Difficulty difficulty)
        {
            return Task.Run(() => Run(interleaved, channels, sampleRate, songLength, progress, difficulty));
        }

        static AnalysisOutput Run(float[] interleaved, int channels, int sampleRate, double songLength, AnalysisProgress progress, Difficulty difficulty)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            if (progress == null) progress = new AnalysisProgress();
            var watch = Stopwatch.StartNew();

            if (songLength < GameConfig.MinSongLengthSec) throw new AnalysisFailedException(NoRhythmMessage);

            progress.stage = MonoStage;
            progress.value = 0f;
            float[] mono = OnsetDetector.ToMono(interleaved, channels);
            progress.value = GameConfig.AnalysisProgressMonoEnd;

            if (OnsetDetector.FrameCount(mono.Length) <= 0) throw new AnalysisFailedException(NoRhythmMessage);

            OnsetResult onsets = OnsetDetector.Detect(mono, sampleRate, progress);
            if (onsets.onsets.Count < GameConfig.MinOnsetCount) throw new AnalysisFailedException(NoRhythmMessage);

            progress.stage = BpmStage;
            progress.value = GameConfig.AnalysisProgressPeakEnd;
            BpmResult bpm = BpmEstimator.Estimate(onsets.strengthCurve, onsets.hopSec, songLength);

            progress.stage = MapStage;
            progress.value = GameConfig.AnalysisProgressBpmEnd;
            BeatMap map = Generate(onsets, bpm, songLength, difficulty);

            progress.value = 1f;
            watch.Stop();
            return new AnalysisOutput { map = map, onsets = onsets, bpm = bpm, elapsedSec = watch.Elapsed.TotalSeconds };
        }

        /// <summary>노트 후보 하나(5.6 루프 결과: 칸마다 점수가 가장 높은 온셋, 5.6 에너지 기준을 통과한 칸만).</summary>
        public struct NoteCandidate
        {
            public int slotIndex;
            /// <summary>칸 안 온셋 점수 = strength × 대역 비중(종류 결정·같은 박 묶음 안 순위).</summary>
            public double score;
            /// <summary>칸 시각 ±창의 대역별 최대 플럭스 / 그 대역 전체 평균 중 최댓값(후보 선별, 5.6 B).</summary>
            public double energy;
            public ObstacleKind kind;
            /// <summary>정박 칸(firstBeatTime에서 짝수 번째 반박자).</summary>
            public bool OnBeat => (slotIndex & 1) == 0;
        }

        /// <summary>난이도별 노트 비율(5.7, GameConfig).</summary>
        public static double NoteRatio(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return GameConfig.DifficultyNoteRatioEasy;
                case Difficulty.Normal: return GameConfig.DifficultyNoteRatioNormal;
                default: return GameConfig.DifficultyNoteRatioHard;
            }
        }

        /// <summary>남길 노트 수 = round(비율 × 후보 수). .5는 0에서 먼 쪽으로 올린다(MidpointRounding.AwayFromZero).</summary>
        public static int KeepCount(int candidateCount, Difficulty difficulty)
        {
            int keep = (int)Math.Round(NoteRatio(difficulty) * candidateCount, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(candidateCount, keep));
        }

        /// <summary>
        /// 5.6: 온셋을 반박자 칸으로 정렬하고 후보 칸을 만든다. 난이도 무관. slotIndex 오름차순.
        /// A) 칸 시각에서 ±SlotOnsetWindowRatio × 반박자 안의 온셋만 인정한다(멀리 있는 소리를 칸으로 끌어오지 않음).
        /// 칸마다 점수(strength × 대역 비중)가 가장 높은 온셋의 대역으로 종류를 정한다.
        /// B) 칸 시각 ±같은 창의 절대 에너지(대역 플럭스 / 대역 전체 평균의 최댓값) 상위 CandidateEnergyRatio만 남긴다(동점은 slotIndex 오름차순).
        /// </summary>
        public static List<NoteCandidate> Candidates(OnsetResult o, BpmResult b, double songLength)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (b == null) throw new ArgumentNullException(nameof(b));
            var grid = new BeatMap { bpm = b.bpm, firstBeatTime = b.firstBeatTime, songLength = songLength };

            double lastAllowed = songLength - GameConfig.EndEmptySec;
            double window = GameConfig.SlotOnsetWindowRatio * grid.HalfBeatSec;
            double[] bandMean = BandMeans(o);
            double[] bandWeight = BandWeights(bandMean);
            var best = new Dictionary<int, NoteCandidate>();
            foreach (Onset onset in o.onsets)
            {
                int idx = grid.NearestSlot(onset.time);
                double t = grid.SlotTime(idx);
                if (t < GameConfig.LeadInEmptySec || t > lastAllowed) continue;
                if (Math.Abs(onset.time - t) > window) continue;
                double score = onset.strength * bandWeight[(int)onset.band];
                if (!best.TryGetValue(idx, out NoteCandidate cur) || score > cur.score)
                    best[idx] = new NoteCandidate { slotIndex = idx, score = score, kind = BandToKind(onset.band) };
            }

            var list = new List<NoteCandidate>(best.Count);
            foreach (NoteCandidate c in best.Values)
            {
                NoteCandidate withEnergy = c;
                withEnergy.energy = SlotEnergy(o, bandMean, grid.SlotTime(c.slotIndex), window);
                list.Add(withEnergy);
            }

            list.Sort((x, y) =>
            {
                int e = y.energy.CompareTo(x.energy);
                return e != 0 ? e : x.slotIndex.CompareTo(y.slotIndex);
            });
            int keep = (int)Math.Round(GameConfig.CandidateEnergyRatio * list.Count, MidpointRounding.AwayFromZero);
            list.RemoveRange(keep, list.Count - keep);
            list.Sort((x, y) => x.slotIndex.CompareTo(y.slotIndex));
            return list;
        }

        /// <summary>
        /// 5.6 후보 → 5.7 난이도 선별(D): 정박 칸을 먼저, 그다음 엇박 칸. 같은 묶음 안에서는 점수 내림차순(동점은 slotIndex 오름차순).
        /// 앞에서 KeepCount개를 남기고 slotIndex 오름차순으로 노트를 만든다. (E1 조합은 아직 구현하지 않음.)
        /// </summary>
        public static BeatMap Generate(OnsetResult o, BpmResult b, double songLength, Difficulty difficulty)
        {
            List<NoteCandidate> candidates = Candidates(o, b, songLength);

            var map = new BeatMap
            {
                bpm = b.bpm,
                firstBeatTime = b.firstBeatTime,
                songLength = songLength,
                bpmFallbackUsed = b.fallback,
                difficulty = difficulty,
                notes = new List<Note>(),
            };

            var ranked = new List<NoteCandidate>(candidates);
            ranked.Sort((x, y) =>
            {
                if (x.OnBeat != y.OnBeat) return x.OnBeat ? -1 : 1;
                int c = y.score.CompareTo(x.score);
                return c != 0 ? c : x.slotIndex.CompareTo(y.slotIndex);
            });
            int keep = KeepCount(ranked.Count, difficulty);
            var kept = ranked.GetRange(0, keep);
            kept.Sort((x, y) => x.slotIndex.CompareTo(y.slotIndex));

            foreach (NoteCandidate c in kept)
                map.notes.Add(new Note { time = map.SlotTime(c.slotIndex), kind = c.kind, slotIndex = c.slotIndex });
            return map;
        }

        /// <summary>칸 시각 t ±window 안의 대역별 최대 플럭스를 그 대역 전체 평균으로 나눈 값 중 최댓값(5.6 B). 프레임 시각은 OnsetDetector.FrameTime과 같은 정의.</summary>
        static double SlotEnergy(OnsetResult o, double[] bandMean, double t, double window)
        {
            if (o.bandFlux == null || !(o.hopSec > 0.0)) return 0.0;
            double frame0 = GameConfig.FftSize / 2 * o.hopSec / GameConfig.HopSize; // 창 중심 오프셋(N/2 샘플)
            int from = (int)Math.Ceiling((t - window - frame0) / o.hopSec);
            int to = (int)Math.Floor((t + window - frame0) / o.hopSec);
            double energy = 0.0;
            for (int band = 0; band < o.bandFlux.Length; band++)
            {
                float[] flux = o.bandFlux[band];
                if (flux == null || !(bandMean[band] > 0.0)) continue;
                int lo = Math.Max(0, from), hi = Math.Min(flux.Length - 1, to);
                double peak = 0.0;
                for (int m = lo; m <= hi; m++) if (flux[m] > peak) peak = flux[m];
                energy = Math.Max(energy, peak / bandMean[band]);
            }
            return energy;
        }

        static ObstacleKind BandToKind(Band band)
        {
            switch (band)
            {
                case Band.Low: return ObstacleKind.Box;
                case Band.High: return ObstacleKind.Laundry;
                default: return ObstacleKind.Door;
            }
        }

        /// <summary>대역별 전체 평균 플럭스(Low, Mid, High). 플럭스가 없으면 0.</summary>
        static double[] BandMeans(OnsetResult o)
        {
            var mean = new double[3];
            if (o.bandFlux == null) return mean;
            for (int b = 0; b < mean.Length && b < o.bandFlux.Length; b++)
            {
                float[] flux = o.bandFlux[b];
                if (flux == null || flux.Length == 0) continue;
                double sum = 0.0;
                for (int i = 0; i < flux.Length; i++) sum += flux[i];
                mean[b] = sum / flux.Length;
            }
            return mean;
        }

        /// <summary>
        /// 칸 안 대역 비교용 가중치 = (대역 평균 플럭스 / 세 대역 평균 합)^OnsetBandShareExponent.
        /// strength(= 플럭스 / 임계값)는 스케일이 없어서, 클릭이 다른 대역으로 새면 누설 대역과 거의 동률이 된다.
        /// 대역의 에너지 비중으로 가중하면 실제 소리가 난 대역이 이긴다. 지수 0이면 가중치 1(명세 원안).
        /// </summary>
        static double[] BandWeights(double[] mean)
        {
            var weight = new double[] { 1.0, 1.0, 1.0 };
            double total = 0.0;
            for (int b = 0; b < mean.Length; b++) total += mean[b];
            if (!(total > 0.0)) return weight;
            for (int b = 0; b < weight.Length; b++)
                weight[b] = Math.Pow(mean[b] / total, GameConfig.OnsetBandShareExponent);
            return weight;
        }
    }
}
