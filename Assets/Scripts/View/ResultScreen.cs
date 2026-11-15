using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UiText = UnityEngine.UI.Text; // 이 클래스의 string Text 속성과 이름이 겹쳐 별칭을 쓴다.

namespace NoiseRun
{
    /// <summary>
    /// 결과 화면(T8, 구현명세 8절). 마지막 장면 위에 반투명 검은 패널(ResultPanelAlpha)을 덮고
    /// 결과(탈출 성공/잡힘), 곡 이름, 난이도, 점수, Perfect/Good/Miss 수, 최대 콤보, 조작 안내를 보여 준다.
    /// 480×270 기준 픽셀, 화면 중앙이 0이고 위쪽이 +. 보일 때마다 Canvas의 맨 마지막 자식으로 올려 HUD 위에 그린다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        const string EscapedTitle = "탈출 성공!";
        const string CaughtTitle = "잡혔다!";
        const string DifficultyPrefix = "난이도: ";
        const string ScorePrefix = "점수: ";
        const string MaxComboPrefix = "최대 콤보: ";
        const string HintLine = "R: 다시 · N: 다른 곡 · ←/→: 난이도";
        const string Ellipsis = "..."; // Galmuri11에 "…" 글리프가 있는지 확인하지 않아 ASCII로 쓴다

        UiText titleText;
        UiText songText;
        UiText difficultyText;
        UiText scoreText;
        UiText judgeText;
        UiText comboText;
        UiText hintText;

        /// <summary>
        /// 표시 중인 모든 문구를 줄바꿈으로 이은 것(테스트용). 숨겨져 있으면 빈 문자열.
        /// 순서: 제목, 곡 이름, 난이도, 점수, Perfect/Good/Miss, 최대 콤보, 안내.
        /// </summary>
        public string Text
        {
            get
            {
                if (!Visible) return string.Empty;
                return string.Join("\n",
                    titleText.text, songText.text, difficultyText.text, scoreText.text, judgeText.text, comboText.text, hintText.text);
            }
        }

        /// <summary>화면이 보이는 중인지.</summary>
        public bool Visible => gameObject.activeSelf;

        /// <summary>Canvas 아래에 숨긴 상태로 만든다. Bootstrap이 호출한다.</summary>
        public static ResultScreen Create(Transform canvasRoot)
        {
            var go = new GameObject("ResultScreen", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var screen = go.AddComponent<ResultScreen>();
            screen.Build();
            go.SetActive(false);
            return screen;
        }

        void Build()
        {
            Image panel = UiFactory.MakeImage(transform, PixelArt.WhitePixel, Vector2.zero, Vector2.zero);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.color = new Color(0f, 0f, 0f, GameConfig.ResultPanelAlpha);

            titleText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeTitle, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultTitleY));
            songText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultSongY));
            songText.horizontalOverflow = HorizontalWrapMode.Overflow; // 곡 이름은 한 줄만. 길면 SetSongName이 줄이고 자른다
            difficultyText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultDifficultyY));
            scoreText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeHeader, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultScoreY));
            judgeText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultJudgeY));
            comboText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeBody, TextAnchor.MiddleCenter, new Vector2(0f, GameConfig.UiResultComboY));
            hintText = UiFactory.MakeText(transform, HintLine, GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        /// <summary>결과를 채우고 보인다. 다른 UI(HUD 등) 위에 그려지도록 Canvas의 맨 마지막 자식으로 올린다.</summary>
        public void Show(ResultData d)
        {
            titleText.text = d.escaped ? EscapedTitle : CaughtTitle;
            SetSongName(d.songName ?? string.Empty);
            SetDifficulty(d.difficulty);
            scoreText.text = ScorePrefix + d.score.ToString(CultureInfo.InvariantCulture);
            judgeText.text = string.Format(CultureInfo.InvariantCulture, "Perfect: {0}   Good: {1}   Miss: {2}", d.perfect, d.good, d.miss);
            comboText.text = MaxComboPrefix + d.maxCombo.ToString(CultureInfo.InvariantCulture);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 곡 이름을 한 줄로 쓴다. 줄바꿈되면 아래 난이도 줄과 겹치므로(사용자 스크린샷, 긴 mp3 파일 이름),
        /// 기준 폭(RefWidth − 좌우 UiMargin)에 안 들어가면 본문 크기로 줄이고, 그래도 넘치면 뒤를 잘라 "…"를 붙인다.
        /// </summary>
        void SetSongName(string name)
        {
            float maxWidth = GameConfig.RefWidth - GameConfig.UiMargin * 2f;
            songText.fontSize = GameConfig.TextSizeHeader;
            songText.text = name;
            if (songText.preferredWidth <= maxWidth) return;

            songText.fontSize = GameConfig.TextSizeBody;
            if (songText.preferredWidth <= maxWidth) return;

            int keep = name.Length;
            do
            {
                keep--;
                songText.text = name.Substring(0, keep) + Ellipsis;
            } while (keep > 1 && songText.preferredWidth > maxWidth);
        }

        /// <summary>난이도 줄만 다시 그린다(결과 화면에서 ←/→로 난이도를 바꿀 때).</summary>
        public void SetDifficulty(Difficulty d)
        {
            difficultyText.text = DifficultyPrefix + MenuScreens.DifficultyName(d);
        }

        /// <summary>숨긴다.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
