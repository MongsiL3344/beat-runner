using System;
using UnityEngine.TestTools;
using NUnit.Framework;

namespace NoiseRun.Tests
{
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class FftTests
    {
        const int SampleRate = 44100;
        const double SineHz = 1000.0;
        const float SineAmplitude = 0.5f;
        const double ParsevalRelativeTolerance = 1e-4;

        static float[] Sine(int n)
        {
            var x = new float[n];
            for (int i = 0; i < n; i++)
                x[i] = (float)(SineAmplitude * Math.Sin(2.0 * Math.PI * SineHz * i / SampleRate));
            return x;
        }

        [Test]
        public void Sine1000Hz_PeakBinIsExpected()
        {
            int n = GameConfig.FftSize;
            int expectedBin = (int)Math.Round(SineHz * n / SampleRate);
            float[] window = Fft.Hann(n);
            float[] re = Sine(n);
            var im = new float[n];
            for (int i = 0; i < n; i++) re[i] *= window[i];

            new Fft(n).Forward(re, im);

            int peak = 1;
            double peakMag = 0.0;
            for (int k = 1; k <= n / 2; k++)
            {
                double mag = Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]);
                if (mag > peakMag) { peakMag = mag; peak = k; }
            }
            Assert.AreEqual(expectedBin, peak);
        }

        [Test]
        public void Sine1000Hz_ParsevalHolds()
        {
            int n = GameConfig.FftSize;
            float[] re = Sine(n);
            var im = new float[n];

            double timeEnergy = 0.0;
            for (int i = 0; i < n; i++) timeEnergy += (double)re[i] * re[i];

            new Fft(n).Forward(re, im);

            double freqEnergy = 0.0;
            for (int k = 0; k < n; k++) freqEnergy += (double)re[k] * re[k] + (double)im[k] * im[k];
            freqEnergy /= n;

            Assert.AreEqual(1.0, freqEnergy / timeEnergy, ParsevalRelativeTolerance);
        }

        [Test]
        public void Hann_IsZeroAtStartAndOneAtCenter()
        {
            int n = GameConfig.FftSize;
            float[] w = Fft.Hann(n);
            Assert.AreEqual(n, w.Length);
            Assert.AreEqual(0f, w[0], 1e-6f);
            Assert.AreEqual(1f, w[n / 2], 1e-6f);
        }
    }
}
