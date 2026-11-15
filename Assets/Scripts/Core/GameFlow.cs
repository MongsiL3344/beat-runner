using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NoiseRun
{
    public enum GameState { Title, Calibrating, SelectFile, Analyzing, Preview, Countdown, Playing, Paused, Resuming, Caught, Escaped, Result }

    /// <summary>
    /// 상태 머신과 입력 분배. 입력은 Update에서 Keyboard.current로만 읽고,
    /// 한 프레임의 입력은 그 프레임 시작 시점의 상태 하나에서만 처리한다. 화면 그리기는 MenuScreens·AnalysisDebugView·ResultScreen이 맡는다.
    /// 시간에 의존하는 로직은 두 곳으로 모은다: 곡 시간에 묶인 Countdown·Playing은 Step(songTime, dt), 타이머로 흐르는 연출
    /// (Preview 진행 확인, Resuming 3·2·1, Caught, Escaped)은 Advance(dt). Update가 둘을 부르고 테스트는 직접 부른다.
    /// 흐름: Title → SelectFile → Analyzing → Preview(플라이스루, Space/Enter 스킵) → Countdown(곡 시간 −2.1~0, 이웃 걸어 들어옴·놀람·돌아봄)
    /// → Playing(판정·추격자) ⇄ Paused/Resuming, Playing → Caught(8.3) 또는 Escaped(8.2) → Result(←/→ 난이도, R 다시, N 다른 곡).
    /// startAt=debug이면 분석 뒤 Preview 대신 분석 디버그 화면(곡을 처음부터 재생)으로 간다.
    /// Time.timeScale은 Preview·Caught·Escaped = PresentationTimeScale(개발 옵션, 기본 1), Paused·Resuming = 0, 나머지 1이다.
    /// 연출 단계가 바뀔 때마다 "[Seq] 연출 단계 t=경과초"를 Console에 남긴다. Countdown은 t = 곡 시간 + CountdownStepSec × CountdownSteps(카운트다운 시작 기준),
    /// Resuming은 재개 카운트다운 시작 기준 실시간이다.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        enum SelectView { Main, SongList }

        // 연출 이름·단계 이름(SequenceStage와 [Seq] 로그가 같은 문자열을 쓴다)
        const string PreviewName = "Preview";
        const string CaughtName = "Caught";
        const string EscapedName = "Escaped";
        const string CountdownName = "Countdown";
        const string ResumingName = "Resuming";
        public const string StageHold = "hold";
        public const string StageFly = "fly";
        public const string StageHoldEnd = "holdEnd";
        public const string StageReturn = "return";
        public const string StageSlow = "slow";
        public const string StageStare = "stare";
        public const string StageTurn = "turn";
        public const string StageWindup = "windup";
        public const string StageHit = "hit";
        public const string StageLinger = "linger";
        public const string StageGameOver = "gameover";
        public const string StageRun = "run";
        public const string StageOpen = "open";
        public const string StageEnter = "enter";
        public const string StageClose = "close";
        public const string StageBanner = "banner";
        public const string StageGo = "go";         // Countdown 끝(곡 0초). "3"·"2"·"1" 단계 이름은 숫자 그대로
        public const string StageResume = "resume"; // Resuming 끝(곡 재개). "3"·"2"·"1" 단계 이름은 숫자 그대로

        // 곡 시작 전 카운트다운 길이 = 재개 카운트다운 길이(초)
        const float CountdownTotalSec = GameConfig.CountdownStepSec * GameConfig.CountdownSteps;

        const string GoText = "GO";
        const string GameOverText = "GAME OVER";
        const string EscapedBannerText = "탈출 성공!";

        // Caught 연출 단계 번호
        const int CaughtSlow = 0, CaughtStare = 1, CaughtWindup = 2, CaughtHit = 3, CaughtLinger = 4, CaughtGameOver = 5;
        // Escaped 연출 단계 번호
        const int EscapeRun = 0, EscapeOpen = 1, EscapeEnter = 2, EscapeClose = 3, EscapeBanner = 4;

        // Caught 단계 시작 시각(잡힌 순간 기준, 초). 히트스톱은 타임라인을 멈추므로 포함하지 않는다.
        static readonly float CaughtStareAt = GameConfig.CaughtSlowSec;
        static readonly float CaughtTurnAt = CaughtStareAt + GameConfig.CaughtTurnDelaySec;
        static readonly float CaughtWindupAt = CaughtStareAt + GameConfig.CaughtStareSec;
        static readonly float CaughtHitAt = CaughtWindupAt + GameConfig.CaughtWindupSec;
        static readonly float CaughtGameOverAt = CaughtHitAt + GameConfig.CaughtLingerSec;
        static readonly float CaughtResultAt = CaughtGameOverAt + GameConfig.GameOverHoldSec;

        public GameState State { get; private set; } = GameState.Title;
        public event Action<GameState, GameState> StateChanged;

        /// <summary>분석이 끝난 곡의 비트맵. 분석 성공 뒤(Preview)부터 곡을 해제할 때까지 값이 있다. R로 난이도가 바뀌면 같은 곡의 새 맵으로 교체된다.</summary>
        public BeatMap Map { get; private set; }

        /// <summary>SelectFile 화면에 띄우는 직전 오류 문구. 없으면 null.</summary>
        public string LastError => lastError;

        /// <summary>
        /// 다음 분석에 쓸 난이도(5.7). 런타임에만 유지하고 저장하지 않는다. 설정하면 파일 선택·Songs 목록·결과 화면의 난이도 표시도 바뀐다.
        /// SelectFile·Result에서 ←/→로 한 단계씩 바뀐다. Result에서 바꾸면 R(다시)로 새 판을 시작할 때 그 난이도로 맵을 다시 만든다.
        /// </summary>
        public Difficulty CurrentDifficulty
        {
            get => currentDifficulty;
            set
            {
                currentDifficulty = value;
                if (menus != null) menus.Difficulty = value;
                if (result != null && State == GameState.Result) result.SetDifficulty(value);
            }
        }

        /// <summary>true면 Playing에서 노트 시각마다 정답 동작을 자동 실행한다. Bootstrap이 DevOptions.AutoPlay로 정하고 테스트가 직접 바꾼다.</summary>
        public bool AutoPlay { get; set; }

        /// <summary>true면 분석 성공 뒤 Preview 대신 T3 분석 디버그 화면(분석 결과를 그리고 곡을 처음부터 재생)으로 간다(startAt=debug). Bootstrap이 정한다.</summary>
        public bool DebugPreview { get; set; }

        /// <summary>Preview·Caught·Escaped 동안의 Time.timeScale(개발 옵션 timeScale). 0보다 커야 한다. Bootstrap이 DevOptions.TimeScale로 정한다.</summary>
        public float PresentationTimeScale
        {
            get => presentationTimeScale;
            set => presentationTimeScale = value > 0f ? value : 1f;
        }

        /// <summary>"caught" 또는 "escaped"면 Playing에 들어가는 즉시 해당 연출을 시작한다(개발 옵션 forceEnd). 그 밖은 무시. Bootstrap이 정한다.</summary>
        public string ForceEnd { get; set; }

        /// <summary>
        /// true(기본)면 창 포커스를 잃을 때 일시정지한다: Playing은 Paused로, Resuming은 Paused로 돌아가고,
        /// Countdown 중 포커스를 잃은 채 곡 0초(Playing 진입)가 되면 곧바로 일시정지한다. false면 포커스 변화를 무시한다
        /// (Editor에서 Game 뷰 밖을 눌러도 흐름이 바뀌지 않아야 하는 PlayMode 테스트가 끈다).
        /// </summary>
        public bool PauseOnFocusLoss { get; set; } = true;

        /// <summary>현재 Playing(또는 Countdown)에 들어온 뒤 autoPlay가 실행한 동작 수. 새 판 준비에서 0으로 돌린다.</summary>
        public int AutoActionsFired { get; private set; }

        /// <summary>박자 판정기(6.2). GameFlow가 만들어 Init에서 한 번 구독하며, 새 판을 준비할 때마다 Reset한다.</summary>
        public RhythmJudge Judge { get; } = new RhythmJudge();

        /// <summary>추격자(7절). Bootstrap이 만들어 Init으로 넘기며, 새 판을 준비할 때마다 ResetChase한다.</summary>
        public ChaserController Chaser => chaser;

        /// <summary>마지막으로 끝난 판의 요약. 잡힘·탈출 연출이 시작될 때 정해지고 Result에 그대로 쓴다. 한 판도 끝나지 않았으면 기본값.</summary>
        public ResultData LastResult { get; private set; }

        /// <summary>
        /// 진행 중인 연출의 현재 단계 이름(테스트·로그용). Preview: hold/fly/holdEnd/return, Caught: slow/stare/windup/hit/linger/gameover,
        /// Escaped: run/open/enter/close/banner. 연출 중이 아니면 null.
        /// Escaped 단계는 각각 시작하는 일을 뜻한다: open = 문 열림, enter = 도착해 문으로 들어감, close = 주인공이 숨고 문이 닫히는 중,
        /// banner = 문 닫힘 + "탈출 성공!" 표시.
        /// </summary>
        public string SequenceStage => stage;

        Conductor conductor;
        MenuScreens menus;
        AnalysisDebugView debugView;
        LevelBuilder level;
        PlayerController player;
        CameraDirector cameraDirector;
        Hud hud;
        BeatBarUI beatBar;
        ChaserController chaser;
        ResultScreen result;

        Difficulty currentDifficulty = GameConfig.DefaultDifficulty;
        float presentationTimeScale = 1f;
        int nextAutoNote;      // 다음에 지나갈 노트(Map.notes 인덱스)
        bool playerRunning;
        bool focused = true;   // 마지막으로 받은 창 포커스(OnApplicationFocus). Awake에서 Application.isFocused로 시작한다

        // 연출 공통
        string stage;
        float seqTime;         // 연출 시작 기준 경과(초). Caught는 히트스톱 시간을 포함하지 않는다.
        bool previewIsDebug;
        int lastPreviewSegment;

        // 카운트다운(곡 시간 기준)
        int countdownN;        // 지금 보이는 숫자. 0 = 아직 아무것도 안 띄움
        bool chaserArrived;
        bool goShown;

        // 재개 카운트다운(실시간)
        float resumeElapsed;
        int resumeN;

        // Caught
        int caughtStep;
        bool caughtTurned;
        float hitStopLeft;
        float caughtPlayerX0;
        float caughtChaserGap0;
        float caughtPlayerX;

        // Escaped
        int escapeStep;
        float escapeStartX;
        float escapeExitX;
        float escapeArriveAt;

        SelectView selectView = SelectView.Main;
        string songsFolder = string.Empty;
        string[] songs = Array.Empty<string>();
        int songIndex;
        string lastError;
        string songName = string.Empty;

        int loadToken;
        AudioClip currentClip;
        System.Collections.IEnumerator loadRoutine;

        AnalysisProgress progress;
        Task<AnalysisOutput> analysisTask;
        AnalysisOutput output;

        void Awake()
        {
            focused = Application.isFocused;
        }

        internal void Init(Conductor conductorToUse, MenuScreens menuScreens, AnalysisDebugView debugViewToUse,
            LevelBuilder levelToUse, PlayerController playerToUse, ChaserController chaserToUse, CameraDirector cameraDirectorToUse,
            Hud hudToUse, BeatBarUI beatBarToUse, ResultScreen resultScreen)
        {
            conductor = conductorToUse;
            menus = menuScreens;
            debugView = debugViewToUse;
            level = levelToUse;
            player = playerToUse;
            chaser = chaserToUse;
            cameraDirector = cameraDirectorToUse;
            hud = hudToUse;
            beatBar = beatBarToUse;
            result = resultScreen;
            Judge.Judged += OnJudged;
            chaser.Caught += OnCaught;
            menus.Difficulty = currentDifficulty;
            Render();
        }

        public void ChangeState(GameState next)
        {
            if (next == State) return;
            GameState prev = State;
            State = next;
            Time.timeScale = TimeScaleFor(next);
            if (next == GameState.SelectFile)
            {
                selectView = SelectView.Main;
                ClearLevel();
            }
            Render();
            StateChanged?.Invoke(prev, next);
        }

        float TimeScaleFor(GameState s)
        {
            switch (s)
            {
                case GameState.Preview: return previewIsDebug ? 1f : presentationTimeScale;
                case GameState.Caught:
                case GameState.Escaped: return presentationTimeScale;
                case GameState.Paused:
                case GameState.Resuming: return 0f;
                default: return 1f;
            }
        }

        /// <summary>파일 선택창과 Songs 목록을 건너뛰고 로딩을 시작한다.</summary>
        public void StartFromFile(string path)
        {
            BeginSession(string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path));

            int token = loadToken;
            loadRoutine = AudioLoader.Load(path, clip => OnLoaded(token, clip), error => OnLoadFailed(token, error));
            StartCoroutine(loadRoutine);
        }

        /// <summary>테스트용. 디코딩을 건너뛰고 분석부터 시작한다. clip은 이후 GameFlow가 소유한다(해제 때 Destroy).</summary>
        public void StartFromClip(AudioClip clip, string name)
        {
            BeginSession(name ?? string.Empty);

            currentClip = clip;
            conductor.Load(clip);
            BeginAnalysis();
        }

        /// <summary>이전 로딩·분석·클립을 정리하고 Analyzing 화면으로 들어간다.</summary>
        void BeginSession(string name)
        {
            CancelWork();
            ReleaseClip();
            lastError = null;
            songName = name;
            menus.SongName = name;

            if (State != GameState.Analyzing) ChangeState(GameState.Analyzing);
            else Render();
        }

        // ───────────── 입력 ─────────────

        void Update()
        {
            GameState before = State;
            if (Keyboard.current != null) HandleInput(Keyboard.current);

            // 분석 결과는 입력과 상관없이 확인한다. 입력이 상태를 바꾼 프레임이면 건너뛴다.
            if (State == GameState.Analyzing && analysisTask != null) PollAnalysis();

            // 입력이나 분석 결과가 상태를 바꾼 프레임에는 새 상태의 시간 진행을 건너뛴다.
            if (State != before) return;

            switch (State)
            {
                case GameState.Countdown:
                case GameState.Playing:
                    Step(conductor.SongTime, Time.deltaTime);
                    break;
                case GameState.Preview:
                case GameState.Escaped:
                    Advance(Time.deltaTime);
                    break;
                case GameState.Caught:
                    Advance(hitStopLeft > 0f ? Time.unscaledDeltaTime : Time.deltaTime); // 히트스톱은 timeScale 0이라 실시간으로 잰다
                    break;
                case GameState.Resuming:
                    Advance(Time.unscaledDeltaTime); // timeScale 0
                    break;
            }
        }

        void HandleInput(Keyboard kb)
        {
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

            // 상태별로 한 번만 분기한다. 상태가 바뀐 프레임에는 새 상태의 입력 처리가 실행되지 않는다.
            switch (State)
            {
                case GameState.Title:
                    if (enter) ChangeState(GameState.SelectFile);
                    break;
                case GameState.SelectFile:
                    if (selectView == SelectView.Main) UpdateSelectMain(kb, enter);
                    else UpdateSongList(kb, enter);
                    break;
                case GameState.Analyzing:
                    if (kb.escapeKey.wasPressedThisFrame) CancelAnalyzing();
                    break;
                case GameState.Preview:
                    if (kb.escapeKey.wasPressedThisFrame) BackToSelect();
                    else if (kb.spaceKey.wasPressedThisFrame || enter) SkipPreview();
                    break;
                case GameState.Playing:
                    if (kb.escapeKey.wasPressedThisFrame) PauseGame();
                    else HandlePlayingKeys(kb);
                    break;
                case GameState.Paused:
                    if (kb.escapeKey.wasPressedThisFrame) ResumeGame();
                    else if (kb.qKey.wasPressedThisFrame) BackToSelect();
                    break;
                case GameState.Result:
                    if (kb.rKey.wasPressedThisFrame) Retry();
                    else if (kb.nKey.wasPressedThisFrame) BackToSelect();
                    else StepDifficulty(kb);
                    break;
                // Countdown·Resuming·Caught·Escaped: 입력 없음(Resuming 중 Esc 무시)
            }
        }

        /// <summary>Space·↑→Jump(↑는 사용자 요청, T8 뒤), ↓→Slide, →→Wave. 곡 시작 전(songTime &lt; 0)과 곡이 끝난 뒤의 입력은 무시한다.
        /// Space와 ↑를 같은 프레임에 눌러도 점프 입력은 한 번이다(두 번째가 헛박자로 판정되지 않게).</summary>
        void HandlePlayingKeys(Keyboard kb)
        {
            if (Map == null) return;
            if (kb.spaceKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) Press(ActionType.Jump);
            if (kb.downArrowKey.wasPressedThisFrame) Press(ActionType.Slide);
            if (kb.rightArrowKey.wasPressedThisFrame) Press(ActionType.Wave);
        }

        void Press(ActionType action)
        {
            double t = conductor.SongTime;
            if (t < 0 || t > Map.songLength) return;
            Judge.OnInput(action, t);
        }

        /// <summary>
        /// 창 포커스 변화를 기억하고, 포커스를 잃으면 Playing은 일시정지하고 재개 카운트다운 중이면 다시 일시정지로 돌아간다.
        /// Countdown 중에 잃은 포커스는 Playing에 들어갈 때 확인한다(BeginPlaying). 포커스를 얻는 것은 기억만 한다. PauseOnFocusLoss가 false면 상태를 바꾸지 않는다.
        /// </summary>
        public void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (hasFocus || !PauseOnFocusLoss) return;
            if (State == GameState.Playing) PauseGame();
            else if (State == GameState.Resuming) ChangeState(GameState.Paused);
        }

        void UpdateSelectMain(Keyboard kb, bool enter)
        {
            if (enter) OpenFileDialog();
            else if (kb.fKey.wasPressedThisFrame) OpenSongList();
            else StepDifficulty(kb);
        }

        /// <summary>←/→로 난이도를 한 단계 낮추거나 높인다. 쉬움·어려움에서 멈춘다(순환 없음).</summary>
        void StepDifficulty(Keyboard kb)
        {
            int delta = kb.leftArrowKey.wasPressedThisFrame ? -1 : kb.rightArrowKey.wasPressedThisFrame ? 1 : 0;
            if (delta == 0) return;
            CurrentDifficulty = (Difficulty)Mathf.Clamp((int)CurrentDifficulty + delta, (int)Difficulty.Easy, (int)Difficulty.Hard);
        }

        void UpdateSongList(Keyboard kb, bool enter)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                selectView = SelectView.Main;
                Render();
            }
            else if (kb.upArrowKey.wasPressedThisFrame)
            {
                if (songs.Length > 0 && songIndex > 0) { songIndex--; Render(); }
            }
            else if (kb.downArrowKey.wasPressedThisFrame)
            {
                if (songs.Length > 0 && songIndex < songs.Length - 1) { songIndex++; Render(); }
            }
            else if (enter)
            {
                if (songs.Length > 0) StartFromFile(songs[songIndex]);
            }
            else
            {
                StepDifficulty(kb);
            }
        }

        // ───────────── 동작 ─────────────

        void OpenFileDialog()
        {
            string path;
            try
            {
                path = FilePicker.OpenDialog();
            }
            catch (Exception e)
            {
                Debug.LogWarning("파일 선택창을 열 수 없습니다: " + e);
                OpenSongList();
                lastError = "파일 선택창을 열 수 없어 Songs 폴더 목록으로 대신합니다 (" + e.GetType().Name + ")";
                Render();
                return;
            }

            if (path != null) StartFromFile(path); // null은 취소: 그대로 둔다
        }

        void OpenSongList()
        {
            selectView = SelectView.SongList;
            songIndex = 0;
            try
            {
                songsFolder = FilePicker.SongsFolder;
                songs = FilePicker.ListSongs(songsFolder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Songs 폴더를 읽을 수 없습니다: " + e);
                songs = Array.Empty<string>();
                lastError = "Songs 폴더를 읽을 수 없습니다: " + e.Message;
            }
            Render();
        }

        void CancelAnalyzing()
        {
            CancelWork();
            ReleaseClip();
            lastError = null;
            ChangeState(GameState.SelectFile);
        }

        /// <summary>
        /// Preview·Paused·Resuming·Result에서 파일 선택으로 돌아간다(Esc in Preview, Paused의 Q, Result의 N).
        /// 음악 정지, 레벨 정리, 클립 해제, Time.timeScale 복귀. 다른 상태에서는 아무것도 하지 않는다.
        /// </summary>
        public void BackToSelect()
        {
            if (State != GameState.Preview && State != GameState.Paused && State != GameState.Resuming && State != GameState.Result) return;
            ReleaseClip(); // conductor.Stop + ClearLevel 포함
            lastError = null;
            ChangeState(GameState.SelectFile);
        }

        // ───────────── Preview ─────────────

        /// <summary>
        /// 새 판 준비: 레벨을 만들고 판정·HUD·타이밍 바·주인공(x=0 대기, 오른쪽)·추격자(숨김)를 처음 상태로 돌린다.
        /// 이전 판의 음악 페이드아웃(볼륨 1 복구)과 카메라 흔들림도 끝낸다. 분석 직후(Preview)와 R(다시) 모두 여기서 시작한다. HUD와 타이밍 바는 숨긴 채 둔다.
        /// </summary>
        void PrepareRound()
        {
            conductor.CancelFade();
            cameraDirector.StopShake();
            level.Build(Map);
            nextAutoNote = 0;
            AutoActionsFired = 0;
            Judge.Reset(Map);
            hud.Clear();
            hud.SetVisible(false);
            beatBar.Bind(Map, conductor);
            beatBar.SetVisible(false);

            player.gameObject.SetActive(true);
            player.ResetPose();
            player.Face(true);
            player.SetX(0f);
            player.SetRunning(false);
            playerRunning = false;

            chaser.ResetChase();
            chaser.SetRunning(false); // ResetChase는 달리기 상태를 건드리지 않는다
            chaser.gameObject.SetActive(false);

            stage = null;
            seqTime = 0f;
            countdownN = 0;
            chaserArrived = false;
            goShown = false;
        }

        /// <summary>분석 성공 뒤: 레벨을 만들고 카메라 플라이스루를 시작한다(8.1). 이웃은 숨기고 HUD·타이밍 바도 숨긴다.</summary>
        void EnterPreview()
        {
            previewIsDebug = false;
            PrepareRound();

            cameraDirector.Follow(player.transform);
            cameraDirector.StartFlythrough(0f, level.ExitX);

            lastPreviewSegment = 0;
            SetStage(StageHold, PreviewName);
            ChangeState(GameState.Preview);
        }

        /// <summary>
        /// Preview 진행 확인: 플라이스루 구간이 바뀔 때마다 [Seq]를 남기고, 끝나면 Countdown으로 간다.
        /// 카메라 진행은 CameraDirector.Advance가 따로 한다(startAt=debug 화면에서는 아무것도 하지 않는다).
        /// </summary>
        void AdvancePreview(float dt)
        {
            if (previewIsDebug) return;
            seqTime += dt;

            if (!cameraDirector.FlythroughActive)
            {
                EnterCountdown();
                return;
            }

            int segment = cameraDirector.FlythroughSegment;
            if (segment != lastPreviewSegment && segment >= 0)
            {
                lastPreviewSegment = segment;
                SetStage(SegmentStage(segment), PreviewName);
            }
        }

        static string SegmentStage(int segment)
        {
            switch (segment)
            {
                case 0: return StageHold;
                case 1: return StageFly;
                case 2: return StageHoldEnd;
                default: return StageReturn;
            }
        }

        /// <summary>미리보기를 건너뛴다(Space/Enter): 카메라를 즉시 시작 위치로 옮기고 Countdown으로 간다. Preview가 아니거나 startAt=debug 화면이면 무시.</summary>
        public void SkipPreview()
        {
            if (State != GameState.Preview || previewIsDebug) return;
            EnterCountdown();
        }

        // ───────────── Countdown ─────────────

        /// <summary>
        /// 카운트다운 시작(8.1 5구간): 곡을 CountdownStepSec × CountdownSteps 뒤로 예약하고, 주인공은 x=0에 선 채 두며,
        /// 이웃은 화면 밖 왼쪽에서 걸어 들어올 준비를 한다. 이후 장면은 곡 시간으로 Step이 결정한다.
        /// </summary>
        void EnterCountdown()
        {
            stage = null;
            cameraDirector.SkipFlythrough();
            cameraDirector.Follow(player.transform);

            hud.SetVisible(true);
            beatBar.SetVisible(true);

            chaser.ResetChase();
            chaser.gameObject.SetActive(true);
            chaser.SetRunning(true); // 걸어 들어옴(달리기 프레임)
            chaser.PlaceAt(-GameConfig.ChaserWalkInStartDistance);
            chaserArrived = false;
            countdownN = 0;
            goShown = false;

            conductor.ScheduleStart(GameConfig.CountdownStepSec * GameConfig.CountdownSteps);

            ChangeState(GameState.Countdown); // Render가 오버레이를 모두 지운다
            UpdateCountdown(conductor.SongTime);
        }

        /// <summary>
        /// 곡 시간이 음수인 동안의 장면(8.1): 숫자 3·2·1, "3" 동안 이웃이 걸어 들어와 대기, "2" 시작에 주인공이 왼쪽을 보며 놀라고(Face(false) + PlaySurprise),
        /// "1" 시작에 다시 오른쪽을 보며 "!"·놀람 점프를 끝낸다(EndSurprise, 대기 프레임). 주인공은 x=0에 서 있다. 숫자가 바뀔 때마다 [Seq] Countdown을 남긴다.
        /// </summary>
        void UpdateCountdown(double songTime)
        {
            player.SetX(0f);

            double step = GameConfig.CountdownStepSec;
            int n = Math.Min(GameConfig.CountdownSteps, Math.Max(1, (int)Math.Ceiling(-songTime / step - 1e-9)));
            if (n != countdownN)
            {
                countdownN = n;
                string label = n.ToString(CultureInfo.InvariantCulture);
                menus.ShowCountdown(label);
                LogSeq(CountdownName, label, songTime + CountdownTotalSec);
                if (n == GameConfig.CountdownSteps - 1)
                {
                    player.Face(false);
                    player.PlaySurprise();
                }
                else if (n == 1)
                {
                    player.Face(true);
                    player.EndSurprise(); // "!"(SurpriseMarkSec)와 놀람 점프는 실시간이라 곡 시간 경계에서 확실히 끝낸다
                }
            }

            double walkStart = -GameConfig.CountdownSteps * step;
            float u = Mathf.Clamp01((float)((songTime - walkStart) / GameConfig.ChaserWalkInSec));
            chaser.PlaceAt(-Mathf.Lerp(GameConfig.ChaserWalkInStartDistance, GameConfig.ChaserStartDistance, u));
            if (u >= 1f && !chaserArrived)
            {
                chaserArrived = true;
                chaser.SetRunning(false); // 도착: 대기 프레임
            }
        }

        /// <summary>
        /// 곡 0초: 둘 다 달리기 시작, "GO" 표시. forceEnd 개발 옵션이 있으면 바로 그 연출로 간다.
        /// 카운트다운 중 창 포커스를 잃은 채이면(PauseOnFocusLoss) Playing에 들어가자마자 일시정지한다.
        /// </summary>
        void BeginPlaying(double songTime)
        {
            ChangeState(GameState.Playing);

            player.Face(true);
            player.SetRunning(true);
            playerRunning = true;
            chaser.SetRunning(true);
            chaser.Tick(songTime, Map.songLength, 0f, 0f); // 이웃을 주인공 뒤 ChaserStartDistance에 둔다

            menus.ShowCountdown(GoText);
            goShown = true;
            LogSeq(CountdownName, StageGo, songTime + CountdownTotalSec);

            if (ForceEnd == "caught") StartCaught();
            else if (ForceEnd == "escaped")
            {
                // 곡 끝 장면을 바로 본다: 주인공·이웃을 곡 끝 시각의 자리로 옮긴 뒤 탈출을 시작한다(그대로 두면 x=0에서 비상구까지 곡 길이만큼 달린다).
                float endX = Mathf.Min(level.TimeToX(Map.songLength), level.ExitX);
                player.SetX(endX);
                chaser.PlaceAt(endX - GameConfig.ChaserStartDistance);
                StartEscape();
            }

            if (State == GameState.Playing && PauseOnFocusLoss && !focused) PauseGame();
        }

        // ───────────── 일시정지 ─────────────

        /// <summary>Playing → Paused: 곡 시계·음악을 멈추고 Time.timeScale을 0으로 하고 일시정지 화면을 띄운다. Playing이 아니면 무시.</summary>
        public void PauseGame()
        {
            if (State != GameState.Playing) return;
            goShown = false;
            conductor.Pause();
            ChangeState(GameState.Paused); // timeScale 0, Render가 일시정지 화면을 띄운다
        }

        /// <summary>Paused → Resuming: 멈춘 화면 그대로 3·2·1(각 CountdownStepSec, 실시간) 뒤 곡을 이어 재생하고 Playing으로 간다. Paused가 아니면 무시.</summary>
        public void ResumeGame()
        {
            if (State != GameState.Paused) return;
            resumeElapsed = 0f;
            resumeN = 0;
            ChangeState(GameState.Resuming);
            UpdateResumeText();
        }

        void AdvanceResuming(float dt)
        {
            resumeElapsed += dt;
            if (resumeElapsed >= GameConfig.CountdownStepSec * GameConfig.CountdownSteps)
            {
                LogSeq(ResumingName, StageResume, resumeElapsed);
                conductor.Resume();
                ChangeState(GameState.Playing); // timeScale 1, Render가 숫자를 지운다
                return;
            }
            UpdateResumeText();
        }

        void UpdateResumeText()
        {
            int n = Math.Max(1, GameConfig.CountdownSteps - (int)(resumeElapsed / GameConfig.CountdownStepSec));
            if (n == resumeN) return;
            resumeN = n;
            string label = n.ToString(CultureInfo.InvariantCulture);
            menus.ShowCountdown(label);
            LogSeq(ResumingName, label, resumeElapsed);
        }

        // ───────────── 판정 ─────────────

        /// <summary>
        /// 판정 결과를 주인공 반응·문 날리기·추격자·HUD로 나눠 보낸다(6.3, 7.1). 판정기가 점수·콤보를 먼저 갱신하므로 여기서 읽으면 새 값이다.
        /// Perfect/Good/EmptyBeat는 누른 동작 애니메이션, 오답·놓침(Miss)은 피격 + 추격자 접근, OffBeat는 휘청거림 + 추격자 접근이다.
        /// </summary>
        void OnJudged(JudgeEvent e)
        {
            switch (e.judgement)
            {
                case Judgement.Perfect:
                case Judgement.Good:
                case Judgement.EmptyBeat:
                    player.DoAction(e.action, PlayerController.ActionDuration(Map.HalfBeatSec));
                    break;
                case Judgement.Miss:
                    player.PlayHit();
                    chaser.Approach(GameConfig.ChaserMissApproach);
                    break;
                case Judgement.OffBeat:
                    player.PlayStumble();
                    chaser.Approach(GameConfig.ChaserOffBeatApproach);
                    break;
            }

            level.OnJudged(e);
            chaser.OnComboChanged(Judge.Combo);
            hud.SetScore(Judge.Score);
            hud.SetCombo(Judge.Combo);
            hud.ShowJudgement(e.judgement);
        }

        /// <summary>추격자가 잡았다(ChaserController.Caught, Tick 안에서 한 번). Playing이면 8.3 잡힘 연출을 시작한다.</summary>
        void OnCaught()
        {
            if (State != GameState.Playing) return;
            StartCaught();
        }

        // ───────────── Step: Countdown·Playing ─────────────

        /// <summary>
        /// 곡 시간에 묶인 한 프레임. Countdown이면 장면(3·2·1·이웃 걷기·놀람·돌아봄)을 곡 시간으로 맞추고 songTime ≥ 0에서 Playing으로 넘어간다.
        /// Playing이면 주인공 x를 곡 시간에 맞추고(비상구에서 멈춤), 지나간 노트마다 커서를 전진시키며 autoPlay면 아직 판정되지 않은 노트에
        /// 정답 동작을 판정기로 넣는다. 그다음 Judge.Tick으로 놓친 노트를 Miss 처리하고, 추격자를 진행시킨다(잡히면 이 안에서 Caught 연출이 시작된다).
        /// 곡이 끝나면(songTime ≥ SongLength) 이웃은 곡 끝 위치 기준에서 멈추고 Escaped 연출이 시작된다.
        /// Update가 Conductor.SongTime과 Time.deltaTime으로 부르며, 테스트는 시계를 올리고 직접 부른다. 다른 상태에서는 아무것도 하지 않는다.
        /// </summary>
        public void Step(double songTime, float dt)
        {
            if (Map == null) return;

            if (State == GameState.Countdown)
            {
                if (songTime < 0.0)
                {
                    UpdateCountdown(songTime);
                    return;
                }
                BeginPlaying(songTime);
            }

            if (State != GameState.Playing) return; // 연출 시작(forceEnd)으로 상태가 바뀐 경우 포함
            StepPlaying(songTime, dt);
        }

        void StepPlaying(double songTime, float dt)
        {
            if (goShown && songTime >= GameConfig.GoTextSec)
            {
                goShown = false;
                menus.ShowCountdown(null);
            }

            float exitX = level.ExitX;
            float x = Mathf.Min(level.TimeToX(songTime), exitX);
            player.SetX(x);

            bool running = x < exitX;
            if (running != playerRunning)
            {
                playerRunning = running;
                player.SetRunning(running);
            }

            // 커서는 autoPlay와 상관없이 전진시킨다. 중간에 autoPlay를 켜도 지나간 노트를 한꺼번에 실행하지 않는다.
            // (프레임이 건너뛰어 노트 여러 개를 한 번에 지나면 모두 차례로 실행한다.)
            // 수동 입력이 이미 판정한 노트는 autoPlay가 다시 넣지 않는다.
            var notes = Map.notes;
            while (nextAutoNote < notes.Count && notes[nextAutoNote].time <= songTime)
            {
                Note note = notes[nextAutoNote];
                if (AutoPlay && !note.judged)
                {
                    Judge.OnInput(RhythmJudge.RequiredAction(note.kind), note.time);
                    AutoActionsFired++;
                }
                nextAutoNote++;
            }

            Judge.Tick(songTime);

            bool songRunning = songTime < Map.songLength;
            chaser.SetRunning(songRunning);
            float chaserAnchorX = songRunning ? x : Mathf.Min(level.TimeToX(Map.songLength), exitX);
            chaser.Tick(songTime, Map.songLength, dt, chaserAnchorX);

            // 잡혔으면 위 Tick 안에서 이미 Caught 연출이 시작되어 State가 Playing이 아니다.
            if (State == GameState.Playing && !songRunning) StartEscape();
        }

        // ───────────── Advance: 타이머 연출 ─────────────

        /// <summary>
        /// 타이머로 흐르는 연출을 dt초 진행한다. Preview(플라이스루 끝 확인), Resuming(실시간 3·2·1), Caught(8.3), Escaped(8.2)를 맡고 다른 상태에서는 아무것도 하지 않는다.
        /// Update가 Time.deltaTime(Resuming과 히트스톱 중 Caught는 unscaledDeltaTime)으로 부르며, 테스트는 직접 부른다.
        /// dt가 커도 지나간 단계를 차례로 모두 실행한다. 히트스톱은 Caught 타임라인의 한 구간(dt를 CaughtHitStopSec만큼 소비)이라 테스트에서도 결정적이다.
        /// </summary>
        public void Advance(float dt)
        {
            switch (State)
            {
                case GameState.Preview: AdvancePreview(dt); break;
                case GameState.Resuming: AdvanceResuming(dt); break;
                case GameState.Caught: AdvanceCaught(dt); break;
                case GameState.Escaped: AdvanceEscaped(dt); break;
            }
        }

        void SetStage(string stageName, string sequenceName)
        {
            stage = stageName;
            LogSeq(sequenceName, stageName, seqTime);
        }

        /// <summary>"[Seq] 연출 단계 t=경과초(소수 3자리)". 경과는 0 아래로 내려가지 않게 적는다.</summary>
        static void LogSeq(string sequenceName, string stageName, double t)
        {
            Debug.Log("[Seq] " + sequenceName + " " + stageName + " t=" + Math.Max(0.0, t).ToString("F3", CultureInfo.InvariantCulture));
        }

        static float EaseOut(float u)
        {
            return 1f - (1f - u) * (1f - u);
        }

        // ───────────── Caught ─────────────

        void StartCaught()
        {
            caughtStep = CaughtSlow;
            caughtTurned = false;
            hitStopLeft = 0f;
            seqTime = 0f;
            caughtPlayerX0 = player.transform.position.x;
            caughtPlayerX = caughtPlayerX0;
            caughtChaserGap0 = caughtPlayerX0 - chaser.transform.position.x;
            LastResult = BuildResult(false);

            hud.SetVisible(false);
            beatBar.SetVisible(false);
            StartCoroutine(conductor.FadeOutAndStop(GameConfig.CaughtSlowSec)); // 취소는 Conductor.CancelFade(세대 번호)로만 한다

            ChangeState(GameState.Caught); // timeScale = 개발 옵션 값, Render가 오버레이를 지운다
            SetStage(StageSlow, CaughtName);
            ApplyCaughtSlow(0f);
        }

        /// <summary>감속(8.3 1구간): 주인공 속도 ScrollSpeed → 0(easeOut)을 적분한 x, 달리기 애니 속도 1 → 0, 이웃은 playerX − 간격(현재 간격 → ChaserFaceOffset)에 따라와 선다.</summary>
        void ApplyCaughtSlow(float u)
        {
            float rest = (1f - u) * (1f - u);
            caughtPlayerX = caughtPlayerX0 + GameConfig.ScrollSpeed * GameConfig.CaughtSlowSec / 3f * (1f - rest * (1f - u));
            player.SetX(caughtPlayerX);
            player.SetRunAnimSpeed(rest);
            chaser.PlaceAt(caughtPlayerX - Mathf.Lerp(caughtChaserGap0, GameConfig.ChaserFaceOffset, EaseOut(u)));
        }

        void AdvanceCaught(float dt)
        {
            float remaining = dt;
            while (true)
            {
                if (hitStopLeft > 0f)
                {
                    float used = Mathf.Min(remaining, hitStopLeft);
                    hitStopLeft -= used;
                    remaining -= used;
                    if (hitStopLeft > 0f) return;
                    EndHitStop();
                }

                if (caughtStep < CaughtHit && remaining >= CaughtHitAt - seqTime)
                {
                    remaining -= CaughtHitAt - seqTime;
                    seqTime = CaughtHitAt; // 강타 시각에서 정확히 멈춰 히트스톱을 건다
                }
                else
                {
                    seqTime += remaining;
                    remaining = 0f;
                }

                UpdateCaught();
                if (State != GameState.Caught || remaining <= 0f) return;
            }
        }

        void UpdateCaught()
        {
            if (caughtStep == CaughtSlow)
            {
                ApplyCaughtSlow(Mathf.Clamp01(seqTime / GameConfig.CaughtSlowSec));
                if (seqTime >= CaughtStareAt)
                {
                    ApplyCaughtSlow(1f);
                    player.SetRunning(false); // 마주봄: 둘 다 대기 프레임
                    chaser.SetRunning(false);
                    caughtStep = CaughtStare;
                    SetStage(StageStare, CaughtName);
                }
            }

            if (caughtStep == CaughtStare && !caughtTurned && seqTime >= CaughtTurnAt)
            {
                caughtTurned = true;
                player.Face(false);
                LogSeq(CaughtName, StageTurn, seqTime);
            }

            if (caughtStep == CaughtStare && seqTime >= CaughtWindupAt)
            {
                chaser.SetSwingFrame(0); // 빗자루 들어올림
                chaser.PlaceAt(caughtPlayerX - GameConfig.ChaserStandOffset); // 한 걸음 다가섬
                caughtStep = CaughtWindup;
                SetStage(StageWindup, CaughtName);
            }

            if (caughtStep == CaughtWindup && seqTime >= CaughtHitAt)
            {
                chaser.SetSwingFrame(1); // 내리침
                player.ShowFlattened();
                cameraDirector.Shake(GameConfig.CaughtShakeAmp, GameConfig.CaughtShakeSec);
                hitStopLeft = GameConfig.CaughtHitStopSec;
                Time.timeScale = 0f; // 히트스톱: 실시간으로 CaughtHitStopSec 동안 정지
                caughtStep = CaughtHit;
                SetStage(StageHit, CaughtName);
            }

            if (caughtStep == CaughtLinger && seqTime >= CaughtGameOverAt)
            {
                menus.ShowBanner(GameOverText);
                caughtStep = CaughtGameOver;
                SetStage(StageGameOver, CaughtName);
            }

            if (caughtStep == CaughtGameOver && seqTime >= CaughtResultAt) EnterResult();
        }

        void EndHitStop()
        {
            hitStopLeft = 0f;
            Time.timeScale = TimeScaleFor(GameState.Caught);
            chaser.SetSwingFrame(2); // 여운: 솔이 머리를 누른 채 유지
            caughtStep = CaughtLinger;
            SetStage(StageLinger, CaughtName);
        }

        // ───────────── Escaped ─────────────

        /// <summary>
        /// 탈출 시작(8.2, 곡 끝): 주인공은 지금 x에서 같은 속도로 비상구까지 달리고(자체 타이머), 이웃은 그 자리에 선다.
        /// 도착 EscapeDoorOpenSec 전 문 열림 → 도착 → EscapeEnterSec 뒤 주인공 숨김 → EscapeDoorCloseSec 뒤 문 닫힘 + "탈출 성공!" EscapeBannerSec → Result.
        /// </summary>
        void StartEscape()
        {
            escapeStep = EscapeRun;
            seqTime = 0f;
            escapeStartX = player.transform.position.x;
            escapeExitX = level.ExitX;
            escapeArriveAt = Mathf.Max(0f, escapeExitX - escapeStartX) / GameConfig.ScrollSpeed;
            LastResult = BuildResult(true);

            chaser.SetRunning(false);
            beatBar.SetVisible(false); // 곡이 끝나 다음 노트가 없다. 점수 HUD는 남긴다
            ChangeState(GameState.Escaped);
            SetStage(StageRun, EscapedName);
            UpdateEscape();
        }

        void AdvanceEscaped(float dt)
        {
            seqTime += dt;
            UpdateEscape();
        }

        void UpdateEscape()
        {
            float hideAt = escapeArriveAt + GameConfig.EscapeEnterSec;
            float closeAt = hideAt + GameConfig.EscapeDoorCloseSec;
            float resultAt = closeAt + GameConfig.EscapeBannerSec;

            if (escapeStep < EscapeClose) // 숨은 뒤에는 위치를 건드리지 않는다
                player.SetX(Mathf.Min(escapeStartX + GameConfig.ScrollSpeed * seqTime, escapeExitX));

            if (escapeStep == EscapeRun && seqTime >= Mathf.Max(0f, escapeArriveAt - GameConfig.EscapeDoorOpenSec))
            {
                level.SetExitOpen(true);
                escapeStep = EscapeOpen;
                SetStage(StageOpen, EscapedName);
            }

            if (escapeStep == EscapeOpen && seqTime >= escapeArriveAt)
            {
                escapeStep = EscapeEnter;
                SetStage(StageEnter, EscapedName);
            }

            if (escapeStep == EscapeEnter && seqTime >= hideAt)
            {
                player.SetVisible(false);
                escapeStep = EscapeClose;
                SetStage(StageClose, EscapedName);
            }

            if (escapeStep == EscapeClose && seqTime >= closeAt)
            {
                level.SetExitOpen(false);
                menus.ShowBanner(EscapedBannerText);
                escapeStep = EscapeBanner;
                SetStage(StageBanner, EscapedName);
            }

            if (escapeStep == EscapeBanner && seqTime >= resultAt) EnterResult();
        }

        // ───────────── Result ─────────────

        ResultData BuildResult(bool escaped)
        {
            return new ResultData
            {
                escaped = escaped,
                songName = songName,
                difficulty = Map.difficulty,
                score = Judge.Score,
                perfect = Judge.PerfectCount,
                good = Judge.GoodCount,
                miss = Judge.MissCount,
                maxCombo = Judge.MaxCombo,
            };
        }

        /// <summary>연출이 끝났다: HUD·타이밍 바를 숨기고 마지막 장면 위에 결과 화면을 띄운다(Time.timeScale 1).</summary>
        void EnterResult()
        {
            stage = null;
            hud.SetVisible(false);
            beatBar.SetVisible(false);
            ChangeState(GameState.Result);
        }

        /// <summary>
        /// Result → Countdown(R): 같은 클립을 다시 쓴다(재분석 없음). CurrentDifficulty가 Map.difficulty와 다르면 저장해 둔 온셋·BPM으로 맵만 다시 만든다.
        /// 레벨·판정·추격자·HUD를 처음 상태로 돌리고 미리보기 없이 카운트다운부터 시작한다. Result가 아니면 무시.
        /// </summary>
        public void Retry()
        {
            if (State != GameState.Result || Map == null) return;

            if (Map.difficulty != CurrentDifficulty)
            {
                try
                {
                    BeatMap regenerated = BeatMapGenerator.Generate(output.onsets, output.bpm, Map.songLength, CurrentDifficulty);
                    output.map = regenerated;
                    Map = regenerated;
                }
                catch (AnalysisFailedException e)
                {
                    FailToSelect(e.Message);
                    return;
                }
            }

            PrepareRound();
            EnterCountdown();
        }

        // ───────────── 정리 ─────────────

        /// <summary>
        /// 레벨 오브젝트와 주인공·추격자·카메라 추적·HUD·타이밍 바·결과 화면·연출 상태를 정리한다. 여러 번 불러도 된다.
        /// </summary>
        void ClearLevel()
        {
            if (conductor != null) conductor.CancelFade();
            stage = null;
            hitStopLeft = 0f;
            caughtStep = CaughtSlow;
            escapeStep = EscapeRun;
            countdownN = 0;
            chaserArrived = false;
            goShown = false;
            Time.timeScale = 1f;

            if (cameraDirector != null) cameraDirector.Follow(null);
            if (player != null)
            {
                player.ResetPose();
                player.gameObject.SetActive(false);
            }
            if (chaser != null)
            {
                chaser.SetRunning(false);
                chaser.ResetChase();
                chaser.gameObject.SetActive(false);
            }
            if (level != null) level.Clear();
            if (hud != null)
            {
                hud.Clear();
                hud.SetVisible(false);
            }
            if (beatBar != null)
            {
                beatBar.Bind(null, null);
                beatBar.SetVisible(false);
            }
            if (result != null) result.Hide();
            Judge.Reset(null);
            nextAutoNote = 0;
            playerRunning = false;
        }

        void OnDestroy()
        {
            Time.timeScale = 1f; // 일시정지·연출 중 파괴돼도 다음 테스트·세션에 0이 새지 않게 한다
            CancelWork();
            // conductor·화면은 함께 파괴되므로 건드리지 않고 클립만 해제한다. 레벨은 만든 텍스처가 있어 비운다.
            if (level != null) level.Clear();
            if (currentClip != null)
            {
                Destroy(currentClip);
                currentClip = null;
            }
        }

        /// <summary>
        /// 진행 중인 로딩 코루틴을 멈추고(using 블록의 UnityWebRequest 해제) 로딩·분석 결과를 버린다.
        /// 분석 Task는 중단할 수 없어 끝까지 돌지만 결과는 아무도 읽지 않는다.
        /// </summary>
        void CancelWork()
        {
            loadToken++;
            StopLoad();

            Task<AnalysisOutput> abandoned = analysisTask;
            analysisTask = null;
            progress = null;
            if (abandoned != null)
            {
                // 버린 Task의 예외도 관측해 두어야 UnobservedTaskException이 남지 않는다.
                abandoned.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        void StopLoad()
        {
            var routine = loadRoutine;
            loadRoutine = null;
            if (routine == null) return;
            StopCoroutine(routine);
            (routine as IDisposable)?.Dispose();
        }

        void ReleaseClip()
        {
            ClearLevel();
            if (conductor != null)
            {
                conductor.Stop();
                conductor.Load(null);
            }
            if (currentClip != null)
            {
                Destroy(currentClip);
                currentClip = null;
            }
            output = null;
            Map = null;
        }

        bool IsStale(int token) => token != loadToken || State != GameState.Analyzing;

        void OnLoaded(int token, AudioClip clip)
        {
            if (IsStale(token))
            {
                if (clip != null) Destroy(clip);
                return;
            }

            currentClip = clip;
            conductor.Load(clip); // 재생은 분석이 끝난 뒤(Countdown 또는 디버그 Preview 진입 때) 시작한다
            BeginAnalysis();
        }

        void OnLoadFailed(int token, string error)
        {
            if (IsStale(token)) return;
            FailToSelect("파일을 읽을 수 없습니다: " + error);
        }

        /// <summary>메인 스레드에서 PCM을 읽고 워커 스레드 분석을 시작한다(5.1).</summary>
        void BeginAnalysis()
        {
            progress = new AnalysisProgress();
            try
            {
                float[] interleaved = AudioLoader.ReadInterleaved(currentClip);
                analysisTask = BeatMapGenerator.AnalyzeDetailedAsync(
                    interleaved, currentClip.channels, currentClip.frequency, currentClip.length, progress, CurrentDifficulty);
            }
            catch (Exception e)
            {
                Debug.LogWarning("분석을 시작할 수 없습니다: " + e);
                FailToSelect("분석 중 오류가 났습니다: " + e.Message);
                return;
            }
            Render();
        }

        void PollAnalysis()
        {
            Task<AnalysisOutput> task = analysisTask;
            if (!task.IsCompleted)
            {
                menus.ShowAnalyzing(progress.value, progress.stage);
                return;
            }

            analysisTask = null;
            progress = null;
            if (task.IsFaulted)
            {
                FailToSelect(DescribeAnalysisError(task.Exception));
            }
            else if (task.IsCanceled)
            {
                FailToSelect("분석 중 오류가 났습니다: 분석이 취소되었습니다");
            }
            else
            {
                output = task.Result;
                Map = output.map;
                if (DebugPreview)
                {
                    // 시계를 먼저 시작한다. ChangeState → Render가 곡 시간(conductor.SongTime)을 바로 읽는다.
                    previewIsDebug = true;
                    conductor.ScheduleStart(0); // startAt=debug: 곡을 처음부터 재생하며 분석 디버그 화면을 띄움
                    ChangeState(GameState.Preview);
                }
                else
                {
                    EnterPreview();
                }
            }
        }

        static string DescribeAnalysisError(AggregateException error)
        {
            foreach (Exception inner in error.Flatten().InnerExceptions)
                if (inner is AnalysisFailedException) return inner.Message;

            Exception first = error.GetBaseException();
            Debug.LogWarning("분석 중 오류: " + first);
            return "분석 중 오류가 났습니다: " + first.Message;
        }

        /// <summary>곡을 해제하고 오류 문구와 함께 파일 선택 화면으로 돌아간다.</summary>
        void FailToSelect(string message)
        {
            ReleaseClip();
            lastError = message;
            selectView = SelectView.Main;
            if (State != GameState.SelectFile) ChangeState(GameState.SelectFile);
            else Render();
        }

        // ───────────── 화면 ─────────────

        void Render()
        {
            if (menus == null) return;

            if (State != GameState.Preview || !previewIsDebug) debugView.Hide();
            if (State != GameState.Result && result != null) result.Hide();

            switch (State)
            {
                case GameState.Title:
                    menus.ShowTitle();
                    break;
                case GameState.SelectFile:
                    if (selectView == SelectView.Main) menus.ShowSelect(lastError);
                    else menus.ShowSongList(songsFolder, songs, songIndex, lastError);
                    break;
                case GameState.Analyzing:
                    if (analysisTask == null) menus.ShowLoading();
                    else menus.ShowAnalyzing(progress.value, progress.stage);
                    break;
                case GameState.Preview:
                    menus.HideAll();
                    if (previewIsDebug) debugView.Show(output, conductor);
                    else menus.ShowPreviewInfo(songName, Map.bpm);
                    break;
                case GameState.Paused:
                    menus.HideAll();
                    menus.ShowPause(true);
                    break;
                case GameState.Result:
                    menus.HideAll();
                    result.Show(LastResult);
                    break;
                default:
                    menus.HideAll();
                    break;
            }
        }
    }
}
