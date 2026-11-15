using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// F4 박자 판정(6.2). 입력은 가장 가까운 반박자 칸 하나에만 대응시킨다.
    /// 점수·콤보·노트 상태를 먼저 바꾼 뒤 Judged를 보내므로, 핸들러는 갱신된 Combo·Score를 읽는다.
    /// MonoBehaviour가 아니다. GameFlow가 만들고 곡 시간(Conductor.SongTime)을 넘긴다.
    /// </summary>
    public sealed class RhythmJudge
    {
        const double MsPerSec = 1000.0;

        public event Action<JudgeEvent> Judged;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int PerfectCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MissCount { get; private set; }

        BeatMap map;
        /// <summary>칸 인덱스 → map.notes 인덱스. 칸당 노트는 최대 1개다.</summary>
        readonly Dictionary<int, int> noteBySlot = new Dictionary<int, int>();
        /// <summary>Tick이 아직 보지 않은 첫 노트. 노트는 시간순이므로 앞으로만 움직인다(분할 상환 O(1)).</summary>
        int tickCursor;
        /// <summary>이 맵에 쓰는 창(Reset에서 GoodWindowFor·PerfectWindowFor로 정함).</summary>
        double goodWindow = GameConfig.GoodWindowSec;
        double perfectWindow = GameConfig.PerfectWindowSec;

        /// <summary>곡의 Good 창 = min(GoodWindowSec, 반박자 × GoodWindowMaxHalfBeatRatio). 빠른 곡에서 이웃 칸 창이 겹쳐 헛박자 구간이 사라지지 않게 한다.</summary>
        public static double GoodWindowFor(BeatMap m)
        {
            return m == null ? GameConfig.GoodWindowSec : Math.Min(GameConfig.GoodWindowSec, m.HalfBeatSec * GameConfig.GoodWindowMaxHalfBeatRatio);
        }

        /// <summary>곡의 Perfect 창 = min(PerfectWindowSec, 그 곡의 Good 창).</summary>
        public static double PerfectWindowFor(BeatMap m)
        {
            return Math.Min(GameConfig.PerfectWindowSec, GoodWindowFor(m));
        }

        /// <summary>새 판 시작. 점수·콤보·횟수를 0으로, 모든 노트를 미판정으로 되돌린다. null이면 판정하지 않는다.</summary>
        public void Reset(BeatMap newMap)
        {
            map = newMap;
            Score = 0;
            Combo = 0;
            MaxCombo = 0;
            PerfectCount = 0;
            GoodCount = 0;
            MissCount = 0;
            tickCursor = 0;
            goodWindow = GoodWindowFor(map);
            perfectWindow = PerfectWindowFor(map);
            noteBySlot.Clear();
            if (map?.notes == null) return;
            for (int i = 0; i < map.notes.Count; i++)
            {
                Note note = map.notes[i];
                note.judged = false;
                note.hasPending = false;
                noteBySlot[note.slotIndex] = i;
            }
        }

        /// <summary>키 입력 하나를 판정한다. 곡 시작 전(t &lt; 0)·곡 끝 뒤(t &gt; songLength) 입력은 무시한다.</summary>
        public void OnInput(ActionType action, double songTime)
        {
            if (map?.notes == null) return;
            if (songTime < 0.0 || songTime > map.songLength) return;

            // 리뷰 M1: 프레임이 늦게 와서 이번 입력보다 앞 노트의 Good 창이 이미 지났으면 그 Miss를 먼저 낸다.
            // 그래야 판정 순서가 시간순이 되어 Perfect 직후 콤보가 0이 되는 일이 없다.
            Tick(songTime);

            int slot = map.NearestSlot(songTime);
            double delta = songTime - map.SlotTime(slot);
            double absDelta = Math.Abs(delta);

            if (absDelta > goodWindow)
            {
                Combo = 0;
                Emit(new JudgeEvent(Judgement.OffBeat, -1, delta, action));
                return;
            }

            if (!noteBySlot.TryGetValue(slot, out int index) || map.notes[index].judged)
            {
                Emit(new JudgeEvent(Judgement.EmptyBeat, -1, delta, action));
                return;
            }

            Note note = map.notes[index];
            // E1 조합(kind 플래그 2개 이상, hasPending 대기) 분기는 T14에서 추가한다. 지금은 첫 플래그의 동작만 정답이다.
            note.judged = true;
            if (action == RequiredAction(note.kind))
            {
                Judgement judgement = absDelta <= perfectWindow ? Judgement.Perfect : Judgement.Good;
                if (judgement == Judgement.Perfect) PerfectCount++;
                else GoodCount++;
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
                Score += ScoreFor(judgement, Combo);
                Emit(new JudgeEvent(judgement, index, delta, action));
            }
            else
            {
                Combo = 0;
                MissCount++;
                Emit(new JudgeEvent(Judgement.Miss, index, delta, action));
            }
        }

        /// <summary>매 프레임. Good 창이 지난 미판정 노트를 시간순으로 모두 Miss 처리한다(프레임이 여러 노트를 건너뛰어도 노트마다 1번).</summary>
        public void Tick(double songTime)
        {
            if (map?.notes == null) return;
            List<Note> notes = map.notes;
            while (tickCursor < notes.Count)
            {
                Note note = notes[tickCursor];
                if (!note.judged)
                {
                    if (songTime <= note.time + goodWindow) return;
                    note.judged = true;
                    Combo = 0;
                    MissCount++;
                    Emit(new JudgeEvent(Judgement.Miss, tickCursor, songTime - note.time, RequiredAction(note.kind)));
                }
                tickCursor++;
            }
        }

        /// <summary>장애물 종류의 정답 동작. 조합이면 Box → Laundry → Door 순서로 첫 플래그(E1 조합 판정은 T14).</summary>
        public static ActionType RequiredAction(ObstacleKind kind)
        {
            if ((kind & ObstacleKind.Box) != 0) return ActionType.Jump;
            if ((kind & ObstacleKind.Laundry) != 0) return ActionType.Slide;
            return ActionType.Wave;
        }

        /// <summary>
        /// 6.2 점수: base × (1 + min(combo, ComboBonusCap) / ComboBonusCap). combo는 가산 직후 값.
        /// double로 계산한 뒤 가장 가까운 정수로 반올림한다(0.5는 0에서 먼 쪽). Perfect·Good 외는 0.
        /// </summary>
        public static int ScoreFor(Judgement judgement, int comboAfter)
        {
            int baseScore;
            switch (judgement)
            {
                case Judgement.Perfect: baseScore = GameConfig.ScorePerfect; break;
                case Judgement.Good: baseScore = GameConfig.ScoreGood; break;
                default: return 0;
            }
            int bonusCombo = Math.Max(0, Math.Min(comboAfter, GameConfig.ComboBonusCap));
            double multiplier = 1.0 + (double)bonusCombo / GameConfig.ComboBonusCap;
            return (int)Math.Round(baseScore * multiplier, MidpointRounding.AwayFromZero);
        }

        void Emit(JudgeEvent e)
        {
            string ms = (e.delta * MsPerSec).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
            Debug.Log($"[Judge] {e.judgement} Δ={ms}ms note={e.noteIndex} action={e.action}");
            Judged?.Invoke(e);
        }
    }
}
