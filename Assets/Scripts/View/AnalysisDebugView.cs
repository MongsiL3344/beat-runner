using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>
    /// T3 분석 디버그 화면. 가로 타임라인이 곡 시간에 맞춰 흐르고 가운데 선이 현재 재생 위치다.
    /// 대역별 온셋 막대, 정박·반박 세로선, 노트, 정박 깜빡임 사각형, 상단 분석 요약을 그린다.
    /// 보이는 구간(GameConfig.DebugWindowSec)의 요소만 오브젝트 풀로 재배치하므로 매 프레임 생성·파괴가 없다
    /// (풀은 처음 필요한 만큼만 늘어나고 줄어들지 않는다).
    /// </summary>
    public sealed class AnalysisDebugView : MonoBehaviour
    {
        /// <summary>같은 레이어에 속한 Image 묶음. 매 프레임 Begin → Next 반복 → End로 쓴다.</summary>
        sealed class Pool
        {
            readonly RectTransform container;
            readonly List<Image> items = new List<Image>();
            int used;
            int prevUsed;

            public Pool(RectTransform parentContainer) { container = parentContainer; }

            public void Begin() { used = 0; }

            public Image Next()
            {
                if (used == items.Count)
                {
                    Image created = UiFactory.MakeImage(container, PixelArt.WhitePixel, Vector2.zero, Vector2.zero);
                    ((RectTransform)created.transform).pivot = new Vector2(0.5f, 0f); // 아래 가운데 기준
                    created.gameObject.SetActive(false);
                    items.Add(created);
                }
                Image img = items[used];
                if (used >= prevUsed) img.gameObject.SetActive(true);
                used++;
                return img;
            }

            public void End()
            {
                for (int i = used; i < prevUsed; i++) items[i].gameObject.SetActive(false);
                prevUsed = used;
            }

            /// <summary>쓰던 요소를 모두 숨긴다(다음 Begin 전에 상태를 맞춘다).</summary>
            public void Clear()
            {
                Begin();
                End();
            }
        }

        Conductor conductor;
        AnalysisOutput output;
        BeatMap map;
        List<Onset> onsets;

        Text infoText;
        Text timeText;
        Pool linePool;
        Pool onsetPool;
        Pool notePool;
        Image blink;
        long shownStep = long.MinValue;

        float pixelsPerSec;

        /// <summary>Canvas 아래에 숨긴 상태로 만든다. Bootstrap이 호출한다.</summary>
        public static AnalysisDebugView Create(Transform canvasRoot)
        {
            var go = new GameObject("AnalysisDebugView", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var view = go.AddComponent<AnalysisDebugView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        /// <summary>분석 결과를 보여 주기 시작한다. c.SongTime에 맞춰 스크롤한다.</summary>
        public void Show(AnalysisOutput result, Conductor c)
        {
            output = result;
            map = result.map;
            onsets = result.onsets.onsets;
            conductor = c;
            shownStep = long.MinValue;

            infoText.text = BuildInfo(result);
            gameObject.SetActive(true);
            Redraw();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            linePool.Clear();
            onsetPool.Clear();
            notePool.Clear();
            output = null;
            map = null;
            onsets = null;
            conductor = null;
        }

        void Update()
        {
            if (output == null || conductor == null) return;
            Redraw();
        }

        // ───────────── 구성 ─────────────

        void Build()
        {
            pixelsPerSec = GameConfig.RefWidth / GameConfig.DebugWindowSec;
            Transform root = transform;

            Image background = UiFactory.MakeImage(root, PixelArt.WhitePixel, GameConfig.RefResolution, Vector2.zero);
            background.color = GameConfig.DebugBackgroundColor;
            var backgroundRect = (RectTransform)background.transform; // 화면 비율이 달라도 전체를 덮는다
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            // 레이어 순서(뒤 → 앞): 기준선 / 세로선 / 온셋 막대 / 노트 / 재생 위치선 / 깜빡임 / 글자
            float regionHeight = GameConfig.DebugRegionTopY - GameConfig.DebugRegionBottomY;
            MakeBaseline(root, GameConfig.DebugLowBaseY);
            MakeBaseline(root, GameConfig.DebugMidBaseY);
            MakeBaseline(root, GameConfig.DebugHighBaseY);

            linePool = new Pool(MakeLayer(root, "Lines"));
            onsetPool = new Pool(MakeLayer(root, "Onsets"));
            notePool = new Pool(MakeLayer(root, "Notes"));

            Image playhead = UiFactory.MakeImage(root, PixelArt.WhitePixel,
                new Vector2(GameConfig.DebugPlayheadWidth, regionHeight),
                new Vector2(0f, (GameConfig.DebugRegionTopY + GameConfig.DebugRegionBottomY) * 0.5f));
            playhead.color = GameConfig.DebugPlayheadColor;

            blink = UiFactory.MakeImage(root, PixelArt.WhitePixel,
                new Vector2(GameConfig.DebugBlinkSize, GameConfig.DebugBlinkSize), new Vector2(0f, GameConfig.DebugBlinkY));
            blink.color = GameConfig.DebugBlinkOffColor;

            float leftX = -(GameConfig.RefWidth * 0.5f - GameConfig.UiMargin);
            float rightX = -leftX;
            infoText = UiFactory.MakeText(root, string.Empty, GameConfig.TextSizeBody, TextAnchor.UpperLeft, new Vector2(leftX, GameConfig.DebugInfoTopY));
            timeText = UiFactory.MakeText(root, string.Empty, GameConfig.TextSizeBody, TextAnchor.UpperRight, new Vector2(rightX, GameConfig.DebugInfoTopY));
            UiFactory.MakeText(root, "Esc: 돌아가기", GameConfig.TextSizeBody, TextAnchor.LowerCenter, new Vector2(0f, GameConfig.UiFooterY));
        }

        static RectTransform MakeLayer(Transform parent, string layerName)
        {
            var go = new GameObject(layerName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        static void MakeBaseline(Transform parent, float y)
        {
            Image line = UiFactory.MakeImage(parent, PixelArt.WhitePixel,
                new Vector2(GameConfig.RefWidth, GameConfig.DebugBaselineHeight), new Vector2(0f, y));
            line.color = GameConfig.DebugBaselineColor;
        }

        static string BuildInfo(AnalysisOutput result)
        {
            BeatMap m = result.map;
            return string.Format(CultureInfo.InvariantCulture,
                "BPM {0:F2}   첫 박 {1:F3}초   신뢰도 {2:F2}\n기본값 사용: {3}   온셋 {4}   노트 {5}   분석 {6:F2}초",
                m.bpm, m.firstBeatTime, result.bpm.confidence,
                m.bpmFallbackUsed ? "예" : "아니오",
                result.onsets.onsets.Count, m.notes.Count, result.elapsedSec);
        }

        // ───────────── 그리기 ─────────────

        void Redraw()
        {
            double t = conductor.SongTime;
            double half = GameConfig.DebugWindowSec * 0.5;
            double from = t - half;
            double to = t + half;

            DrawLines(t, from, to);
            DrawOnsets(t, from, to);
            DrawNotes(t, from, to);
            DrawBlink(t);
            DrawTime(t);
        }

        float XOf(double time, double now) => (float)((time - now) * pixelsPerSec);

        static void Place(Image img, float x, float bottomY, float width, float height, Color color)
        {
            var rt = (RectTransform)img.transform;
            rt.anchoredPosition = new Vector2(x, bottomY);
            rt.sizeDelta = new Vector2(width, height);
            img.color = color;
        }

        void DrawLines(double t, double from, double to)
        {
            double halfBeat = map.HalfBeatSec;
            float regionHeight = GameConfig.DebugRegionTopY - GameConfig.DebugRegionBottomY;

            linePool.Begin();
            int firstSlot = (int)System.Math.Ceiling((from - map.firstBeatTime) / halfBeat);
            int lastSlot = (int)System.Math.Floor((to - map.firstBeatTime) / halfBeat);
            for (int k = firstSlot; k <= lastSlot; k++)
            {
                double time = map.SlotTime(k);
                if (time < 0.0 || time > map.songLength) continue;

                bool beat = (k & 1) == 0; // 짝수 칸 = 정박
                Image line = linePool.Next();
                Place(line, XOf(time, t), GameConfig.DebugRegionBottomY,
                    beat ? GameConfig.DebugBeatLineWidth : GameConfig.DebugHalfBeatLineWidth, regionHeight,
                    beat ? GameConfig.DebugBeatLineColor : GameConfig.DebugHalfBeatLineColor);
            }
            linePool.End();
        }

        void DrawOnsets(double t, double from, double to)
        {
            onsetPool.Begin();
            for (int i = FirstOnsetAtOrAfter(from); i < onsets.Count; i++)
            {
                Onset o = onsets[i];
                if (o.time > to) break;

                float height = Mathf.Max(GameConfig.DebugOnsetMinHeight, Mathf.Clamp01(o.strength / GameConfig.DebugStrengthClamp) * GameConfig.DebugBandRowHeight);
                Image bar = onsetPool.Next();
                Place(bar, XOf(o.time, t), BandBaseY(o.band), GameConfig.DebugOnsetWidth, height, BandColor(o.band));
            }
            onsetPool.End();
        }

        void DrawNotes(double t, double from, double to)
        {
            List<Note> notes = map.notes;
            notePool.Begin();
            // 노트는 시간순이다. 화면 왼쪽 끝 이후 첫 노트를 이진 탐색으로 찾는다.
            int lo = 0, hi = notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (notes[mid].time < from) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < notes.Count; i++)
            {
                Note n = notes[i];
                if (n.time > to) break;

                Image box = notePool.Next();
                Place(box, XOf(n.time, t), GameConfig.DebugNoteRowY - GameConfig.DebugNoteSize * 0.5f,
                    GameConfig.DebugNoteSize, GameConfig.DebugNoteSize, NoteColor(n.kind));
            }
            notePool.End();
        }

        void DrawBlink(double t)
        {
            double beatSec = map.HalfBeatSec * 2.0;
            bool on = false;
            if (t >= map.firstBeatTime && t <= map.songLength)
            {
                double beatIndex = System.Math.Floor((t - map.firstBeatTime) / beatSec);
                double since = t - (map.firstBeatTime + beatIndex * beatSec);
                on = since < GameConfig.DebugBlinkSec;
            }
            blink.color = on ? GameConfig.DebugBlinkOnColor : GameConfig.DebugBlinkOffColor;
        }

        void DrawTime(double t)
        {
            // DebugTimeTextStepSec 단위가 바뀔 때만 글자를 다시 만든다(프레임마다 문자열 할당 방지).
            long step = (long)System.Math.Floor(t / GameConfig.DebugTimeTextStepSec);
            if (step == shownStep) return;
            shownStep = step;
            timeText.text = string.Format(CultureInfo.InvariantCulture, "곡 시간 {0:F1} / {1:F1}초", t, map.songLength);
        }

        int FirstOnsetAtOrAfter(double time)
        {
            int lo = 0, hi = onsets.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (onsets[mid].time < time) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        static float BandBaseY(Band band)
        {
            switch (band)
            {
                case Band.Low: return GameConfig.DebugLowBaseY;
                case Band.Mid: return GameConfig.DebugMidBaseY;
                default: return GameConfig.DebugHighBaseY;
            }
        }

        static Color BandColor(Band band)
        {
            switch (band)
            {
                case Band.Low: return GameConfig.DebugLowColor;
                case Band.Mid: return GameConfig.DebugMidColor;
                default: return GameConfig.DebugHighColor;
            }
        }

        /// <summary>Box=저음, Door=중음, Laundry=고음 색. 플래그가 둘 이상(E1)이면 조합 색.</summary>
        static Color NoteColor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Box: return GameConfig.DebugLowColor;
                case ObstacleKind.Door: return GameConfig.DebugMidColor;
                case ObstacleKind.Laundry: return GameConfig.DebugHighColor;
                default: return GameConfig.DebugComboNoteColor;
            }
        }
    }
}
