using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace NoiseRun.Tests
{
    /// <summary>
    /// HPSS 타악기 분석(구현명세 5.3). 지속음(디스토션 기타·보컬 흉내)이 깔린 드럼 곡은 타악기 플럭스로,
    /// 드럼 없는 지속음만 있는 곡은 원음 플럭스로 분석해야 한다.
    /// </summary>
    public class HpssTests
    {
        // ---- 신호 생성 인자(메인 세션이 같은 신호로 percussiveRatio를 재서 PercussiveRatioMin을 정한다) ----
        const int SampleRate = 44100;
        const double TrackSeconds = 32.0;
        const float Bpm = 120f;
        /// <summary>첫 정박 시각(0이 아니게, 한 박(0.5초)보다 작게).</summary>
        const double FirstBeatSec = 0.15;
        const uint DrumSeed = 20261115u;
        const uint SustainedSeed = 20261115u;
        /// <summary>지속음 tanh 클리핑 세기(0이면 클리핑 없음).</summary>
        const float SustainedDrive = 1.0f;
        /// <summary>믹스 이득. 드럼(최대 진폭 1) 대 지속음(최대 진폭 약 1) = 1 : 0.1.</summary>
        const float DrumMixGain = 1f;
        const float SustainedMixGain = 0.1f;

        // ---- 판정 기준 ----
        /// <summary>온셋이 히트 근처라고 보는 거리 = 칸 인정 창(SlotOnsetWindowRatio × 반박자, 120 BPM에서 37.5ms).</summary>
        const double HitToleranceSec = GameConfig.SlotOnsetWindowRatio * 30.0 / Bpm;
        /// <summary>히트에서 먼 온셋의 허용 비율. 드럼 꼬리·음 전환 때문에 남는 몇 개에 주는 여유.</summary>
        const double MaxFarOnsetFraction = 0.10;
        /// <summary>온셋이 하나라도 잡힌 히트의 최소 비율(온셋을 아예 안 만들어도 통과하는 것을 막는다).</summary>
        const double MinHitCoverage = 0.9;
        /// <summary>히트 칸에 놓인 노트 중 종류가 맞는 최소 비율.</summary>
        const double MinKindAccuracy = 0.8;
        /// <summary>종류별로 노트가 된 칸의 최소 비율. 어려움은 후보 중 에너지 상위 CandidateEnergyRatio만 남기고, 탈락이 한 종류에 몰릴 수 있어서 넉넉히 잡는다.</summary>
        const double MinKindCoverage = 0.5;
        const double SlotTimeTolerance = 1e-6;

        List<TestAudio.DrumHit> hits;
        OnsetResult mixed;
        OnsetResult sustainedOnly;

        [OneTimeSetUp]
        public void BuildSignalsAndDetect()
        {
            hits = TestAudio.DrumHits(TrackSeconds, Bpm, FirstBeatSec);
            float[] drums = TestAudio.DrumTrack(SampleRate, TrackSeconds, Bpm, FirstBeatSec, DrumSeed);
            float[] sustained = TestAudio.SustainedTrack(SampleRate, TrackSeconds, SustainedSeed, SustainedDrive);
            mixed = OnsetDetector.Detect(TestAudio.Mix(drums, DrumMixGain, sustained, SustainedMixGain), SampleRate, null);
            sustainedOnly = OnsetDetector.Detect(sustained, SampleRate, null);
        }

        static ObstacleKind ExpectedKind(TestAudio.DrumKind kind)
        {
            switch (kind)
            {
                case TestAudio.DrumKind.Kick: return ObstacleKind.Box;
                case TestAudio.DrumKind.Snare: return ObstacleKind.Door;
                default: return ObstacleKind.Laundry;
            }
        }

        double DistanceToNearestHit(double time)
        {
            double best = double.MaxValue;
            foreach (TestAudio.DrumHit hit in hits) best = Math.Min(best, Math.Abs(hit.time - time));
            return best;
        }

        // ---------------------------------------------------------------- 지속음 + 드럼

        [Test]
        public void SustainedPlusDrums_UsesPercussiveAnalysis()
        {
            string values = " (percussiveRatio=" + mixed.percussiveRatio.ToString("F4") + ", min=" + GameConfig.PercussiveRatioMin + ")";
            Assert.IsTrue(mixed.percussive, "drums over a sustained bed must switch to the percussive flux" + values);
            Assert.GreaterOrEqual(mixed.percussiveRatio, GameConfig.PercussiveRatioMin, values);
        }

        [Test]
        public void SustainedPlusDrums_OnsetsStayNearDrumHits()
        {
            Assert.IsTrue(mixed.percussive, "precondition: percussive analysis (percussiveRatio=" + mixed.percussiveRatio.ToString("F4") + ")");
            Assert.Greater(mixed.onsets.Count, 0, "no onsets at all");

            int far = 0;
            foreach (Onset o in mixed.onsets)
                if (DistanceToNearestHit(o.time) > HitToleranceSec) far++;
            double farFraction = (double)far / mixed.onsets.Count;
            Assert.LessOrEqual(farFraction, MaxFarOnsetFraction,
                far + " of " + mixed.onsets.Count + " onsets are farther than " + HitToleranceSec + "s from every drum hit");

            int covered = 0;
            foreach (TestAudio.DrumHit hit in hits)
            {
                foreach (Onset o in mixed.onsets)
                {
                    if (Math.Abs(o.time - hit.time) > HitToleranceSec) continue;
                    covered++;
                    break;
                }
            }
            Assert.GreaterOrEqual((double)covered / hits.Count, MinHitCoverage,
                "only " + covered + " of " + hits.Count + " drum hits have an onset within " + HitToleranceSec + "s");
        }

        [Test]
        public void SustainedPlusDrums_HardMap_KickBoxSnareDoorHiHatLaundry()
        {
            Assert.IsTrue(mixed.percussive, "precondition: percussive analysis (percussiveRatio=" + mixed.percussiveRatio.ToString("F4") + ")");
            // 정답 BPM·위상으로 칸을 만든다(BPM 추정은 이 테스트의 대상이 아니다). 칸 번호 = 히트 번호.
            var bpm = new BpmResult { bpm = Bpm, firstBeatTime = FirstBeatSec, confidence = 1f };

            BeatMap map = BeatMapGenerator.Generate(mixed, bpm, TrackSeconds, Difficulty.Hard);

            var kinds = (TestAudio.DrumKind[])Enum.GetValues(typeof(TestAudio.DrumKind));
            var slots = new Dictionary<TestAudio.DrumKind, int>();
            var notes = new Dictionary<TestAudio.DrumKind, int>();
            var correct = new Dictionary<TestAudio.DrumKind, int>();
            foreach (TestAudio.DrumKind k in kinds) { slots[k] = 0; notes[k] = 0; correct[k] = 0; }

            // 노트가 들어갈 수 있는 칸: 앞 비움·끝 비움 사이
            foreach (TestAudio.DrumHit hit in hits)
                if (hit.time >= GameConfig.LeadInEmptySec && hit.time <= TrackSeconds - GameConfig.EndEmptySec) slots[hit.kind]++;

            foreach (Note n in map.notes)
            {
                Assert.Less(n.slotIndex, hits.Count, "note slot " + n.slotIndex + " is past the last drum hit");
                TestAudio.DrumHit hit = hits[n.slotIndex];
                Assert.AreEqual(hit.time, n.time, SlotTimeTolerance, "slot " + n.slotIndex + " is not the hit's time");
                notes[hit.kind]++;
                if (n.kind == ExpectedKind(hit.kind)) correct[hit.kind]++;
            }

            foreach (TestAudio.DrumKind k in kinds)
            {
                Assert.Greater(slots[k], 0, k + ": the test signal has no such hit inside the playable range");
                Assert.GreaterOrEqual((double)notes[k] / slots[k], MinKindCoverage,
                    k + ": only " + notes[k] + " of " + slots[k] + " slots became notes");
                Assert.GreaterOrEqual((double)correct[k] / notes[k], MinKindAccuracy,
                    k + ": only " + correct[k] + " of " + notes[k] + " notes are " + ExpectedKind(k));
            }
        }

        // ---------------------------------------------------------------- 지속음만

        [Test]
        public void SustainedOnly_FallsBackToMixFlux()
        {
            string values = " (percussiveRatio=" + sustainedOnly.percussiveRatio.ToString("F4") + ", min=" + GameConfig.PercussiveRatioMin + ")";
            Assert.IsFalse(sustainedOnly.percussive, "a signal without drums must keep the original flux" + values);
            Assert.Less(sustainedOnly.percussiveRatio, GameConfig.PercussiveRatioMin, values);
            // 원음 플럭스로 되돌아가도 온셋은 그대로 나온다(분석 불가로 막히지 않는다)
            Assert.Greater(sustainedOnly.onsets.Count, 0, "the original-flux path found no onsets in the sustained notes");
        }
    }
}
