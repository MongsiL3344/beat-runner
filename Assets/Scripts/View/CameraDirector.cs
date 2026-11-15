using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 카메라 연출(구현명세 4.2). 추적(Follow): 추적을 시작하면 플레이 줌(PlayCameraOrthoSize)으로 바꾸고,
    /// 대상이 화면 가로 PlayerViewportX 위치에 오도록 x를 맞추고, y는 복도 띠 한가운데로 고정한다.
    /// 위치는 도트 격자(1/PPU)에 맞춰 반올림해 스크롤 중 흔들림(지터)을 막는다.
    /// T8: 미리보기 플라이스루(StartFlythrough)와 강타 흔들림(Shake)을 더한다. 플라이스루 동안은 Follow 대상을 무시하고,
    /// 흔들림은 Follow·플라이스루·정지 위치에 감쇠 오프셋으로 더한다. 진행은 Advance(dt) 하나가 맡고 LateUpdate가 Time.deltaTime으로 부른다.
    /// 컴포넌트는 씬 카메라가 아니라 별도 오브젝트에 붙는다(Bootstrap.Restart가 한꺼번에 지울 수 있게).
    /// </summary>
    public sealed class CameraDirector : MonoBehaviour
    {
        Camera cam;
        Transform target;

        // 플라이스루(미리보기 4구간)
        bool flyActive;
        float flyStartX;
        float flyEndX;
        float flyElapsed;

        // 흔들림. shakeApplied는 지난 Advance가 카메라에 더한 오프셋(정지 위치를 되찾을 때 뺀다).
        float shakeAmp;
        float shakeDuration;
        float shakeElapsed;
        Vector2 shakeDir;
        Vector3 shakeApplied;
        readonly System.Random shakeRandom = new System.Random();

        /// <summary>parent 아래에 새 GameObject를 만들어 컴포넌트를 붙인다. cam에는 아무것도 붙이지 않는다.</summary>
        public static CameraDirector Create(Camera cam, Transform parent)
        {
            var go = new GameObject("CameraDirector");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<CameraDirector>();
            director.cam = cam;
            return director;
        }

        /// <summary>플라이스루가 진행 중인지.</summary>
        public bool FlythroughActive => flyActive;

        /// <summary>
        /// 플라이스루 현재 구간. 0 = 시작 위치 비춤(PreviewHoldStartSec), 1 = 비상구까지 훑기(PreviewFlySec),
        /// 2 = 비상구에서 멈춤(PreviewHoldEndSec), 3 = 시작 위치로 복귀(PreviewReturnSec). 비활성이면 −1.
        /// </summary>
        public int FlythroughSegment
        {
            get
            {
                if (!flyActive) return -1;
                float e = flyElapsed;
                if (e < GameConfig.PreviewHoldStartSec) return 0;
                e -= GameConfig.PreviewHoldStartSec;
                if (e < GameConfig.PreviewFlySec) return 1;
                e -= GameConfig.PreviewFlySec;
                if (e < GameConfig.PreviewHoldEndSec) return 2;
                return 3;
            }
        }

        /// <summary>흔들림이 진행 중인지(Shake 뒤 duration이 지나거나 StopShake·Follow(null)·SkipFlythrough가 끝내기 전까지). 테스트가 읽음.</summary>
        public bool ShakeActive => shakeDuration > 0f;

        /// <summary>
        /// 대상을 따라간다. null이면 현재 위치·줌 그대로 멈추고, 진행 중인 플라이스루·흔들림을 끝낸다(흔들림 오프셋은 걷어 낸다).
        /// 지정하면 플레이 줌으로 바꾸고, 플라이스루 중이 아니면 즉시 한 번 위치를 맞춘다(플라이스루 중이면 끝난 뒤 따라간다).
        /// </summary>
        public void Follow(Transform target)
        {
            this.target = target;
            if (target == null)
            {
                flyActive = false;
                StopShake();
                return;
            }
            if (cam == null) return;
            cam.orthographicSize = GameConfig.PlayCameraOrthoSize;
            if (!flyActive) SetPosition(FramePosition(target.position.x));
        }

        /// <summary>
        /// 미리보기 플라이스루를 시작한다: startX 비춤 → endX까지 easeInOutCubic 훑기 → endX에서 멈춤 → startX로 easeInOutCubic 복귀
        /// (구간 시간은 GameConfig.Preview*). 각 지점의 구도는 Follow와 같다(그 x가 화면 폭 PlayerViewportX에, 플레이 줌).
        /// 즉시 시작 구도로 옮긴다. 끝나면 비활성이 되고 Follow 대상이 있으면 다시 따라간다.
        /// </summary>
        public void StartFlythrough(float startX, float endX)
        {
            flyActive = true;
            flyStartX = startX;
            flyEndX = endX;
            flyElapsed = 0f;
            if (cam == null) return;
            cam.orthographicSize = GameConfig.PlayCameraOrthoSize;
            SetPosition(FramePosition(startX));
        }

        /// <summary>플라이스루를 끝내고 즉시 시작 구도로 옮긴다. 흔들림도 끝낸다. 비활성이면 아무것도 하지 않는다.</summary>
        public void SkipFlythrough()
        {
            if (!flyActive) return;
            flyActive = false;
            StopShake();
            if (cam != null) SetPosition(FramePosition(flyStartX));
        }

        /// <summary>
        /// 화면 흔들림. amplitude(유닛)에서 시작해 duration(초) 동안 선형으로 0까지 줄어드는 무작위 오프셋을
        /// Follow·플라이스루·정지 위치에 더한다. 오프셋은 도트 격자에 맞춘다. 진행 중에 다시 부르면 새로 시작한다.
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                StopShake();
                return;
            }
            shakeAmp = amplitude;
            shakeDuration = duration;
            shakeElapsed = 0f;
            shakeDir = RandomDirection();
        }

        /// <summary>
        /// 플라이스루·흔들림 타이머를 dt(초)만큼 진행하고 카메라 위치를 적용한다. LateUpdate가 Time.deltaTime으로 부르고,
        /// 테스트는 직접 부른다. dt = 0(히트스톱)이면 흔들림 방향을 바꾸지 않아 화면이 그대로 멈춘다.
        /// </summary>
        public void Advance(float dt)
        {
            if (cam == null) return;

            Vector3 basePos;
            bool hasBase = false;
            basePos = cam.transform.position - shakeApplied;

            if (flyActive)
            {
                flyElapsed += dt;
                if (flyElapsed >= FlythroughTotalSec)
                {
                    flyActive = false;
                    if (target == null)
                    {
                        basePos = FramePosition(flyStartX);
                        hasBase = true;
                    }
                }
                else
                {
                    basePos = FramePosition(FlythroughX());
                    hasBase = true;
                }
            }
            if (!hasBase && target != null) basePos = FramePosition(target.position.x);

            Vector3 offset = Vector3.zero;
            if (shakeDuration > 0f)
            {
                shakeElapsed += dt;
                if (shakeElapsed >= shakeDuration)
                {
                    shakeDuration = 0f;
                }
                else
                {
                    if (dt > 0f) shakeDir = RandomDirection();
                    float amp = shakeAmp * (1f - shakeElapsed / shakeDuration);
                    float ppu = GameConfig.PixelsPerUnit;
                    offset = new Vector3(Mathf.Round(shakeDir.x * amp * ppu) / ppu, Mathf.Round(shakeDir.y * amp * ppu) / ppu, 0f);
                }
            }

            shakeApplied = offset;
            cam.transform.position = basePos + offset;
        }

        void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        // ───────────── 내부 ─────────────

        static float FlythroughTotalSec =>
            GameConfig.PreviewHoldStartSec + GameConfig.PreviewFlySec + GameConfig.PreviewHoldEndSec + GameConfig.PreviewReturnSec;

        /// <summary>플라이스루 경과 시간에서 카메라가 비출 월드 x. 도트 격자에 맞춘다.</summary>
        float FlythroughX()
        {
            float e = flyElapsed;
            float x;
            if (e < GameConfig.PreviewHoldStartSec)
            {
                x = flyStartX;
            }
            else if ((e -= GameConfig.PreviewHoldStartSec) < GameConfig.PreviewFlySec)
            {
                x = Mathf.LerpUnclamped(flyStartX, flyEndX, EaseInOutCubic(e / GameConfig.PreviewFlySec));
            }
            else if ((e -= GameConfig.PreviewFlySec) < GameConfig.PreviewHoldEndSec)
            {
                x = flyEndX;
            }
            else
            {
                e -= GameConfig.PreviewHoldEndSec;
                float t = GameConfig.PreviewReturnSec > 0f ? Mathf.Clamp01(e / GameConfig.PreviewReturnSec) : 1f;
                x = Mathf.LerpUnclamped(flyEndX, flyStartX, EaseInOutCubic(t));
            }
            float ppu = GameConfig.PixelsPerUnit;
            return Mathf.Round(x * ppu) / ppu;
        }

        static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f) return 4f * t * t * t;
            float u = -2f * t + 2f;
            return 1f - u * u * u / 2f;
        }

        /// <summary>월드 x가 화면 폭 PlayerViewportX에 오는 카메라 위치(Follow 구도). 줌은 바꾸지 않는다.</summary>
        Vector3 FramePosition(float x)
        {
            // 화면 폭의 PlayerViewportX 위치에 대상이 오도록: 카메라 x = 대상 x + 반폭 × (1 − 2 × PlayerViewportX).
            // 절대 위치가 아니라 일정한 오프셋만 도트 격자(1/PPU)에 맞춘다. 그래야 대상이 화면에서 항상 같은 자리에 있다.
            float halfWidth = cam.orthographicSize * cam.aspect;
            float ppu = GameConfig.PixelsPerUnit;
            float offset = Mathf.Round(halfWidth * (1f - 2f * GameConfig.PlayerViewportX) * ppu) / ppu;
            Vector3 p = cam.transform.position;
            p.x = x + offset;
            p.y = Mathf.Round(GameConfig.CameraY * ppu) / ppu;
            return p;
        }

        /// <summary>흔들림 없이 위치를 바로 둔다(Follow·플라이스루 시작·건너뛰기용). 진행 중인 흔들림은 다음 Advance가 다시 더한다.</summary>
        void SetPosition(Vector3 p)
        {
            cam.transform.position = p;
            shakeApplied = Vector3.zero;
        }

        /// <summary>진행 중인 흔들림을 끝내고 카메라에 더해 둔 오프셋을 걷어 낸다. 흔들림이 없으면 아무것도 하지 않는다.</summary>
        public void StopShake()
        {
            shakeDuration = 0f;
            shakeElapsed = 0f;
            if (cam != null && shakeApplied != Vector3.zero) cam.transform.position -= shakeApplied;
            shakeApplied = Vector3.zero;
        }

        Vector2 RandomDirection()
        {
            double angle = shakeRandom.NextDouble() * 2.0 * System.Math.PI;
            return new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle));
        }
    }
}
