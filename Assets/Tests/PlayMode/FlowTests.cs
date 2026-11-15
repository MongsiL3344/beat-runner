using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

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
        static readonly double ClipSeconds = GameConfig.MinSongLengthSec * 2.0;

        // NUnit runs base [SetUp] (InputTestFixture.Setup) before this one,
        // and this [TearDown] before the base TearDown.
        [SetUp]
        public void BootstrapSetUp()
        {
            Bootstrap.Restart(new FakeSongClock());
        }

        [TearDown]
        public void BootstrapTearDown()
        {
            // Leave a fresh, default Title-state Bootstrap for whatever runs next.
            Bootstrap.Restart(null);
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
            flow.DebugPreview = true; // startAt=debug path: Preview instead of Playing
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);

            flow.StartFromClip(clip, "click");
            Assert.AreEqual(GameState.Analyzing, flow.State);

            yield return WaitWhileAnalyzing(flow);

            Assert.AreEqual(GameState.Preview, flow.State, "LastError: " + flow.LastError);
            Assert.IsNotNull(flow.Map);
            Assert.AreEqual(ClickBpm, flow.Map.bpm, BpmTolerance);
            Assert.IsNull(flow.LastError);
        }

        [UnityTest]
        public IEnumerator StartFromClip_ClickTrack_ReachesPlayingWithMap()
        {
            var flow = Bootstrap.Instance.Flow;
            float[] samples = TestAudio.ClickTrack(AnalysisSampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, AnalysisSampleRate);
            var changes = new List<GameState>();
            flow.StateChanged += (prev, next) => changes.Add(next);

            flow.StartFromClip(clip, "click");
            yield return WaitWhileAnalyzing(flow);

            Assert.AreEqual(GameState.Playing, flow.State, "LastError: " + flow.LastError);
            Assert.IsFalse(changes.Contains(GameState.Preview), "T5 goes straight to Playing");
            Assert.AreEqual(ClickBpm, flow.Map.bpm, BpmTolerance);
            Assert.AreEqual(GameConfig.DefaultDifficulty, flow.Map.difficulty);
            Assert.IsNull(flow.LastError);
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
            flow.DebugPreview = true; // startAt=debug path: Preview instead of Playing
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
            flow.DebugPreview = true; // startAt=debug path: Preview instead of Playing
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

        /// <summary>Runs frames until the flow leaves Analyzing (or the real-time timeout passes).</summary>
        static IEnumerator WaitWhileAnalyzing(GameFlow flow)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (flow.State == GameState.Analyzing && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
        }
    }
}
