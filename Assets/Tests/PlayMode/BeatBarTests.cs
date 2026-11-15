using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NoiseRun.Tests
{
    /// <summary>
    /// T6: 타이밍바(BeatBarUI)의 선 위치·대상 노트·정박 아이콘 배율과 HUD 문구.
    /// 오디오 없이 손으로 만든 BeatMap과 곡 시간 값을 직접 넣는다(Refresh).
    /// </summary>
    public class BeatBarTests
    {
        const float Tolerance = 0.001f;
        const string LineName = "TimingLine";
        const string IconName = "BeatIcon";
        const int PulseBeatIndex = 2;   // 몇 번째 정박에서 확인할지
        const int ScoreSample = 1234;
        const int ComboSample = 12;

        // 노트 간격은 타이밍바 리드 시간에서 유도한다: 가까운 노트 = 리드의 절반, 먼 노트 = 리드의 두 배.
        const double NearGapSec = GameConfig.TimingBarLeadSec / 2.0;
        const double FarGapSec = GameConfig.TimingBarLeadSec * 2.0;

        GameObject canvasObject;
        BeatBarUI bar;
        Hud hud;

        [SetUp]
        public void CreateCanvas()
        {
            canvasObject = new GameObject("BeatBarTestsCanvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            bar = BeatBarUI.Create(canvasObject.transform);
            hud = Hud.Create(canvasObject.transform);
        }

        [TearDown]
        public void DestroyCanvas()
        {
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }

        /// <summary>노트 0 = 앞 비움 구간 끝, 노트 1 = 가까운 노트(리드 절반 뒤), 노트 2 = 먼 노트(리드 두 배 뒤).</summary>
        static BeatMap MakeMap()
        {
            double first = GameConfig.LeadInEmptySec;
            var map = new BeatMap
            {
                bpm = GameConfig.FallbackBpm,
                firstBeatTime = 0.0,
                songLength = first + NearGapSec + FarGapSec + GameConfig.EndEmptySec,
                difficulty = Difficulty.Normal,
                notes = new List<Note>(),
            };
            double[] times = { first, first + NearGapSec, first + NearGapSec + FarGapSec };
            foreach (double t in times)
                map.notes.Add(new Note { time = t, kind = ObstacleKind.Box, slotIndex = map.NearestSlot(t) });
            return map;
        }

        int ActiveLineCount()
        {
            int count = 0;
            foreach (RectTransform rt in bar.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == LineName && rt.gameObject.activeSelf) count++;
            return count;
        }

        // ── 정적 함수 ──

        [Test]
        public void LineOffset_AtNoteTime_IsZero()
        {
            Assert.AreEqual(0f, BeatBarUI.LineOffsetPx(0.0), Tolerance);
        }

        [Test]
        public void LineOffset_AtLeadTime_IsHalfWidth()
        {
            Assert.AreEqual(GameConfig.TimingBarWidth / 2f, BeatBarUI.HalfWidthPx, Tolerance);
            Assert.AreEqual(BeatBarUI.HalfWidthPx, BeatBarUI.LineOffsetPx(GameConfig.TimingBarLeadSec), Tolerance);
        }

        [Test]
        public void LineOffset_FartherThanLead_IsClampedToHalfWidth()
        {
            Assert.AreEqual(BeatBarUI.HalfWidthPx, BeatBarUI.LineOffsetPx(GameConfig.TimingBarLeadSec * 2.0), Tolerance);
        }

        [Test]
        public void LineOffset_HalfLead_IsHalfOfHalfWidth()
        {
            Assert.AreEqual(BeatBarUI.HalfWidthPx / 2f, BeatBarUI.LineOffsetPx(NearGapSec), Tolerance);
        }

        [Test]
        public void LineOffset_LateBeyondGoodWindow_IsClampedAtMinimum()
        {
            float minimum = -BeatBarUI.ZoneHalfWidthPx(GameConfig.GoodWindowSec);
            Assert.Less(minimum, 0f);
            Assert.AreEqual(minimum, BeatBarUI.LineOffsetPx(-GameConfig.GoodWindowSec), Tolerance);
            Assert.AreEqual(minimum, BeatBarUI.LineOffsetPx(-GameConfig.TimingBarLeadSec), Tolerance);
            // 늦었지만 Good 창 안이면 그 사이 값(가운데를 조금 지난 위치).
            float inside = BeatBarUI.LineOffsetPx(-GameConfig.PerfectWindowSec);
            Assert.Less(inside, 0f);
            Assert.GreaterOrEqual(inside, minimum);
        }

        [Test]
        public void LineOffset_IsWholePixels()
        {
            double step = GameConfig.TimingBarLeadSec / 7.0;
            for (int i = -2; i <= 9; i++)
            {
                float v = BeatBarUI.LineOffsetPx(i * step);
                Assert.AreEqual(Mathf.Round(v), v, Tolerance, "i=" + i);
            }
        }

        [Test]
        public void ZoneHalfWidth_MatchesWindowRatio()
        {
            float good = BeatBarUI.ZoneHalfWidthPx(GameConfig.GoodWindowSec);
            float perfect = BeatBarUI.ZoneHalfWidthPx(GameConfig.PerfectWindowSec);
            float goodExact = (float)(GameConfig.GoodWindowSec / GameConfig.TimingBarLeadSec) * BeatBarUI.HalfWidthPx;
            float perfectExact = (float)(GameConfig.PerfectWindowSec / GameConfig.TimingBarLeadSec) * BeatBarUI.HalfWidthPx;

            // 정수 픽셀로 반올림하므로 정확한 비율과 최대 반 픽셀(최소 1px 보정이 있으면 그만큼) 차이난다.
            Assert.AreEqual(goodExact, good, 0.5f + Tolerance);
            Assert.AreEqual(perfectExact, perfect, 0.5f + Tolerance);
            Assert.Greater(good, perfect, "Good 창이 Perfect 창보다 넓어야 한다");
            Assert.AreEqual(Mathf.Round(good), good, Tolerance);
            Assert.AreEqual(Mathf.Round(perfect), perfect, Tolerance);
        }

        [Test]
        public void ZoneHalfWidth_IsAtLeastOnePixel()
        {
            Assert.AreEqual(1f, BeatBarUI.ZoneHalfWidthPx(0.0), Tolerance);
        }

        // ── 대상 노트·선 위치 ──

        [Test]
        public void Refresh_AtNoteTime_TargetsThatNoteWithLinesAtCenter()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            bar.Refresh(map.notes[0].time);

            Assert.AreEqual(0, bar.TargetNoteIndex);
            Assert.AreEqual(0f, bar.CurrentLineOffsetPx, Tolerance);
            Assert.AreEqual(2, ActiveLineCount());
        }

        [Test]
        public void Refresh_FarNote_LinesWaitAtEdges()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            bar.Refresh(map.notes[0].time - FarGapSec);

            Assert.AreEqual(0, bar.TargetNoteIndex);
            Assert.AreEqual(BeatBarUI.HalfWidthPx, bar.CurrentLineOffsetPx, Tolerance);
        }

        [Test]
        public void Refresh_LateButUnjudged_KeepsTargetAndClampsOffset()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            bar.Refresh(map.notes[0].time + GameConfig.GoodWindowSec);

            Assert.AreEqual(0, bar.TargetNoteIndex);
            Assert.AreEqual(-BeatBarUI.ZoneHalfWidthPx(GameConfig.GoodWindowSec), bar.CurrentLineOffsetPx, Tolerance);
        }

        [Test]
        public void Judged_RetargetsToNextNote_AndNearNoteLinesJumpToIntermediate()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            double now = map.notes[0].time;
            bar.Refresh(now);
            Assert.AreEqual(0, bar.TargetNoteIndex);

            map.notes[0].judged = true;
            bar.Refresh(now);

            double timeToNext = map.notes[1].time - now;
            Assert.AreEqual(1, bar.TargetNoteIndex);
            Assert.AreEqual(BeatBarUI.LineOffsetPx(timeToNext), bar.CurrentLineOffsetPx, Tolerance);
            Assert.Less(bar.CurrentLineOffsetPx, BeatBarUI.HalfWidthPx, "리드 시간보다 가까우면 끝이 아니라 중간 위치여야 한다");
            Assert.Greater(bar.CurrentLineOffsetPx, 0f);
        }

        [Test]
        public void Judged_FarNextNote_LinesStayAtEdges()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            double now = map.notes[1].time;
            map.notes[0].judged = true;
            map.notes[1].judged = true;
            bar.Refresh(now);

            Assert.AreEqual(2, bar.TargetNoteIndex);
            Assert.AreEqual(BeatBarUI.HalfWidthPx, bar.CurrentLineOffsetPx, Tolerance);
        }

        [Test]
        public void AllJudged_NoTarget_LinesHidden()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            bar.Refresh(map.notes[0].time);
            Assert.AreEqual(2, ActiveLineCount());

            foreach (Note n in map.notes) n.judged = true;
            bar.Refresh(map.notes[map.notes.Count - 1].time);

            Assert.AreEqual(-1, bar.TargetNoteIndex);
            Assert.AreEqual(0f, bar.CurrentLineOffsetPx, Tolerance);
            Assert.AreEqual(0, ActiveLineCount());
        }

        [Test]
        public void ResetJudged_TargetsFirstNoteAgain()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            foreach (Note n in map.notes) n.judged = true;
            bar.Refresh(map.notes[0].time);
            Assert.AreEqual(-1, bar.TargetNoteIndex);

            foreach (Note n in map.notes) n.judged = false; // RhythmJudge.Reset과 같은 상태
            bar.Refresh(map.notes[0].time);
            Assert.AreEqual(0, bar.TargetNoteIndex);
            Assert.AreEqual(2, ActiveLineCount());
        }

        [Test]
        public void Unbind_HidesLinesAndClearsTarget()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            bar.Refresh(map.notes[0].time);

            bar.Bind(null, null);
            Assert.AreEqual(-1, bar.TargetNoteIndex);
            Assert.AreEqual(0, ActiveLineCount());

            bar.Refresh(map.notes[0].time); // 맵 없이 불려도 오류 없이 숨은 채로
            Assert.AreEqual(-1, bar.TargetNoteIndex);
            Assert.AreEqual(0, ActiveLineCount());
        }

        [Test]
        public void SetVisible_TogglesBar()
        {
            Assert.IsFalse(bar.gameObject.activeSelf, "처음에는 숨김");
            bar.SetVisible(true);
            Assert.IsTrue(bar.gameObject.activeSelf);
            bar.SetVisible(false);
            Assert.IsFalse(bar.gameObject.activeSelf);
        }

        // ── 정박 아이콘 ──

        [Test]
        public void IconScale_PulsesRightAfterMainBeat_NotBetweenOrOffBeat()
        {
            BeatMap map = MakeMap();
            map.firstBeatTime = map.HalfBeatSec / 2.0; // 0부터 맞춰져 있지 않은 맵에서도 같아야 한다
            double beatSec = 2.0 * map.HalfBeatSec;
            Assert.Less(GameConfig.BeatIconPulseSec, map.HalfBeatSec, "전제: 펄스가 반박자 안에 끝난다");

            double beat = map.firstBeatTime + PulseBeatIndex * beatSec;
            double pulseHalf = GameConfig.BeatIconPulseSec / 2.0;

            Assert.AreEqual(GameConfig.BeatIconPulseScale, BeatBarUI.IconScale(map, beat), Tolerance);
            Assert.AreEqual(GameConfig.BeatIconPulseScale, BeatBarUI.IconScale(map, beat + pulseHalf), Tolerance);
            Assert.AreEqual(1f, BeatBarUI.IconScale(map, beat + GameConfig.BeatIconPulseSec * 2.0), Tolerance, "박 사이");
            Assert.AreEqual(1f, BeatBarUI.IconScale(map, beat + map.HalfBeatSec + pulseHalf), Tolerance, "반박자 칸 직후");
            Assert.AreEqual(1f, BeatBarUI.IconScale(map, beat + beatSec - pulseHalf), Tolerance, "다음 정박 직전");
        }

        [Test]
        public void IconScale_BeforeSongStart_IsOne()
        {
            BeatMap map = MakeMap();
            Assert.AreEqual(1f, BeatBarUI.IconScale(map, -GameConfig.BeatIconPulseSec / 2.0), Tolerance);
            Assert.AreEqual(1f, BeatBarUI.IconScale(null, 0.0), Tolerance);
        }

        [Test]
        public void Refresh_AppliesIconScaleToIcon()
        {
            BeatMap map = MakeMap();
            bar.Bind(map, null);
            Transform icon = bar.transform.Find(IconName);
            Assert.IsNotNull(icon);

            double beat = map.firstBeatTime + PulseBeatIndex * 2.0 * map.HalfBeatSec;
            bar.Refresh(beat + GameConfig.BeatIconPulseSec / 2.0);
            Assert.AreEqual(GameConfig.BeatIconPulseScale, icon.localScale.x, Tolerance);

            bar.Refresh(beat + GameConfig.BeatIconPulseSec * 2.0);
            Assert.AreEqual(1f, icon.localScale.x, Tolerance);
        }

        // ── HUD ──

        [Test]
        public void Hud_ShowJudgement_SetsLabelAndAdvanceClearsIt()
        {
            Assert.IsNull(hud.JudgementLabel);
            hud.ShowJudgement(Judgement.Perfect);
            Assert.IsNotNull(hud.JudgementLabel);

            hud.Advance(GameConfig.JudgeTextSec / 2f);
            Assert.IsNotNull(hud.JudgementLabel, "표시 시간 전에는 남아 있다");

            hud.Advance(GameConfig.JudgeTextSec);
            Assert.IsNull(hud.JudgementLabel);
        }

        [Test]
        public void Hud_EachJudgementHasDistinctLabel()
        {
            var labels = new HashSet<string>();
            foreach (Judgement j in new[] { Judgement.Perfect, Judgement.Good, Judgement.Miss, Judgement.OffBeat })
            {
                hud.ShowJudgement(j);
                Assert.IsNotNull(hud.JudgementLabel, j.ToString());
                Assert.IsTrue(labels.Add(hud.JudgementLabel), j + " 문구가 다른 판정과 같다");
            }
        }

        [Test]
        public void Hud_EmptyBeat_ShowsNothing()
        {
            hud.ShowJudgement(Judgement.EmptyBeat);
            Assert.IsNull(hud.JudgementLabel);

            hud.ShowJudgement(Judgement.Good);
            string shown = hud.JudgementLabel;
            hud.ShowJudgement(Judgement.EmptyBeat);
            Assert.AreEqual(shown, hud.JudgementLabel, "EmptyBeat는 떠 있는 문구도 바꾸지 않는다");
        }

        [Test]
        public void Hud_ScoreAndCombo_Labels()
        {
            hud.SetScore(ScoreSample);
            Assert.IsTrue(hud.ScoreLabel.Contains(ScoreSample.ToString()), hud.ScoreLabel);

            hud.SetCombo(ComboSample);
            Assert.IsTrue(hud.ComboLabel.Contains(ComboSample.ToString()), hud.ComboLabel);

            hud.SetCombo(0);
            Assert.AreEqual(string.Empty, hud.ComboLabel, "콤보 0이면 숨김(빈 문구)");
        }

        [Test]
        public void Hud_Clear_ResetsEverything()
        {
            hud.SetScore(ScoreSample);
            hud.SetCombo(ComboSample);
            hud.ShowJudgement(Judgement.Miss);

            hud.Clear();

            Assert.IsTrue(hud.ScoreLabel.Contains("0"));
            Assert.IsFalse(hud.ScoreLabel.Contains(ScoreSample.ToString()));
            Assert.AreEqual(string.Empty, hud.ComboLabel);
            Assert.IsNull(hud.JudgementLabel);
        }

        [Test]
        public void Hud_StartsHidden_SetVisibleToggles()
        {
            Assert.IsFalse(hud.gameObject.activeSelf);
            hud.SetVisible(true);
            Assert.IsTrue(hud.gameObject.activeSelf);
            hud.SetVisible(false);
            Assert.IsFalse(hud.gameObject.activeSelf);
        }
    }
}
