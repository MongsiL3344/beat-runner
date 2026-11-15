using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>Canvas 하위 Text/Image 생성 헬퍼. legacy UnityEngine.UI.Text + Galmuri11 동적 폰트.</summary>
    public static class UiFactory
    {
        static bool fontWarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            fontWarned = false;
        }

        /// <summary>
        /// 텍스트를 만든다. anchoredPos는 화면 중앙 기준이며 anchor가 가리키는 점(예: UpperCenter는 윗변 중앙)의 위치다.
        /// 폭은 기준 해상도 폭에서 좌우 여백을 뺀 값이고, 길면 줄바꿈한다.
        /// </summary>
        public static Text MakeText(Transform parent, string s, int size, TextAnchor anchor, Vector2 anchoredPos)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<Text>();
            text.font = LoadFont();
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.text = s ?? string.Empty;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            int lines = 1;
            if (!string.IsNullOrEmpty(s))
                foreach (char c in s) if (c == '\n') lines++;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = PivotFor(anchor);
            rt.sizeDelta = new Vector2(GameConfig.RefWidth - GameConfig.UiMargin * 2f, size * 1.5f * lines);
            rt.anchoredPosition = anchoredPos;
            return text;
        }

        public static Image MakeImage(Transform parent, Sprite sprite, Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject("Image", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return image;
        }

        static Vector2 PivotFor(TextAnchor a)
        {
            float x, y;
            switch (a)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: x = 0f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = 1f; break;
                default: x = 0.5f; break;
            }
            switch (a)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = 1f; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: y = 0f; break;
                default: y = 0.5f; break;
            }
            return new Vector2(x, y);
        }

        static Font LoadFont()
        {
            var font = Resources.Load<Font>(GameConfig.FontResourcePath);
            if (font != null) return font;

            if (!fontWarned)
            {
                fontWarned = true;
                Debug.LogWarning("UiFactory: 폰트 Resources/" + GameConfig.FontResourcePath + " 를 찾지 못해 기본 폰트를 씁니다(한글이 깨질 수 있음).");
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
