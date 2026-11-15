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

        /// <summary>불러오기·분석 화면에 띄우는 곡 이름. ShowLoading/ShowAnalyzing 전에 지정한다.</summary>
        public string SongName { get; set; } = string.Empty;

        public MenuScreens(Transform canvas)
        {
            canvasRoot = canvas;
        }

        public void HideAll()
        {
            DestroyRoot();
            mode = Mode.None;
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

        void NewRoot(Mode next)
        {
            DestroyRoot();
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
        }

        static void MakeError(Transform root, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Text t = UiFactory.MakeText(root, message, GameConfig.TextSizeBody, TextAnchor.UpperCenter, new Vector2(0f, GameConfig.UiMessageY));
            t.color = GameConfig.UiErrorColor;
        }
    }
}
