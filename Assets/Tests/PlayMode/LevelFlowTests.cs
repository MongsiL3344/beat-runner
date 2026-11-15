using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NoiseRun.Tests
{
    /// <summary>
    /// T5 flow: difficulty selection with the arrow keys, analysis → Playing, player running in sync with the song clock,
    /// autoPlay actions and Esc out of Playing. Time is driven by a FakeSongClock and GameFlow.Step.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class LevelFlowTests : InputTestFixture
    {
        const int SampleRate = 44100;
        const float ClickBpm = 120f;
        const double ClickFirstBeatSec = 0.5;
        const double AnalysisTimeoutSec = 30.0;
        const int MaxWaitFrames = 10;
        const float StepDt = 0.016f;
        const float PositionTolerance = 1e-3f;
        const double PastTheEndSec = 100.0;
        const string DifficultyHint = "←/→: 난이도";
        const string SongListTitle = "Songs 폴더";
        const int CrossedNotes = 4;
        static readonly double ClipSeconds = GameConfig.MinSongLengthSec * 2.0;

        FakeSongClock clock;

        // NUnit runs base [SetUp] (InputTestFixture.Setup) before this one, and this [TearDown] before the base TearDown.
        [SetUp]
        public void BootstrapSetUp()
        {
            clock = new FakeSongClock();
            Bootstrap.Restart(clock);
            // These tests cover running, judging and the HUD; long no-input steps would otherwise end the run in Caught (T7, ChaserTests).
            Bootstrap.Instance.Chaser.NoCatch = true;
        }

        [TearDown]
        public void BootstrapTearDown()
        {
            Bootstrap.Restart(null);
        }

        // ───────────── 난이도 선택 ─────────────

        [UnityTest]
        public IEnumerator SelectFile_LeftRight_StepsDifficultyAndStopsAtEnds()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.ChangeState(GameState.SelectFile);
            yield return null;

            Assert.AreEqual(GameConfig.DefaultDifficulty, flow.CurrentDifficulty);
            Assert.AreEqual(Difficulty.Normal, flow.CurrentDifficulty);
            StringAssert.Contains("난이도: 보통", ScreenText());
            StringAssert.Contains(DifficultyHint, ScreenText());

            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty);
            StringAssert.Contains("난이도: 쉬움", ScreenText());

            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty, "must stop at Easy, not wrap around");

            yield return Tap(keyboard.rightArrowKey);
            yield return Tap(keyboard.rightArrowKey);
            Assert.AreEqual(Difficulty.Hard, flow.CurrentDifficulty);
            StringAssert.Contains("난이도: 어려움", ScreenText());

            yield return Tap(keyboard.rightArrowKey);
            Assert.AreEqual(Difficulty.Hard, flow.CurrentDifficulty, "must stop at Hard, not wrap around");
            Assert.AreEqual(GameState.SelectFile, flow.State);
        }

        [UnityTest]
        public IEnumerator SongList_LeftRight_StepsDifficultyAndStopsAtEnds()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.ChangeState(GameState.SelectFile);
            yield return null;

            yield return Tap(keyboard.fKey);
            StringAssert.Contains(SongListTitle, ScreenText());
            Assert.AreEqual(GameState.SelectFile, flow.State);
            StringAssert.Contains(DifficultyHint, ScreenText());

            yield return Tap(keyboard.leftArrowKey);
            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty);
            StringAssert.Contains("난이도: 쉬움", ScreenText());
            StringAssert.Contains(SongListTitle, ScreenText(), "stepping must keep the list screen");

            yield return Tap(keyboard.rightArrowKey);
            yield return Tap(keyboard.rightArrowKey);
            yield return Tap(keyboard.rightArrowKey);
            Assert.AreEqual(Difficulty.Hard, flow.CurrentDifficulty);
            StringAssert.Contains("난이도: 어려움", ScreenText());
        }

        // ───────────── 분석 → Playing ─────────────

        [UnityTest]
        public IEnumerator StartFromClip_UsesChosenDifficulty_AndReachesPlaying()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.ChangeState(GameState.SelectFile);
            yield return null;
            float[] samples = ClickSamples(Band.Low, null);

            yield return Tap(keyboard.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, flow.CurrentDifficulty);
            yield return ReachPlaying(flow, samples, "click-easy");
            Assert.AreEqual(Difficulty.Easy, flow.Map.difficulty);
            int easyCount = flow.Map.notes.Count;

            // Starting again from Playing replaces the previous session.
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, samples, "click-hard");
            Assert.AreEqual(Difficulty.Hard, flow.Map.difficulty);
            Assert.Less(easyCount, flow.Map.notes.Count, "Hard keeps every candidate slot, Easy only the top part");

            Assert.IsTrue(Bootstrap.Instance.Player.gameObject.activeSelf, "the player is shown while Playing");
            Assert.IsNotNull(Bootstrap.Instance.Level.ObstacleFor(0), "the level is built when Playing starts");
        }

        // ───────────── Playing: 달리기·자동 플레이 ─────────────

        [UnityTest]
        public IEnumerator Playing_Step_PlayerXFollowsSongTimeAndStopsAtExit()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = false;

            LevelBuilder level = Bootstrap.Instance.Level;
            PlayerController player = Bootstrap.Instance.Player;
            BeatMap map = flow.Map;

            int index = map.notes.Count / 2;
            clock.Time = map.notes[index].time;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);

            Transform obstacle = level.ObstacleFor(index);
            Assert.IsNotNull(obstacle);
            Assert.AreEqual(obstacle.position.x, player.transform.position.x, PositionTolerance);
            Assert.AreEqual(level.TimeToX(map.notes[index].time), player.transform.position.x, PositionTolerance);
            Assert.IsNull(player.CurrentAction, "without autoPlay the player only runs");

            // Past the end of the song the player stops at the exit.
            clock.Time = map.songLength + PastTheEndSec;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);
            Assert.AreEqual(level.ExitX, player.transform.position.x, PositionTolerance);
            Assert.IsNull(player.CurrentAction);
        }

        [UnityTest]
        public IEnumerator Playing_AutoPlay_FiresCorrectActionPerObstacleKind()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            var seen = new HashSet<ObstacleKind>();

            // Low on-beat + High off-beat → Box and Laundry, Mid on-beat → Door (spec 4.6 AnalysisTests).
            var tracks = new[]
            {
                (name: "low-high", samples: ClickSamples(Band.Low, Band.High)),
                (name: "mid", samples: ClickSamples(Band.Mid, null)),
            };

            foreach (var track in tracks)
            {
                yield return ReachPlaying(flow, track.samples, track.name);
                flow.AutoPlay = true;

                LevelBuilder level = Bootstrap.Instance.Level;
                PlayerController player = Bootstrap.Instance.Player;
                BeatMap map = flow.Map;

                // First note of every kind present, in time order (the autoPlay cursor only moves forward).
                var firstOfKind = new List<int>();
                var kindsHere = new HashSet<ObstacleKind>();
                for (int i = 0; i < map.notes.Count; i++)
                    if (kindsHere.Add(map.notes[i].kind)) firstOfKind.Add(i);

                foreach (int i in firstOfKind)
                {
                    Note note = map.notes[i];
                    clock.Time = note.time;
                    flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt); // may skip several notes at once

                    Transform obstacle = level.ObstacleFor(i);
                    Assert.IsNotNull(obstacle, "note " + i);
                    Assert.AreEqual(obstacle.position.x, player.transform.position.x, PositionTolerance, "note " + i);
                    Assert.AreEqual(ExpectedAction(note.kind), player.CurrentAction, "note " + i + " kind " + note.kind);
                    seen.Add(note.kind);
                }
            }

            CollectionAssert.AreEquivalent(
                new[] { ObstacleKind.Box, ObstacleKind.Laundry, ObstacleKind.Door }, seen.ToArray(), "every obstacle kind must be exercised");
        }

        [UnityTest]
        public IEnumerator Playing_AutoPlay_SameStepTwiceDoesNotRefireOrRestartTheAction()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = true;

            PlayerController player = Bootstrap.Instance.Player;
            BeatMap map = flow.Map;
            float duration = PlayerController.ActionDuration(map.HalfBeatSec);
            double t = map.notes[0].time;
            int expected = map.notes.Count(n => n.time <= t);

            clock.Time = t;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);
            Assert.AreEqual(expected, flow.AutoActionsFired);
            Assert.IsNotNull(player.CurrentAction);

            player.Advance(duration / 2f);
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt); // same song time: nothing new passed
            Assert.AreEqual(expected, flow.AutoActionsFired, "the same note must not fire twice");
            Assert.IsNotNull(player.CurrentAction);

            // Had the second Step restarted the action, 0.6 × duration more would not finish it yet.
            player.Advance(duration * 0.6f);
            Assert.IsNull(player.CurrentAction, "the action must not be restarted by a repeated Step");
        }

        [UnityTest]
        public IEnumerator Playing_AutoPlay_OneStepAcrossManyNotesFiresEachOnce()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = true;

            BeatMap map = flow.Map;
            int crossed = Mathf.Min(CrossedNotes, map.notes.Count);
            Assert.Greater(crossed, 1, "the click track must produce several notes");
            double t = map.notes[crossed - 1].time;
            int expected = map.notes.Count(n => n.time <= t);
            int before = flow.AutoActionsFired;

            clock.Time = t;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);
            Assert.AreEqual(before + expected, flow.AutoActionsFired);
        }

        // ───────────── Playing: Esc ─────────────

        [UnityTest]
        public IEnumerator Playing_Esc_ReturnsToSelectFileAndClearsLevel()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.CurrentDifficulty = Difficulty.Hard;
            float[] samples = ClickSamples(Band.Low, null);
            AudioClip clip = TestAudio.ToClip(samples, SampleRate);
            flow.StartFromClip(clip, "click");
            yield return WaitWhileAnalyzing(flow);
            Assert.AreEqual(GameState.Playing, flow.State, "LastError: " + flow.LastError);

            LevelBuilder level = Bootstrap.Instance.Level;
            Transform obstacle = level.ObstacleFor(0);
            Assert.IsNotNull(obstacle);

            Press(keyboard.escapeKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Playing; i++)
                yield return null;
            Release(keyboard.escapeKey);
            yield return null; // Destroy runs at the end of the frame

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsTrue(obstacle == null, "the obstacle objects must be destroyed");
            Assert.IsTrue(level.ObstacleFor(0) == null, "the level must be empty after leaving Playing");
            Assert.IsFalse(Bootstrap.Instance.Player.gameObject.activeSelf, "the player is hidden outside Playing");
            Assert.IsNull(flow.Map);
            Assert.IsNull(flow.LastError);
            Assert.IsTrue(clip == null, "the song clip must be destroyed when leaving Playing");
        }

        [UnityTest]
        public IEnumerator Playing_Esc_ResetsPlayerPose()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = false;

            PlayerController player = Bootstrap.Instance.Player;
            player.DoAction(ActionType.Jump, PlayerController.ActionDuration(flow.Map.HalfBeatSec));
            player.Advance(StepDt);
            Assert.AreEqual(ActionType.Jump, player.CurrentAction);
            Assert.Greater(player.transform.position.y, GameConfig.FloorY);

            Press(keyboard.escapeKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Playing; i++)
                yield return null;
            Release(keyboard.escapeKey);
            yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsNull(player.CurrentAction, "leaving Playing cancels the running action");
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, PositionTolerance);
        }

        // ───────────── Playing: 박자 판정(T6) ─────────────

        [UnityTest]
        public IEnumerator Playing_SpaceOnBoxNote_JudgesPerfectJumpAndUpdatesHud()
        {
            yield return PressOnNote(ClickSamples(Band.Low, Band.High), ObstacleKind.Box, k => k.spaceKey, ActionType.Jump);
        }

        [UnityTest]
        public IEnumerator Playing_DownOnLaundryNote_JudgesPerfectSlideAndUpdatesHud()
        {
            yield return PressOnNote(ClickSamples(Band.Low, Band.High), ObstacleKind.Laundry, k => k.downArrowKey, ActionType.Slide);
        }

        [UnityTest]
        public IEnumerator Playing_RightOnDoorNote_JudgesPerfectWaveAndUpdatesHud()
        {
            yield return PressOnNote(ClickSamples(Band.Mid, null), ObstacleKind.Door, k => k.rightArrowKey, ActionType.Wave);
        }

        [UnityTest]
        public IEnumerator Playing_WrongKeyOnNote_JudgesMissWithPressedAction()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, Band.High), "low-high");
            flow.AutoPlay = false;

            BeatMap map = flow.Map;
            int index = FirstNoteOfKind(map, ObstacleKind.Box);
            var events = new List<JudgeEvent>();
            PlayerController.Reaction reactionAtJudgement = PlayerController.Reaction.None;
            ActionType? actionAtJudgement = null;
            // 반응 시간(HitFlashSec)이 Tap의 두 프레임 실제 deltaTime보다 짧을 수 있어 판정 직후 상태를 잡는다.
            flow.Judge.Judged += ev =>
            {
                events.Add(ev);
                if (ev.noteIndex != index) return;
                reactionAtJudgement = Bootstrap.Instance.Player.CurrentReaction;
                actionAtJudgement = Bootstrap.Instance.Player.CurrentAction;
            };

            clock.Time = map.notes[index].time;
            yield return Tap(keyboard.downArrowKey); // Box needs Jump

            // 같은 Tick에서 앞 노트들의 무입력 Miss(정답 동작이 Slide인 빨래 포함)도 나오므로 이 노트의 이벤트만 본다.
            Assert.AreEqual(1, events.Count(e => e.noteIndex == index));
            JudgeEvent miss = events.First(e => e.noteIndex == index);
            Assert.AreEqual(ActionType.Slide, miss.action);
            Assert.AreEqual(Judgement.Miss, miss.judgement);
            Assert.AreEqual(index, miss.noteIndex);
            Assert.AreEqual(PlayerController.Reaction.Hit, reactionAtJudgement);
            Assert.IsNull(actionAtJudgement, "a wrong key shows only the hit frame");
        }

        [UnityTest]
        public IEnumerator Playing_KeyBeforeSongStart_IsIgnored()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = false;

            var events = new List<JudgeEvent>();
            flow.Judge.Judged += events.Add;

            clock.Time = -GameConfig.CountdownStepSec;
            yield return Tap(keyboard.spaceKey);
            yield return Tap(keyboard.downArrowKey);
            yield return Tap(keyboard.rightArrowKey);

            Assert.AreEqual(0, events.Count, "inputs while songTime < 0 must not be judged");
            Assert.IsNull(Bootstrap.Instance.Hud.JudgementLabel);
            Assert.AreEqual(0, flow.Judge.Score);
        }

        [UnityTest]
        public IEnumerator Playing_FullAutoPlay_PerfectOnEveryNote()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, Band.High), "low-high");
            flow.AutoPlay = true;

            BeatMap map = flow.Map;
            foreach (Note note in map.notes)
            {
                clock.Time = note.time;
                flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);
            }
            clock.Time = map.songLength;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);

            Assert.AreEqual(0, flow.Judge.MissCount);
            Assert.AreEqual(map.notes.Count, flow.Judge.PerfectCount);
            Assert.AreEqual(map.notes.Count, flow.Judge.MaxCombo);
            Assert.Greater(flow.Judge.Score, 0);
        }

        [UnityTest]
        public IEnumerator Playing_NoInput_MissesEveryNote()
        {
            var flow = Bootstrap.Instance.Flow;
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, Band.High), "low-high");
            flow.AutoPlay = false;

            BeatMap map = flow.Map;
            clock.Time = map.songLength + PastTheEndSec;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);

            Assert.AreEqual(map.notes.Count, flow.Judge.MissCount);
            Assert.AreEqual(0, flow.Judge.PerfectCount);
            Assert.AreEqual(0, flow.Judge.Score);
        }

        [UnityTest]
        public IEnumerator Playing_ShowsHudAndBar_AndEscHidesThem()
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            Assert.IsFalse(Bootstrap.Instance.Hud.gameObject.activeSelf, "the HUD starts hidden");

            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, ClickSamples(Band.Low, null), "click");
            flow.AutoPlay = false;
            Hud hud = Bootstrap.Instance.Hud;
            BeatBarUI bar = Bootstrap.Instance.BeatBar;
            Assert.IsTrue(hud.gameObject.activeSelf);
            Assert.IsNotEmpty(hud.ScoreLabel);
            Assert.AreEqual(0, bar.TargetNoteIndex, "the bar is bound to the first note");

            clock.Time = flow.Map.songLength + PastTheEndSec;
            flow.Step(Bootstrap.Instance.Conductor.SongTime, StepDt);
            Assert.Greater(flow.Judge.MissCount, 0);

            Press(keyboard.escapeKey);
            for (int i = 0; i < MaxWaitFrames && flow.State == GameState.Playing; i++)
                yield return null;
            Release(keyboard.escapeKey);
            yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);
            Assert.IsFalse(hud.gameObject.activeSelf);
            Assert.IsNull(hud.JudgementLabel);
            Assert.AreEqual(-1, bar.TargetNoteIndex, "the bar is unbound outside Playing");
            Assert.AreEqual(0, flow.Judge.Score);
            Assert.AreEqual(0, flow.Judge.MissCount);
        }

        // ───────────── 도우미 ─────────────

        static float[] ClickSamples(Band onBeat, Band? offBeat)
        {
            return TestAudio.ClickTrack(SampleRate, ClipSeconds, ClickBpm, ClickFirstBeatSec, onBeat, offBeat);
        }

        /// <summary>The correct action for an obstacle kind (spec 6.2): Box→Jump, Laundry→Slide, Door→Wave.</summary>
        static ActionType ExpectedAction(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Box: return ActionType.Jump;
                case ObstacleKind.Laundry: return ActionType.Slide;
                case ObstacleKind.Door: return ActionType.Wave;
                default: throw new System.ArgumentException("unexpected kind " + kind);
            }
        }

        static int FirstNoteOfKind(BeatMap map, ObstacleKind kind)
        {
            for (int i = 0; i < map.notes.Count; i++)
                if (map.notes[i].kind == kind) return i;
            Assert.Fail("the track has no " + kind + " note");
            return -1;
        }

        /// <summary>Plays to the first note of the kind, presses the key exactly on it and expects a Perfect with the key's action.</summary>
        IEnumerator PressOnNote(float[] samples, ObstacleKind kind, System.Func<Keyboard, ButtonControl> key, ActionType expected)
        {
            var flow = Bootstrap.Instance.Flow;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            flow.CurrentDifficulty = Difficulty.Hard;
            yield return ReachPlaying(flow, samples, "press-" + kind);
            flow.AutoPlay = false;

            BeatMap map = flow.Map;
            int index = FirstNoteOfKind(map, kind);
            var events = new List<JudgeEvent>();
            ActionType? actionAtJudgement = null;
            // GameFlow의 핸들러(Init에서 먼저 구독)가 동작을 시작한 직후의 상태를 잡는다. Tap의 두 프레임 동안 실제 deltaTime이 동작 시간을 넘길 수 있다.
            flow.Judge.Judged += ev =>
            {
                events.Add(ev);
                if (ev.noteIndex == index) actionAtJudgement = Bootstrap.Instance.Player.CurrentAction;
            };

            clock.Time = map.notes[index].time;
            yield return Tap(key(keyboard));

            Assert.AreEqual(expected, RhythmJudge.RequiredAction(kind));
            JudgeEvent e = events.First(ev => ev.noteIndex == index);
            Assert.AreEqual(Judgement.Perfect, e.judgement);
            Assert.AreEqual(expected, e.action);
            Assert.AreEqual(expected, actionAtJudgement);

            Hud hud = Bootstrap.Instance.Hud;
            Assert.IsNotNull(hud.JudgementLabel);
            Assert.Greater(flow.Judge.Score, 0);
            StringAssert.Contains(flow.Judge.Score.ToString(), hud.ScoreLabel);
        }

        /// <summary>One key press over two frames, so the next press registers as a new press.</summary>
        IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>Starts the analysis of the samples and runs frames until the flow reaches Playing.</summary>
        static IEnumerator ReachPlaying(GameFlow flow, float[] samples, string songName)
        {
            AudioClip clip = TestAudio.ToClip(samples, SampleRate);
            flow.StartFromClip(clip, songName);
            yield return WaitWhileAnalyzing(flow);
            Assert.AreEqual(GameState.Playing, flow.State, "LastError: " + flow.LastError);
            Assert.IsNotNull(flow.Map);
        }

        static IEnumerator WaitWhileAnalyzing(GameFlow flow)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + AnalysisTimeoutSec;
            while (flow.State == GameState.Analyzing && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
        }

        /// <summary>All text of the active menu screen (destroyed screens are deactivated at once).</summary>
        static string ScreenText()
        {
            Text[] texts = Bootstrap.Instance.Canvas.GetComponentsInChildren<Text>();
            return string.Join("\n", texts.Select(t => t.text));
        }
    }
}
