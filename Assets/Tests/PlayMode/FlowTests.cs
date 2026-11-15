using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NoiseRun.Tests
{
    /// <summary>
    /// Runs without InputTestFixture so the real AutoStart result is inspected.
    /// Other fixtures always leave a fresh Title-state Bootstrap behind, so the assertions
    /// hold regardless of test order.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class BootstrapAutoStartTests
    {
        [UnityTest]
        public IEnumerator AutoStart_CreatesSingleBootstrapInTitle()
        {
            yield return null;

            Assert.IsNotNull(Bootstrap.Instance);
            Assert.IsNotNull(Bootstrap.Instance.Flow);
            Assert.AreEqual(1, Object.FindObjectsByType<Bootstrap>().Length);
            Assert.AreEqual(GameState.Title, Bootstrap.Instance.Flow.State);
        }
    }

    /// <summary>
    /// State transitions end to end (spec 4.5, 8): analysis → Preview → Countdown → Playing ⇄ Paused/Resuming → Caught/Escaped → Result → R/N.
    /// Song time comes from a RecordingClock, GameFlow.Step drives Countdown and Playing, GameFlow.Advance drives the timer based sequences.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class FlowTests : InputTestFixture
    {
        const int MaxWaitFrames = 10;

        const int AnalysisSampleRate = 44100;
        const float ClickBpm = 120f;
        const double ClickFirstBeatSec = 0.5;
        const double BpmTolerance = 2.0;           // spec 4.6: BPM error within +-2
        const double AnalysisTimeoutSec = 30.0;    // real-time guard for the analysis task
        const float SecondClickBpm = 150f;
        const int PostCancelFrames = 10;           // extra frames after the reference analysis finished
        const string NoRhythmMessage = "리듬을 찾을 수 없는 곡입니다. 다른 곡을 선택하세요";
        const float StepDt = 0.016f;
        const float AdvanceDt = 0.05f;             // shorter than every sequence stage, so each stage is observable
        const float RunToEndSec = 100f;            // one Advance that runs a whole sequence
        const int MaxSequenceAdvances = 400;
        const float PositionTolerance = 1e-3f;
        const float SeqTimeTolerance = 0.1f;       // spec 8: stage times within +-0.1s
        const float FadeObserveSec = 0.15f;        // real time to let the 0.6s music fade-out start before it is cancelled
        const float RealTimeMarginSec = 3f;        // extra real time on top of a sequence's length for the frame-driven tests
        const string PauseText = "일시정지"; // 일시정지 화면 제목 Text 한 개와 정확히 같은 문구. HUD 조작키 안내("Esc 일시정지")와 구분하려고 부분 문자열이 아니라 항목 일치로 찾는다
        const string GameOverText = "GAME OVER";
        const string EscapedText = "탈출 성공!";
        static readonly double ClipSeconds = GameConfig.MinSongLengthSec * 2.0;
        static readonly float ResumeTotalSec = GameConfig.CountdownStepSec * GameConfig.CountdownSteps;
        static readonly Regex SeqLine = new Regex(@"^\[Seq\] (\w+) (\w+) t=(\d+\.\d{3})$");

        /// <summary>A FakeSongClock that also records Pause/Resume, so Conductor.Pause/Resume can be observed.</summary>
        sealed class RecordingClock : ISongClock
        {
            public double Time;
            public bool Paused;
            public int PauseCalls;
            public int ResumeCalls;

            public double SongTime => Time;
            public void Start(double delaySec) { Time = -delaySec; }
            public void Pause() { Paused = true; PauseCalls++; }
            public void Resume() { Paused = false; ResumeCalls++; }
        }

        RecordingClock clock;
        readonly List<string> seqLogs = new List<string>();

        // NUnit runs base [SetUp] (InputTestFixture.Setup) before this one,
        // and this [TearDown] before the base TearDown.
        [SetUp]
        public void BootstrapSetUp()
        {
            clock = new RecordingClock();
            seqLogs.Clear();
            Application.logMessageReceived += OnLog;
            Bootstrap.Restart(clock);
            Bootstrap.Instance.Flow.PauseOnFocusLoss = false; // an Editor window focus change must not pause the flows these tests run
        }

        [TearDown]
        public void BootstrapTearDown()
        {
            Application.logMessageReceived -= OnLog;
            // Leave a fresh, default Title-state Bootstrap for whatever runs next.
            Bootstrap.Restart(null);
            Assert.AreEqual(1f, Time.timeScale, "a finished test must not leave Time.timeScale changed");
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("[Seq]")) seqLogs.Add(message);
        }

        [UnityTest]
        public IEnumerator Title_Enter_GoesToSelectFileOnce()
        {
            var flow = Bootstrap.Instance.Flow;
            Assert.AreEqual(GameState.Title, flow.State);

            var changes = new List<(GameState prev, GameState next)>();
            flow.StateChanged += (prev, next) => changes.Add((prev, next));

            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            Press(keyboard.enterKey);
            for (int i = 0; i < MaxWaitFrames && flow.State != GameState.SelectFile; i++)
                yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);

            // Do not press Enter again: in SelectFile it opens the native dialog.
            Release(keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(GameState.Title, changes[0].prev);
            Assert.AreEqual(GameState.SelectFile, changes[0].next);
            Assert.AreEqual(GameState.SelectFile, flow.State);
        }

        [UnityTest]
        public IEnumerator Title_H_OpensControlsHelp_HAndEscClose_StaysOnTitle()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            yield return Tap(keyboard.hKey);
            Assert.IsTrue(flow.HelpOpen);
            Assert.IsTrue(CanvasTexts().Any(t => t.Contains("슬라이드 (빨래)")), "the help lists which key clears which obstacle");

            yield return Tap(keyboard.hKey);
            Assert.IsFalse(flow.HelpOpen);
            CollectionAssert.Contains(CanvasTexts(), "H: 조작법");

            yield return Tap(keyboard.hKey);
            yield return Tap(keyboard.escapeKey);
            Assert.IsFalse(flow.HelpOpen);
            Assert.AreEqual(GameState.Title, flow.State, "Esc only closes the help");
        }

        [Test]
        public void Restart_LeavesSingleBootstrapInTitle()
        {
            var first = Bootstrap.Instance;
            var second = Bootstrap.Restart(new FakeSongClock());
            Bootstrap.Restart(new FakeSongClock());

            Assert.IsTrue(first == null, "previous Bootstrap should be destroyed immediately");
            Assert.IsTrue(second == null, "intermediate Bootstrap should be destroyed immediately");
            Assert.AreEqual(1, Object.FindObjectsByType<Bootstrap>().Length);
            Assert.IsNotNull(Bootstrap.Instance);
            Assert.AreEqual(GameState.Title, Bootstrap.Instance.Flow.State);
        }

        [UnityTest]
        public IEnumerator StartFromClip_ClickTrack_DebugPreview_ReachesPreviewWithMap()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.DebugPreview = true; // startAt=debug path: analysis debug screen instead of the flythrough
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);

            flow.StartFromClip(clip, "click");
            Assert.AreEqual(GameState.Analyzing, flow.State);

            yield return WaitWhileAnalyzing(flow);

            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);
            Assert.IsNotNull(flow.Map);
            Assert.AreEqual(ClickBpm, flow.Map.bpm, BpmTolerance);
            Assert.IsNull(flow.LastError);
            Assert.IsFalse(Bootstrap.Instance.CameraDirector.FlythroughActive, "the debug screen has no camera flythrough");

            flow.SkipPreview(); // Space/Enter must not leave the debug screen either
            Assert.AreEqual(GameState.Preview, flow.State);
        }

        [UnityTest]
        public IEnumerator StartFromClip_ClickTrack_ReachesPreviewWithMapAndStartPose()
        {
            var flow = Bootstrap.Instance.Flow;
            var changes = new List<GameState>();
            flow.StateChanged += (prev, next) => changes.Add(next);

            yield return ReachPreview(flow, ClickClip(Band.Low, null), "click");

            CollectionAssert.AreEqual(new[] { GameState.Analyzing, GameState.Preview }, changes, "analysis ends in Preview, not in Playing");
            Assert.AreEqual(ClickBpm, flow.Map.bpm, BpmTolerance);
            Assert.AreEqual(GameConfig.DefaultDifficulty, flow.Map.difficulty);
            Assert.IsNull(flow.LastError);
            Assert.AreEqual(1f, Time.timeScale);

            // Spec 8.1: the player stands at x=0, the neighbor, HUD and timing bar are hidden, the camera flythrough runs.
            Bootstrap b = Bootstrap.Instance;
            Assert.IsTrue(b.Player.gameObject.activeSelf);
            Assert.AreEqual(0f, b.Player.transform.position.x, PositionTolerance);
            Assert.IsNull(b.Player.CurrentAction);
            Assert.IsFalse(b.Chaser.gameObject.activeSelf, "the neighbor is hidden during Preview");
            Assert.IsFalse(b.Hud.gameObject.activeSelf);
            Assert.IsNotNull(b.Level.ObstacleFor(0), "the level is built for the flythrough");
            Assert.IsTrue(b.CameraDirector.FlythroughActive);
            Assert.AreEqual(GameFlow.StageHold, flow.SequenceStage);
            StringAssert.Contains("click", string.Join("\n", CanvasTexts()));
            StringAssert.Contains("BPM", string.Join("\n", CanvasTexts()));
            Assert.IsTrue(seqLogs.Any(l => l.StartsWith("[Seq] Preview hold t=")), "stage start is logged");
        }

        [UnityTest]
        public IEnumerator Preview_FlythroughEnd_GoesToCountdown()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPreview(flow, ClickClip(Band.Low, null), "click");
            CameraDirector camera = Bootstrap.Instance.CameraDirector;
            float total = GameConfig.PreviewHoldStartSec + GameConfig.PreviewFlySec + GameConfig.PreviewHoldEndSec + GameConfig.PreviewReturnSec;

            camera.Advance(GameConfig.PreviewHoldStartSec + GameConfig.PreviewFlySec * 0.5f);
            flow.Advance(0f);
            Assert.AreEqual(GameState.Preview, flow.State, "the flythrough is still running");
            Assert.AreEqual(GameFlow.StageFly, flow.SequenceStage);
            Assert.IsTrue(seqLogs.Any(l => l.StartsWith("[Seq] Preview fly t=")));

            camera.Advance(total);
            flow.Advance(0f);

            Assert.AreEqual(GameState.Countdown, flow.State);
            Assert.IsFalse(camera.FlythroughActive);
            Assert.AreEqual(-ResumeTotalSec, clock.Time, PositionTolerance, "the song is scheduled CountdownStepSec x CountdownSteps ahead");
            Assert.IsNull(flow.SequenceStage);
        }

        [UnityTest]
        public IEnumerator Preview_EnterSkipsToCountdownAndStartsTheSong()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            yield return ReachPreview(flow, ClickClip(Band.Low, null), "click");
            Assert.IsTrue(Bootstrap.Instance.CameraDirector.FlythroughActive);

            yield return Tap(keyboard.enterKey);

            Assert.AreEqual(GameState.Countdown, flow.State);
            Assert.IsFalse(Bootstrap.Instance.CameraDirector.FlythroughActive, "skipping ends the flythrough at once");
            Assert.AreEqual(-ResumeTotalSec, clock.Time, PositionTolerance);
            Assert.IsTrue(CanvasTexts().Contains("3"), "the countdown starts with 3");
            Assert.IsTrue(Bootstrap.Instance.Hud.gameObject.activeSelf, "HUD appears with the countdown");

            // Countdown ends at song time 0 with "GO", which disappears after GoTextSec.
            clock.Time = 0.0;
            flow.Step(clock.Time, 0f);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.IsTrue(CanvasTexts().Contains("GO"));
            clock.Time = GameConfig.GoTextSec + 0.1;
            flow.Step(clock.Time, 0f);
            Assert.IsFalse(CanvasTexts().Contains("GO"));
        }

        [UnityTest]
        public IEnumerator Countdown_NeighborWalksIn_PlayerSurprisedThenLooksBack()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            yield return ReachPreview(flow, ClickClip(Band.Low, null), "click");
            yield return Tap(keyboard.spaceKey);
            Assert.AreEqual(GameState.Countdown, flow.State);

            Bootstrap b = Bootstrap.Instance;
            SpriteRenderer playerBody = b.Player.GetComponent<SpriteRenderer>();
            double step = GameConfig.CountdownStepSec;

            // "3": song time −2.1 … −1.4. The player stands at x=0 and the neighbor walks in from off screen.
            clock.Time = -GameConfig.CountdownSteps * step;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(0f, b.Player.transform.position.x, PositionTolerance, "the player stands during the countdown");
            Assert.IsTrue(b.Chaser.gameObject.activeSelf);
            Assert.AreEqual(-GameConfig.ChaserWalkInStartDistance, b.Chaser.transform.position.x, PositionTolerance);
            Assert.IsFalse(playerBody.flipX, "facing right");
            Assert.AreNotEqual(PlayerController.Reaction.Surprise, b.Player.CurrentReaction);

            clock.Time = -(GameConfig.CountdownSteps - 0.5) * step;
            flow.Step(clock.Time, StepDt);
            float halfWay = -Mathf.Lerp(GameConfig.ChaserWalkInStartDistance, GameConfig.ChaserStartDistance, 0.5f);
            Assert.AreEqual(halfWay, b.Chaser.transform.position.x, PositionTolerance);
            Assert.AreEqual(0f, b.Player.transform.position.x, PositionTolerance);
            Assert.IsTrue(CanvasTexts().Contains("3"));
            Assert.IsFalse(b.Player.SurpriseMarkVisible);

            // "2": the neighbor has arrived, the player looks left and is startled.
            clock.Time = -1.5 * step;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(-GameConfig.ChaserStartDistance, b.Chaser.transform.position.x, PositionTolerance);
            Assert.AreEqual(PlayerController.Reaction.Surprise, b.Player.CurrentReaction);
            Assert.IsTrue(b.Player.SurpriseMarkVisible, "the \"!\" mark shows above the head");
            Assert.IsTrue(playerBody.flipX, "facing left, toward the neighbor");
            Assert.IsTrue(CanvasTexts().Contains("2"));

            // "1": the player looks to the right again. The "!" (SurpriseMarkSec = one step) and the startle hop are over by now, so the player waits calmly.
            clock.Time = -0.5 * step;
            flow.Step(clock.Time, StepDt);
            Assert.IsFalse(playerBody.flipX, "facing right again");
            Assert.IsFalse(b.Player.SurpriseMarkVisible, "the \"!\" is gone when the player looks back");
            Assert.AreEqual(PlayerController.Reaction.None, b.Player.CurrentReaction, "the startle hop is over");
            Assert.IsTrue(CanvasTexts().Contains("1"));
            Assert.AreEqual(GameState.Countdown, flow.State);

            // Song time 0: both run.
            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.IsFalse(playerBody.flipX);
            Assert.AreEqual(-GameConfig.ChaserStartDistance, b.Chaser.transform.position.x, PositionTolerance);
        }

        [UnityTest]
        public IEnumerator Countdown_SeqLogs_FollowTheTimetable()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachCountdown(flow, ClickClip(Band.Low, null), "seq");

            // Frame by frame like Update does: song time -2.1 → 0 in StepDt steps, then song time 0.
            for (double t = -ResumeTotalSec; t < 0.0; t += StepDt)
            {
                clock.Time = t;
                flow.Step(t, StepDt);
            }
            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Playing, flow.State);

            // Spec 8.1: "3" starts with the countdown, each next number CountdownStepSec later, "go" at the end (±0.1s). t = song time + 2.1.
            float step = GameConfig.CountdownStepSec;
            Assert.AreEqual(4, seqLogs.Count(l => l.StartsWith("[Seq] Countdown ")), "one log per stage, no repeats");
            var seq = SeqTimes("Countdown");
            Assert.AreEqual(0f, seq["3"], SeqTimeTolerance);
            Assert.AreEqual(step, seq["2"], SeqTimeTolerance);
            Assert.AreEqual(2f * step, seq["1"], SeqTimeTolerance);
            Assert.AreEqual(ResumeTotalSec, seq[GameFlow.StageGo], SeqTimeTolerance);
        }

        [UnityTest]
        public IEnumerator Playing_Esc_Pauses_ThenEscResumesAfterCountdown_SongTimeContinuous()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "pause");
            Bootstrap.Instance.Chaser.NoCatch = true;
            const double pausedAt = 5.0;
            clock.Time = pausedAt;
            flow.Step(clock.Time, StepDt);

            yield return Tap(keyboard.escapeKey);
            Assert.AreEqual(GameState.Paused, flow.State);
            Assert.IsTrue(clock.Paused, "the conductor stops the song clock");
            Assert.AreEqual(1, clock.PauseCalls);
            Assert.AreEqual(0f, Time.timeScale);
            CollectionAssert.Contains(CanvasTexts(), PauseText);
            Assert.AreEqual(pausedAt, clock.Time, "song time stands still while paused");

            yield return Tap(keyboard.escapeKey);
            Assert.AreEqual(GameState.Resuming, flow.State);
            Assert.AreEqual(0f, Time.timeScale, "the picture stays frozen while 3·2·1 runs");
            Assert.IsTrue(clock.Paused, "the song resumes only after the countdown");
            Assert.IsTrue(CanvasTexts().Contains("3"));
            CollectionAssert.DoesNotContain(CanvasTexts(), PauseText, "the pause screen gives way to the countdown");

            yield return Tap(keyboard.escapeKey); // Esc while resuming is ignored
            Assert.AreEqual(GameState.Resuming, flow.State);
            Assert.IsTrue(clock.Paused);

            // The real Update added real time to the countdown during the taps, so stage boundaries are not asserted here
            // (Resuming_CountdownStagesAndSeqLogs_FollowTheTimetable does that without running frames). Run out the countdown.
            flow.Advance(ResumeTotalSec);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.IsFalse(clock.Paused);
            Assert.AreEqual(1, clock.ResumeCalls);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(pausedAt, Bootstrap.Instance.Conductor.SongTime, "song time continues from where it stopped");
        }

        [UnityTest]
        public IEnumerator Resuming_CountdownStagesAndSeqLogs_FollowTheTimetable()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "resume stages");
            Bootstrap.Instance.Chaser.NoCatch = true;

            // No frame runs between ResumeGame and the last Advance, so only the dt given here moves the countdown.
            flow.PauseGame();
            flow.ResumeGame();
            Assert.AreEqual(GameState.Resuming, flow.State);

            var labels = new List<string>();
            for (int i = 0; i < MaxSequenceAdvances && flow.State == GameState.Resuming; i++)
            {
                string label = CountdownLabel();
                if (label != null && (labels.Count == 0 || labels[labels.Count - 1] != label)) labels.Add(label);
                flow.Advance(StepDt);
            }

            CollectionAssert.AreEqual(new[] { "3", "2", "1" }, labels);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.AreEqual(1, clock.ResumeCalls);

            // Spec 8.4: 3 at 0, 2 at 0.7, 1 at 1.4, resume at 2.1 (real time since the countdown began, ±0.1s).
            float step = GameConfig.CountdownStepSec;
            Assert.AreEqual(4, seqLogs.Count(l => l.StartsWith("[Seq] Resuming ")), "one log per stage, no repeats");
            var seq = SeqTimes("Resuming");
            Assert.AreEqual(0f, seq["3"], SeqTimeTolerance);
            Assert.AreEqual(step, seq["2"], SeqTimeTolerance);
            Assert.AreEqual(2f * step, seq["1"], SeqTimeTolerance);
            Assert.AreEqual(ResumeTotalSec, seq[GameFlow.StageResume], SeqTimeTolerance);
        }

        [UnityTest]
        public IEnumerator Resuming_RealFrames_ReachesPlayingAfterTheCountdown()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "resume real");
            Bootstrap.Instance.Chaser.NoCatch = true;

            // Real frames only: Time.timeScale is 0 here, so the countdown must be measured in unscaled time or it never ends.
            flow.PauseGame();
            flow.ResumeGame();
            Assert.AreEqual(GameState.Resuming, flow.State);
            Assert.AreEqual(0f, Time.timeScale);

            yield return new WaitForSecondsRealtime(ResumeTotalSec * 0.5f);
            Assert.AreEqual(GameState.Resuming, flow.State, "halfway through 3·2·1 the song has not resumed");
            Assert.AreEqual(0, clock.ResumeCalls);

            yield return new WaitForSecondsRealtime(ResumeTotalSec * 0.5f + RealTimeMarginSec);
            Assert.AreEqual(GameState.Playing, flow.State, "the countdown ran out in real time");
            Assert.AreEqual(1, clock.ResumeCalls);
            Assert.IsFalse(clock.Paused);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator FocusLost_PausesPlaying_AndSendsResumingBackToPaused()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "focus");
            Bootstrap.Instance.Chaser.NoCatch = true;
            flow.PauseOnFocusLoss = true; // from here on nothing yields, so only the calls below can change the focus

            flow.OnApplicationFocus(true);
            Assert.AreEqual(GameState.Playing, flow.State, "gaining focus changes nothing");

            flow.OnApplicationFocus(false);
            Assert.AreEqual(GameState.Paused, flow.State);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(clock.Paused);

            flow.ResumeGame();
            Assert.AreEqual(GameState.Resuming, flow.State);
            flow.OnApplicationFocus(false);
            Assert.AreEqual(GameState.Paused, flow.State, "losing focus during the resume countdown pauses again");
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(CanvasTexts().Contains("3"), "the resume countdown is cancelled");
            CollectionAssert.Contains(CanvasTexts(), PauseText);
            Assert.AreEqual(0, clock.ResumeCalls, "the song never resumed");

            flow.ResumeGame();
            flow.Advance(ResumeTotalSec);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.AreEqual(1, clock.ResumeCalls);
        }

        [UnityTest]
        public IEnumerator FocusLostDuringCountdown_PausesAsSoonAsPlayingBegins()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachCountdown(flow, ClickClip(Band.Low, null), "focus countdown");
            Bootstrap.Instance.Chaser.NoCatch = true;
            flow.PauseOnFocusLoss = true;

            flow.OnApplicationFocus(false);
            Assert.AreEqual(GameState.Countdown, flow.State, "the countdown takes no pause; the song is already scheduled");
            Assert.AreEqual(0, clock.PauseCalls);

            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Paused, flow.State, "the window is still unfocused when the song starts");
            Assert.AreEqual(1, clock.PauseCalls);
            Assert.IsTrue(clock.Paused);
            Assert.AreEqual(0f, Time.timeScale);
            CollectionAssert.Contains(CanvasTexts(), PauseText);

            // Back in the window the usual Paused → Resuming → Playing works.
            flow.OnApplicationFocus(true);
            flow.ResumeGame();
            flow.Advance(ResumeTotalSec);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.AreEqual(1, clock.ResumeCalls);
        }

        [UnityTest]
        public IEnumerator FocusRegainedDuringCountdown_DoesNotPause()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachCountdown(flow, ClickClip(Band.Low, null), "focus regained");
            Bootstrap.Instance.Chaser.NoCatch = true;
            flow.PauseOnFocusLoss = true;

            flow.OnApplicationFocus(false);
            flow.OnApplicationFocus(true);
            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);

            Assert.AreEqual(GameState.Playing, flow.State, "focus was back before the song started");
            Assert.AreEqual(0, clock.PauseCalls);
        }

        [UnityTest]
        public IEnumerator PauseOnFocusLossOff_IgnoresFocusLossInEveryState()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachCountdown(flow, ClickClip(Band.Low, null), "focus off");
            Bootstrap.Instance.Chaser.NoCatch = true;
            Assert.IsFalse(flow.PauseOnFocusLoss, "the test SetUp turns it off");

            flow.OnApplicationFocus(false); // lost during the countdown and never regained
            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Playing, flow.State, "the countdown check is skipped too");

            flow.OnApplicationFocus(false);
            Assert.AreEqual(GameState.Playing, flow.State);
            Assert.AreEqual(0, clock.PauseCalls);

            flow.PauseGame();
            flow.ResumeGame();
            flow.OnApplicationFocus(false);
            Assert.AreEqual(GameState.Resuming, flow.State, "the resume countdown keeps running");
            Assert.AreEqual(1, clock.PauseCalls, "only the PauseGame call paused the song");
        }

        [UnityTest]
        public IEnumerator Paused_Q_ReturnsToSelectFileAndClearsEverything()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            AudioClip clip = ClickClip(Band.Low, null);
            yield return ReachPlaying(flow, clip, "quit");
            Bootstrap.Instance.Chaser.NoCatch = true;
            Transform obstacle = Bootstrap.Instance.Level.ObstacleFor(0);
            Assert.IsNotNull(obstacle);

            flow.PauseGame();
            Assert.AreEqual(GameState.Paused, flow.State);
            yield return Tap(keyboard.qKey);
            yield return null; // Destroy runs at the end of the frame

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsNull(flow.Map);
            Assert.IsNull(flow.LastError);
            Assert.IsTrue(clip == null, "the song clip is released");
            Assert.IsTrue(obstacle == null, "the level is cleared");
            Assert.IsFalse(Bootstrap.Instance.Player.gameObject.activeSelf);
            Assert.IsFalse(Bootstrap.Instance.Chaser.gameObject.activeSelf);
            Assert.IsFalse(Bootstrap.Instance.Hud.gameObject.activeSelf);
            CollectionAssert.DoesNotContain(CanvasTexts(), PauseText);
        }

        [UnityTest]
        public IEnumerator SongEnd_RunsEscapeStagesInOrder_ThenResultEscaped()
        {
            var flow = Bootstrap.Instance.Flow;
            yield return ReachPlaying(flow, ClickClip(Band.Low, Band.High), "escape");
            flow.AutoPlay = true;
            Bootstrap b = Bootstrap.Instance;
            BeatMap map = flow.Map;
            foreach (Note note in map.notes)
            {
                clock.Time = note.time;
                flow.Step(clock.Time, StepDt);
            }
            Assert.AreEqual(GameState.Playing, flow.State);

            clock.Time = map.songLength;
            flow.Step(clock.Time, StepDt);

            Assert.AreEqual(GameState.Escaped, flow.State);
            float exitX = b.Level.ExitX;
            float neighborX = b.Chaser.transform.position.x;
            float arriveSec = GameConfig.ExitDistanceAfterEnd / GameConfig.ScrollSpeed;
            var stages = new List<string> { flow.SequenceStage };
            SpriteRenderer playerBody = b.Player.GetComponent<SpriteRenderer>();
            Assert.IsFalse(b.Level.ExitIsOpen, "the exit door starts closed");

            for (int i = 0; i < MaxSequenceAdvances && flow.State == GameState.Escaped; i++)
            {
                flow.Advance(AdvanceDt);
                Assert.AreEqual(neighborX, b.Chaser.transform.position.x, PositionTolerance, "the neighbor stands still");
                if (flow.State != GameState.Escaped || flow.SequenceStage == stages[stages.Count - 1]) continue;

                stages.Add(flow.SequenceStage);
                switch (flow.SequenceStage)
                {
                    case GameFlow.StageOpen:
                        Assert.IsTrue(b.Level.ExitIsOpen);
                        Assert.Less(b.Player.transform.position.x, exitX, "the door opens before the player arrives");
                        break;
                    case GameFlow.StageEnter:
                        Assert.AreEqual(exitX, b.Player.transform.position.x, PositionTolerance);
                        Assert.IsTrue(playerBody.enabled);
                        break;
                    case GameFlow.StageClose:
                        Assert.IsFalse(playerBody.enabled, "the player has gone through the door");
                        Assert.IsTrue(b.Level.ExitIsOpen);
                        break;
                    case GameFlow.StageBanner:
                        Assert.IsFalse(b.Level.ExitIsOpen, "the door closes");
                        CollectionAssert.Contains(CanvasTexts(), EscapedText);
                        break;
                }
            }

            CollectionAssert.AreEqual(
                new[] { GameFlow.StageRun, GameFlow.StageOpen, GameFlow.StageEnter, GameFlow.StageClose, GameFlow.StageBanner }, stages);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.IsNull(flow.SequenceStage);
            Assert.AreEqual(1f, Time.timeScale);

            // [Seq] times follow the table in spec 8.2 (±0.1s).
            var seq = SeqTimes("Escaped");
            Assert.AreEqual(0f, seq[GameFlow.StageRun], SeqTimeTolerance);
            Assert.AreEqual(arriveSec - GameConfig.EscapeDoorOpenSec, seq[GameFlow.StageOpen], SeqTimeTolerance);
            Assert.AreEqual(arriveSec, seq[GameFlow.StageEnter], SeqTimeTolerance);
            Assert.AreEqual(arriveSec + GameConfig.EscapeEnterSec, seq[GameFlow.StageClose], SeqTimeTolerance);
            Assert.AreEqual(arriveSec + GameConfig.EscapeEnterSec + GameConfig.EscapeDoorCloseSec, seq[GameFlow.StageBanner], SeqTimeTolerance);

            ResultData d = flow.LastResult;
            Assert.IsTrue(d.escaped);
            Assert.AreEqual("escape", d.songName);
            Assert.AreEqual(map.difficulty, d.difficulty);
            Assert.AreEqual(flow.Judge.Score, d.score);
            Assert.AreEqual(flow.Judge.PerfectCount, d.perfect);
            Assert.AreEqual(flow.Judge.GoodCount, d.good);
            Assert.AreEqual(flow.Judge.MissCount, d.miss);
            Assert.AreEqual(flow.Judge.MaxCombo, d.maxCombo);
            Assert.Greater(d.score, 0);
            Assert.IsTrue(b.Result.Visible);
            StringAssert.Contains("escape", b.Result.Text);
            Assert.IsFalse(b.Hud.gameObject.activeSelf, "HUD and timing bar are hidden under the result panel");
        }

        [UnityTest]
        public IEnumerator NoInput_GetsCaught_RunsCaughtStagesInOrder_ThenResultWithJudgeCounts()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "caught");
            Bootstrap b = Bootstrap.Instance;
            BeatMap map = flow.Map;

            // A few perfect notes first so the result has a score and a combo, then no input until the neighbor catches up.
            flow.AutoPlay = true;
            const int perfectNotes = 5;
            for (int i = 0; i < perfectNotes; i++)
            {
                clock.Time = map.notes[i].time;
                flow.Step(clock.Time, StepDt);
            }
            flow.AutoPlay = false;
            Assert.AreEqual(perfectNotes, flow.Judge.PerfectCount);

            for (double t = map.notes[perfectNotes].time; t < map.songLength && flow.State == GameState.Playing; t += AdvanceDt)
            {
                clock.Time = t;
                flow.Step(t, AdvanceDt);
            }
            Assert.AreEqual(GameState.Caught, flow.State, "no input must end in being caught before the song ends");
            Assert.AreEqual(GameFlow.StageSlow, flow.SequenceStage);
            Assert.IsFalse(b.Hud.gameObject.activeSelf, "HUD and timing bar hide when the slowdown starts");

            float startX = b.Player.transform.position.x;
            var stages = new List<string> { flow.SequenceStage };
            for (int i = 0; i < MaxSequenceAdvances && flow.State == GameState.Caught; i++)
            {
                flow.Advance(AdvanceDt);
                if (flow.State != GameState.Caught || flow.SequenceStage == stages[stages.Count - 1]) continue;

                stages.Add(flow.SequenceStage);
                float gap = b.Player.transform.position.x - b.Chaser.transform.position.x;
                switch (flow.SequenceStage)
                {
                    case GameFlow.StageStare:
                        // Speed ScrollSpeed → 0 (easeOut) integrates to ScrollSpeed × CaughtSlowSec / 3.
                        Assert.AreEqual(startX + GameConfig.ScrollSpeed * GameConfig.CaughtSlowSec / 3f, b.Player.transform.position.x, 0.02f);
                        Assert.AreEqual(GameConfig.ChaserFaceOffset, gap, 0.02f);
                        break;
                    case GameFlow.StageWindup:
                        Assert.AreEqual(GameConfig.ChaserStandOffset, gap, 0.02f, "one step closer for the swing");
                        break;
                    case GameFlow.StageHit:
                        Assert.AreEqual(0f, Time.timeScale, "hit stop");
                        break;
                    case GameFlow.StageLinger:
                        Assert.AreEqual(1f, Time.timeScale, "the hit stop is over");
                        Assert.AreEqual(GameConfig.ChaserStandOffset, gap, 0.02f);
                        break;
                    case GameFlow.StageGameOver:
                        CollectionAssert.Contains(CanvasTexts(), GameOverText);
                        break;
                }
            }

            CollectionAssert.AreEqual(
                new[] { GameFlow.StageSlow, GameFlow.StageStare, GameFlow.StageWindup, GameFlow.StageHit, GameFlow.StageLinger, GameFlow.StageGameOver },
                stages);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.AreEqual(1f, Time.timeScale);

            // [Seq] times follow the table in spec 8.3 (±0.1s).
            float stare = GameConfig.CaughtSlowSec;
            float windup = stare + GameConfig.CaughtStareSec;
            float hit = windup + GameConfig.CaughtWindupSec;
            var seq = SeqTimes("Caught");
            Assert.AreEqual(0f, seq[GameFlow.StageSlow], SeqTimeTolerance);
            Assert.AreEqual(stare, seq[GameFlow.StageStare], SeqTimeTolerance);
            Assert.AreEqual(stare + GameConfig.CaughtTurnDelaySec, seq[GameFlow.StageTurn], SeqTimeTolerance);
            Assert.AreEqual(windup, seq[GameFlow.StageWindup], SeqTimeTolerance);
            Assert.AreEqual(hit, seq[GameFlow.StageHit], SeqTimeTolerance);
            Assert.AreEqual(hit, seq[GameFlow.StageLinger], SeqTimeTolerance);
            Assert.AreEqual(hit + GameConfig.CaughtLingerSec, seq[GameFlow.StageGameOver], SeqTimeTolerance);

            ResultData d = flow.LastResult;
            Assert.IsFalse(d.escaped);
            Assert.AreEqual("caught", d.songName);
            Assert.AreEqual(Difficulty.Hard, d.difficulty);
            Assert.AreEqual(flow.Judge.Score, d.score);
            Assert.AreEqual(perfectNotes, d.perfect);
            Assert.AreEqual(flow.Judge.GoodCount, d.good);
            Assert.AreEqual(flow.Judge.MissCount, d.miss);
            Assert.Greater(d.miss, 0);
            Assert.AreEqual(flow.Judge.MaxCombo, d.maxCombo);
            Assert.GreaterOrEqual(d.maxCombo, perfectNotes);
            Assert.Greater(d.score, 0);
            Assert.IsTrue(b.Result.Visible);
            StringAssert.Contains("caught", b.Result.Text);
            StringAssert.Contains(MenuScreens.DifficultyName(Difficulty.Hard), b.Result.Text);
        }

        [UnityTest]
        public IEnumerator Result_ArrowsChangeOnlyDifficulty_ThenRetryRegeneratesMapAndResetsTheRound()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            Bootstrap b = Bootstrap.Instance;
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Mid, null); // Door notes
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);
            yield return ReachPlaying(flow, clip, "retry");
            Assert.AreEqual(Difficulty.Normal, flow.Map.difficulty);

            // The same analysis the flow ran, to compute the expected Easy map without re-running the flow.
            Task<AnalysisOutput> reference = BeatMapGenerator.AnalyzeDetailedAsync(
                samples, 1, AnalysisSampleRate, ClipSeconds, new AnalysisProgress(), Difficulty.Normal);
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (!reference.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.IsTrue(reference.IsCompleted, "reference analysis did not finish");
            AnalysisOutput analysis = reference.Result;
            BeatMap expectedEasy = BeatMapGenerator.Generate(analysis.onsets, analysis.bpm, analysis.map.songLength, Difficulty.Easy);

            // Play a round with perfect door hits, so there is state to reset.
            flow.AutoPlay = true;
            BeatMap firstMap = flow.Map;
            int normalCount = firstMap.notes.Count;
            foreach (Note note in firstMap.notes)
            {
                clock.Time = note.time;
                flow.Step(clock.Time, StepDt);
            }
            Assert.Greater(flow.Judge.Score, 0);
            Assert.Greater(flow.Judge.Combo, GameConfig.ComboMilestones[0], "the combo passed the first milestone");
            Assert.Greater(b.Chaser.Distance, GameConfig.ChaserStartDistance, "the neighbor was pushed back");
            Transform oldDoor = b.Level.ObstacleFor(0);
            Assert.IsNotNull(oldDoor);

            clock.Time = firstMap.songLength;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Escaped, flow.State);
            flow.Advance(RunToEndSec);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.IsFalse(b.Player.GetComponent<SpriteRenderer>().enabled, "the player went through the door");

            // ←/→ in Result change CurrentDifficulty one step at a time (no wrap) and the screen's difficulty only.
            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty);
            StringAssert.Contains(MenuScreens.DifficultyName(Difficulty.Easy), b.Result.Text);
            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty, "must stop at Easy, not wrap around");
            yield return Tap(keyboard.rightArrowKey);
            Assert.AreEqual(Difficulty.Normal, flow.CurrentDifficulty);
            StringAssert.Contains(MenuScreens.DifficultyName(Difficulty.Normal), b.Result.Text);
            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.AreSame(firstMap, flow.Map, "the map is not touched until R");
            Assert.AreEqual(Difficulty.Normal, flow.LastResult.difficulty, "the result still describes the round that was played");

            // R: same clip, no re-analysis, map regenerated for the new difficulty, countdown without a preview.
            var states = new List<GameState>();
            flow.StateChanged += (prev, next) => states.Add(next);
            yield return Tap(keyboard.rKey);

            CollectionAssert.AreEqual(new[] { GameState.Countdown }, states, "no Analyzing and no Preview");
            Assert.IsTrue(clip != null, "the clip is reused");
            Assert.AreNotSame(firstMap, flow.Map);
            Assert.AreEqual(Difficulty.Easy, flow.Map.difficulty);
            Assert.AreEqual(expectedEasy.notes.Count, flow.Map.notes.Count, "same notes as BeatMapGenerator.Generate for Easy");
            Assert.Less(expectedEasy.notes.Count, normalCount);
            Assert.AreEqual(-ResumeTotalSec, clock.Time, PositionTolerance);
            yield return null; // the old level is destroyed at the end of the frame

            Assert.IsFalse(b.Result.Visible);
            Assert.AreEqual(0, flow.Judge.Score);
            Assert.AreEqual(0, flow.Judge.Combo);
            Assert.AreEqual(0, flow.Judge.MaxCombo);
            Assert.AreEqual(0, flow.Judge.PerfectCount + flow.Judge.GoodCount + flow.Judge.MissCount);
            Assert.IsTrue(flow.Map.notes.All(n => !n.judged));
            Assert.AreEqual(GameConfig.ChaserStartDistance, b.Chaser.Distance, PositionTolerance);
            Assert.IsFalse(b.Chaser.IsCaught);
            Assert.IsTrue(oldDoor == null, "the previous level, including the door that flew away, is gone");
            Transform door = b.Level.ObstacleFor(0);
            Assert.IsNotNull(door);
            Assert.IsTrue(door.gameObject.activeSelf);
            Assert.AreEqual(b.Level.TimeToX(flow.Map.notes[0].time), door.position.x, PositionTolerance, "a fresh door in its original place");
            Assert.IsFalse(b.Level.ExitIsOpen);
            Assert.IsTrue(b.Player.GetComponent<SpriteRenderer>().enabled, "the player is back");
            Assert.AreEqual(0f, b.Player.transform.position.x, PositionTolerance);
            Assert.IsTrue(b.Hud.gameObject.activeSelf);
            Assert.AreEqual(1f, Time.timeScale);

            // The new round plays: the countdown ends in Playing with the new map.
            clock.Time = 0.0;
            flow.Step(clock.Time, StepDt);
            Assert.AreEqual(GameState.Playing, flow.State);
        }

        [UnityTest]
        public IEnumerator Result_Retry_WithSameDifficulty_KeepsTheMap()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.ForceEnd = "escaped";
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "same");
            BeatMap map = flow.Map;
            Assert.AreEqual(GameState.Escaped, flow.State, "forceEnd=escaped starts the escape on entering Playing");

            // forceEnd skips the round, so judge a note by hand: the kept map must come back with the flag cleared and the score gone.
            Note first = map.notes[0];
            flow.Judge.OnInput(RhythmJudge.RequiredAction(first.kind), first.time);
            Assert.IsTrue(first.judged);
            Assert.Greater(flow.Judge.Score, 0);
            Assert.Greater(flow.Judge.PerfectCount + flow.Judge.GoodCount, 0);

            flow.Advance(RunToEndSec);
            Assert.AreEqual(GameState.Result, flow.State);

            flow.Retry();

            Assert.AreEqual(GameState.Countdown, flow.State);
            Assert.AreSame(map, flow.Map, "no regeneration when the difficulty did not change");
            Assert.IsFalse(Bootstrap.Instance.Result.Visible);
            Assert.IsTrue(map.notes.All(n => !n.judged), "the reused map starts the new round unjudged");
            Assert.AreEqual(0, flow.Judge.Score);
            Assert.AreEqual(0, flow.Judge.Combo);
            Assert.AreEqual(0, flow.Judge.MaxCombo);
            Assert.AreEqual(0, flow.Judge.PerfectCount);
            Assert.AreEqual(0, flow.Judge.GoodCount);
            Assert.AreEqual(0, flow.Judge.MissCount);
        }

        [UnityTest]
        public IEnumerator Caught_Result_R_ResetsEverythingTheCaughtSequenceChanged()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard; // dense enough that no input ends in being caught before the song ends
            yield return ReachPlaying(flow, ClickClip(Band.Low, null), "retry caught");
            Bootstrap b = Bootstrap.Instance;
            BeatMap map = flow.Map;

            flow.AutoPlay = true;
            const int perfectNotes = 5;
            for (int i = 0; i < perfectNotes; i++)
            {
                clock.Time = map.notes[i].time;
                flow.Step(clock.Time, StepDt);
            }
            flow.AutoPlay = false;
            for (double t = map.notes[perfectNotes].time; t < map.songLength && flow.State == GameState.Playing; t += AdvanceDt)
            {
                clock.Time = t;
                flow.Step(t, AdvanceDt);
            }
            Assert.AreEqual(GameState.Caught, flow.State);

            // Real frames for a moment so the real-time music fade-out is under way, then jump to the end of the sequence.
            yield return new WaitForSecondsRealtime(FadeObserveSec);
            Assert.AreEqual(GameState.Caught, flow.State);
            Assert.Less(b.Conductor.Volume, 1f, "the music is fading out");
            flow.Advance(RunToEndSec);
            Assert.AreEqual(GameState.Result, flow.State);

            // Everything Retry must undo is really set at this point.
            Assert.IsTrue(b.Player.IsFlattened);
            Assert.IsFalse(b.Player.FacingRight);
            Assert.AreEqual(0f, b.Player.RunAnimScale);
            Assert.AreEqual(2, b.Chaser.SwingFrame);
            Assert.IsTrue(b.CameraDirector.ShakeActive, "the hit shake has not run out");
            Assert.Less(b.Conductor.Volume, 1f, "the fade-out was cut short by the jump to Result");
            Assert.Greater(flow.Judge.Score, 0);
            Assert.Greater(flow.Judge.MaxCombo, 0);
            Assert.IsTrue(map.notes.Any(n => n.judged));

            flow.Retry();

            Assert.AreEqual(GameState.Countdown, flow.State);
            Assert.IsTrue(b.Player.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(b.Player.IsFlattened, "the player is no longer squashed");
            Assert.IsTrue(b.Player.FacingRight);
            Assert.AreEqual(PlayerController.Reaction.None, b.Player.CurrentReaction);
            Assert.IsFalse(b.Player.SurpriseMarkVisible);
            Assert.AreEqual(1f, b.Player.RunAnimScale);
            Assert.AreEqual(-1, b.Chaser.SwingFrame, "the neighbor puts the broom down");
            Assert.AreEqual(1f, b.Conductor.Volume);
            Assert.IsFalse(b.CameraDirector.ShakeActive, "the camera does not keep shaking into the countdown");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, flow.Judge.Score);
            Assert.AreEqual(0, flow.Judge.Combo);
            Assert.AreEqual(0, flow.Judge.MaxCombo);
            Assert.AreEqual(0, flow.Judge.PerfectCount + flow.Judge.GoodCount + flow.Judge.MissCount);
            Assert.IsTrue(map.notes.All(n => !n.judged));

            // The cancelled fade coroutine is still alive until its next frame; it must leave the volume alone.
            yield return null;
            yield return null;
            Assert.AreEqual(1f, b.Conductor.Volume, "a cancelled fade does nothing");
            Assert.IsFalse(b.CameraDirector.ShakeActive);
        }

        [UnityTest]
        public IEnumerator ForceEndCaught_RealFrames_ReachesResultWithoutManualAdvance()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.ForceEnd = "caught";
            yield return ReachCountdown(flow, ClickClip(Band.Low, null), "real caught");

            // Update alone runs everything from here: Step at song time 0 → Playing → forced Caught, then Advance with the right dt,
            // including the 0.08s hit stop (Time.timeScale 0), which only ends if it is counted in unscaled time.
            float sequenceSec = GameConfig.CaughtSlowSec + GameConfig.CaughtStareSec + GameConfig.CaughtWindupSec
                + GameConfig.CaughtHitStopSec + GameConfig.CaughtLingerSec + GameConfig.GameOverHoldSec;
            clock.Time = 0.0;
            double deadline = Time.realtimeSinceStartupAsDouble + sequenceSec + RealTimeMarginSec;
            while (flow.State != GameState.Result && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            Assert.AreEqual(GameState.Result, flow.State, "stuck in " + flow.State + " / " + flow.SequenceStage);
            Assert.IsFalse(flow.LastResult.escaped);
            Assert.AreEqual(1f, Time.timeScale);
            var seq = SeqTimes("Caught");
            CollectionAssert.IsSubsetOf(
                new[] { GameFlow.StageSlow, GameFlow.StageStare, GameFlow.StageWindup, GameFlow.StageHit, GameFlow.StageLinger, GameFlow.StageGameOver },
                seq.Keys, "every stage ran, so the hit stop ended");
        }

        [UnityTest]
        public IEnumerator ForceEndCaught_ThenResult_ThenN_ReturnsToSelectFileAndReleasesClip()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            flow.PresentationTimeScale = 2f;
            flow.ForceEnd = "caught";
            AudioClip clip = ClickClip(Band.Low, null);

            yield return ReachPreview(flow, clip, "forced");
            Assert.AreEqual(2f, Time.timeScale, "Preview runs at the development time scale");
            flow.SkipPreview();
            Assert.AreEqual(1f, Time.timeScale, "Countdown runs at normal speed");

            clock.Time = 0.0;
            flow.Step(clock.Time, 0f);
            Assert.AreEqual(GameState.Caught, flow.State, "forceEnd=caught starts the sequence on entering Playing");
            Assert.AreEqual(GameFlow.StageSlow, flow.SequenceStage);
            Assert.AreEqual(2f, Time.timeScale, "Caught runs at the development time scale");

            flow.Advance(RunToEndSec);
            Assert.AreEqual(GameState.Result, flow.State);
            Assert.IsFalse(flow.LastResult.escaped);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(Bootstrap.Instance.Result.Visible);

            yield return Tap(keyboard.nKey);
            yield return null; // Destroy runs at the end of the frame

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsNull(flow.Map);
            Assert.IsTrue(clip == null, "the clip is released");
            Assert.IsFalse(Bootstrap.Instance.Result.Visible);
            Assert.IsFalse(Bootstrap.Instance.Player.gameObject.activeSelf);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator StartFromClip_Silence_ReturnsToSelectFileWithError()
        {
            var flow = Bootstrap.Instance.Flow;
            var samples = new float[(int)(AnalysisSampleRate * ClipSeconds)];
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);

            flow.StartFromClip(clip, "silence");
            yield return WaitWhileAnalyzing(flow);

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.AreEqual(NoRhythmMessage, flow.LastError);
            Assert.IsNull(flow.Map);
        }

        [UnityTest]
        public IEnumerator Preview_Esc_ReturnsToSelectFileAndReleasesSong()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.DebugPreview = true; // startAt=debug path: analysis debug screen
            var keyboard = InputSystem.AddDevice<Keyboard>();
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);
            flow.StartFromClip(clip, "click");
            yield return WaitWhileAnalyzing(flow);
            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);

            Press(keyboard.escapeKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Preview; i++)
                yield return null;
            Release(keyboard.escapeKey);
            yield return null; // Destroy runs at the end of the frame

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsNull(flow.Map);
            Assert.IsNull(flow.LastError);
            Assert.IsTrue(clip == null, "the song clip must be destroyed when Preview ends");
        }

        [UnityTest]
        public IEnumerator FlythroughPreview_Esc_ReturnsToSelectFileAndRestoresTimeScale()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
            flow.PresentationTimeScale = 3f;
            AudioClip clip = ClickClip(Band.Low, null);
            yield return ReachPreview(flow, clip, "click");
            Assert.AreEqual(3f, Time.timeScale);

            yield return Tap(keyboard.escapeKey);
            yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsNull(flow.Map);
            Assert.IsTrue(clip == null);
            Assert.IsFalse(Bootstrap.Instance.CameraDirector.FlythroughActive);
        }

        [UnityTest]
        public IEnumerator Analyzing_Esc_CancelsAndLateResultNeverReachesPreview()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);
            var changes = new List<GameState>();
            flow.StateChanged += (prev, next) => changes.Add(next);

            flow.StartFromClip(clip, "click");
            Assert.AreEqual(GameState.Analyzing, flow.State);
            // Esc is handled before the result poll in the next Update, so the cancel always wins.
            Press(keyboard.escapeKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Analyzing; i++)
                yield return null;
            Release(keyboard.escapeKey);
            Assert.AreEqual(GameState.SelectFile, flow.State);

            // An identical analysis started later finishes after the abandoned one; then run extra frames.
            Task<AnalysisOutput> reference = BeatMapGenerator.AnalyzeDetailedAsync(
                samples, 1, AnalysisSampleRate, ClipSeconds, new AnalysisProgress(), GameConfig.DefaultDifficulty);
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (!reference.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.IsTrue(reference.IsCompleted, "reference analysis did not finish");
            for (int i = 0; i < PostCancelFrames; i++)
                yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsNull(flow.Map);
            Assert.IsNull(flow.LastError);
            Assert.IsFalse(changes.Contains(GameState.Preview), "a cancelled analysis must never reach Preview");
            Assert.IsFalse(changes.Contains(GameState.Playing), "a cancelled analysis must never reach Playing");
            Assert.IsTrue(clip == null, "the song clip must be destroyed when the analysis is cancelled");
        }

        [UnityTest]
        public IEnumerator StartFromClip_WhileAnalyzing_OnlySecondResultIsUsed()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.DebugPreview = true; // startAt=debug path: analysis debug screen
            float[] first = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            float[] second = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, SecondClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip firstClip = TestAudio.ToClip(first, AnalysisSampleRate);
            AudioClip secondClip = TestAudio.ToClip(second, AnalysisSampleRate);
            int previewEntries = 0;
            flow.StateChanged += (prev, next) => { if (next == GameState.Preview) previewEntries++; };

            flow.StartFromClip(firstClip, "first");
            Assert.AreEqual(GameState.Analyzing, flow.State);
            flow.StartFromClip(secondClip, "second");
            Assert.AreEqual(GameState.Analyzing, flow.State);

            yield return WaitWhileAnalyzing(flow);
            yield return null;

            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);
            Assert.AreEqual(SecondClickBpm, flow.Map.bpm, BpmTolerance);
            Assert.AreEqual(1, previewEntries);
            Assert.IsTrue(firstClip == null, "the replaced clip must be destroyed");
            Assert.IsFalse(secondClip == null, "the clip of the second analysis must stay alive in Preview");
        }

        // ───────────── 도우미 ─────────────

        static AudioClip ClickClip(Band onBeat, Band? offBeat)
        {
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, onBeat, offBeat);
            return TestAudio.ToClip(samples, AnalysisSampleRate);
        }

        /// <summary>Starts the analysis of the clip and runs frames until the flow reaches the flythrough Preview.</summary>
        static IEnumerator ReachPreview(GameFlow flow, AudioClip clip, string songName)
        {
            flow.StartFromClip(clip, songName);
            yield return WaitWhileAnalyzing(flow);
            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);
        }

        /// <summary>Preview → Countdown by skipping the flythrough.</summary>
        IEnumerator ReachCountdown(GameFlow flow, AudioClip clip, string songName)
        {
            yield return ReachPreview(flow, clip, songName);
            flow.SkipPreview();
            Assert.AreEqual(GameState.Countdown, flow.State);
        }

        /// <summary>Preview → Countdown → song time 0 → Playing (or the forced sequence when flow.ForceEnd is set).</summary>
        IEnumerator ReachPlaying(GameFlow flow, AudioClip clip, string songName)
        {
            yield return ReachCountdown(flow, clip, songName);
            clock.Time = 0.0;
            flow.Step(clock.Time, 0f);
            if (string.IsNullOrEmpty(flow.ForceEnd)) Assert.AreEqual(GameState.Playing, flow.State);
        }

        /// <summary>One key press over two frames, so the next press registers as a new press.</summary>
        IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>Text of every active UI Text under the canvas (screens and overlays).</summary>
        static List<string> CanvasTexts()
        {
            return Bootstrap.Instance.Canvas.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
        }

        /// <summary>The countdown number ("3", "2" or "1") currently shown on the canvas, or null.</summary>
        static string CountdownLabel()
        {
            List<string> texts = CanvasTexts();
            return new[] { "3", "2", "1" }.FirstOrDefault(texts.Contains);
        }

        /// <summary>First logged time per stage of a [Seq] sequence (e.g. "Caught").</summary>
        Dictionary<string, float> SeqTimes(string sequence)
        {
            var times = new Dictionary<string, float>();
            foreach (string line in seqLogs)
            {
                Match m = SeqLine.Match(line);
                Assert.IsTrue(m.Success, "unexpected [Seq] format: " + line);
                if (m.Groups[1].Value != sequence || times.ContainsKey(m.Groups[2].Value)) continue;
                times[m.Groups[2].Value] = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return times;
        }

        /// <summary>Runs frames until the flow leaves Analyzing (or the real-time timeout passes).</summary>
        static IEnumerator WaitWhileAnalyzing(GameFlow flow)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (flow.State == GameState.Analyzing && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
        }
    }
}
