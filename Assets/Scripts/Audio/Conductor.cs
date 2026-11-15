using System.Collections;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>곡 시간 제공과 예약 재생. 시간은 ISongClock에서, 소리는 자기 AudioSource에서 나온다.</summary>
    public sealed class Conductor : MonoBehaviour
    {
        AudioSource source;
        ISongClock clock;
        int fadeId; // FadeOutAndStop 세대. 바뀌면 진행 중인 페이드가 스스로 끝난다.

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

        /// <summary>재생 음량(AudioSource.volume). 페이드아웃 중에는 내려가고 CancelFade·Stop 등이 1로 되돌린다. 테스트가 읽음.</summary>
        public float Volume => Source.volume;

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
            CancelFade();
            Source.Stop();
            Source.clip = clip;
        }

        /// <summary>delaySec 뒤에 0초 지점부터 재생하도록 예약한다.</summary>
        public void ScheduleStart(double delaySec)
        {
            CancelFade();
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

        /// <summary>시계와 소리를 멈춘다. 진행 중인 페이드아웃은 취소하고 볼륨을 되돌린다.</summary>
        public void Pause()
        {
            CancelFade();
            Clock.Pause();
            Source.Pause();
        }

        public void Resume()
        {
            Clock.Resume();
            Source.UnPause();
        }

        /// <summary>소리를 멈춘다. 진행 중인 페이드아웃은 취소하고 볼륨을 되돌린다.</summary>
        public void Stop()
        {
            CancelFade();
            Source.Stop();
        }

        /// <summary>
        /// 볼륨을 sec 동안 0으로 내린 뒤 멈추고 볼륨을 1로 되돌린다(StartCoroutine으로 돌린다). 시간은 실시간(unscaled)이라
        /// Time.timeScale·히트스톱의 영향을 받지 않고, 페이드 중에도 SongTime은 계속 흐른다(시계는 건드리지 않음).
        /// 페이드 중 Stop/Load/ScheduleStart/Pause가 불리면 페이드를 취소하고 볼륨을 1로 되돌린다. 다시 부르면 이전 페이드는 취소된다.
        /// </summary>
        public IEnumerator FadeOutAndStop(float sec)
        {
            var s = Source;
            int id = ++fadeId;
            float startVolume = s.volume;
            float elapsed = 0f;
            while (elapsed < sec)
            {
                s.volume = Mathf.Lerp(startVolume, 0f, elapsed / sec);
                yield return null;
                if (id != fadeId) yield break; // 다른 호출이 취소함(볼륨은 그쪽이 복구)
                elapsed += Time.unscaledDeltaTime;
            }
            if (id != fadeId) yield break;
            s.Stop();
            s.volume = 1f;
        }

        /// <summary>
        /// 진행 중인 FadeOutAndStop을 취소하고 볼륨을 1로 되돌린다(세대 번호를 올리므로 코루틴 핸들이 남아 있어도 취소된 페이드는 아무것도 하지 않는다).
        /// 진행 중인 페이드가 없어도 부를 수 있다.
        /// </summary>
        public void CancelFade()
        {
            fadeId++;
            Source.volume = 1f;
        }
    }
}
