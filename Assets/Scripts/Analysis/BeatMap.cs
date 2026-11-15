using System;
using System.Collections.Generic;

namespace NoiseRun
{
    /// <summary>온셋 대역. 저음 → 택배 상자, 중음 → 방화문, 고음 → 빨래.</summary>
    public enum Band { Low, Mid, High }

    /// <summary>장애물 종류. 조합(E1)은 플래그 OR.</summary>
    [Flags] public enum ObstacleKind { None = 0, Box = 1, Laundry = 2, Door = 4 }

    /// <summary>Space, ↓, →.</summary>
    public enum ActionType { Jump, Slide, Wave }

    /// <summary>OffBeat = 헛박자, EmptyBeat = 격자 안·장애물 없음.</summary>
    public enum Judgement { Perfect, Good, Miss, OffBeat, EmptyBeat }

    /// <summary>쉬움·보통·어려움(5.7). 난이도별로 남길 장애물 칸 비율이 다르다.</summary>
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>strength = 플럭스 / 임계값(≥1).</summary>
    public struct Onset { public double time; public Band band; public float strength; }

    public sealed class Note
    {
        /// <summary>정렬된 칸의 시각(초).</summary>
        public double time;
        /// <summary>Box/Laundry/Door, E1이면 조합.</summary>
        public ObstacleKind kind;
        /// <summary>반박자 칸 인덱스.</summary>
        public int slotIndex;
        public bool judged;
        /// <summary>E1 조합 입력 중간 상태.</summary>
        public ActionType pendingFirst;
        public bool hasPending;
    }

    public sealed class BeatMap
    {
        public float bpm;
        /// <summary>첫 정박 시각(초), 0 ≤ firstBeatTime &lt; 박 길이.</summary>
        public double firstBeatTime;
        public double HalfBeatSec => 30.0 / bpm;
        public double songLength;
        public bool bpmFallbackUsed;
        /// <summary>이 맵을 만든 난이도(5.7).</summary>
        public Difficulty difficulty;
        /// <summary>시간순.</summary>
        public List<Note> notes;
        public double SlotTime(int idx) => firstBeatTime + idx * HalfBeatSec;
        public int NearestSlot(double t) => (int)Math.Round((t - firstBeatTime) / HalfBeatSec);
    }

    public sealed class OnsetResult
    {
        public float[][] bandFlux;
        public float[][] bandThreshold;
        public float[] strengthCurve;
        public List<Onset> onsets;
        public double hopSec;
        /// <summary>HPSS 타악기 플럭스로 분석했으면 true, 원음 플럭스(드럼이 거의 없는 곡)면 false.</summary>
        public bool percussive;
        /// <summary>곡 전체 타악기 비중 ΣP²/Σ(P²+H²)(5.3). 프레임이 없으면 0.</summary>
        public float percussiveRatio;
    }

    public sealed class BpmResult
    {
        public float bpm;
        public double firstBeatTime;
        public float confidence;
        public bool fallback;
    }

    /// <summary>워커가 쓰고 메인이 읽는다.</summary>
    public sealed class AnalysisProgress
    {
        public volatile float value;
        public volatile string stage;
    }

    /// <summary>
    /// 분석 전체 결과(디버그 화면용). 명세 4.2의 AnalyzeAsync는 BeatMap만 돌려주므로
    /// 온셋·BPM 결과와 소요 시간이 필요한 곳은 BeatMapGenerator.AnalyzeDetailedAsync를 쓴다.
    /// </summary>
    public sealed class AnalysisOutput
    {
        public BeatMap map;
        public OnsetResult onsets;
        public BpmResult bpm;
        /// <summary>워커에서 잰 분석 소요 시간(초).</summary>
        public double elapsedSec;
    }

    /// <summary>분석 불가(곡이 너무 짧음, 온셋 부족, 프레임 없음). Message는 화면에 그대로 띄우는 문구다.</summary>
    public sealed class AnalysisFailedException : Exception
    {
        public AnalysisFailedException(string message) : base(message) { }
    }

    public readonly struct JudgeEvent
    {
        public readonly Judgement judgement;
        public readonly int noteIndex;
        public readonly double delta;
        public readonly ActionType action;

        public JudgeEvent(Judgement judgement, int noteIndex, double delta, ActionType action)
        {
            this.judgement = judgement;
            this.noteIndex = noteIndex;
            this.delta = delta;
            this.action = action;
        }
    }

    public sealed class ResultData
    {
        public bool escaped;
        public Difficulty difficulty;
        public int score, perfect, good, miss, maxCombo, best;
    }

    /// <summary>E4 싱크 보정 결과.</summary>
    public sealed class CalibrationResult
    {
        public bool ok;
        public double offsetSec;
        public double medianSec;
        public double madSec;
        public int used;
        public string failReason;
    }
}
