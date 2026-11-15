using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 같은 GameObject의 SpriteRenderer에 프레임을 순환 재생한다(구현명세 4.2).
    /// Time.deltaTime 기반. loop=false면 마지막 프레임에서 멈춘다.
    /// 진행은 프레임 단위(position, 0..프레임 수)로 들고 있어서 SetFps로 재생 중에 속도만 바꿔도 지금 프레임·진행이 이어진다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        SpriteRenderer spriteRenderer;
        Sprite[] frames;
        float fps;
        bool loop;
        float position; // 프레임 단위 진행. 정수부 = 지금 프레임 인덱스
        bool playing;
        bool finished; // loop=false 재생이 마지막 프레임에 닿음

        SpriteRenderer Renderer
        {
            get
            {
                if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
                return spriteRenderer;
            }
        }

        /// <summary>
        /// 첫 프레임부터 재생한다. frames가 비었거나 null이면 아무것도 바꾸지 않고, fps가 0 이하이면 첫 프레임만 보여 준다.
        /// </summary>
        public void Play(Sprite[] frames, float fps, bool loop)
        {
            this.frames = frames;
            this.fps = fps;
            this.loop = loop;
            position = 0f;
            playing = false;
            finished = false;

            if (frames == null || frames.Length == 0) return;
            Renderer.sprite = frames[0];
            playing = fps > 0f && frames.Length > 1;
        }

        /// <summary>
        /// 지금 프레임과 진행을 그대로 두고 재생 속도만 바꾼다(T8 잡힘 감속). 0 이하이면 지금 프레임에서 멈추고, 다시 양수를 주면 그 자리부터 이어 간다.
        /// Play로 건 프레임이 없거나(SetFrame 고정 포함) loop=false 재생이 이미 끝났으면 속도만 기억하고 아무것도 다시 돌리지 않는다.
        /// </summary>
        public void SetFps(float fps)
        {
            this.fps = Mathf.Max(0f, fps);
            playing = frames != null && frames.Length > 1 && !finished && this.fps > 0f;
        }

        /// <summary>재생을 멈추고 한 장을 고정한다.</summary>
        public void SetFrame(Sprite s)
        {
            playing = false;
            finished = false;
            frames = null;
            Renderer.sprite = s;
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>재생 중이면 dt초만큼 시간을 진행해 해당 프레임을 보여 준다. Update가 Time.deltaTime으로 부른다.</summary>
        public void Advance(float dt)
        {
            if (!playing) return;

            position += dt * fps;
            int count = frames.Length;
            int index;
            if (loop)
            {
                position %= count;
                index = (int)position;
                if (index >= count) index = count - 1; // 부동소수점 오차 방지
            }
            else
            {
                index = (int)position;
                if (index >= count - 1)
                {
                    index = count - 1;
                    playing = false;
                    finished = true;
                }
            }
            Renderer.sprite = frames[index];
        }
    }
}
