using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>
    /// T6: RhythmJudge 판정 규칙(6.2). 손으로 만든 BeatMap과 곡 시간 숫자만 쓰므로 씬·시계가 필요 없다.
    /// 모든 시간은 GameConfig 창과 BeatMap 칸에서 계산한다(숫자 직접 쓰기 금지).
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class JudgeTests
    {
        const int SlotBox = 4;
        const int SlotLaundry = 6;
        const int SlotDoor = 8;
        const int SlotEmpty = 5;      // 노트가 없는 격자 칸
        const int SlotsAfterLast = 4; // 마지막 노트 뒤 곡 여백(칸)
        const int ExtraCombo = 5;     // 콤보 상한을 넘기는 추가 노트 수

        BeatMap map;
        RhythmJudge judge;
        List<JudgeEvent> events;

        // 칸 사이 반박자의 절반에서 Good 창 밖으로 벗어난 오프셋들(GameConfig에서 유도)
        static double PerfectOffset => GameConfig.PerfectWindowSec * 0.5;
        static double GoodOffset => (GameConfig.PerfectWindowSec + GameConfig.GoodWindowSec) * 0.5;
        double OffBeatOffset => GameConfig.GoodWindowSec + (map.HalfBeatSec * 0.5 - GameConfig.GoodWindowSec) * 0.5;

        [SetUp]
        public void SetUp()
        {
            map = MakeMap((SlotBox, ObstacleKind.Box), (SlotLaundry, ObstacleKind.Laundry), (SlotDoor, ObstacleKind.Door));
            StartJudge(map);
        }

        static BeatMap MakeMap(params (int slot, ObstacleKind kind)[] slots)
        {
            var m = new BeatMap
            {
                bpm = GameConfig.FallbackBpm,
                firstBeatTime = 0.0,
                difficulty = Difficulty.Normal,
                notes = new List<Note>(),
            };
            int last = 0;
            foreach (var (slot, kind) in slots)
            {
                m.notes.Add(new Note { time = m.SlotTime(slot), kind = kind, slotIndex = slot });
                last = Math.Max(last, slot);
            }
            m.songLength = m.SlotTime(last + SlotsAfterLast);
            return m;
        }

        void StartJudge(BeatMap m)
        {
            judge = new RhythmJudge();
            events = new List<JudgeEvent>();
            judge.Judged += events.Add;
            judge.Reset(m);
        }

        double Slot(int slot) => map.SlotTime(slot);

        JudgeEvent Last => events[events.Count - 1];

        // ── 판정 순서 (리뷰 M1) ──

        [Test]
        public void InputAfterEarlierNoteWindowPassed_MissesEarlierNoteFirst()
        {
            // 프레임이 늦게 와서 Tick 없이 다음 노트 입력이 먼저 들어온 경우: 앞 노트 Miss가 먼저, 콤보는 이번 Perfect로 1.
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry));

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(Judgement.Miss, events[0].judgement);
            Assert.AreEqual(0, events[0].noteIndex);
            Assert.AreEqual(Judgement.Perfect, events[1].judgement);
            Assert.AreEqual(1, events[1].noteIndex);
            Assert.AreEqual(1, judge.Combo);
            Assert.AreEqual(1, judge.MaxCombo);
        }

        [Test]
        public void FastSong_GoodWindowShrinksToHalfBeatRatio()
        {
            var fast = new BeatMap { bpm = GameConfig.BpmMax, firstBeatTime = 0.0, difficulty = Difficulty.Normal, notes = new List<Note>() };
            fast.notes.Add(new Note { time = fast.SlotTime(SlotBox), kind = ObstacleKind.Box, slotIndex = SlotBox });
            fast.songLength = fast.SlotTime(SlotBox + SlotsAfterLast);
            double fastGood = RhythmJudge.GoodWindowFor(fast);
            Assert.AreEqual(fast.HalfBeatSec * GameConfig.GoodWindowMaxHalfBeatRatio, fastGood, 1e-9);
            Assert.Less(fastGood, GameConfig.GoodWindowSec, "전제: 가장 빠른 곡에서는 창이 줄어든다");
            Assert.LessOrEqual(RhythmJudge.PerfectWindowFor(fast), fastGood);

            // 기본 상한 안이지만 이 곡의 창 밖인 입력 → 헛박자. 보통 빠르기 맵(SetUp)에서는 같은 Δ가 Good이다.
            double between = (fastGood + GameConfig.GoodWindowSec) * 0.5;
            StartJudge(fast);
            judge.OnInput(ActionType.Jump, fast.SlotTime(SlotBox) + between);
            Assert.AreEqual(Judgement.OffBeat, Last.judgement);

            StartJudge(map);
            judge.OnInput(ActionType.Jump, Slot(SlotBox) + between);
            Assert.AreEqual(Judgement.Good, Last.judgement);
        }

        // ── Perfect / Good ──

        [Test]
        public void EarlyAndLateInsidePerfectWindow_ArePerfect()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox) - PerfectOffset);
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry) + PerfectOffset);

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(Judgement.Perfect, events[0].judgement);
            Assert.AreEqual(Judgement.Perfect, events[1].judgement);
            Assert.AreEqual(-PerfectOffset, events[0].delta, 1e-9);
            Assert.AreEqual(PerfectOffset, events[1].delta, 1e-9);
            Assert.AreEqual(0, events[0].noteIndex);
            Assert.AreEqual(1, events[1].noteIndex);
            Assert.AreEqual(2, judge.PerfectCount);
            Assert.AreEqual(2, judge.Combo);
            Assert.IsTrue(map.notes[0].judged && map.notes[1].judged);
        }

        [Test]
        public void EarlyAndLateInsideGoodWindow_AreGood()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox) - GoodOffset);
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry) + GoodOffset);

            Assert.AreEqual(Judgement.Good, events[0].judgement);
            Assert.AreEqual(Judgement.Good, events[1].judgement);
            Assert.AreEqual(2, judge.GoodCount);
            Assert.AreEqual(0, judge.PerfectCount);
            Assert.AreEqual(2, judge.Combo);
            Assert.AreEqual(RhythmJudge.ScoreFor(Judgement.Good, 1) + RhythmJudge.ScoreFor(Judgement.Good, 2), judge.Score);
        }

        [Test]
        public void EachActionClearsItsObstacleKind()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox));
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry));
            judge.OnInput(ActionType.Wave, Slot(SlotDoor));

            Assert.AreEqual(3, events.Count);
            foreach (JudgeEvent e in events) Assert.AreEqual(Judgement.Perfect, e.judgement);
            Assert.AreEqual(ActionType.Jump, events[0].action);
            Assert.AreEqual(ActionType.Slide, events[1].action);
            Assert.AreEqual(ActionType.Wave, events[2].action);
        }

        // ── Miss ──

        [Test]
        public void WrongKey_IsMissOnTheNote()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox)); // 정답 → 콤보 1
            judge.OnInput(ActionType.Wave, Slot(SlotLaundry)); // 빨래는 Slide

            Assert.AreEqual(Judgement.Miss, Last.judgement);
            Assert.AreEqual(1, Last.noteIndex);
            Assert.AreEqual(ActionType.Wave, Last.action);
            Assert.AreEqual(0, judge.Combo);
            Assert.AreEqual(1, judge.MissCount);
            Assert.IsTrue(map.notes[1].judged);

            int before = events.Count;
            judge.Tick(Slot(SlotLaundry) + map.HalfBeatSec); // 이미 판정된 노트는 Tick이 다시 Miss로 세지 않는다
            Assert.AreEqual(before, events.Count);
            Assert.AreEqual(1, judge.MissCount);
        }

        [Test]
        public void NoInput_TickPastGoodWindow_IsMiss()
        {
            Note note = map.notes[0];
            judge.Tick(note.time + GameConfig.GoodWindowSec); // 창 끝까지는 아직 Miss가 아니다
            Assert.AreEqual(0, events.Count);
            Assert.IsFalse(note.judged);

            double late = note.time + GameConfig.GoodWindowSec + map.HalfBeatSec * 0.01; // .NET Standard 2.0에는 Math.BitIncrement가 없다
            judge.Tick(late);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Judgement.Miss, Last.judgement);
            Assert.AreEqual(0, Last.noteIndex);
            Assert.AreEqual(ActionType.Jump, Last.action); // 요구 동작
            Assert.AreEqual(late - note.time, Last.delta, 1e-9);
            Assert.AreEqual(1, judge.MissCount);
            Assert.AreEqual(0, judge.Combo);
            Assert.IsTrue(note.judged);

            judge.Tick(late + map.HalfBeatSec * 0.1); // 같은 노트에 다시 Miss가 나지 않는다
            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public void InputAfterTickMiss_IsEmptyBeat()
        {
            judge.Tick(Slot(SlotBox) + map.HalfBeatSec * 0.5);
            Assert.AreEqual(Judgement.Miss, Last.judgement);

            judge.OnInput(ActionType.Jump, Slot(SlotBox) + GameConfig.GoodWindowSec * 0.5);
            Assert.AreEqual(Judgement.EmptyBeat, Last.judgement);
            Assert.AreEqual(1, judge.MissCount);
        }

        [Test]
        public void TickSkippingSeveralNotes_EmitsOneMissEachInTimeOrder()
        {
            judge.Tick(map.songLength); // 한 프레임에 노트 3개를 모두 지난다

            Assert.AreEqual(map.notes.Count, events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                Assert.AreEqual(Judgement.Miss, events[i].judgement);
                Assert.AreEqual(i, events[i].noteIndex);
                Assert.AreEqual(RhythmJudge.RequiredAction(map.notes[i].kind), events[i].action);
                Assert.AreEqual(map.songLength - map.notes[i].time, events[i].delta, 1e-9);
            }
            Assert.AreEqual(map.notes.Count, judge.MissCount);

            judge.Tick(map.songLength + map.HalfBeatSec);
            Assert.AreEqual(map.notes.Count, events.Count);
        }

        [Test]
        public void TickSkipsNotesAlreadyJudgedByInput()
        {
            // 가운데 노트를 먼저 판정. 입력 전에 Tick이 돌아 이미 창이 지난 첫 노트가 먼저 Miss된다(리뷰 M1).
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry));
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(Judgement.Miss, events[0].judgement);
            Assert.AreEqual(0, events[0].noteIndex);
            Assert.AreEqual(Judgement.Perfect, events[1].judgement);
            events.Clear();

            judge.Tick(map.songLength);

            // 입력으로 판정된 가운데 노트는 건너뛰고 마지막 노트만 Miss
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(2, events[0].noteIndex);
            Assert.AreEqual(2, judge.MissCount);
        }

        // ── OffBeat / EmptyBeat ──

        [Test]
        public void OffGrid_IsOffBeat_ResetsComboAndKeepsNote()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox));
            judge.OnInput(ActionType.Slide, Slot(SlotLaundry));
            int scoreBefore = judge.Score;
            Assert.AreEqual(2, judge.Combo);

            double t = Slot(SlotDoor) - OffBeatOffset; // 문 칸 직전이지만 Good 창 밖(뒤쪽이면 입력 전 Tick이 문 노트를 먼저 Miss 처리함)
            judge.OnInput(ActionType.Wave, t);

            Assert.AreEqual(Judgement.OffBeat, Last.judgement);
            Assert.AreEqual(-1, Last.noteIndex);
            Assert.AreEqual(ActionType.Wave, Last.action);
            Assert.AreEqual(t - Slot(SlotDoor), Last.delta, 1e-9);
            Assert.AreEqual(0, judge.Combo);
            Assert.AreEqual(2, judge.MaxCombo);
            Assert.AreEqual(scoreBefore, judge.Score);
            Assert.AreEqual(0, judge.MissCount);
            Assert.IsFalse(map.notes[2].judged);

            judge.OnInput(ActionType.Wave, Slot(SlotDoor)); // 노트는 아직 판정할 수 있다
            Assert.AreEqual(Judgement.Perfect, Last.judgement);
        }

        [Test]
        public void OffBeatBetweenSlots_UsesNearestSlotDelta()
        {
            double t = Slot(SlotEmpty) - OffBeatOffset;
            judge.OnInput(ActionType.Jump, t);

            Assert.AreEqual(Judgement.OffBeat, Last.judgement);
            Assert.AreEqual(t - Slot(SlotEmpty), Last.delta, 1e-9);
        }

        [Test]
        public void GridSlotWithoutNote_IsEmptyBeatWithoutPenalty()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox)); // 콤보 1
            int score = judge.Score;

            judge.OnInput(ActionType.Slide, Slot(SlotEmpty) + PerfectOffset);

            Assert.AreEqual(Judgement.EmptyBeat, Last.judgement);
            Assert.AreEqual(-1, Last.noteIndex);
            Assert.AreEqual(ActionType.Slide, Last.action);
            Assert.AreEqual(1, judge.Combo);
            Assert.AreEqual(score, judge.Score);
            Assert.AreEqual(0, judge.MissCount);
        }

        [Test]
        public void SameSlotPressedTwice_SecondIsEmptyBeat()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox));
            int score = judge.Score;

            judge.OnInput(ActionType.Jump, Slot(SlotBox) + PerfectOffset);

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(Judgement.Perfect, events[0].judgement);
            Assert.AreEqual(Judgement.EmptyBeat, events[1].judgement);
            Assert.AreEqual(-1, events[1].noteIndex);
            Assert.AreEqual(1, judge.Combo);
            Assert.AreEqual(score, judge.Score);
            Assert.AreEqual(1, judge.PerfectCount);
        }

        [Test]
        public void InputBeforeSongStartOrAfterSongEnd_IsIgnored()
        {
            judge.OnInput(ActionType.Jump, -map.HalfBeatSec);
            judge.OnInput(ActionType.Jump, map.songLength + map.HalfBeatSec);

            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, judge.Combo);
            Assert.AreEqual(0, judge.Score);
        }

        // ── 점수·콤보 ──

        [Test]
        public void ScoreFor_FollowsFormulaAndCapsCombo()
        {
            int cap = GameConfig.ComboBonusCap;
            foreach (int combo in new[] { 1, cap / 2, cap - 1, cap, cap + 1, cap * 3 })
            {
                double multiplier = 1.0 + Math.Min(combo, cap) / (double)cap;
                Assert.AreEqual((int)Math.Round(GameConfig.ScorePerfect * multiplier, MidpointRounding.AwayFromZero),
                    RhythmJudge.ScoreFor(Judgement.Perfect, combo), $"Perfect combo {combo}");
                Assert.AreEqual((int)Math.Round(GameConfig.ScoreGood * multiplier, MidpointRounding.AwayFromZero),
                    RhythmJudge.ScoreFor(Judgement.Good, combo), $"Good combo {combo}");
            }

            Assert.AreEqual(GameConfig.ScorePerfect * 2, RhythmJudge.ScoreFor(Judgement.Perfect, cap)); // 상한에서 2배
            Assert.AreEqual(RhythmJudge.ScoreFor(Judgement.Perfect, cap), RhythmJudge.ScoreFor(Judgement.Perfect, cap * 3));
            Assert.Greater(RhythmJudge.ScoreFor(Judgement.Perfect, cap), RhythmJudge.ScoreFor(Judgement.Perfect, 1));
        }

        [Test]
        public void ScoreFor_MissOffBeatEmptyBeat_AreZero()
        {
            Assert.AreEqual(0, RhythmJudge.ScoreFor(Judgement.Miss, 1));
            Assert.AreEqual(0, RhythmJudge.ScoreFor(Judgement.OffBeat, 1));
            Assert.AreEqual(0, RhythmJudge.ScoreFor(Judgement.EmptyBeat, 1));
        }

        [Test]
        public void AccumulatedScore_UsesComboAfterEachHit_AndStopsGrowingAtCap()
        {
            int count = GameConfig.ComboBonusCap + ExtraCombo;
            var slots = new (int, ObstacleKind)[count];
            for (int i = 0; i < count; i++) slots[i] = (SlotBox + i * 2, ObstacleKind.Box);
            map = MakeMap(slots);
            StartJudge(map);

            int expected = 0;
            for (int i = 0; i < count; i++)
            {
                judge.OnInput(ActionType.Jump, map.notes[i].time);
                expected += RhythmJudge.ScoreFor(Judgement.Perfect, i + 1);
                Assert.AreEqual(i + 1, judge.Combo);
                Assert.AreEqual(expected, judge.Score, $"hit {i + 1}");
            }

            Assert.AreEqual(count, judge.MaxCombo);
            Assert.AreEqual(count, judge.PerfectCount);
            // 상한을 넘긴 뒤의 Perfect는 상한 배율(최대)로 고정
            int perfectAtCap = RhythmJudge.ScoreFor(Judgement.Perfect, GameConfig.ComboBonusCap);
            Assert.AreEqual(perfectAtCap, RhythmJudge.ScoreFor(Judgement.Perfect, count));
        }

        [Test]
        public void MaxComboKeptAfterMiss_AndComboRestarts()
        {
            map = MakeMap((SlotBox, ObstacleKind.Box), (SlotBox + 2, ObstacleKind.Box),
                (SlotBox + 4, ObstacleKind.Box), (SlotBox + 6, ObstacleKind.Box));
            StartJudge(map);

            judge.OnInput(ActionType.Jump, map.notes[0].time);
            judge.OnInput(ActionType.Jump, map.notes[1].time);
            Assert.AreEqual(2, judge.MaxCombo);

            judge.OnInput(ActionType.Slide, map.notes[2].time); // 틀린 키
            Assert.AreEqual(0, judge.Combo);
            Assert.AreEqual(2, judge.MaxCombo);

            judge.OnInput(ActionType.Jump, map.notes[3].time);
            Assert.AreEqual(1, judge.Combo);
            Assert.AreEqual(2, judge.MaxCombo);
            int perfect1 = RhythmJudge.ScoreFor(Judgement.Perfect, 1);
            Assert.AreEqual(perfect1 + RhythmJudge.ScoreFor(Judgement.Perfect, 2) + perfect1, judge.Score);
        }

        // ── 이벤트 ──

        [Test]
        public void StateIsUpdatedBeforeJudgedFires()
        {
            var seen = new List<(Judgement j, int combo, int score, int miss, bool judged)>();
            judge.Judged += e => seen.Add((e.judgement, judge.Combo, judge.Score, judge.MissCount,
                e.noteIndex >= 0 && map.notes[e.noteIndex].judged));

            judge.OnInput(ActionType.Jump, Slot(SlotBox));
            judge.OnInput(ActionType.Wave, Slot(SlotLaundry)); // 틀린 키
            judge.OnInput(ActionType.Wave, Slot(SlotDoor) - OffBeatOffset); // 헛박자(문 노트 창 앞)
            judge.Tick(map.songLength); // 문 노트 Miss

            Assert.AreEqual(4, seen.Count);
            Assert.AreEqual((Judgement.Perfect, 1, RhythmJudge.ScoreFor(Judgement.Perfect, 1), 0, true), seen[0]);
            Assert.AreEqual((Judgement.Miss, 0, RhythmJudge.ScoreFor(Judgement.Perfect, 1), 1, true), seen[1]);
            Assert.AreEqual((Judgement.OffBeat, 0, RhythmJudge.ScoreFor(Judgement.Perfect, 1), 1, false), seen[2]);
            Assert.AreEqual((Judgement.Miss, 0, RhythmJudge.ScoreFor(Judgement.Perfect, 1), 2, true), seen[3]);
        }

        [Test]
        public void EveryEvent_LogsJudgeLine()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Judge\] Perfect Δ=[+\-]?\d+(\.\d+)?ms note=0 action=Jump$"));
            judge.OnInput(ActionType.Jump, Slot(SlotBox));
        }

        // ── 보조 규칙 ──

        [Test]
        public void RequiredAction_MapsKinds()
        {
            Assert.AreEqual(ActionType.Jump, RhythmJudge.RequiredAction(ObstacleKind.Box));
            Assert.AreEqual(ActionType.Slide, RhythmJudge.RequiredAction(ObstacleKind.Laundry));
            Assert.AreEqual(ActionType.Wave, RhythmJudge.RequiredAction(ObstacleKind.Door));
        }

        [Test]
        public void Reset_ClearsStateAndNoteFlags()
        {
            judge.OnInput(ActionType.Jump, Slot(SlotBox));
            judge.Tick(map.songLength);
            Assert.Greater(judge.MissCount, 0);

            judge.Reset(map);

            Assert.AreEqual(0, judge.Score);
            Assert.AreEqual(0, judge.Combo);
            Assert.AreEqual(0, judge.MaxCombo);
            Assert.AreEqual(0, judge.PerfectCount);
            Assert.AreEqual(0, judge.GoodCount);
            Assert.AreEqual(0, judge.MissCount);
            foreach (Note n in map.notes)
            {
                Assert.IsFalse(n.judged);
                Assert.IsFalse(n.hasPending);
            }

            events.Clear();
            judge.OnInput(ActionType.Jump, Slot(SlotBox)); // 다시 판정 가능
            Assert.AreEqual(Judgement.Perfect, Last.judgement);
            judge.Reset(map);
            judge.Tick(map.songLength); // Tick 커서도 처음으로 돌아가 있다
            Assert.AreEqual(map.notes.Count + 1, events.Count);
        }

        [Test]
        public void ResetWithNullMap_IsInert()
        {
            judge.Reset(null);

            Assert.DoesNotThrow(() =>
            {
                judge.OnInput(ActionType.Jump, Slot(SlotBox));
                judge.Tick(map.songLength);
            });
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, judge.Score);
            Assert.AreEqual(0, judge.MissCount);
        }
    }
}
