using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>
    /// T7: 추격자 거리 모델(7.1). 앞부분은 ChaserController만 직접 다루고(곡 시간·dt를 숫자로 넣음),
    /// 뒷부분은 Bootstrap + FakeSongClock + GameFlow.Step으로 판정 연결·잡힘 상태·3분 균형을 확인한다.
    /// 거리 기대값은 모두 GameConfig에서 계산한다.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class ChaserTests : InputTestFixture
    {
        const double SongLength = 100.0;
        const float PlayerX = 10f;
        const float Tolerance = 1e-4f;
        const float SettleDt = 1f;          // ChaserLerpTau의 열 배 이상: 표시 거리가 사실상 다 따라감
        const int LastComboChecked = 350;

        const int SampleRate = 44100;
        const float ClickBpm = 120f;
        const int MaxWaitFrames = 10;
        const float RunToEndSec = 100f;     // 한 번의 Advance로 연출 전체를 끝까지 돌림
        const double ClickFirstBeatSec = 0.5;
        const double ThreeMinutesSec = 180.0;
        const double AnalysisTimeoutSec = 60.0;
        const float FlowStepDt = 0.05f;

        ChaserController chaser;
        int caughtCount;
        FakeSongClock clock;

        [SetUp]
        public void BootstrapSetUp()
        {
            clock = new FakeSongClock();
            Bootstrap.Restart(clock);
            Bootstrap.Instance.Flow.PauseOnFocusLoss = false; // an Editor window focus change must not pause the flows these tests run
        }

        [TearDown]
        public void BootstrapTearDown()
        {
            if (chaser != null) Object.DestroyImmediate(chaser.gameObject);
            chaser = null;
            Bootstrap.Restart(null);
        }

        /// <summary>단독 추격자: 시작 거리에 두고 한 번 배치(dt 0)한다.</summary>
        void MakeChaser()
        {
            chaser = ChaserController.Create(null);
            caughtCount = 0;
            chaser.Caught += () => caughtCount++;
            chaser.ResetChase();
            chaser.Tick(0.0, SongLength, 0f, PlayerX);
        }

        static float CloseSpeed(double songTime)
        {
            float p = Mathf.Clamp01((float)(songTime / SongLength));
            return GameConfig.ChaserCloseSpeedStart + (GameConfig.ChaserCloseSpeedEnd - GameConfig.ChaserCloseSpeedStart) * p;
        }

        // ───────────── 거리 모델 (단독) ─────────────

        [Test]
        public void NoInput_ClosesInOverTime_FasterAsTheSongProgresses()
        {
            MakeChaser();
            Assert.AreEqual(GameConfig.ChaserStartDistance, chaser.Distance, Tolerance);

            double[] times = { 0.0, SongLength * 0.5, SongLength * 0.99 };
            float previousStep = 0f;
            foreach (double t in times)
            {
                float before = chaser.Distance;
                chaser.Tick(t, SongLength, SettleDt, PlayerX);
                float step = before - chaser.Distance;
                Assert.AreEqual(CloseSpeed(t) * SettleDt, step, Tolerance, "closing at t=" + t);
                Assert.Greater(step, previousStep, "closing must speed up with song progress");
                previousStep = step;
            }
            Assert.AreEqual(0, caughtCount);
        }

        [Test]
        public void BeforeSongStartAndAfterSongEnd_DoesNotCloseIn()
        {
            MakeChaser();
            float start = chaser.Distance;

            chaser.Tick(-1.0, SongLength, SettleDt, PlayerX);
            Assert.AreEqual(start, chaser.Distance, "lead-in (song time < 0)");

            chaser.Tick(SongLength, SongLength, SettleDt, PlayerX);
            chaser.Tick(SongLength + 5.0, SongLength, SettleDt, PlayerX);
            Assert.AreEqual(start, chaser.Distance, "after the song ends the neighbour stops (8.2)");
        }

        [Test]
        public void Approach_JumpsDistanceAtOnce_DisplayFollowsOnTick()
        {
            MakeChaser();
            float start = chaser.Distance;

            chaser.Approach(GameConfig.ChaserMissApproach);
            Assert.AreEqual(start - GameConfig.ChaserMissApproach, chaser.Distance, Tolerance);
            Assert.AreEqual(start, chaser.DisplayDistance, Tolerance, "the drawn distance only moves on Tick");

            chaser.Tick(-1.0, SongLength, SettleDt, PlayerX); // 곡 전: 거리는 그대로, 표시만 따라감
            Assert.AreEqual(chaser.Distance, chaser.DisplayDistance, Tolerance);
            Assert.AreEqual(PlayerX - chaser.DisplayDistance, chaser.transform.position.x, Tolerance);
            Assert.AreEqual(GameConfig.FloorY, chaser.transform.position.y, Tolerance);

            float before = chaser.Distance;
            chaser.Approach(GameConfig.ChaserOffBeatApproach);
            Assert.AreEqual(before - GameConfig.ChaserOffBeatApproach, chaser.Distance, Tolerance);
        }

        [Test]
        public void ComboMilestones_PushBack_AndNeverPastMaxDistance()
        {
            MakeChaser();
            // 잡힘 거리 바로 앞에서 시작해야 밀어내기가 여러 번 쌓이고 상한에도 닿는다.
            chaser.Approach(chaser.Distance - GameConfig.ChaserCatchDistance - 0.1f);
            float low = chaser.Distance;
            var expectedMilestones = new[] { 10, 25, 50, 100, 200, 300 };
            Assert.Greater(low + expectedMilestones.Length * GameConfig.ChaserMilestonePush, GameConfig.ChaserMaxDistance,
                "precondition: the pushes must reach the cap");

            var pushedAt = new List<int>();
            for (int combo = 1; combo <= LastComboChecked; combo++)
            {
                float before = chaser.Distance;
                chaser.OnComboChanged(combo);
                if (chaser.Distance != before)
                {
                    pushedAt.Add(combo);
                    Assert.AreEqual(Mathf.Min(before + GameConfig.ChaserMilestonePush, GameConfig.ChaserMaxDistance), chaser.Distance, Tolerance,
                        "push at combo " + combo);
                }
            }

            CollectionAssert.AreEqual(expectedMilestones, pushedAt);
            Assert.AreEqual(GameConfig.ChaserMaxDistance, chaser.Distance, Tolerance);
        }

        [Test]
        public void SameComboTwice_PushesOnce_AndAComboResetCanPushAgain()
        {
            MakeChaser();
            chaser.Approach(GameConfig.ChaserMilestonePush * 2f); // 상한에 걸리지 않게 먼저 당겨 둠
            float start = chaser.Distance;

            chaser.OnComboChanged(10);
            chaser.OnComboChanged(10); // EmptyBeat 등으로 같은 콤보가 다시 와도
            Assert.AreEqual(start + GameConfig.ChaserMilestonePush, chaser.Distance, Tolerance);

            chaser.OnComboChanged(0); // Miss
            for (int combo = 1; combo <= 10; combo++) chaser.OnComboChanged(combo);
            Assert.AreEqual(start + 2f * GameConfig.ChaserMilestonePush, chaser.Distance, Tolerance);
        }

        [Test]
        public void IsMilestone_ListedCombosThenEveryRepeat()
        {
            foreach (int combo in new[] { 10, 25, 50, 100, 200, 300, 1000 })
                Assert.IsTrue(ChaserController.IsMilestone(combo), combo.ToString());
            foreach (int combo in new[] { 0, 1, 9, 11, 75, 150, 250, 301 })
                Assert.IsFalse(ChaserController.IsMilestone(combo), combo.ToString());
        }

        [Test]
        public void ReachingCatchDistance_FiresCaughtOnceOnTick_AndFreezesTheDistance()
        {
            MakeChaser();
            chaser.Approach(GameConfig.ChaserMaxDistance * 10f);
            Assert.AreEqual(GameConfig.ChaserCatchDistance, chaser.Distance, Tolerance, "the distance stops at the catch distance");
            Assert.AreEqual(0, caughtCount, "catching is decided on Tick, not inside a judgement");

            chaser.Tick(1.0, SongLength, FlowStepDt, PlayerX);
            Assert.AreEqual(1, caughtCount);
            Assert.IsTrue(chaser.IsCaught);

            chaser.Tick(2.0, SongLength, FlowStepDt, PlayerX);
            chaser.OnComboChanged(10);
            chaser.Approach(GameConfig.ChaserMissApproach);
            Assert.AreEqual(1, caughtCount, "Caught fires only once");
            Assert.AreEqual(GameConfig.ChaserCatchDistance, chaser.Distance, Tolerance, "no push or approach after being caught");

            chaser.ResetChase();
            Assert.IsFalse(chaser.IsCaught);
            Assert.AreEqual(GameConfig.ChaserStartDistance, chaser.Distance, Tolerance);
            Assert.AreEqual(GameConfig.ChaserStartDistance, chaser.DisplayDistance, Tolerance);
        }

        [Test]
        public void NoCatch_NeverFiresCaught()
        {
            MakeChaser();
            chaser.NoCatch = true;
            chaser.Approach(GameConfig.ChaserMaxDistance * 10f);
            for (int i = 0; i < 10; i++) chaser.Tick(i, SongLength, SettleDt, PlayerX);

            Assert.AreEqual(0, caughtCount);
            Assert.IsFalse(chaser.IsCaught);
            Assert.AreEqual(GameConfig.ChaserCatchDistance, chaser.Distance, Tolerance);
        }

        // ───────────── GameFlow 연결 ─────────────

        [UnityTest]
        public IEnumerator Playing_OffBeatAndMiss_BringTheChaserCloser()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, 30.0, Difficulty.Hard);
            flow.AutoPlay = false;
            ChaserController c = Bootstrap.Instance.Chaser;
            BeatMap map = flow.Map;
            Note first = map.notes[0];

            // dt 0이면 시간 가속 없이 즉시 접근만 남는다.
            double offBeatTime = first.time - map.HalfBeatSec * 0.5; // 두 칸 사이 = 어느 창에도 들지 않음
            clock.Time = offBeatTime;
            flow.Step(clock.Time, 0f);
            float start = c.Distance;
            var judged = new List<Judgement>();
            flow.Judge.Judged += e => judged.Add(e.judgement);

            flow.Judge.OnInput(ActionType.Jump, offBeatTime);
            CollectionAssert.AreEqual(new[] { Judgement.OffBeat }, judged);
            Assert.AreEqual(start - GameConfig.ChaserOffBeatApproach, c.Distance, Tolerance);

            clock.Time = first.time + GameConfig.GoodWindowSec * 2.0;
            flow.Step(clock.Time, 0f);
            Assert.AreEqual(1, flow.Judge.MissCount);
            Assert.AreEqual(start - GameConfig.ChaserOffBeatApproach - GameConfig.ChaserMissApproach, c.Distance, Tolerance);
        }

        [UnityTest]
        public IEnumerator Playing_TenPerfects_PushTheChaserBack()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, 30.0, Difficulty.Hard);
            flow.AutoPlay = true;
            ChaserController c = Bootstrap.Instance.Chaser;
            BeatMap map = flow.Map;
            int milestone = GameConfig.ComboMilestones[0];
            Assert.Greater(map.notes.Count, milestone, "the click track must have enough notes");
            Assert.LessOrEqual(GameConfig.ChaserStartDistance + GameConfig.ChaserMilestonePush, GameConfig.ChaserMaxDistance, "precondition: no cap");

            for (int i = 0; i < milestone; i++)
            {
                clock.Time = map.notes[i].time;
                flow.Step(clock.Time, 0f);
            }

            Assert.AreEqual(milestone, flow.Judge.Combo);
            Assert.AreEqual(GameConfig.ChaserStartDistance + GameConfig.ChaserMilestonePush, c.Distance, Tolerance);
        }

        [UnityTest]
        public IEnumerator Playing_NoInput_GetsCaught_ThenCaughtSequenceEndsInResult_AndNReturnsToSelectFile()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return ReachPlaying(flow, ThreeMinutesSec, Difficulty.Normal);
            flow.AutoPlay = false;
            ChaserController c = Bootstrap.Instance.Chaser;
            Assert.IsTrue(c.gameObject.activeSelf, "the chaser is shown while Playing");

            double caughtAt = RunUntil(flow, flow.Map.songLength);

            Assert.AreEqual(GameState.Caught, flow.State);
            Assert.IsTrue(c.IsCaught);
            Assert.AreEqual(GameConfig.ChaserCatchDistance, c.Distance, Tolerance);
            Debug.Log($"[ChaserTests] 무입력(보통, 3분 클릭 트랙): {caughtAt:F2}초에 잡힘, Miss {flow.Judge.MissCount}개, 첫 노트 {flow.Map.notes[0].time:F2}초");

            // Caught에서는 Step이 아무것도 바꾸지 않는다(판정·추격자 진행 없음). 장면은 Advance가 연출 시간표로만 움직인다.
            float x = c.transform.position.x;
            int misses = flow.Judge.MissCount;
            clock.Time = flow.Map.songLength;
            flow.Step(clock.Time, FlowStepDt);
            Assert.AreEqual(x, c.transform.position.x);
            Assert.AreEqual(misses, flow.Judge.MissCount);

            flow.Advance(RunToEndSec);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.IsFalse(flow.LastResult.escaped);
            Assert.AreEqual(misses, flow.LastResult.miss);
            Assert.IsTrue(c.gameObject.activeSelf, "the last scene stays under the result panel");

            Press(keyboard.nKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Result; i++)
                yield return null;
            Release(keyboard.nKey);
            yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsFalse(c.gameObject.activeSelf, "the chaser is hidden outside Playing");
            Assert.IsFalse(c.IsCaught, "leaving resets the chase");
        }

        [UnityTest]
        public IEnumerator Playing_FlawlessThreeMinutes_IsNeverCaught_AndChaserStopsAtSongEnd()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, ThreeMinutesSec, Difficulty.Normal);
            flow.AutoPlay = true;
            ChaserController c = Bootstrap.Instance.Chaser;
            BeatMap map = flow.Map;

            float minDistance = float.MaxValue;
            for (double t = 0.0; t < map.songLength; t += FlowStepDt)
            {
                clock.Time = t;
                flow.Step(t, FlowStepDt);
                minDistance = Mathf.Min(minDistance, c.Distance);
                if (flow.State != GameState.Playing) break;
            }

            Assert.AreEqual(GameState.Playing, flow.State, "a flawless run must not be caught");
            Assert.AreEqual(0, flow.Judge.MissCount);
            Debug.Log($"[ChaserTests] 무실수 autoPlay(보통, 3분 클릭 트랙): 노트 {map.notes.Count}개, 최대 콤보 {flow.Judge.MaxCombo}, " +
                      $"최소 거리 {minDistance:F3}, 곡 끝 거리 {c.Distance:F3}");

            // 곡 끝: 탈출 연출이 시작되고, 주인공은 비상구로 계속 달리며 이웃은 그 자리에 선다(8.2).
            clock.Time = map.songLength;
            flow.Step(clock.Time, SettleDt);
            Assert.AreEqual(GameState.Escaped, flow.State);
            float stoppedX = c.transform.position.x;
            float distance = c.Distance;
            flow.Advance(1f);
            Assert.AreEqual(GameState.Escaped, flow.State, "the escape sequence is still running");
            Assert.AreEqual(stoppedX, c.transform.position.x, Tolerance);
            Assert.AreEqual(distance, c.Distance);
            Assert.AreEqual(Bootstrap.Instance.Level.ExitX, Bootstrap.Instance.Player.transform.position.x, Tolerance);
        }

        // ───────────── 도우미 ─────────────

        /// <summary>FlowStepDt 간격으로 곡 시간을 올리며 Step한다. Playing을 벗어나면 그 시각을 돌려주고, 끝까지 가면 -1.</summary>
        double RunUntil(GameFlow flow, double endTime)
        {
            for (double t = 0.0; t < endTime; t += FlowStepDt)
            {
                clock.Time = t;
                flow.Step(t, FlowStepDt);
                if (flow.State != GameState.Playing) return t;
            }
            return -1.0;
        }

        IEnumerator ReachPlaying(GameFlow flow, double seconds, Difficulty difficulty)
        {
            flow.CurrentDifficulty = difficulty;
            float[] samples = TestAudio.ClickTrack(SampleRate, seconds, ClickBpm, ClickFirstBeatSec, Band.Low, Band.High);
            flow.StartFromClip(TestAudio.ToClip(samples, SampleRate), "chaser-click");
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (flow.State == GameState.Analyzing && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);
            Assert.IsNotNull(flow.Map);
            Assert.IsNotEmpty(flow.Map.notes);

            flow.SkipPreview();
            Assert.AreEqual(GameState.Countdown, flow.State);
            clock.Time = 0.0;
            flow.Step(clock.Time, 0f);
            Assert.AreEqual(GameState.Playing, flow.State);
        }
    }
}
