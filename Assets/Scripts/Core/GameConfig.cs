using UnityEngine;

namespace NoiseRun
{
    /// <summary>모든 튜닝 수치(구현명세 7.3). 다른 파일에 숫자를 직접 쓰지 않는다.</summary>
    public static class GameConfig
    {
        // ── 분석 (7.3, 5절) ──
        /// <summary>FFT 창 크기(샘플).</summary>
        public const int FftSize = 1024;
        /// <summary>프레임 간격(샘플).</summary>
        public const int HopSize = 512;
        public const float LowBandHzMin = 20f;
        public const float LowBandHzMax = 250f;
        public const float MidBandHzMin = 250f;
        public const float MidBandHzMax = 2000f;
        public const float HighBandHzMin = 2000f;
        public const float HighBandHzMax = 8000f;
        /// <summary>적응 임계값 평균 구간 반폭(±초).</summary>
        public const double ThresholdHalfWindowSec = 0.5;
        /// <summary>평균 대비 배수.</summary>
        public const float ThresholdMultiplier = 1.5f;
        /// <summary>무음 구간 오검출 방지용 하한(대역 전체 평균 대비).</summary>
        public const float ThresholdFloorRatio = 0.1f;
        public const float BpmMin = 70f;
        public const float BpmMax = 180f;
        /// <summary>강도 곡선 추세 제거 구간 반폭(±초).</summary>
        public const double BpmDetrendHalfWindowSec = 0.5;
        /// <summary>미세 보정 범위(±비율).</summary>
        public const float BpmRefineRangePct = 0.015f;
        /// <summary>미세 보정 간격(BPM).</summary>
        public const float BpmRefineStep = 0.02f;
        /// <summary>이보다 낮으면 BPM 추정 실패.</summary>
        public const float BpmConfidenceMin = 0.1f;
        public const float FallbackBpm = 120f;
        /// <summary>전 대역 온셋 수가 이보다 적으면 분석 불가.</summary>
        public const int MinOnsetCount = 8;
        /// <summary>곡 길이가 이보다 짧으면 분석 불가(초).</summary>
        public const double MinSongLengthSec = 10.0;
        /// <summary>곡 앞 비우는 구간(초).</summary>
        public const double LeadInEmptySec = 3.0;
        /// <summary>곡 끝 직전 비우는 구간(초).</summary>
        public const double EndEmptySec = 1.0;
        /// <summary>E1: 두 번째 대역이 이 비율 이상이면 조합.</summary>
        public const float ComboObstacleRatio = 0.8f;
        /// <summary>BPM 옥타브 교정: L̂/2 근처 lag의 r가 r(L*)의 이 비율 이상이면 그 lag를 L*로 쓴다(150 BPM이 75로 잡히는 오류 방지).</summary>
        public const float BpmHalfLagRatio = 0.5f;
        /// <summary>칸 안 대역 비교 지수: 비교값 = strength × (대역 평균 플럭스 / 세 대역 평균 합)^지수. 0이면 명세 5.6 원안(strength만 비교). 클릭이 다른 대역으로 새는 것을 막는다.</summary>
        public const float OnsetBandShareExponent = 0.5f;
        /// <summary>5.1 분석 진행률 구간 경계. 모노 변환 0–MonoEnd, 플럭스 MonoEnd–FluxEnd, 피크 FluxEnd–PeakEnd, BPM PeakEnd–BpmEnd, 맵 BpmEnd–1. 워커가 읽으므로 const.</summary>
        public const float AnalysisProgressMonoEnd = 0.05f;
        public const float AnalysisProgressFluxEnd = 0.80f;
        public const float AnalysisProgressPeakEnd = 0.85f;
        public const float AnalysisProgressBpmEnd = 0.95f;
        /// <summary>FFT 루프에서 진행률을 갱신하는 프레임 간격(volatile 쓰기 빈도 제한용).</summary>
        public const int AnalysisProgressFrameInterval = 64;

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

        // ── 분석 진행 화면 (T3, 480×270 기준) ──
        public const float UiAnalyzeStageY = 16f;
        public const float UiAnalyzeBarY = -8f;
        public const float UiAnalyzeBarWidth = 320f;
        public const float UiAnalyzeBarHeight = 10f;
        public const float UiAnalyzePercentY = -30f;
        public static readonly Color UiAnalyzeBarBackColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color UiAnalyzeBarFillColor = new Color(0.4f, 0.9f, 0.5f, 1f);
        /// <summary>오류 메시지 글자색(Songs 목록·선택 화면).</summary>
        public static readonly Color UiErrorColor = new Color(1f, 0.55f, 0.55f);

        // ── 분석 디버그 화면 (T3, 구현명세 10절, 480×270 기준·화면 중앙이 0·위쪽이 +) ──
        /// <summary>화면 폭에 담기는 곡 길이(초). 재생 위치선은 화면 중앙.</summary>
        public const float DebugWindowSec = 8f;
        /// <summary>상단 정보 텍스트의 윗변 y.</summary>
        public const float DebugInfoTopY = 130f;
        /// <summary>타임라인 영역의 위·아래 y(세로선이 이 범위를 채운다).</summary>
        public const float DebugRegionTopY = 88f;
        public const float DebugRegionBottomY = -96f;
        /// <summary>대역 한 줄의 막대 최대 높이.</summary>
        public const float DebugBandRowHeight = 40f;
        /// <summary>대역별 막대 밑변 y.</summary>
        public const float DebugLowBaseY = 46f;
        public const float DebugMidBaseY = 2f;
        public const float DebugHighBaseY = -42f;
        /// <summary>이 strength에서 막대가 최대 높이가 된다(그 이상은 같은 높이).</summary>
        public const float DebugStrengthClamp = 8f;
        /// <summary>온셋 막대의 최소 높이(약한 온셋도 보이게).</summary>
        public const float DebugOnsetMinHeight = 1f;
        /// <summary>곡 시간 텍스트 갱신·표시 단위(초). 표시는 소수 1자리.</summary>
        public const double DebugTimeTextStepSec = 0.1;
        public const float DebugOnsetWidth = 2f;
        public const float DebugBeatLineWidth = 2f;
        public const float DebugHalfBeatLineWidth = 1f;
        public const float DebugPlayheadWidth = 2f;
        public const float DebugBaselineHeight = 1f;
        public const float DebugNoteRowY = -62f;
        public const float DebugNoteSize = 14f;
        public const float DebugBlinkY = -84f;
        public const float DebugBlinkSize = 16f;
        /// <summary>정박 직후 이 시간(초) 동안 사각형이 켜진다.</summary>
        public const double DebugBlinkSec = 0.1;
        public static readonly Color DebugBackgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
        public static readonly Color DebugLowColor = new Color(0.95f, 0.25f, 0.25f, 1f);
        public static readonly Color DebugMidColor = new Color(0.3f, 0.85f, 0.35f, 1f);
        public static readonly Color DebugHighColor = new Color(0.35f, 0.55f, 1f, 1f);
        /// <summary>E1 조합 노트(플래그가 둘 이상)의 색.</summary>
        public static readonly Color DebugComboNoteColor = Color.white;
        public static readonly Color DebugBeatLineColor = new Color(1f, 1f, 1f, 0.8f);
        public static readonly Color DebugHalfBeatLineColor = new Color(1f, 1f, 1f, 0.3f);
        public static readonly Color DebugBaselineColor = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color DebugPlayheadColor = new Color(1f, 0.9f, 0.2f, 1f);
        public static readonly Color DebugBlinkOnColor = new Color(1f, 0.9f, 0.2f, 1f);
        public static readonly Color DebugBlinkOffColor = new Color(0.25f, 0.25f, 0.2f, 1f);

        // ── 도트 스프라이트·갤러리 (T4, 7.3·9절) ──
        /// <summary>달리기 프레임 속도(fps, 7.3).</summary>
        public const float RunAnimFps = 12f;
        /// <summary>갤러리에서 달리기가 아닌 애니메이션(점프·음파·휘두르기)의 프레임 속도. 프레임이 보이도록 느리게.</summary>
        public const float GalleryAnimFps = 4f;
        /// <summary>갤러리 배치 단위는 480×270 기준 픽셀. 화면 가장자리 여백.</summary>
        public const int GalleryMarginPx = 8;
        /// <summary>같은 그룹의 프레임 사이 간격.</summary>
        public const int GalleryFrameGapPx = 4;
        /// <summary>그룹 사이 간격.</summary>
        public const int GalleryGroupGapPx = 12;
        /// <summary>줄 사이 간격.</summary>
        public const int GalleryLineGapPx = 8;
        /// <summary>어두운 외곽선(k)과 밝은 색(w)이 모두 보이도록 중간 회색.</summary>
        public static readonly Color GalleryBackgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        /// <summary>배경이 스프라이트 뒤에 오도록 하는 정렬 순서.</summary>
        public const int GalleryBackgroundSortingOrder = -10;
    }
}
