using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NoiseRun
{
    public enum GameState { Title, Calibrating, SelectFile, Analyzing, Preview, Countdown, Playing, Paused, Caught, Escaped, Result }

    /// <summary>
    /// 상태 머신과 입력 분배. 입력은 Update에서 Keyboard.current로만 읽고,
    /// 한 프레임의 입력은 그 프레임 시작 시점의 상태 하나에서만 처리한다. 화면 그리기는 MenuScreens·AnalysisDebugView가 맡는다.
    /// T5 범위: Title, SelectFile(+Songs 목록, ←/→ 난이도), Analyzing(로딩 → 분석), Playing(분석 직후 곧바로 진입하는 임시 흐름: 레벨·주인공 달리기·카메라 추적).
    /// startAt=debug이면 분석 뒤 Playing 대신 Preview(분석 디버그 화면을 띄우고 곡을 처음부터 재생)로 간다.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        enum SelectView { Main, SongList }

        public GameState State { get; private set; } = GameState.Title;
        public event Action<GameState, GameState> StateChanged;

        /// <summary>분석이 끝난 곡의 비트맵. 분석 성공 뒤(Preview)부터 곡을 해제할 때까지 값이 있다.</summary>
        public BeatMap Map { get; private set; }

        /// <summary>SelectFile 화면에 띄우는 직전 오류 문구. 없으면 null.</summary>
        public string LastError => lastError;

        /// <summary>
        /// 다음 분석에 쓸 난이도(5.7). 런타임에만 유지하고 저장하지 않는다. 설정하면 파일 선택·Songs 목록의 난이도 표시도 바뀐다.
        /// SelectFile에서 ←/→로 한 단계씩 바뀐다.
        /// </summary>
        public Difficulty CurrentDifficulty
        {
            get => currentDifficulty;
            set
            {
                currentDifficulty = value;
                if (menus != null) menus.Difficulty = value;
            }
        }

        /// <summary>true면 Playing에서 노트 시각마다 정답 동작을 자동 실행한다. Bootstrap이 DevOptions.AutoPlay로 정하고 테스트가 직접 바꾼다.</summary>
        public bool AutoPlay { get; set; }

        /// <summary>true면 분석 성공 뒤 Playing 대신 T3 분석 디버그 화면(Preview)으로 간다(startAt=debug). Bootstrap이 정한다.</summary>
        public bool DebugPreview { get; set; }

        /// <summary>현재 Playing에 들어온 뒤 autoPlay가 실행한 동작 수. EnterPlaying에서 0으로 돌린다.</summary>
        public int AutoActionsFired { get; private set; }

        Conductor conductor;
        MenuScreens menus;
        AnalysisDebugView debugView;
        LevelBuilder level;
        PlayerController player;
        CameraDirector cameraDirector;

        Difficulty currentDifficulty = GameConfig.DefaultDifficulty;
        int nextAutoNote;      // 다음에 지나갈 노트(Map.notes 인덱스)
        bool playerRunning;

        SelectView selectView = SelectView.Main;
        string songsFolder = string.Empty;
        string[] songs = Array.Empty<string>();
        int songIndex;
        string lastError;

        int loadToken;
        AudioClip currentClip;
        System.Collections.IEnumerator loadRoutine;

        AnalysisProgress progress;
        Task<AnalysisOutput> analysisTask;
        AnalysisOutput output;

        internal void Init(Conductor conductorToUse, MenuScreens menuScreens, AnalysisDebugView debugViewToUse,
            LevelBuilder levelToUse, PlayerController playerToUse, CameraDirector cameraDirectorToUse)
        {
            conductor = conductorToUse;
            menus = menuScreens;
            debugView = debugViewToUse;
            level = levelToUse;
            player = playerToUse;
            cameraDirector = cameraDirectorToUse;
            menus.Difficulty = currentDifficulty;
            Render();
        }

        public void ChangeState(GameState next)
        {
            if (next == State) return;
            GameState prev = State;
            State = next;
            if (next == GameState.SelectFile)
            {
                selectView = SelectView.Main;
                ClearLevel();
            }
            Render();
            StateChanged?.Invoke(prev, next);
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
        public void StartFromClip(AudioClip clip, string songName)
        {
            BeginSession(songName ?? string.Empty);

            currentClip = clip;
            conductor.Load(clip);
            BeginAnalysis();
        }

        /// <summary>이전 로딩·분석·클립을 정리하고 Analyzing 화면으로 들어간다.</summary>
        void BeginSession(string songName)
        {
            CancelWork();
            ReleaseClip();
            lastError = null;
            menus.SongName = songName;

            if (State != GameState.Analyzing) ChangeState(GameState.Analyzing);
            else Render();
        }

        // ───────────── 입력 ─────────────

        void Update()
        {
            if (Keyboard.current != null) HandleInput(Keyboard.current);

            // 분석 결과는 입력과 상관없이 확인한다. 입력이 상태를 바꾼 프레임이면 건너뛴다.
            if (State == GameState.Analyzing && analysisTask != null) PollAnalysis();

            // 입력이 Playing을 벗어나게 한 프레임에는 건너뛴다.
            if (State == GameState.Playing) Step(conductor.SongTime, Time.deltaTime);
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
                    if (kb.escapeKey.wasPressedThisFrame) ExitPreview();
                    break;
                case GameState.Playing:
                    if (kb.escapeKey.wasPressedThisFrame) ExitPlaying();
                    break;
            }
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

        /// <summary>Preview 종료(임시, T8에서 Countdown 흐름으로 바뀐다): 음악 정지, 클립 해제, 파일 선택으로.</summary>
        void ExitPreview()
        {
            ReleaseClip();
            lastError = null;
            ChangeState(GameState.SelectFile);
        }

        /// <summary>
        /// Playing 종료(T5 임시: 일시정지 없이 Esc로 바로 나간다. T8에서 Paused가 생긴다).
        /// 음악 정지, 레벨 정리, 클립 해제, 파일 선택으로.
        /// </summary>
        void ExitPlaying()
        {
            ReleaseClip(); // conductor.Stop + ClearLevel 포함
            lastError = null;
            ChangeState(GameState.SelectFile);
        }

        /// <summary>
        /// 분석 성공 뒤 곧바로 Playing(T5 임시. T8에서 Preview·Countdown이 끼어든다).
        /// 곡 시작 전 CountdownStepSec × CountdownSteps 동안은 곡 시간이 음수이고, 그동안 주인공은 x &lt; 0에서 달린다. 카운트다운 문구는 없다.
        /// </summary>
        void EnterPlaying()
        {
            level.Build(Map);
            nextAutoNote = 0;
            AutoActionsFired = 0;

            conductor.ScheduleStart(GameConfig.CountdownStepSec * GameConfig.CountdownSteps);

            player.gameObject.SetActive(true);
            player.Face(true);
            player.SetX(Mathf.Min(level.TimeToX(conductor.SongTime), level.ExitX));
            player.SetRunning(true);
            playerRunning = true;
            cameraDirector.Follow(player.transform);

            ChangeState(GameState.Playing);
        }

        /// <summary>
        /// Playing 한 프레임: 주인공 x를 곡 시간에 맞추고(비상구에서 멈춤), autoPlay면 지나간 노트마다 정답 동작을 한 번씩 실행한다.
        /// Update가 Conductor.SongTime과 Time.deltaTime으로 부르며, 테스트는 시계를 올리고 직접 부른다.
        /// 판정·추격자·종료 조건은 T6~T8에서 여기에 붙는다.
        /// </summary>
        public void Step(double songTime, float dt)
        {
            if (State != GameState.Playing || Map == null) return;

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
            var notes = Map.notes;
            while (nextAutoNote < notes.Count && notes[nextAutoNote].time <= songTime)
            {
                if (AutoPlay)
                {
                    player.DoAction(CorrectAction(notes[nextAutoNote].kind), PlayerController.ActionDuration(Map.HalfBeatSec));
                    AutoActionsFired++;
                }
                nextAutoNote++;
            }
        }

        /// <summary>장애물 종류별 정답 동작(Box→Jump, Laundry→Slide, Door→Wave). 조합(E1)은 아직 없어 첫 플래그만 본다.</summary>
        static ActionType CorrectAction(ObstacleKind kind)
        {
            if ((kind & ObstacleKind.Box) != 0) return ActionType.Jump;
            if ((kind & ObstacleKind.Laundry) != 0) return ActionType.Slide;
            return ActionType.Wave;
        }

        /// <summary>레벨 오브젝트와 주인공·카메라 추적을 정리한다. 여러 번 불러도 된다.</summary>
        void ClearLevel()
        {
            if (cameraDirector != null) cameraDirector.Follow(null);
            if (player != null)
            {
                player.ResetPose();
                player.gameObject.SetActive(false);
            }
            if (level != null) level.Clear();
            nextAutoNote = 0;
            playerRunning = false;
        }

        void OnDestroy()
        {
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
            conductor.Load(clip); // 재생은 분석이 끝난 뒤(Playing 또는 Preview 진입 때) 시작한다
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
                    conductor.ScheduleStart(0); // startAt=debug: 곡을 처음부터 재생하며 분석 디버그 화면을 띄움
                    ChangeState(GameState.Preview);
                }
                else
                {
                    EnterPlaying();
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

        void FailToSelect(string message)
        {
            ReleaseClip();
            lastError = message;
            selectView = SelectView.Main;
            if (State == GameState.Analyzing) ChangeState(GameState.SelectFile);
            else Render();
        }

        // ───────────── 화면 ─────────────

        void Render()
        {
            if (menus == null) return;

            if (State != GameState.Preview) debugView.Hide();

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
                    debugView.Show(output, conductor);
                    break;
                default:
                    menus.HideAll();
                    break;
            }
        }
    }
}
