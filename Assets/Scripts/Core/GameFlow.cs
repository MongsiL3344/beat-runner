using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NoiseRun
{
    public enum GameState { Title, Calibrating, SelectFile, Analyzing, Preview, Countdown, Playing, Paused, Caught, Escaped, Result }

    /// <summary>
    /// 상태 머신과 화면 전환, 입력 분배. 입력은 Update에서 Keyboard.current로만 읽고,
    /// 한 프레임의 입력은 그 프레임 시작 시점의 상태 하나에서만 처리한다.
    /// T1 범위: Title, SelectFile(+Songs 목록), Analyzing(임시: 파일 정보 표시·재생).
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        enum SelectView { Main, SongList }

        public GameState State { get; private set; } = GameState.Title;
        public event Action<GameState, GameState> StateChanged;

        Conductor conductor;
        Transform canvasRoot;
        GameObject screenRoot;

        SelectView selectView = SelectView.Main;
        string songsFolder = string.Empty;
        string[] songs = Array.Empty<string>();
        int songIndex;
        string lastError;

        int loadToken;
        string loadingName = string.Empty;
        string clipInfo;
        AudioClip currentClip;
        System.Collections.IEnumerator loadRoutine;

        internal void Init(Conductor conductorToUse, Transform canvas)
        {
            conductor = conductorToUse;
            canvasRoot = canvas;
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
            loadToken++;
            int token = loadToken;
            StopLoad();
            ReleaseClip();
            lastError = null;
            loadingName = string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);

            if (State != GameState.Analyzing) ChangeState(GameState.Analyzing);
            else Render();

            loadRoutine = AudioLoader.Load(path, clip => OnLoaded(token, clip), error => OnLoadFailed(token, error));
            StartCoroutine(loadRoutine);
        }

        // ───────────── 입력 ─────────────

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

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
            loadToken++; // 진행 중인 로딩 결과는 버린다
            StopLoad();
            ReleaseClip();
            lastError = null;
            ChangeState(GameState.SelectFile);
        }

        void OnDestroy()
        {
            loadToken++;
            StopLoad();
            // conductor는 함께 파괴되므로 건드리지 않고 클립만 해제한다.
            if (currentClip != null)
            {
                Destroy(currentClip);
                currentClip = null;
            }
        }

        /// <summary>진행 중인 로딩 코루틴을 멈추고 using 블록의 UnityWebRequest를 해제한다.</summary>
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
            clipInfo = null;
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
            conductor.Load(clip);
            conductor.ScheduleStart(0);

            clipInfo = string.Format(CultureInfo.InvariantCulture,
                "파일: {0}\n길이: {1:F2}초\n채널: {2}\n샘플레이트: {3}Hz",
                loadingName, clip.length, clip.channels, clip.frequency);
            Render();
        }

        void OnLoadFailed(int token, string error)
        {
            if (IsStale(token)) return;

            lastError = "파일을 읽을 수 없습니다: " + error;
            selectView = SelectView.Main;
            if (State == GameState.Analyzing) ChangeState(GameState.SelectFile);
            else Render();
        }

        // ───────────── 화면 ─────────────

        void Render()
        {
            if (canvasRoot == null) return;

            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
                Destroy(screenRoot);
            }
            screenRoot = new GameObject("Screen", typeof(RectTransform));
            var rt = (RectTransform)screenRoot.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            switch (State)
            {
                case GameState.Title: RenderTitle(); break;
                case GameState.SelectFile:
                    if (selectView == SelectView.Main) RenderSelectMain();
                    else RenderSongList();
                    break;
                case GameState.Analyzing: RenderAnalyzing(); break;
            }
        }

        Transform Root => screenRoot.transform;

        void RenderTitle()
        {
            UiFactory.MakeText(Root, "층간소음 런", GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiTitleY));
            UiFactory.MakeText(Root, "Enter: 시작", GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiTitlePromptY));
        }

        void RenderSelectMain()
        {
            UiFactory.MakeText(Root, "음악 파일 선택", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            UiFactory.MakeText(Root, "Enter: 파일 선택창 열기\nF: Songs 폴더 목록", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiBodyY));
            RenderError();
        }

        void RenderSongList()
        {
            UiFactory.MakeText(Root, "Songs 폴더", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            UiFactory.MakeText(Root, songsFolder, GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiListPathY));

            if (songs.Length == 0)
            {
                UiFactory.MakeText(Root, "폴더에 mp3/wav/ogg 파일을 넣으세요", GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiListTopY));
            }
            else
            {
                int rows = GameConfig.SongListVisibleRows;
                int first = Mathf.Clamp(songIndex - rows / 2, 0, Mathf.Max(0, songs.Length - rows));
                int last = Mathf.Min(songs.Length, first + rows);
                for (int i = first; i < last; i++)
                {
                    bool selected = i == songIndex;
                    string label = (selected ? "> " : "  ") + Path.GetFileName(songs[i]);
                    Text t = UiFactory.MakeText(Root, label, GameConfig.TextSizeBody, TextAnchor.UpperCenter,
                        new Vector2(0f, GameConfig.UiListTopY - (i - first) * GameConfig.UiListRowHeight));
                    if (selected) t.color = Color.yellow;
                }
            }

            RenderError();
            string footer = songs.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, "↑/↓: 선택 ({0}/{1})   Enter: 재생   Esc: 뒤로", songIndex + 1, songs.Length)
                : "Esc: 뒤로";
            UiFactory.MakeText(Root, footer, GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        void RenderAnalyzing()
        {
            if (clipInfo == null)
            {
                UiFactory.MakeText(Root, "불러오는 중: " + loadingName, GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, Vector2.zero);
            }
            else
            {
                UiFactory.MakeText(Root, clipInfo, GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            }
            UiFactory.MakeText(Root, "Esc: 정지하고 돌아가기", GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        void RenderError()
        {
            if (string.IsNullOrEmpty(lastError)) return;
            Text t = UiFactory.MakeText(Root, lastError, GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiMessageY));
            t.color = new Color(1f, 0.55f, 0.55f);
        }
    }
}
