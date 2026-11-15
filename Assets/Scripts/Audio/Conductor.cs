using UnityEngine;

namespace NoiseRun
{
    /// <summary>곡 시간 제공과 예약 재생. 시간은 ISongClock에서, 소리는 자기 AudioSource에서 나온다.</summary>
    public sealed class Conductor : MonoBehaviour
    {
        AudioSource source;
        ISongClock clock;

        /// <summary>시간 소스. 기본은 DspSongClock, 테스트는 FakeSongClock으로 교체한다.</summary>
        public ISongClock Clock
        {
            get
            {
                if (clock == null) clock = new DspSongClock();
                return clock;
            }
            set { clock = value ?? new DspSongClock(); }
        }

        public double SongTime => Clock.SongTime;

        /// <summary>
        /// 개발 옵션 freezeAt용. 값이 있고 시계가 FakeSongClock이면 ScheduleStart가 시계를 0으로 되돌리지 않고
        /// 이 값으로 곡 시간을 유지한다. 재생 소리는 그대로 나온다.
        /// </summary>
        public double? FrozenSongTime { get; set; }

        public double SongLength => (source != null && source.clip != null) ? source.clip.length : 0.0;

        AudioSource Source
        {
            get
            {
                if (source == null)
                {
                    source = gameObject.GetComponent<AudioSource>();
                    if (source == null) source = gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.loop = false;
                    source.spatialBlend = 0f;
                }
                return source;
            }
        }

        void Awake()
        {
            _ = Source;
        }

        /// <summary>재생할 클립을 지정한다(null이면 비운다). 재생 중이면 멈춘다.</summary>
        public void Load(AudioClip clip)
        {
            Source.Stop();
            Source.clip = clip;
        }

        /// <summary>delaySec 뒤에 0초 지점부터 재생하도록 예약한다.</summary>
        public void ScheduleStart(double delaySec)
        {
            var s = Source;
            if (s.clip == null)
            {
                Debug.LogWarning("Conductor.ScheduleStart: 클립이 없습니다.");
                return;
            }
            s.Stop();
            s.time = 0f;
            double startDsp;
            if (Clock is DspSongClock dspClock)
            {
                dspClock.Start(delaySec);
                startDsp = dspClock.StartDsp;
            }
            else
            {
                Clock.Start(delaySec);
                if (FrozenSongTime.HasValue && Clock is FakeSongClock fake) fake.Time = FrozenSongTime.Value;
                startDsp = AudioSettings.dspTime + delaySec;
            }
            s.PlayScheduled(startDsp);
        }

        public void Pause()
        {
            Clock.Pause();
            Source.Pause();
        }

        public void Resume()
        {
            Clock.Resume();
            Source.UnPause();
        }

        public void Stop()
        {
            Source.Stop();
        }
    }
}
