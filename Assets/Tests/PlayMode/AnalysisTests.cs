using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class AnalysisTests
    {
        const int SampleRate = 44100;
        const int Channels = 1;
        const float TaskTimeoutSec = 60f;

        // spec 4.6 / T3 acceptance criteria
        const float BpmTolerance = 2f;
        const double PhaseToleranceSec = 0.03;
        const double GridToleranceSec = 0.001;

        /// <summary>First beat as a fraction of the beat length (non-zero, so the phase search has something to find).</summary>
        const double FirstBeatFraction = 0.3;
        const double TrackSeconds = GameConfig.MinSongLengthSec * 3;
        const double ShortTrackSeconds = GameConfig.MinSongLengthSec / 2;
        const double SilenceSeconds = GameConfig.MinSongLengthSec * 2;
        const double LongTrackSeconds = 240.0;
        const float LongTrackBpm = 105f;
        const int FallbackCurveFrames = 2000;
        const int MinDistinctProgressValues = 5;
        /// <summary>Longest allowed gap between two main-thread frames while the worker analyzes (generous: CI jitter, GC, editor hiccups).</summary>
        const float MaxFrameGapSec = 0.25f;
        const int MinFramesDuringAnalysis = 2;
        /// <summary>Share of the expected note slots that must actually become notes (clicks are exact, so nearly all of them should).
        /// Hard keeps only the top CandidateEnergyRatio of the slots by energy (spec 5.6 B), so expectations are scaled by it.</summary>
        const double MinNoteFraction = 0.8 * GameConfig.CandidateEnergyRatio;
        const int StereoChannels = 2;

        // synthetic strength curves for the estimator unit tests
        const uint CurveSeed = 20241115u;
        const float ImpulseDensity = 0.03f;
        const int ImpulseCurveFrames = 6000;
        const int OctaveCurveFrames = 3000;
        /// <summary>Pulse period in frames. The true beat; the autocorrelation peak sits at twice this lag.</summary>
        const int OctavePulsePeriodFrames = 30;
        /// <summary>Every second pulse is this much weaker, so r(2P) beats r(P) although P is the real beat (r(P)/r(2P) stays above BpmHalfLagRatio).</summary>
        const float OctaveWeakPulse = 0.4f;
        /// <summary>Non-periodic clutter impulses (amplitude, per-frame probability): they add energy to r0 but no correlation,
        /// so r(2P)/r0 lands just above BpmConfidenceMin while r(P)/r0 stays below it.</summary>
        const float OctaveClutterAmplitude = 2f;
        const float OctaveClutterDensity = 0.045f;

        sealed class Holder { public AnalysisOutput Output; }

        // ---------------------------------------------------------------- helpers

        /// <summary>Waits for a Task without blocking the main thread; fails the test on timeout.</summary>
        static IEnumerator WaitFor(Task task)
        {
            float start = Time.realtimeSinceStartup;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup - start > TaskTimeoutSec)
                    Assert.Fail("Analysis task did not finish within " + TaskTimeoutSec + "s");
                yield return null;
            }
        }

        static IEnumerator Analyze(float[] samples, Holder holder, int channels = Channels)
        {
            double songLength = (double)samples.Length / channels / SampleRate;
            var task = BeatMapGenerator.AnalyzeDetailedAsync(samples, channels, SampleRate, songLength, new AnalysisProgress(), Difficulty.Hard);
            yield return WaitFor(task);
            Assert.IsFalse(task.IsFaulted, task.Exception != null ? task.Exception.ToString() : "");
            holder.Output = task.Result;
        }

        static IEnumerator ExpectAnalysisFailure(float[] mono)
        {
            double songLength = (double)mono.Length / SampleRate;
            var task = BeatMapGenerator.AnalyzeDetailedAsync(mono, Channels, SampleRate, songLength, new AnalysisProgress(), Difficulty.Hard);
            yield return WaitFor(task);
            Assert.IsTrue(task.IsFaulted);
            var failed = task.Exception.InnerException as AnalysisFailedException;
            Assert.IsNotNull(failed, "expected AnalysisFailedException but got " + task.Exception.InnerException);
            Assert.AreEqual(BeatMapGenerator.NoRhythmMessage, failed.Message);
        }

        /// <summary>Distance between two times on a circle of the given period.</summary>
        static double CircularDiff(double a, double b, double period)
        {
            double d = ((a - b) % period + period) % period;
            return Math.Min(d, period - d);
        }

        enum GridPos { OnBeat, HalfBeat, Other }

        /// <summary>Where a note sits relative to the beats the test audio was generated with.</summary>
        static GridPos InputGridPos(double noteTime, double firstBeat, double period)
        {
            if (CircularDiff(noteTime, firstBeat, period) <= PhaseToleranceSec) return GridPos.OnBeat;
            if (CircularDiff(noteTime, firstBeat + period * 0.5, period) <= PhaseToleranceSec) return GridPos.HalfBeat;
            return GridPos.Other;
        }

        /// <summary>Number of grid slots between the empty lead-in and the empty end, for a click every slotSec.</summary>
        static double ExpectedSlots(double slotSec)
        {
            return (TrackSeconds - GameConfig.LeadInEmptySec - GameConfig.EndEmptySec) / slotSec;
        }

        static float NextUnit(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return (state >> 8) / 16777216f;
        }

        /// <summary>Non-periodic impulses at pseudo-random frames (fixed seed, so deterministic).</summary>
        static float[] RandomImpulseCurve()
        {
            var curve = new float[ImpulseCurveFrames];
            uint state = CurveSeed;
            for (int i = 0; i < curve.Length; i++)
                if (NextUnit(ref state) < ImpulseDensity) curve[i] = 0.5f + NextUnit(ref state);
            return curve;
        }

        /// <summary>Pulses every OctavePulsePeriodFrames (every second one weak) plus non-periodic clutter impulses.</summary>
        static float[] AlternatingPulseCurve()
        {
            var curve = new float[OctaveCurveFrames];
            uint state = CurveSeed;
            for (int i = 0; i < curve.Length; i++)
                if (NextUnit(ref state) < OctaveClutterDensity) curve[i] = OctaveClutterAmplitude;
            for (int k = 0; k * OctavePulsePeriodFrames < curve.Length; k++)
                curve[k * OctavePulsePeriodFrames] += k % 2 == 0 ? 1f : OctaveWeakPulse;
            return curve;
        }

        /// <summary>The estimator's o' (curve minus its moving average, clamped at 0), recomputed here to check the test's preconditions.</summary>
        static double[] Detrend(float[] o, double hopSec)
        {
            int m = o.Length;
            int w = (int)Math.Round(GameConfig.BpmDetrendHalfWindowSec / hopSec);
            var op = new double[m];
            for (int i = 0; i < m; i++)
            {
                int lo = Math.Max(0, i - w);
                int hi = Math.Min(m - 1, i + w);
                double sum = 0.0;
                for (int j = lo; j <= hi; j++) sum += o[j];
                op[i] = Math.Max(0.0, o[i] - sum / (hi - lo + 1));
            }
            return op;
        }

        static double Autocorr(double[] op, int lag)
        {
            double sum = 0.0;
            for (int i = 0; i + lag < op.Length; i++) sum += op[i] * op[i + lag];
            return sum / (op.Length - lag);
        }

        static IEnumerator AssertBpmAndPhase(float bpm)
        {
            double period = 60.0 / bpm;
            double firstBeat = period * FirstBeatFraction;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, bpm, firstBeat, Band.Low, null);

            var holder = new Holder();
            yield return Analyze(mono, holder);

            BeatMap map = holder.Output.map;
            Assert.AreEqual(bpm, map.bpm, BpmTolerance);
            Assert.IsFalse(map.bpmFallbackUsed);
            Assert.GreaterOrEqual(map.firstBeatTime, 0.0);
            Assert.Less(map.firstBeatTime, 60.0 / map.bpm);
            // The grid is half-beat based, so a phase that lands on the half beat is the same grid; compare against the beat.
            Assert.LessOrEqual(CircularDiff(map.firstBeatTime, firstBeat, period), PhaseToleranceSec);
        }

        // ---------------------------------------------------------------- BPM / phase

        [UnityTest]
        public IEnumerator ClickTrack120Bpm_EstimatesBpmAndPhase()
        {
            yield return AssertBpmAndPhase(120f);
        }

        [UnityTest]
        public IEnumerator ClickTrack90Bpm_EstimatesBpmAndPhase()
        {
            yield return AssertBpmAndPhase(90f);
        }

        [UnityTest]
        public IEnumerator ClickTrack150Bpm_EstimatesBpmAndPhase()
        {
            yield return AssertBpmAndPhase(150f);
        }

        static IEnumerator AssertStereoBpmAndPhase(bool left, bool right)
        {
            const float bpm = 120f;
            double period = 60.0 / bpm;
            double firstBeat = period * FirstBeatFraction;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, bpm, firstBeat, Band.Low, null);
            float[] stereo = TestAudio.ToInterleavedStereo(mono, left, right);

            var holder = new Holder();
            yield return Analyze(stereo, holder, StereoChannels);

            BeatMap map = holder.Output.map;
            Assert.AreEqual(bpm, map.bpm, BpmTolerance);
            Assert.IsFalse(map.bpmFallbackUsed);
            Assert.AreEqual(TrackSeconds, map.songLength, 1.0 / SampleRate, "song length must count frames, not interleaved samples");
            Assert.LessOrEqual(CircularDiff(map.firstBeatTime, firstBeat, period), PhaseToleranceSec);
        }

        [UnityTest]
        public IEnumerator StereoDualMono_EstimatesBpmAndPhase()
        {
            yield return AssertStereoBpmAndPhase(true, true);
        }

        [UnityTest]
        public IEnumerator StereoLeftChannelOnly_EstimatesBpmAndPhase()
        {
            yield return AssertStereoBpmAndPhase(true, false);
        }

        // ---------------------------------------------------------------- grid

        [UnityTest]
        public IEnumerator Notes_LieOnHalfBeatGrid_OnePerSlot_LeadInAndEndEmpty()
        {
            const float bpm = 120f;
            double period = 60.0 / bpm;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, bpm, period * FirstBeatFraction, Band.Low, Band.High);

            var holder = new Holder();
            yield return Analyze(mono, holder);

            BeatMap map = holder.Output.map;
            Assert.IsNotNull(map.notes);
            Assert.GreaterOrEqual(map.notes.Count, MinNoteFraction * ExpectedSlots(period * 0.5), "too few notes for a click on every half beat");

            var seen = new HashSet<int>();
            int previousSlot = int.MinValue;
            foreach (Note n in map.notes)
            {
                Assert.LessOrEqual(Math.Abs(n.time - map.SlotTime(n.slotIndex)), GridToleranceSec);
                // the slot grid must also match the input's half-beat grid (the line above is true by construction)
                Assert.LessOrEqual(CircularDiff(n.time, period * FirstBeatFraction, period * 0.5), PhaseToleranceSec,
                    "note at " + n.time + " is off the input's half-beat grid");
                Assert.IsTrue(seen.Add(n.slotIndex), "slot " + n.slotIndex + " has more than one note");
                Assert.Greater(n.slotIndex, previousSlot, "notes must be sorted by slotIndex");
                previousSlot = n.slotIndex;
                Assert.GreaterOrEqual(n.time, GameConfig.LeadInEmptySec);
                Assert.LessOrEqual(n.time, map.songLength - GameConfig.EndEmptySec);
            }
        }

        // ---------------------------------------------------------------- band -> obstacle

        [UnityTest]
        public IEnumerator LowOnBeat_HighOffBeat_BoxOnBeat_LaundryOffBeat()
        {
            const float bpm = 120f;
            double period = 60.0 / bpm;
            double firstBeat = period * FirstBeatFraction;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, bpm, firstBeat, Band.Low, Band.High);

            var holder = new Holder();
            yield return Analyze(mono, holder);

            BeatMap map = holder.Output.map;
            int box = 0, laundry = 0;
            foreach (Note n in map.notes)
            {
                switch (InputGridPos(n.time, firstBeat, period))
                {
                    case GridPos.OnBeat:
                        Assert.AreEqual(ObstacleKind.Box, n.kind, "on-beat note at " + n.time);
                        box++;
                        break;
                    case GridPos.HalfBeat:
                        Assert.AreEqual(ObstacleKind.Laundry, n.kind, "off-beat note at " + n.time);
                        laundry++;
                        break;
                    default:
                        Assert.Fail("note at " + n.time + " is neither on the beat nor on the half beat");
                        break;
                }
            }
            double expectedPerKind = MinNoteFraction * ExpectedSlots(period);
            Assert.GreaterOrEqual(box, expectedPerKind, "too few on-beat boxes");
            Assert.GreaterOrEqual(laundry, expectedPerKind, "too few off-beat laundry");
        }

        [UnityTest]
        public IEnumerator MidOnBeat_NotesAreDoors()
        {
            const float bpm = 120f;
            double period = 60.0 / bpm;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, bpm, period * FirstBeatFraction, Band.Mid, null);

            var holder = new Holder();
            yield return Analyze(mono, holder);

            BeatMap map = holder.Output.map;
            Assert.GreaterOrEqual(map.notes.Count, MinNoteFraction * ExpectedSlots(period), "too few notes for a click on every beat");
            foreach (Note n in map.notes)
                Assert.AreEqual(ObstacleKind.Door, n.kind, "note at " + n.time);
        }

        // ---------------------------------------------------------------- failures

        [UnityTest]
        public IEnumerator Silence_FailsWithNoRhythmError()
        {
            var silence = new float[(int)(SilenceSeconds * SampleRate)];
            yield return ExpectAnalysisFailure(silence);
        }

        [UnityTest]
        public IEnumerator TooShortSong_FailsWithNoRhythmError()
        {
            // plenty of onsets, but shorter than MinSongLengthSec
            float[] mono = TestAudio.ClickTrack(SampleRate, ShortTrackSeconds, 120f, 0.1, Band.Low, Band.High);
            yield return ExpectAnalysisFailure(mono);
        }

        [Test]
        public void ConstantStrengthCurve_UsesFallbackBpm()
        {
            var curve = new float[FallbackCurveFrames];
            for (int i = 0; i < curve.Length; i++) curve[i] = 1f;
            double hopSec = (double)GameConfig.HopSize / SampleRate;
            double songLength = curve.Length * hopSec;

            BpmResult r = BpmEstimator.Estimate(curve, hopSec, songLength);

            Assert.IsTrue(r.fallback);
            Assert.AreEqual(GameConfig.FallbackBpm, r.bpm);
            Assert.GreaterOrEqual(r.firstBeatTime, 0.0);
            Assert.Less(r.firstBeatTime, 60.0 / r.bpm);
        }

        [Test]
        public void ZeroStrengthCurve_UsesFallbackBpmWithoutThrowing()
        {
            var curve = new float[FallbackCurveFrames];
            double hopSec = (double)GameConfig.HopSize / SampleRate;

            BpmResult r = null;
            Assert.DoesNotThrow(() => r = BpmEstimator.Estimate(curve, hopSec, curve.Length * hopSec));

            Assert.IsTrue(r.fallback);
            Assert.AreEqual(GameConfig.FallbackBpm, r.bpm);
        }

        [Test]
        public void RandomImpulseCurve_LowConfidence_UsesFallbackBpm()
        {
            float[] curve = RandomImpulseCurve();
            double hopSec = (double)GameConfig.HopSize / SampleRate;

            BpmResult r = BpmEstimator.Estimate(curve, hopSec, curve.Length * hopSec);

            // r0 > 0 and some lag has positive correlation, so this is the confidence branch, not the r0 == 0 one
            Assert.Greater(r.confidence, 0f);
            Assert.Less(r.confidence, GameConfig.BpmConfidenceMin);
            Assert.IsTrue(r.fallback);
            Assert.AreEqual(GameConfig.FallbackBpm, r.bpm);
            Assert.GreaterOrEqual(r.firstBeatTime, 0.0);
            Assert.Less(r.firstBeatTime, 60.0 / r.bpm);
        }

        [Test]
        public void HalfLagCorrection_ConfidenceIsJudgedBeforeTheReplacement()
        {
            float[] curve = AlternatingPulseCurve();
            double hopSec = (double)GameConfig.HopSize / SampleRate;

            // Preconditions: r(2P) is the peak and confident, r(P) is strong enough for the octave correction
            // but, taken on its own, below the confidence limit. Judging after the replacement would fall back.
            double[] op = Detrend(curve, hopSec);
            double r0 = Autocorr(op, 0);
            double rFull = Autocorr(op, 2 * OctavePulsePeriodFrames);
            double rHalf = Autocorr(op, OctavePulsePeriodFrames);
            string values = " (r(2P)/r0=" + (rFull / r0).ToString("F4") + ", r(P)/r0=" + (rHalf / r0).ToString("F4") + ")";
            Assert.GreaterOrEqual(rFull / r0, GameConfig.BpmConfidenceMin, "test curve: r(2P) not confident" + values);
            Assert.Less(rHalf / r0, GameConfig.BpmConfidenceMin, "test curve: r(P) not below the limit" + values);
            Assert.GreaterOrEqual(rHalf, GameConfig.BpmHalfLagRatio * rFull, "test curve: octave correction would not trigger" + values);

            BpmResult r = BpmEstimator.Estimate(curve, hopSec, curve.Length * hopSec);

            Assert.IsFalse(r.fallback, "a confident tempo must not fall back after the octave correction" + values);
            Assert.AreEqual(rFull / r0, r.confidence, 1e-4, "confidence must be the pre-replacement value" + values);
            Assert.AreEqual(60.0 / (OctavePulsePeriodFrames * hopSec), r.bpm, BpmTolerance, "the half lag (the real beat) must be chosen" + values);
        }

        // ---------------------------------------------------------------- progress / main thread

        [UnityTest]
        public IEnumerator LongSong_ProgressIsMonotonicAndMainThreadKeepsRunning()
        {
            double period = 60.0 / LongTrackBpm;
            double firstBeat = period * FirstBeatFraction;
            float[] mono = TestAudio.ClickTrack(SampleRate, LongTrackSeconds, LongTrackBpm, firstBeat, Band.Low, Band.High);
            double songLength = (double)mono.Length / SampleRate;

            var progress = new AnalysisProgress();
            Task<AnalysisOutput> task = BeatMapGenerator.AnalyzeDetailedAsync(mono, Channels, SampleRate, songLength, progress, Difficulty.Hard);

            var samples = new List<float>();
            int framesDuringAnalysis = 0;
            float maxFrameGap = 0f;
            float start = Time.realtimeSinceStartup;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup - start > TaskTimeoutSec)
                    Assert.Fail("Analysis task did not finish within " + TaskTimeoutSec + "s");
                samples.Add(progress.value);
                // the first delta still belongs to the frame that built the test audio
                if (framesDuringAnalysis > 0) maxFrameGap = Mathf.Max(maxFrameGap, Time.unscaledDeltaTime);
                framesDuringAnalysis++;
                yield return null;
            }
            Assert.IsFalse(task.IsFaulted, task.Exception != null ? task.Exception.ToString() : "");
            samples.Add(progress.value);

            AnalysisOutput output = task.Result;
            Debug.Log("[AnalysisTests] " + LongTrackSeconds + "s track: worker " + output.elapsedSec.ToString("F2")
                + "s, " + framesDuringAnalysis + " main-thread frames during analysis (longest gap " + maxFrameGap.ToString("F3") + "s), "
                + output.onsets.onsets.Count + " onsets, " + output.map.notes.Count + " notes");

            var distinct = new HashSet<float>(samples);
            for (int i = 1; i < samples.Count; i++)
                Assert.GreaterOrEqual(samples[i], samples[i - 1], "progress went backwards at sample " + i);
            Assert.GreaterOrEqual(samples[0], 0f);
            Assert.AreEqual(1f, samples[samples.Count - 1]);
            Assert.GreaterOrEqual(distinct.Count, MinDistinctProgressValues);
            Assert.GreaterOrEqual(framesDuringAnalysis, MinFramesDuringAnalysis);
            Assert.LessOrEqual(maxFrameGap, MaxFrameGapSec, "the main thread stalled while the worker was analyzing");

            Assert.AreEqual(LongTrackBpm, output.map.bpm, BpmTolerance);
            // low on the beat + high on the half beat: either click may be picked as "the beat", so compare on the half-beat circle
            Assert.LessOrEqual(CircularDiff(output.map.firstBeatTime, firstBeat, period * 0.5), PhaseToleranceSec);
        }

        // ---------------------------------------------------------------- difficulty (spec 5.7)

        [TestCase(5, Difficulty.Normal, 4)]
        [TestCase(15, Difficulty.Normal, 11)]
        [TestCase(5, Difficulty.Easy, 2)]
        [TestCase(1, Difficulty.Hard, 1)]
        [TestCase(7, Difficulty.Hard, 7)]
        [TestCase(123, Difficulty.Hard, 123)]
        public void KeepCount_RoundsRatioTimesCountAwayFromZero(int candidateCount, Difficulty difficulty, int expected)
        {
            Assert.AreEqual(expected, BeatMapGenerator.KeepCount(candidateCount, difficulty));
        }

        [UnityTest]
        public IEnumerator Difficulty_KeepsOnBeatsFirstThenStrongest_AsNestedSubsets()
        {
            double period = 60.0 / 120f;
            float[] mono = TestAudio.ClickTrack(SampleRate, TrackSeconds, 120f, period * FirstBeatFraction, Band.Low, Band.High);
            var holder = new Holder();
            yield return Analyze(mono, holder);
            AnalysisOutput a = holder.Output;

            List<BeatMapGenerator.NoteCandidate> candidates = BeatMapGenerator.Candidates(a.onsets, a.bpm, a.map.songLength);
            Assert.Greater(candidates.Count, 0);

            BeatMap easy = BeatMapGenerator.Generate(a.onsets, a.bpm, a.map.songLength, Difficulty.Easy);
            BeatMap normal = BeatMapGenerator.Generate(a.onsets, a.bpm, a.map.songLength, Difficulty.Normal);
            BeatMap hard = BeatMapGenerator.Generate(a.onsets, a.bpm, a.map.songLength, Difficulty.Hard);

            Assert.AreEqual(Difficulty.Easy, easy.difficulty);
            Assert.AreEqual(Difficulty.Normal, normal.difficulty);
            Assert.AreEqual(Difficulty.Hard, hard.difficulty);

            Assert.AreEqual(BeatMapGenerator.KeepCount(candidates.Count, Difficulty.Easy), easy.notes.Count);
            Assert.AreEqual(BeatMapGenerator.KeepCount(candidates.Count, Difficulty.Normal), normal.notes.Count);
            Assert.AreEqual(candidates.Count, hard.notes.Count, "Hard keeps every candidate");
            Assert.Less(easy.notes.Count, normal.notes.Count);
            Assert.Less(normal.notes.Count, hard.notes.Count);

            AssertOnBeatsFirstThenStrongest(easy, candidates);
            AssertOnBeatsFirstThenStrongest(normal, candidates);
            AssertSlotSubset(easy, normal);
            AssertSlotSubset(normal, hard);

            for (int i = 0; i < hard.notes.Count; i++)
                Assert.AreEqual(candidates[i].slotIndex, hard.notes[i].slotIndex);
        }

        // ---------------------------------------------------------------- candidates (spec 5.6 A, B)

        const float HandBpm = 120f;
        const int HandSampleRate = 44100;
        const double HandSongLength = 20.0;
        const int HandFirstSlot = 20;     // 5 s at 120 BPM (half beat 0.25 s), past the lead-in
        const float HandFluxPeak = 10f;

        static double HandHalfBeat => 30.0 / HandBpm;
        static double HandHopSec => (double)GameConfig.HopSize / HandSampleRate;

        /// <summary>Hand-built analysis result: one Low onset per entry (slot, offset in half beats, flux peak), flux 0 elsewhere.</summary>
        static OnsetResult HandOnsets(params (int slot, double offsetHalfBeats, float peak)[] hits)
        {
            int frames = (int)(HandSongLength / HandHopSec);
            var flux = new float[3][];
            for (int b = 0; b < flux.Length; b++) flux[b] = new float[frames];
            var onsets = new List<Onset>();
            foreach (var h in hits)
            {
                double t = (h.slot + h.offsetHalfBeats) * HandHalfBeat;
                int m = (int)Math.Round((t * HandSampleRate - GameConfig.FftSize / 2.0) / GameConfig.HopSize);
                flux[(int)Band.Low][m] = h.peak;
                onsets.Add(new Onset { time = OnsetDetector.FrameTime(m, HandSampleRate), band = Band.Low, strength = 2f });
            }
            return new OnsetResult { bandFlux = flux, onsets = onsets, hopSec = HandHopSec, strengthCurve = new float[frames] };
        }

        static BpmResult HandBpmResult => new BpmResult { bpm = HandBpm, firstBeatTime = 0.0, confidence = 1f };

        [Test]
        public void Candidates_IgnoreOnsetsOutsideTheSlotWindow()
        {
            double inside = GameConfig.SlotOnsetWindowRatio * 0.5;  // half the window: counts
            double outside = GameConfig.SlotOnsetWindowRatio * 2.0; // twice the window: must not be pulled onto the slot
            var hits = new List<(int, double, float)>();
            var allowed = new HashSet<int>();
            for (int i = 0; i < 10; i++)
            {
                int slot = HandFirstSlot + 3 * i;
                hits.Add((slot, 0.0, HandFluxPeak)); allowed.Add(slot);
                hits.Add((slot + 1, inside, HandFluxPeak)); allowed.Add(slot + 1);
                hits.Add((slot + 2, outside, HandFluxPeak));
            }

            List<BeatMapGenerator.NoteCandidate> c = BeatMapGenerator.Candidates(HandOnsets(hits.ToArray()), HandBpmResult, HandSongLength);

            foreach (var x in c)
                Assert.IsTrue(allowed.Contains(x.slotIndex), "slot " + x.slotIndex + " only has an onset outside the window");
            Assert.AreEqual((int)Math.Round(GameConfig.CandidateEnergyRatio * allowed.Count, MidpointRounding.AwayFromZero), c.Count);
        }

        [Test]
        public void Candidates_KeepTheLoudestSlotsByEnergy()
        {
            const int count = 10;
            var hits = new (int, double, float)[count];
            for (int i = 0; i < count; i++) hits[i] = (HandFirstSlot + i, 0.0, i + 1f); // slot energy grows with the index

            List<BeatMapGenerator.NoteCandidate> c = BeatMapGenerator.Candidates(HandOnsets(hits), HandBpmResult, HandSongLength);

            int keep = (int)Math.Round(GameConfig.CandidateEnergyRatio * count, MidpointRounding.AwayFromZero);
            Assert.AreEqual(keep, c.Count);
            for (int i = 0; i < keep; i++)
                Assert.AreEqual(HandFirstSlot + count - keep + i, c[i].slotIndex, "the quietest slots must be the ones dropped");
        }

        static void AssertSlotSubset(BeatMap smaller, BeatMap larger)
        {
            var slots = new HashSet<int>();
            foreach (Note n in larger.notes) slots.Add(n.slotIndex);
            foreach (Note n in smaller.notes)
                Assert.IsTrue(slots.Contains(n.slotIndex), smaller.difficulty + " slot " + n.slotIndex + " missing from " + larger.difficulty);
        }

        /// <summary>Spec 5.7 D: on-beat candidates go before off-beat ones; inside each group the stronger score wins.</summary>
        static void AssertOnBeatsFirstThenStrongest(BeatMap map, List<BeatMapGenerator.NoteCandidate> candidates)
        {
            var kept = new HashSet<int>();
            int prev = int.MinValue;
            foreach (Note n in map.notes)
            {
                Assert.Greater(n.slotIndex, prev, "notes must be in ascending slot order");
                prev = n.slotIndex;
                kept.Add(n.slotIndex);
            }

            bool anyOffBeatKept = false;
            var minKept = new[] { double.MaxValue, double.MaxValue }; // [off-beat, on-beat]
            foreach (var c in candidates)
            {
                if (!kept.Contains(c.slotIndex)) continue;
                if (!c.OnBeat) anyOffBeatKept = true;
                int g = c.OnBeat ? 1 : 0;
                minKept[g] = Math.Min(minKept[g], c.score);
            }
            foreach (var c in candidates)
            {
                if (kept.Contains(c.slotIndex)) continue;
                if (c.OnBeat)
                    Assert.IsFalse(anyOffBeatKept, map.difficulty + ": on-beat slot " + c.slotIndex + " dropped while an off-beat slot was kept");
                Assert.LessOrEqual(c.score, minKept[c.OnBeat ? 1 : 0], map.difficulty + ": dropped slot " + c.slotIndex + " outscores a kept slot of its group");
            }
        }
    }
}
