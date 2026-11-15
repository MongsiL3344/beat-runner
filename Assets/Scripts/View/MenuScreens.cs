using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>
    /// 메뉴 화면 그리기(타이틀, 파일 선택, Songs 목록, 불러오기·분석 진행률). 상태·입력은 GameFlow가 다루고
    /// 여기는 "무엇을 보여 줄지"만 받는다. Show* 한 번이 화면 하나이고, 다른 Show*나 HideAll을 부르면 이전 화면은 지워진다.
    /// ShowAnalyzing만 매 프레임 불려도 되도록 기존 텍스트·막대를 갱신한다(오브젝트를 새로 만들지 않음).
    /// </summary>
    public sealed class MenuScreens
    {
        enum Mode { None, Title, Select, SongList, Loading, Analyzing }

        readonly Transform canvasRoot;
        GameObject screenRoot;
        Mode mode = Mode.None;

        // 분석 화면에서 갱신하는 요소
        Text stageText;
        Text percentText;
        RectTransform barFill;
        string shownStage;
        int shownPercent = -1;

        // 파일 선택·Songs 목록 화면의 난이도 줄(Difficulty 설정 때 다시 그린다)
        Text difficultyText;
        Difficulty difficulty = GameConfig.DefaultDifficulty;

        /// <summary>파일 선택·Songs 목록 화면에 띄우는 현재 난이도. 설정하면 지금 화면의 난이도 줄만 다시 그린다.</summary>
        public Difficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                if (difficultyText != null) difficultyText.text = DifficultyLine(difficulty);
            }
        }

        /// <summary>난이도 한글 이름("쉬움"/"보통"/"어려움"). 결과 화면도 같이 쓴다.</summary>
        public static string DifficultyName(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return "쉬움";
                case Difficulty.Hard: return "어려움";
                default: return "보통";
            }
        }

        static string DifficultyLine(Difficulty d)
        {
            return "난이도: " + DifficultyName(d) + "   ←/→: 난이도";
        }

        /// <summary>불러오기·분석 화면에 띄우는 곡 이름. ShowLoading/ShowAnalyzing 전에 지정한다.</summary>
        public string SongName { get; set; } = string.Empty;

        public MenuScreens(Transform canvas)
        {
            canvasRoot = canvas;
        }

        /// <summary>모든 화면과 오버레이(카운트다운·일시정지·배너·미리보기 정보)를 숨긴다.</summary>
        public void HideAll()
        {
            DestroyRoot();
            mode = Mode.None;
            ShowCountdown(null);
            ShowPause(false);
            ShowBanner(null);
            ShowPreviewInfo(null, 0f);
        }

        // ───────────── 오버레이(T8) ─────────────
        // 화면(screenRoot)과 따로 있어서 Show*가 지우지 않고, 불투명 배경도 없다(게임 장면을 가리지 않음). 켤 때 Canvas 맨 위로 올린다.

        Text countdownLabel;
        Text bannerLabel;
        Text previewLabel;
        GameObject pauseRoot;
        string countdownText;
        string bannerText;

        /// <summary>지금 띄운 카운트다운 문구("3"·"2"·"1"·"GO"). 숨겼으면 마지막으로 넘긴 null/빈 문자열(테스트용).</summary>
        public string CountdownText => countdownText;

        /// <summary>지금 띄운 배너 문구(예: "탈출 성공!", "GAME OVER"). 숨겼으면 null(테스트용).</summary>
        public string BannerText => bannerText;

        /// <summary>일시정지 오버레이가 떠 있는지(테스트용).</summary>
        public bool PauseVisible => pauseRoot != null && pauseRoot.activeSelf;

        /// <summary>화면 가운데에 큰 글자로 카운트다운 문구를 띄운다. null/빈 문자열이면 숨긴다.</summary>
        public void ShowCountdown(string text)
        {
            countdownText = text;
            countdownLabel = SetOverlayLabel(countdownLabel, text, GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, Vector2.zero);
        }

        /// <summary>화면 가운데에 큰 글자로 배너 문구를 띄운다. null이면 숨긴다.</summary>
        public void ShowBanner(string text)
        {
            bannerText = text;
            bannerLabel = SetOverlayLabel(bannerLabel, text, GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, Vector2.zero);
        }

        /// <summary>미리보기 화면 위쪽에 곡 이름과 BPM을 띄운다. songName이 null이면 숨긴다.</summary>
        public void ShowPreviewInfo(string songName, float bpm)
        {
            string text = songName == null
                ? null
                : songName + "   BPM " + bpm.ToString("0.#", CultureInfo.InvariantCulture);
            previewLabel = SetOverlayLabel(previewLabel, text, GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
        }

        /// <summary>
        /// 일시정지 오버레이를 켜거나 끈다: 반투명 어두운 패널 + "일시정지" + "Esc: 계속 · Q: 그만두기".
        /// 재개 카운트다운 숫자는 ShowCountdown으로 따로 띄운다.
        /// </summary>
        public void ShowPause(bool on)
        {
            if (!on)
            {
                if (pauseRoot != null) pauseRoot.SetActive(false);
                return;
            }
            if (pauseRoot == null) BuildPause();
            pauseRoot.SetActive(true);
            pauseRoot.transform.SetAsLastSibling();
        }

        Text SetOverlayLabel(Text label, string text, int size, TextAnchor anchor, Vector2 pos)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return label;
            }
            if (label == null) label = UiFactory.MakeText(canvasRoot, text, size, anchor, pos);
            label.text = text;
            label.gameObject.SetActive(true);
            label.transform.SetAsLastSibling();
            return label;
        }

        void BuildPause()
        {
            pauseRoot = new GameObject("Pause", typeof(RectTransform));
            var rt = (RectTransform)pauseRoot.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image dim = UiFactory.MakeImage(rt, PixelArt.WhitePixel, Vector2.zero, Vector2.zero);
            var dimRect = (RectTransform)dim.transform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            dim.color = new Color(0f, 0f, 0f, GameConfig.PausePanelAlpha);

            UiFactory.MakeText(rt, "일시정지", GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiPauseTitleY));
            UiFactory.MakeText(rt, "Esc: 계속 · Q: 그만두기", GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiPauseHintY));
        }

        public void ShowTitle()
        {
            NewRoot(Mode.Title);
            Transform root = screenRoot.transform;
            UiFactory.MakeText(root, "층간소음 런", GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiTitleY));
            UiFactory.MakeText(root, "Enter: 시작", GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiTitlePromptY));
        }

        /// <summary>파일 선택 첫 화면. message는 직전 오류 문구(없으면 null).</summary>
        public void ShowSelect(string message)
        {
            NewRoot(Mode.Select);
            Transform root = screenRoot.transform;
            UiFactory.MakeText(root, "음악 파일 선택", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            UiFactory.MakeText(root, "Enter: 파일 선택창 열기\nF: Songs 폴더 목록", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiBodyY));
            difficultyText = UiFactory.MakeText(root, DifficultyLine(difficulty), GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiSelectDifficultyY));
            MakeError(root, message);
        }

        /// <summary>Songs 폴더 목록. message는 목록 화면에도 띄우는 오류 문구(없으면 null).</summary>
        public void ShowSongList(string folder, string[] files, int selected, string message = null)
        {
            NewRoot(Mode.SongList);
            Transform root = screenRoot.transform;
            UiFactory.MakeText(root, "Songs 폴더", GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            UiFactory.MakeText(root, folder, GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiListPathY));

            if (files.Length == 0)
            {
                UiFactory.MakeText(root, "폴더에 mp3/wav/ogg 파일을 넣으세요", GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiListTopY));
            }
            else
            {
                int rows = GameConfig.SongListVisibleRows;
                int first = Mathf.Clamp(selected - rows / 2, 0, Mathf.Max(0, files.Length - rows));
                int last = Mathf.Min(files.Length, first + rows);
                for (int i = first; i < last; i++)
                {
                    bool isSelected = i == selected;
                    string label = (isSelected ? "> " : "  ") + Path.GetFileName(files[i]);
                    Text t = UiFactory.MakeText(root, label, GameConfig.TextSizeBody, TextAnchor.UpperCenter,
                        new Vector2(0f, GameConfig.UiListTopY - (i - first) * GameConfig.UiListRowHeight));
                    if (isSelected) t.color = Color.yellow;
                }
            }

            difficultyText = UiFactory.MakeText(root, DifficultyLine(difficulty), GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiListDifficultyY));
            MakeError(root, message);
            string footer = files.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, "↑/↓: 선택 ({0}/{1})   Enter: 재생   Esc: 뒤로", selected + 1, files.Length)
                : "Esc: 뒤로";
            UiFactory.MakeText(root, footer, GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        /// <summary>파일을 읽는 동안의 화면("불러오는 중: 이름").</summary>
        public void ShowLoading()
        {
            NewRoot(Mode.Loading);
            Transform root = screenRoot.transform;
            UiFactory.MakeText(root, "불러오는 중: " + SongName, GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, Vector2.zero);
            UiFactory.MakeText(root, "Esc: 정지하고 돌아가기", GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        /// <summary>분석 진행 화면. 매 프레임 불러도 된다. progress는 0~1.</summary>
        public void ShowAnalyzing(float progress, string stage)
        {
            if (mode != Mode.Analyzing) BuildAnalyzing();

            string stageLabel = stage ?? string.Empty;
            if (stageLabel != shownStage)
            {
                shownStage = stageLabel;
                stageText.text = stageLabel;
            }

            float clamped = Mathf.Clamp01(progress);
            barFill.sizeDelta = new Vector2(GameConfig.UiAnalyzeBarWidth * clamped, GameConfig.UiAnalyzeBarHeight);

            int percent = Mathf.RoundToInt(clamped * 100f);
            if (percent != shownPercent)
            {
                shownPercent = percent;
                percentText.text = percent.ToString(CultureInfo.InvariantCulture) + "%";
            }
        }

        // ───────────── 내부 ─────────────

        void BuildAnalyzing()
        {
            NewRoot(Mode.Analyzing);
            Transform root = screenRoot.transform;
            UiFactory.MakeText(root, "분석 중: " + SongName, GameConfig.TextSizeHeader, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiHeaderY));
            stageText = UiFactory.MakeText(root, string.Empty, GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiAnalyzeStageY));

            Image back = UiFactory.MakeImage(root, PixelArt.WhitePixel,
                new Vector2(GameConfig.UiAnalyzeBarWidth, GameConfig.UiAnalyzeBarHeight), new Vector2(0f, GameConfig.UiAnalyzeBarY));
            back.color = GameConfig.UiAnalyzeBarBackColor;

            // 채움 막대는 왼쪽 끝을 기준으로 폭만 늘린다.
            Image fill = UiFactory.MakeImage(root, PixelArt.WhitePixel,
                new Vector2(0f, GameConfig.UiAnalyzeBarHeight), new Vector2(-GameConfig.UiAnalyzeBarWidth * 0.5f, GameConfig.UiAnalyzeBarY));
            fill.color = GameConfig.UiAnalyzeBarFillColor;
            barFill = (RectTransform)fill.transform;
            barFill.pivot = new Vector2(0f, 0.5f);

            percentText = UiFactory.MakeText(root, string.Empty, GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiAnalyzePercentY));
            UiFactory.MakeText(root, "Esc: 취소", GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));

            shownStage = null;
            shownPercent = -1;
        }

        /// <summary>메뉴 화면(타이틀·파일 선택·불러오기·분석)을 새로 만든다. 게임 장면용 오버레이(일시정지 등)는 메뉴 화면과 함께 뜨지 않으므로 숨긴다.</summary>
        void NewRoot(Mode next)
        {
            DestroyRoot();
            ShowCountdown(null);
            ShowPause(false);
            ShowBanner(null);
            ShowPreviewInfo(null, 0f);
            mode = next;
            screenRoot = new GameObject("Screen", typeof(RectTransform));
            var rt = (RectTransform)screenRoot.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void DestroyRoot()
        {
            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
                Object.Destroy(screenRoot);
            }
            screenRoot = null;
            stageText = null;
            percentText = null;
            barFill = null;
            difficultyText = null;
        }

        static void MakeError(Transform root, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Text t = UiFactory.MakeText(root, message, GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiMessageY));
            t.color = GameConfig.UiErrorColor;
        }
    }
}
