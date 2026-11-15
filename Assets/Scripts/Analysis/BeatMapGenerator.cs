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

        /// <summary>노트 후보 하나(5.6 루프 결과: 칸마다 점수가 가장 높은 온셋).</summary>
        public struct NoteCandidate
        {
            public int slotIndex;
            public double score;
            public ObstacleKind kind;
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
        /// 5.6: 온셋을 반박자 칸으로 정렬하고(리드인·끝 제외) 칸당 점수가 가장 높은 온셋 하나를 후보로 만든다.
        /// 점수 = strength × 대역 비중 가중치. slotIndex 오름차순. 난이도 무관.
        /// </summary>
        public static List<NoteCandidate> Candidates(OnsetResult o, BpmResult b, double songLength)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (b == null) throw new ArgumentNullException(nameof(b));
            var grid = new BeatMap { bpm = b.bpm, firstBeatTime = b.firstBeatTime, songLength = songLength };

            double lastAllowed = songLength - GameConfig.EndEmptySec;
            double[] bandWeight = BandWeights(o);
            var best = new Dictionary<int, NoteCandidate>();
            foreach (Onset onset in o.onsets)
            {
                int idx = grid.NearestSlot(onset.time);
                double t = grid.SlotTime(idx);
                if (t < GameConfig.LeadInEmptySec || t > lastAllowed) continue;
                double score = onset.strength * bandWeight[(int)onset.band];
                if (!best.TryGetValue(idx, out NoteCandidate cur) || score > cur.score)
                    best[idx] = new NoteCandidate { slotIndex = idx, score = score, kind = BandToKind(onset.band) };
            }

            var list = new List<NoteCandidate>(best.Values);
            list.Sort((x, y) => x.slotIndex.CompareTo(y.slotIndex));
            return list;
        }

        /// <summary>
        /// 5.6 후보 → 5.7 난이도 선별: 점수 내림차순(동점은 slotIndex 오름차순)으로 KeepCount개를 남기고
        /// slotIndex 오름차순으로 노트를 만든다. (E1 조합은 아직 구현하지 않음.)
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

        static ObstacleKind BandToKind(Band band)
        {
            switch (band)
            {
                case Band.Low: return ObstacleKind.Box;
                case Band.High: return ObstacleKind.Laundry;
                default: return ObstacleKind.Door;
            }
        }

        /// <summary>
        /// 칸 안 대역 비교용 가중치 = (대역 평균 플럭스 / 세 대역 평균 합)^OnsetBandShareExponent.
        /// strength(= 플럭스 / 임계값)는 스케일이 없어서, 클릭이 다른 대역으로 새면 누설 대역과 거의 동률이 된다.
        /// 대역의 에너지 비중으로 가중하면 실제 소리가 난 대역이 이긴다. 지수 0이면 가중치 1(명세 원안).
        /// </summary>
        static double[] BandWeights(OnsetResult o)
        {
            var weight = new double[] { 1.0, 1.0, 1.0 };
            if (GameConfig.OnsetBandShareExponent == 0f || o.bandFlux == null || o.bandFlux.Length != weight.Length) return weight;

            var mean = new double[weight.Length];
            double total = 0.0;
            for (int b = 0; b < weight.Length; b++)
            {
                float[] flux = o.bandFlux[b];
                if (flux == null || flux.Length == 0) continue;
                double sum = 0.0;
                for (int i = 0; i < flux.Length; i++) sum += flux[i];
                mean[b] = sum / flux.Length;
                total += mean[b];
            }
            if (!(total > 0.0)) return weight;
            for (int b = 0; b < weight.Length; b++)
                weight[b] = Math.Pow(mean[b] / total, GameConfig.OnsetBandShareExponent);
            return weight;
        }
    }
}
