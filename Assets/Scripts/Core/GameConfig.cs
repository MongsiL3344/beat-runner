using UnityEngine;

namespace NoiseRun
{
    /// <summary>모든 튜닝 수치(구현명세 7.3). 다른 파일에 숫자를 직접 쓰지 않는다.</summary>
    public static class GameConfig
    {
        // ── 화면·이동 ──
        public const int PixelsPerUnit = 16;
        public const int RefWidth = 480;
        public const int RefHeight = 270;
        public static Vector2 RefResolution => new Vector2(RefWidth, RefHeight);
        /// <summary>RefHeight / (2 × PixelsPerUnit) = 270 / 32.</summary>
        public const float CameraOrthoSize = 8.4375f;
        /// <summary>dspTime 보간 최대 선행(초).</summary>
        public const double BufferSmoothMaxLeadSec = 0.05;

        /// <summary>Bootstrap이 카메라를 새로 만들 때의 z 위치(2D 템플릿 기본값).</summary>
        public const float CameraZ = -10f;

        // ── 폰트·UI (2.6) ──
        /// <summary>Resources.Load 경로(Assets/Resources/Fonts/Galmuri11.ttf).</summary>
        public const string FontResourcePath = "Fonts/Galmuri11";
        /// <summary>Galmuri11 권장 픽셀 크기. 글자 크기는 이 값의 정수배로 쓴다.</summary>
        public const int FontBaseSize = 12;
        public const int TextSizeBody = FontBaseSize;
        public const int TextSizeHeader = FontBaseSize * 2;
        public const int TextSizeTitle = FontBaseSize * 3;

        /// <summary>480×270 기준 좌우 여백(px).</summary>
        public const float UiMargin = 16f;
        /// <summary>T1 메뉴 화면 배치(480×270 기준, 화면 중앙이 0, 위쪽이 +).</summary>
        public const float UiTitleY = 30f;
        public const float UiTitlePromptY = -40f;
        public const float UiHeaderY = 118f;
        public const float UiBodyY = 70f;
        public const float UiListPathY = 84f;
        public const float UiListTopY = 50f;
        public const float UiListRowHeight = 18f;
        public const float UiMessageY = -80f;
        public const float UiFooterY = -118f;
        /// <summary>Songs 폴더 목록에 한 번에 보이는 줄 수.</summary>
        public const int SongListVisibleRows = 6;
    }
}
