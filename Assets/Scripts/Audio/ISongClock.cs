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
        double pausedTime;     // 멈춘 동안 화면에 보여 줄 값(멈춘 순간의 보간 값)
        double pausedRawTime;  // 멈춘 순간의 보간 전(dspTime 그대로) 곡 시간. 음악이 실제로 멈춘 위치라 재개 기준으로 쓴다
        double holdUntil;      // 재개 직후 곡 시간이 이 값 아래로 내려가지 않게 붙잡는다(뒤로 튐 방지)
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
                if (paused) return pausedTime;

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
                    if (cached < holdUntil) cached = holdUntil;
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
            holdUntil = double.NegativeInfinity;
            lastDsp = dsp;
            smooth = dsp;
            lastFrame = Time.frameCount; // 이 프레임 값을 확정: 같은 프레임에 읽어도 -delaySec
            cached = -delaySec;
            started = true;
            paused = false;
        }

        /// <summary>
        /// 시계를 멈춘다. 멈춘 동안 SongTime은 이 순간 SongTime이 돌려주던(보간된) 값 그대로다(리뷰 L4: 화면이 그 자리에 멈춤).
        /// 재개 기준은 보간 전 dspTime 값이다. 음악은 그 위치에서 멈추므로 보간 값을 기준으로 삼으면 재개 뒤 곡 시간이 소리보다 계속 앞선다.
        /// </summary>
        public void Pause()
        {
            if (!started || paused) return;
            pausedTime = SongTime;
            pausedRawTime = AudioSettings.dspTime - startDsp - pausedAccum;
            paused = true;
        }

        /// <summary>
        /// 시계를 다시 흘린다. 멈춘 시간 누적은 보간 전 값(pausedRawTime) 기준으로 다시 잡아 소리와 맞추고,
        /// 보간분(최대 BufferSmoothMaxLeadSec)만큼 실제 시간이 따라올 때까지는 멈췄던 화면 값(pausedTime)에 붙잡아 둔다. 그래서 타이밍선이 뒤로 튀지 않는다.
        /// </summary>
        public void Resume()
        {
            if (!started || !paused) return;
            double dsp = AudioSettings.dspTime;
            pausedAccum = dsp - startDsp - pausedRawTime;
            paused = false;
            lastDsp = dsp;
            smooth = dsp;
            lastFrame = Time.frameCount;
            holdUntil = pausedTime;
            cached = pausedTime;
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
