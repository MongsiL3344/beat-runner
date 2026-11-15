using UnityEngine;

namespace NoiseRun
{
    /// <summary>곡 시간 시계. Conductor는 이 인터페이스만 안다.</summary>
    public interface ISongClock
    {
        /// <summary>곡 시간(초). Start 후 delaySec 동안은 음수, 0에서 음악이 시작된다.</summary>
        double SongTime { get; }
        void Start(double delaySec);
        void Pause();
        void Resume();
    }

    /// <summary>AudioSettings.dspTime 기반 시계(구현명세 6.4). 오프셋(E4)은 아직 0.</summary>
    public sealed class DspSongClock : ISongClock
    {
        double startDsp;
        double pausedAccum;
        double pauseDsp;
        double lastDsp;
        double smooth;
        double cached;
        int lastFrame = -1;
        bool started;
        bool paused;

        public double SongTime
        {
            get
            {
                if (!started) return 0;
                if (paused) return pauseDsp - startDsp - pausedAccum;

                // 한 프레임에 여러 번 읽어도 같은 값이 나오도록 프레임당 한 번만 갱신한다.
                if (Time.frameCount != lastFrame)
                {
                    lastFrame = Time.frameCount;
                    double dsp = AudioSettings.dspTime;
                    if (dsp == lastDsp)
                    {
                        // dspTime이 갱신되지 않은 프레임은 보간
                        smooth += Time.unscaledDeltaTime;
                    }
                    else
                    {
                        smooth = dsp;
                        lastDsp = dsp;
                    }
                    if (smooth > dsp + GameConfig.BufferSmoothMaxLeadSec)
                        smooth = dsp + GameConfig.BufferSmoothMaxLeadSec;
                    cached = smooth - startDsp - pausedAccum;
                }
                return cached;
            }
        }

        /// <summary>Start가 확정한 곡 시작 dspTime. Conductor가 PlayScheduled에 같은 값을 쓴다.</summary>
        public double StartDsp => startDsp;

        public void Start(double delaySec)
        {
            double dsp = AudioSettings.dspTime;
            startDsp = dsp + delaySec;
            pausedAccum = 0;
            lastDsp = dsp;
            smooth = dsp;
            lastFrame = Time.frameCount; // 이 프레임 값을 확정: 같은 프레임에 읽어도 -delaySec
            cached = -delaySec;
            started = true;
            paused = false;
        }

        public void Pause()
        {
            if (!started || paused) return;
            pauseDsp = AudioSettings.dspTime;
            paused = true;
        }

        public void Resume()
        {
            if (!started || !paused) return;
            double dsp = AudioSettings.dspTime;
            pausedAccum += dsp - pauseDsp;
            paused = false;
            lastDsp = dsp;
            smooth = dsp;
            lastFrame = Time.frameCount;
            cached = dsp - startDsp - pausedAccum;
        }
    }

    /// <summary>테스트용 시계. Time을 직접 설정한다. Start(delay)는 Time = -delay.</summary>
    public sealed class FakeSongClock : ISongClock
    {
        public double Time;

        public double SongTime => Time;

        public void Start(double delaySec) { Time = -delaySec; }
        public void Pause() { }
        public void Resume() { }
    }
}
