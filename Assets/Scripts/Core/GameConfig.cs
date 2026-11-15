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
        /// <summary>5.6 A: 칸 시각에서 ±(이 비율 × 반박자) 안의 온셋만 그 칸으로 인정한다(사용자 결정 0.15. 112 BPM에서 ±40ms).</summary>
        public const double SlotOnsetWindowRatio = 0.15;
        /// <summary>5.6 B: 후보 칸 중 절대 에너지 상위 이 비율만 남긴다(사용자 결정 0.8). 어려움 = 이 후보 전부.</summary>
        public const double CandidateEnergyRatio = 0.8;
        /// <summary>HPSS 화성 성분: 시간 방향 미디언 길이(프레임, 홀수). 31 ≈ 0.36초(44.1kHz). 사용자 결정.</summary>
        public const int HpssHarmonicFrames = 31;
        /// <summary>HPSS 타악기 성분: 주파수 방향 미디언 길이(bin, 홀수). 31 ≈ 1.3kHz(44.1kHz). 사용자 결정.</summary>
        public const int HpssPercussiveBins = 31;
        /// <summary>곡 전체 타악기 비중 ΣP²/Σ(P²+H²)(저음 하한~고음 상한)이 이 값 이상이면 타악기 플럭스로 분석, 미만이면 원음 플럭스(드럼이 거의 없는 곡).
        /// 측정: 드럼 곡 0.200·0.217, 합성 지속음 0.047~0.076 → 로그 가운데 약 0.12(사용자가 정한 방식).</summary>
        public const float PercussiveRatioMin = 0.12f;
        /// <summary>타악기 모드의 적응 임계값 배수(원음 모드는 ThresholdMultiplier). 1.5면 잔여 출렁임까지 초당 약 31개가 잡혀서 사용자 결정으로 4.5(두 곡 6.5~8.7개/초).</summary>
        public const float PercussiveThresholdMultiplier = 4.5f;
        /// <summary>5.7 난이도별로 남길 후보 칸 비율.</summary>
        public const double DifficultyNoteRatioEasy = 0.4;
        public const double DifficultyNoteRatioNormal = 0.7;
        public const double DifficultyNoteRatioHard = 1.0;
        /// <summary>처음 고른 난이도(실행 중에만 유지).</summary>
        public const Difficulty DefaultDifficulty = Difficulty.Normal;

        // ── 화면·이동 ──
        public const int PixelsPerUnit = 16;
        public const int RefWidth = 480;
        public const int RefHeight = 270;
        public static Vector2 RefResolution => new Vector2(RefWidth, RefHeight);
        /// <summary>RefHeight / (2 × PixelsPerUnit) = 270 / 32.</summary>
        public const float CameraOrthoSize = 8.4375f;
        /// <summary>플레이 중 카메라 줌 배율(사용자 결정: 한 층에 집중, 위·아래층은 조금만 보이게). 1080p에서 8배로 정수 배율.</summary>
        public const float CameraZoom = 0.5f;
        /// <summary>CameraDirector.Follow가 쓰는 플레이 중 orthographicSize = CameraOrthoSize × CameraZoom.</summary>
        public const float PlayCameraOrthoSize = CameraOrthoSize * CameraZoom;
        /// <summary>dspTime 보간 최대 선행(초).</summary>
        public const double BufferSmoothMaxLeadSec = 0.05;
        /// <summary>x = 곡 시간 × ScrollSpeed (유닛/초).</summary>
        public const float ScrollSpeed = 8f;
        /// <summary>카메라 추적 시 주인공의 화면 가로 위치(0=왼쪽, 1=오른쪽).</summary>
        public const float PlayerViewportX = 0.4f;
        /// <summary>바닥 높이(유닛).</summary>
        public const float FloorY = 0f;
        /// <summary>복도 천장 높이(유닛, 48px).</summary>
        public const float CeilingHeight = 3f;
        /// <summary>점프 높이(유닛, 20px).</summary>
        public const float JumpHeight = 1.25f;
        /// <summary>동작 연출 최대 시간(초).</summary>
        public const float ActionMaxSec = 0.35f;
        /// <summary>반박자 대비 동작 시간 배수. 동작 시간 = min(ActionMaxSec, 반박자 × 이 값).</summary>
        public const float ActionBeatFactor = 0.9f;
        /// <summary>곡 끝 위치에서 비상구까지(유닛).</summary>
        public const float ExitDistanceAfterEnd = 3f;
        /// <summary>카운트다운 3·2·1 각 표시 시간(초). 시작 카운트다운(곡 시간 음수 구간)과 일시정지 재개 카운트다운이 같이 쓴다.</summary>
        public const float CountdownStepSec = 0.7f;

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

        // ── 흐름·난이도 선택 화면 (T5, 480×270 기준) ──
        /// <summary>파일 선택 첫 화면의 난이도 줄 y(윗변 기준).</summary>
        public const float UiSelectDifficultyY = -30f;
        /// <summary>Songs 목록 화면의 난이도 줄 y(윗변 기준). 목록 맨 아래 줄과 오류 문구 사이.</summary>
        public const float UiListDifficultyY = -62f;

        // ── 레벨·주인공·카메라 (T5) ──
        /// <summary>그리기 순서(2.5절): 배경 0, 복도 1, 장애물 2, 이웃 3, 주인공 4, 이펙트 5.</summary>
        public const int SortOrderBackground = 0;
        public const int SortOrderCorridor = 1;
        public const int SortOrderObstacle = 2;
        public const int SortOrderChaser = 3;
        public const int SortOrderPlayer = 4;
        public const int SortOrderEffect = 5;
        /// <summary>
        /// 카운트다운 단계 수(3·2·1). 곡 시작 전 대기 = CountdownStepSec × CountdownSteps이고, 그동안 주인공은 x=0에 서 있다(T8, 8.1).
        /// 일시정지 뒤 재개(Resuming)의 3·2·1도 같은 단계 수·간격을 쓴다.
        /// </summary>
        public const int CountdownSteps = 3;
        /// <summary>
        /// 카운트다운 길이만큼의 거리(유닛). T5 때 주인공이 곡 시작 전에 달려오던 거리였고, T8부터 주인공은 x=0에 서 있지만
        /// 복도를 시작선 왼쪽으로 이만큼 더 깔아 이웃이 걸어 들어오는 화면 왼쪽 밖과 미리보기 시작 구도를 덮는 데 계속 쓴다.
        /// </summary>
        public const float LeadInDistanceUnits = CountdownStepSec * CountdownSteps * ScrollSpeed;
        /// <summary>복도를 시작선 왼쪽(LeadInDistanceUnits 더 왼쪽)과 비상구 오른쪽으로 더 까는 여유(유닛). 카메라가 주인공 40% 위치라 왼쪽 0.4폭·오른쪽 0.6폭이 보이므로 21:9 와이드(폭 약 39유닛)도 덮는 값.</summary>
        public const float LevelMarginUnits = 32f;
        /// <summary>복도 위·아래층 타일에 곱하는 색(9.3절: 어둡게).</summary>
        public static readonly Color OtherStoryTint = new Color(0.4f, 0.4f, 0.45f, 1f);
        /// <summary>카메라 중심 y. 복도 띠(바닥~천장)의 한가운데.</summary>
        public const float CameraY = FloorY + CeilingHeight / 2f;
        /// <summary>점프 곡선 y = JumpHeight × sin(π × u^지수), u = 경과/동작 시간. 1보다 작으면 빨리 솟는다(autoPlay가 노트 시각에 정확히 누르므로 그 순간 장애물 위에 떠 있어야 한다).</summary>
        public const float JumpRiseExponent = 0.5f;
        /// <summary>음파 이펙트의 주인공 앞 오프셋(유닛, 12px).</summary>
        public const float WaveEffectOffsetUnits = 0.75f;

        // ── 판정·점수 (T6, 6절·7.3) ──
        /// <summary>Perfect 창 ±초. 사용자 결정(T6 직접 플레이 "판정이 너무 박함"): 0.035 → 0.060. 곡마다 RhythmJudge.PerfectWindowFor(실제 Good 창 이하)로 쓴다.</summary>
        public const double PerfectWindowSec = 0.060;
        /// <summary>Good 창 ±초(상한). 사용자 결정: 0.070 → 0.100. 이 밖은 헛박자.
        /// 빠른 곡에서 이웃 칸 창이 겹치지 않도록 실제 창은 min(이 값, 반박자 × GoodWindowMaxHalfBeatRatio)이다(RhythmJudge.GoodWindowFor).</summary>
        public const double GoodWindowSec = 0.100;
        /// <summary>Good 창 반폭의 반박자 대비 상한. 0.4면 칸 사이의 20%(양쪽 창 합 80%)가 헛박자 구간으로 남는다. 150 BPM보다 빠른 곡부터 Good 창이 줄어든다.</summary>
        public const double GoodWindowMaxHalfBeatRatio = 0.4;
        public const int ScorePerfect = 300;
        public const int ScoreGood = 100;
        /// <summary>점수 = base × (1 + min(combo, 이 값) / 이 값). 이 콤보에서 배율 최대 2배.</summary>
        public const int ComboBonusCap = 100;

        // ── 판정 반응 (T6, 6.3) ──
        /// <summary>Miss 때 주인공 맞음 프레임(PlayerHit)을 보여 주는 시간(초). 사용자 결정: 틀린 키도 동작 없이 맞음 프레임만.</summary>
        public const float HitFlashSec = 0.25f;
        /// <summary>헛박자 때 휘청 프레임(PlayerStumble)을 보여 주는 시간(초).</summary>
        public const float StumbleSec = 0.2f;
        /// <summary>방화문이 음파에 날아가는 연출 시간(초). 끝나면 숨긴다.</summary>
        public const float DoorBlownSec = 0.4f;
        /// <summary>날아가는 동안 오른쪽(진행 방향)으로 가는 거리(유닛).</summary>
        public const float DoorBlownDistanceUnits = 3f;
        /// <summary>날아가는 동안 위로 뜨는 높이(유닛).</summary>
        public const float DoorBlownRiseUnits = 1f;
        /// <summary>날아가는 동안 도는 각도(도, 음수 = 시계 방향).</summary>
        public const float DoorBlownSpinDeg = -90f;

        // ── 추격자 (T7, 7절) ──
        // 사용자 결정(T7 작업 전): 줌 0.5에서 주인공 왼쪽은 6.0유닛(반폭 7.5 × 0.8)만 보인다. 잡힘 거리를 ChaserStandOffset(1.5, 몸이 막 닿는 거리)로,
        // 시작 4.5(온몸 보임)·상한 6.0(화면 왼쪽 끝, 반쯤 보임)으로 두고, 나머지는 여유폭 비율 3/8(명세 처음 값 시작 8·잡힘 0 대비)로 줄였다.
        // 그래서 균형(시작에서 Miss 8번이면 잡힘, 7.2 계산)은 처음 값과 같다.
        /// <summary>시작 거리(유닛). 화면상 이웃 x = 주인공 x − 표시 거리.</summary>
        public const float ChaserStartDistance = 4.5f;
        /// <summary>밀어내기 상한(유닛). 화면 왼쪽 끝(이웃 몸 절반이 보임).</summary>
        public const float ChaserMaxDistance = 6.0f;
        /// <summary>곡 시작·끝의 접근 속도(유닛/초). 그 사이는 곡 진행률에 비례(7.1).</summary>
        public const float ChaserCloseSpeedStart = 0.0075f;
        public const float ChaserCloseSpeedEnd = 0.03f;
        /// <summary>Miss·헛박자 때 즉시 접근하는 거리(유닛).</summary>
        public const float ChaserMissApproach = 0.375f;
        public const float ChaserOffBeatApproach = 0.375f;
        /// <summary>밀어내기 시점 콤보. 마지막 값 이후는 ComboMilestoneRepeat의 배수마다(200, 300, ...).</summary>
        public static readonly int[] ComboMilestones = { 10, 25, 50, 100 };
        public const int ComboMilestoneRepeat = 100;
        /// <summary>콤보 마일스톤마다 밀어내는 거리(유닛). 결과는 ChaserMaxDistance를 넘지 않는다.</summary>
        public const float ChaserMilestonePush = 0.75f;
        /// <summary>이 거리 이하면 잡힘(유닛). 거리는 이 값 아래로 내려가지 않는다. 8.3 마주봄 간격 ChaserStandOffset과 같은 값(24px).</summary>
        public const float ChaserCatchDistance = 1.5f;
        /// <summary>표시 거리 추종 시간 상수(초). "확 다가옴"이 0.2초 안팎으로 보이게.</summary>
        public const float ChaserLerpTau = 0.07f;
        /// <summary>[Chaser] 거리 로그를 남기는 곡 시간 간격(초). Miss·헛박자·밀어내기·잡힘은 그때마다 따로 남긴다.</summary>
        public const double ChaserLogIntervalSec = 1.0;

        // ── 연출 (T8, 8절). 사용자 결정(T8 작업 전): 미리보기 동안 이웃 숨김, 카운트다운 동안 주인공은 x=0에 서 있고
        //    "3"에 이웃이 화면 왼쪽 밖에서 걸어 들어오고 "2"에 주인공이 왼쪽을 보고 놀라며 "1"에 다시 오른쪽을 본다. ──
        /// <summary>미리보기 1구간: 시작 위치의 주인공 비춤(초).</summary>
        public const float PreviewHoldStartSec = 1.0f;
        /// <summary>미리보기 2구간: 비상구까지 훑기(초, easeInOutCubic).</summary>
        public const float PreviewFlySec = 2.5f;
        /// <summary>미리보기 3구간: 비상구에서 멈춤(초).</summary>
        public const float PreviewHoldEndSec = 0.4f;
        /// <summary>미리보기 4구간: 시작 위치로 복귀(초, easeInOutCubic).</summary>
        public const float PreviewReturnSec = 0.8f;
        /// <summary>곡 시작과 함께 "GO"를 띄우는 시간(초).</summary>
        public const float GoTextSec = 0.5f;
        /// <summary>카운트다운 "3" 동안 이웃이 걸어 들어오기 시작하는 거리(유닛). 주인공 왼쪽은 6.0유닛만 보이고 이웃 반폭이 약 1.75유닛이라 완전히 화면 밖.</summary>
        public const float ChaserWalkInStartDistance = 8.0f;
        /// <summary>이웃이 ChaserWalkInStartDistance에서 ChaserStartDistance까지 걸어 들어오는 시간(초). "3" 한 칸.</summary>
        public const float ChaserWalkInSec = CountdownStepSec;
        /// <summary>놀람: 제자리 작은 점프 시간(초)과 높이(유닛, 8px).</summary>
        public const float SurpriseHopSec = 0.3f;
        public const float SurpriseHopHeight = 0.5f;
        /// <summary>놀람: 머리 위 "!" 표시 시간(초). "2" 한 칸.</summary>
        public const float SurpriseMarkSec = CountdownStepSec;
        /// <summary>"!" 표시의 주인공 발 기준 y 오프셋(유닛). 주인공 키 1유닛 바로 위.</summary>
        public const float SurpriseMarkOffsetY = 1.25f;
        /// <summary>탈출: 도착 몇 초 전 문 열림 / 도착 뒤 숨김까지 / 숨긴 뒤 문 닫힘까지 / "탈출 성공!" 표시(초).</summary>
        public const float EscapeDoorOpenSec = 0.2f;
        public const float EscapeEnterSec = 0.3f;
        public const float EscapeDoorCloseSec = 0.2f;
        public const float EscapeBannerSec = 1.5f;
        /// <summary>잡힘 1구간 감속(초). 주인공 속도 ScrollSpeed → 0(easeOut), 달리기 프레임 속도도 같이 줄고 음악은 같은 시간 동안 페이드아웃.</summary>
        public const float CaughtSlowSec = 0.6f;
        /// <summary>잡힘 2구간 마주봄(초). 시작 0.2초 뒤(잡힘 기준 0.8초) 주인공이 왼쪽을 본다.</summary>
        public const float CaughtStareSec = 0.8f;
        public const float CaughtTurnDelaySec = 0.2f;
        /// <summary>잡힘 3구간 빗자루 들어올림(초).</summary>
        public const float CaughtWindupSec = 0.3f;
        /// <summary>강타 순간 정지(초, 실시간).</summary>
        public const float CaughtHitStopSec = 0.08f;
        /// <summary>강타 화면 흔들림 진폭(유닛)과 시간(초).</summary>
        public const float CaughtShakeAmp = 0.5f;
        public const float CaughtShakeSec = 0.4f;
        /// <summary>잡힘 5구간 여운(초): 휘두르기 3프레임 유지(강타 1.7초 → 2.4초).</summary>
        public const float CaughtLingerSec = 0.7f;
        /// <summary>"GAME OVER" 표시(초).</summary>
        public const float GameOverHoldSec = 1.6f;
        /// <summary>강타 때 마주 선 간격(유닛, 24px). 휘두르기 프레임의 솔이 이 거리의 납작한 주인공 머리에 닿게 그렸다(9.3).</summary>
        public const float ChaserStandOffset = ChaserCatchDistance;
        /// <summary>감속·마주봄 동안의 간격(유닛, 28px). 사용자 결정(T8): 대기 프레임 몸이 24px에서 약 3px 겹쳐서 넓혀 서고, 들어올림 때 ChaserStandOffset으로 한 걸음 다가선다.</summary>
        public const float ChaserFaceOffset = 1.75f;
        /// <summary>결과 화면 반투명 패널의 불투명도. 사용자 결정(T8): 마지막 장면 위에 덮는다.</summary>
        public const float ResultPanelAlpha = 0.65f;
        /// <summary>일시정지 화면 어두운 패널의 불투명도(게임 장면이 비치게 반투명).</summary>
        public const float PausePanelAlpha = 0.5f;
        /// <summary>일시정지 화면 제목·안내 줄의 가운데 y(480×270 기준·화면 중앙이 0·위쪽이 +). 주인공(약 −17~−47)과 머리 위에 멈춰 있는 판정 문구(약 +20~+50)를 피해 그 위에 둔다(T8 스크린샷: 70/40이면 "PERFECT"와 안내 줄이 겹침).</summary>
        public const float UiPauseTitleY = 90f;
        public const float UiPauseHintY = 62f;
        /// <summary>일시정지 화면의 조작키 4줄 블록 가운데 y(사용자 요청, 제출 준비). 복도 바닥 아래와 타이밍바(윗변 −116) 사이 빈 곳. −85면 마지막 줄이 타이밍바에 닿아(T8 스크린샷) 올렸다.</summary>
        public const float UiPauseControlsY = -79f;
        /// <summary>타이틀 조작법 창: 제목 y, 본문 블록 가운데 y(사용자 요청, 제출 준비). 닫기 안내는 UiFooterY.</summary>
        public const float UiHelpTitleY = 100f;
        public const float UiHelpBodyY = 0f;
        /// <summary>결과 화면 줄 가운데 y: 제목 / 곡 이름 / 난이도 / 점수 / Perfect·Good·Miss / 최대 콤보. 안내 줄은 UiFooterY(아래쪽).</summary>
        public const float UiResultTitleY = 92f;
        public const float UiResultSongY = 54f;
        public const float UiResultDifficultyY = 30f;
        public const float UiResultScoreY = 6f;
        public const float UiResultJudgeY = -20f;
        public const float UiResultComboY = -40f;

        // ── HUD (T6, 480×270 기준·화면 중앙이 0·위쪽이 +) ──
        /// <summary>판정 문구 표시 시간(초).</summary>
        public const float JudgeTextSec = 0.5f;
        /// <summary>판정 문구 위치: 주인공 머리 위의 월드 y(유닛). 주인공 16px(1유닛), 점프 정점 약 2.25유닛, 천장 3유닛.</summary>
        public const float JudgeTextWorldY = 2.5f;
        /// <summary>오른쪽 위 점수·콤보 줄의 윗변 y.</summary>
        public const float UiScoreY = 130f;
        public const float UiComboY = 112f;
        public static readonly Color JudgePerfectColor = new Color(0.3f, 0.9f, 0.4f, 1f);
        public static readonly Color JudgeGoodColor = new Color(1f, 0.85f, 0.2f, 1f);
        public static readonly Color JudgeMissColor = new Color(1f, 0.35f, 0.35f, 1f);
        public static readonly Color JudgeOffBeatColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        // ── 타이밍바 (T6 사용자 결정: 명세 9.3 비트 눈금 대신) ──
        /// <summary>선 두 개가 양 끝에서 가운데까지 오는 시간(초). 가운데 = 다음 장애물 시각. 바 반폭이 이 시간에 해당하는 고정 비율.
        /// 사용자 결정(T6 스크린샷 뒤): 1.0 → 0.5. 선이 2배 빨라지고 초록·노랑 영역 폭이 2배(Perfect 약 8px, Good 약 17px)가 된다. 판정 창(ms)은 그대로.</summary>
        public const double TimingBarLeadSec = 0.5;
        /// <summary>HUD·타이밍바의 스크립트 실행 순서. 기본(0)인 GameFlow(판정)·CameraDirector(LateUpdate 카메라 이동) 뒤에 돌아 같은 프레임의 판정·카메라 위치를 반영한다(T6 리뷰 L1·L2).</summary>
        public const int ViewAfterGameplayExecutionOrder = 100;
        /// <summary>바 크기와 가운데 y(px).</summary>
        public const float TimingBarWidth = 240f;
        public const float TimingBarHeight = 8f;
        public const float TimingBarY = -120f;
        /// <summary>움직이는 타이밍 선 크기(px). 바보다 위아래로 조금 길다.</summary>
        public const float TimingBarLineWidth = 2f;
        public const float TimingBarLineHeight = 14f;
        /// <summary>가운데 붐박스 아이콘(BeatIcon 16×16)의 가운데 y(px). 바 바로 위.</summary>
        public const float BeatIconY = -104f;
        /// <summary>정박 때 아이콘 확대 배율과 지속 시간(초).</summary>
        public const float BeatIconPulseScale = 1.3f;
        public const float BeatIconPulseSec = 0.12f;
        /// <summary>바 영역 색: Good 창 밖(회색), Good 창(노랑), Perfect 창(초록). 판정 문구 색과 같다.</summary>
        public static readonly Color TimingBarMissColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        public static readonly Color TimingBarGoodColor = JudgeGoodColor;
        public static readonly Color TimingBarPerfectColor = JudgePerfectColor;
        public static readonly Color TimingBarLineColor = Color.white;
    }
}
