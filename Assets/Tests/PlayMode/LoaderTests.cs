using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class LoaderTests
    {
        const int SampleRate = 44100;
        const double ClipSeconds = 2.0;
        const double LengthShortfallTolerance = 0.01;
        // Unity's mp3 decoder keeps the encoder delay and last-frame padding (measured 2.115s for this 2.000s source).
        const double Mp3MaxPaddingSec = 0.15;
        const double OggMaxPaddingSec = 0.01;
        const float LoadTimeoutSec = 10f;

        sealed class Runner : MonoBehaviour { }

        sealed class LoadOutcome
        {
            public bool Done;
            public bool TimedOut;
            public int LoadedCalls;
            public int ErrorCalls;
            public AudioClip Clip;
            public string Error;
        }

        string _tempDir;
        GameObject _runnerObject;
        AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _tempDir = Path.Combine(projectRoot, "Temp", "noiserun_tests");
            Directory.CreateDirectory(_tempDir);
            _runnerObject = new GameObject("LoaderTestsRunner");
        }

        [TearDown]
        public void TearDown()
        {
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            if (_runnerObject != null) UnityEngine.Object.Destroy(_runnerObject);
            _clip = null;
            _runnerObject = null;
        }

        /// <summary>Runs AudioLoader.Load and waits for exactly one callback, never longer than the timeout.</summary>
        IEnumerator RunLoad(string path, LoadOutcome outcome)
        {
            var runner = _runnerObject.AddComponent<Runner>();
            runner.StartCoroutine(AudioLoader.Load(
                path,
                clip => { outcome.LoadedCalls++; outcome.Clip = clip; outcome.Done = true; },
                err => { outcome.ErrorCalls++; outcome.Error = err; outcome.Done = true; }));

            float start = Time.realtimeSinceStartup;
            while (!outcome.Done)
            {
                if (Time.realtimeSinceStartup - start > LoadTimeoutSec)
                {
                    outcome.TimedOut = true;
                    yield break;
                }
                yield return null;
            }
            // one more frame so any erroneous second callback would be counted
            yield return null;
            _clip = outcome.Clip;
        }

        static string TestDataPath(string fileName)
        {
            return Path.Combine(Application.dataPath, "Tests/PlayMode/TestData/" + fileName);
        }

        IEnumerator AssertStereoCodecFile(string fileName, double maxPaddingSec)
        {
            string path = TestDataPath(fileName);
            Assert.IsTrue(File.Exists(path), "Test data missing: " + path);

            var outcome = new LoadOutcome();
            yield return RunLoad(path, outcome);

            Assert.IsFalse(outcome.TimedOut, "Load timed out: " + fileName);
            Assert.AreEqual(0, outcome.ErrorCalls, "onError: " + outcome.Error);
            Assert.AreEqual(1, outcome.LoadedCalls);
            Assert.IsNotNull(outcome.Clip);
            Assert.AreEqual(2, outcome.Clip.channels);
            Assert.AreEqual(SampleRate, outcome.Clip.frequency);
            Assert.GreaterOrEqual(outcome.Clip.length, ClipSeconds - LengthShortfallTolerance);
            Assert.LessOrEqual(outcome.Clip.length, ClipSeconds + maxPaddingSec);
        }

        [UnityTest]
        public IEnumerator Wav_LoadsMono44100WithExactSampleCount()
        {
            string path = Path.Combine(_tempDir, "loader_test.wav");
            try
            {
                float[] signal = TestAudio.ClickTrack(SampleRate, ClipSeconds, 120f, 0.5, Band.Low, Band.High);
                TestAudio.WriteWav(path, signal, SampleRate);

                var outcome = new LoadOutcome();
                yield return RunLoad(path, outcome);

                Assert.IsFalse(outcome.TimedOut, "Load timed out");
                Assert.AreEqual(0, outcome.ErrorCalls, "onError: " + outcome.Error);
                Assert.AreEqual(1, outcome.LoadedCalls);
                Assert.IsNotNull(outcome.Clip);
                Assert.AreEqual(1, outcome.Clip.channels);
                Assert.AreEqual(SampleRate, outcome.Clip.frequency);
                Assert.AreEqual((int)(SampleRate * ClipSeconds), outcome.Clip.samples);

                float[] interleaved = AudioLoader.ReadInterleaved(outcome.Clip);
                Assert.AreEqual(outcome.Clip.samples * outcome.Clip.channels, interleaved.Length);
                float peak = 0f;
                for (int i = 0; i < interleaved.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(interleaved[i]));
                // The 5 kHz off-beat click sampled at 44.1 kHz peaks at about 0.788 (0.8 * sin * decay), so allow 0.03.
                Assert.AreEqual(0.8f, peak, 0.03f, "peak sample should match the click amplitude");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [UnityTest]
        public IEnumerator Mp3_LoadsStereo44100About2Seconds()
        {
            yield return AssertStereoCodecFile("stereo_44100_2s.mp3", Mp3MaxPaddingSec);
        }

        [UnityTest]
        public IEnumerator Ogg_LoadsStereo44100About2Seconds()
        {
            yield return AssertStereoCodecFile("stereo_44100_2s.ogg", OggMaxPaddingSec);
        }

        [UnityTest]
        public IEnumerator MissingFile_CallsOnErrorOnly()
        {
            string path = Path.Combine(_tempDir, "does_not_exist_" + Guid.NewGuid().ToString("N") + ".wav");
            var outcome = new LoadOutcome();
            yield return RunLoad(path, outcome);

            Assert.IsFalse(outcome.TimedOut, "Load timed out");
            Assert.AreEqual(1, outcome.ErrorCalls);
            Assert.AreEqual(0, outcome.LoadedCalls);
            Assert.IsFalse(string.IsNullOrEmpty(outcome.Error));
        }

        [UnityTest]
        public IEnumerator UnsupportedExtension_Flac_CallsOnErrorOnly()
        {
            // Existing file, so the failure can only come from the extension.
            string path = Path.Combine(_tempDir, "unsupported_" + Guid.NewGuid().ToString("N") + ".flac");
            File.WriteAllBytes(path, new byte[] { 0x66, 0x4C, 0x61, 0x43 });
            try
            {
                var outcome = new LoadOutcome();
                yield return RunLoad(path, outcome);

                Assert.IsFalse(outcome.TimedOut, "Load timed out");
                Assert.AreEqual(1, outcome.ErrorCalls);
                Assert.AreEqual(0, outcome.LoadedCalls);
                Assert.IsFalse(string.IsNullOrEmpty(outcome.Error));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
