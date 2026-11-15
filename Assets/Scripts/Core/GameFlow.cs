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
    /// T3 범위: Title, SelectFile(+Songs 목록), Analyzing(로딩 → 분석), Preview(임시: 분석 디버그 화면을 띄우고 곡을 처음부터 재생).
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

        Conductor conductor;
        MenuScreens menus;
        AnalysisDebugView debugView;

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

        internal void Init(Conductor conductorToUse, MenuScreens menuScreens, AnalysisDebugView debugViewToUse)
        {
            conductor = conductorToUse;
            menus = menuScreens;
            debugView = debugViewToUse;
            Render();
        }

        public void ChangeState(GameState next)
        {
            if (next == State) return;
            GameState prev = State;
            State = next;
            if (next == GameState.SelectFile) selectView = SelectView.Main;
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
            }
        }

        void UpdateSelectMain(Keyboard kb, bool enter)
        {
            if (enter) OpenFileDialog();
            else if (kb.fKey.wasPressedThisFrame) OpenSongList();
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

        void OnDestroy()
        {
            CancelWork();
            // conductor·화면은 함께 파괴되므로 건드리지 않고 클립만 해제한다.
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
            conductor.Load(clip); // 재생은 Preview 진입 뒤에 한다
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
                    interleaved, currentClip.channels, currentClip.frequency, currentClip.length, progress);
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
                // 시계를 먼저 시작한다. ChangeState → Render가 곡 시간(conductor.SongTime)을 바로 읽는다.
                conductor.ScheduleStart(0); // T3 임시: 곡을 처음부터 재생(Preview 연출은 T8)
                ChangeState(GameState.Preview);
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
