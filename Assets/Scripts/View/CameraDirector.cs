using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 카메라 연출(구현명세 4.2). T5에서는 추적(Follow)만 한다: 추적을 시작하면 플레이 줌(PlayCameraOrthoSize)으로 바꾸고,
    /// 대상이 화면 가로 PlayerViewportX 위치에 오도록 x를 맞추고,
    /// y는 복도 띠 한가운데로 고정한다. 위치는 도트 격자(1/PPU)에 맞춰 반올림해 스크롤 중 흔들림(지터)을 막는다.
    /// 컴포넌트는 씬 카메라가 아니라 별도 오브젝트에 붙는다(Bootstrap.Restart가 한꺼번에 지울 수 있게).
    /// </summary>
    public sealed class CameraDirector : MonoBehaviour
    {
        Camera cam;
        Transform target;

        /// <summary>parent 아래에 새 GameObject를 만들어 컴포넌트를 붙인다. cam에는 아무것도 붙이지 않는다.</summary>
        public static CameraDirector Create(Camera cam, Transform parent)
        {
            var go = new GameObject("CameraDirector");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<CameraDirector>();
            director.cam = cam;
            return director;
        }

        /// <summary>대상을 따라간다. null이면 현재 위치·줌 그대로 멈춘다. 지정하면 플레이 줌으로 바꾸고 즉시 한 번 위치를 맞춘다.</summary>
        public void Follow(Transform target)
        {
            this.target = target;
            if (target == null) return;
            if (cam != null) cam.orthographicSize = GameConfig.PlayCameraOrthoSize;
            Apply();
        }

        void LateUpdate()
        {
            if (target != null) Apply();
        }

        void Apply()
        {
            if (cam == null) return;

            // 화면 폭의 PlayerViewportX 위치에 대상이 오도록: 카메라 x = 대상 x + 반폭 × (1 − 2 × PlayerViewportX).
            // 절대 위치가 아니라 일정한 오프셋만 도트 격자(1/PPU)에 맞춘다. 그래야 대상이 화면에서 항상 같은 자리에 있다.
            float halfWidth = cam.orthographicSize * cam.aspect;
            float ppu = GameConfig.PixelsPerUnit;
            float offset = Mathf.Round(halfWidth * (1f - 2f * GameConfig.PlayerViewportX) * ppu) / ppu;
            Vector3 p = cam.transform.position;
            p.x = target.position.x + offset;
            p.y = Mathf.Round(GameConfig.CameraY * ppu) / ppu;
            cam.transform.position = p;
        }
    }
}
