using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 같은 GameObject의 SpriteRenderer에 프레임을 순환 재생한다(구현명세 4.2).
    /// Time.deltaTime 기반. loop=false면 마지막 프레임에서 멈춘다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteAnimator : MonoBehaviour
    {
        SpriteRenderer spriteRenderer;
        Sprite[] frames;
        float fps;
        bool loop;
        float elapsed;
        bool playing;

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
            elapsed = 0f;
            playing = false;

            if (frames == null || frames.Length == 0) return;
            Renderer.sprite = frames[0];
            playing = fps > 0f && frames.Length > 1;
        }

        /// <summary>재생을 멈추고 한 장을 고정한다.</summary>
        public void SetFrame(Sprite s)
        {
            playing = false;
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

            elapsed += dt;
            int count = frames.Length;
            float cycleSec = count / fps;
            int index;
            if (loop)
            {
                elapsed %= cycleSec;
                index = (int)(elapsed * fps);
                if (index >= count) index = count - 1; // 부동소수점 오차 방지
            }
            else
            {
                index = (int)(elapsed * fps);
                if (index >= count - 1)
                {
                    index = count - 1;
                    playing = false;
                }
            }
            Renderer.sprite = frames[index];
        }
    }
}
