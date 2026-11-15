using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>
    /// 화면 아래 가운데 타이밍바(T6 사용자 결정: 명세 9.3 비트 눈금 대신).
    /// 바 가운데 = 아직 판정되지 않은 다음 장애물의 시각. 가운데부터 초록(Perfect 창), 노랑(Good 창), 회색(창 밖).
    /// 흰 선 두 개가 양 끝에서 가운데로 고정 속도(바 반폭 / TimingBarLeadSec)로 다가오고, 노트가 TimingBarLeadSec보다 멀면 양 끝에서 기다린다.
    /// 앞 장애물이 판정되면(통과든 Miss든) 다음 노트로 바꾸므로, 그 노트가 TimingBarLeadSec보다 가까우면 선이 중간 위치로 바로 옮겨 간다.
    /// 바 위 가운데 붐박스 아이콘은 정박마다 잠깐 커진다.
    /// 모든 길이는 480×270 기준 픽셀이고, 픽셀이 뭉개지지 않도록 영역 반폭과 선 위치는 정수 픽셀로 반올림한다.
    /// </summary>
    [DefaultExecutionOrder(GameConfig.ViewAfterGameplayExecutionOrder)] // GameFlow의 판정(Step) 뒤에 대상 노트를 고른다(리뷰 L2)
    public sealed class BeatBarUI : MonoBehaviour
    {
        /// <summary>바 반폭(px). 노트까지 TimingBarLeadSec 이상 남았을 때 선의 위치.</summary>
        public static float HalfWidthPx => GameConfig.TimingBarWidth / 2f;

        BeatMap map;
        Conductor conductor;
        /// <summary>
        /// 다음 노트를 찾기 시작하는 위치. 노트는 시간순이고 판정은 커서 위치나 그 뒤에서만 순서 없이 일어나므로
        /// 커서 앞 노트는 모두 판정된 상태다. 매 프레임 앞으로만 훑어서 전체를 다시 보지 않는다.
        /// </summary>
        int cursor;

        RectTransform leftLine;
        RectTransform rightLine;
        RectTransform icon;
        Sprite iconSprite;
        RectTransform goodZone;
        RectTransform perfectZone;

        /// <summary>아직 판정되지 않은 첫 노트의 map.notes 인덱스. 없거나 맵이 없으면 −1.</summary>
        public int TargetNoteIndex { get; private set; } = -1;

        /// <summary>마지막 Refresh의 선 위치(가운데부터 px, LineOffsetPx 값). 대상 노트가 없으면 0.</summary>
        public float CurrentLineOffsetPx { get; private set; }

        /// <summary>Canvas 아래에 숨긴 상태로 만든다. Bootstrap이 호출한다.</summary>
        public static BeatBarUI Create(Transform canvasRoot)
        {
            var go = new GameObject("BeatBarUI", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(GameConfig.TimingBarWidth, GameConfig.TimingBarHeight);
            rt.anchoredPosition = new Vector2(0f, GameConfig.TimingBarY);

            var bar = go.AddComponent<BeatBarUI>();
            bar.Build();
            go.SetActive(false);
            return bar;
        }

        /// <summary>
        /// 선이 가운데에서 떨어진 거리(px, 정수로 반올림한 값). timeToNote = 노트 시각 − 곡 시간.
        /// 0이면 0, TimingBarLeadSec이면 바 반폭, 더 멀면 반폭에서 멈춘다.
        /// 노트가 늦었는데 아직 판정 전이면 음수(선이 가운데를 지나침)이고, 노랑 영역 끝(−ZoneHalfWidthPx(GoodWindowSec))에서 멈춘다.
        /// </summary>
        public static float LineOffsetPx(double timeToNote)
        {
            float raw = Mathf.Round((float)(timeToNote / GameConfig.TimingBarLeadSec) * HalfWidthPx);
            return Mathf.Clamp(raw, -ZoneHalfWidthPx(GameConfig.GoodWindowSec), HalfWidthPx);
        }

        /// <summary>판정 창 ±windowSec이 바에서 차지하는 반폭(px, 정수로 반올림하고 최소 1px). 선 속도와 같은 비율이다.</summary>
        public static float ZoneHalfWidthPx(double windowSec)
        {
            return Mathf.Max(1f, Mathf.Round((float)(windowSec / GameConfig.TimingBarLeadSec) * HalfWidthPx));
        }

        /// <summary>
        /// 아이콘 배율. 정박(firstBeatTime + k × 2 × HalfBeatSec) 뒤 BeatIconPulseSec 동안 BeatIconPulseScale, 그 밖은 1.
        /// 곡 시작 전(songTime &lt; 0)과 맵이 없을 때는 1.
        /// </summary>
        public static float IconScale(BeatMap map, double songTime)
        {
            if (map == null || songTime < 0.0) return 1f;
            double beatSec = 2.0 * map.HalfBeatSec;
            double sinceFirst = songTime - map.firstBeatTime;
            if (sinceFirst < 0.0 || beatSec <= 0.0) return 1f;
            double sinceBeat = sinceFirst - Math.Floor(sinceFirst / beatSec) * beatSec;
            return sinceBeat < GameConfig.BeatIconPulseSec ? GameConfig.BeatIconPulseScale : 1f;
        }

        /// <summary>맵과 곡 시간 출처를 정한다. map이 null이면 해제하고 선을 숨긴다.</summary>
        public void Bind(BeatMap beatMap, Conductor songConductor)
        {
            map = beatMap;
            conductor = beatMap != null ? songConductor : null;
            cursor = 0;
            CurrentLineOffsetPx = 0f;
            Retarget();
            if (TargetNoteIndex < 0) SetLinesVisible(false);
            icon.localScale = Vector3.one;
            // 곡마다 판정 창이 다를 수 있다(빠른 곡은 Good 창이 반박자 비율로 줄어듦). 영역 폭을 그 곡의 창에 맞춘다.
            SetZoneWidth(goodZone, RhythmJudge.GoodWindowFor(beatMap));
            SetZoneWidth(perfectZone, RhythmJudge.PerfectWindowFor(beatMap));
        }

        static void SetZoneWidth(RectTransform zone, double windowSec)
        {
            zone.sizeDelta = new Vector2(2f * ZoneHalfWidthPx(windowSec), GameConfig.TimingBarHeight);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        void Update()
        {
            if (map != null && conductor != null) Refresh(conductor.SongTime);
        }

        /// <summary>songTime 기준으로 대상 노트를 다시 고르고 선 위치와 아이콘 배율을 맞춘다.</summary>
        public void Refresh(double songTime)
        {
            Retarget();
            if (TargetNoteIndex < 0)
            {
                CurrentLineOffsetPx = 0f;
                SetLinesVisible(false);
            }
            else
            {
                CurrentLineOffsetPx = LineOffsetPx(map.notes[TargetNoteIndex].time - songTime);
                leftLine.anchoredPosition = new Vector2(-CurrentLineOffsetPx, 0f);
                rightLine.anchoredPosition = new Vector2(CurrentLineOffsetPx, 0f);
                SetLinesVisible(true);
            }

            float scale = IconScale(map, songTime);
            icon.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>커서부터 앞으로 훑어 판정되지 않은 첫 노트를 TargetNoteIndex로 정한다.</summary>
        void Retarget()
        {
            List<Note> notes = map != null ? map.notes : null;
            if (notes == null)
            {
                cursor = 0;
                TargetNoteIndex = -1;
                return;
            }

            // RhythmJudge.Reset으로 판정이 모두 풀리면 커서 바로 앞 노트도 미판정이 된다 → 처음부터 다시 훑는다.
            if (cursor > notes.Count || (cursor > 0 && !notes[cursor - 1].judged)) cursor = 0;
            while (cursor < notes.Count && notes[cursor].judged) cursor++;
            TargetNoteIndex = cursor < notes.Count ? cursor : -1;
        }

        void SetLinesVisible(bool visible)
        {
            if (leftLine.gameObject.activeSelf != visible) leftLine.gameObject.SetActive(visible);
            if (rightLine.gameObject.activeSelf != visible) rightLine.gameObject.SetActive(visible);
        }

        // ───────────── 구성 ─────────────

        void Build()
        {
            Transform root = transform;
            float height = GameConfig.TimingBarHeight;

            // 뒤 → 앞: 회색 전체 / 노랑 Good 창 / 초록 Perfect 창 / 선 두 개 / 아이콘
            MakeZone(root, GameConfig.TimingBarWidth, height, GameConfig.TimingBarMissColor);
            goodZone = MakeZone(root, 2f * ZoneHalfWidthPx(GameConfig.GoodWindowSec), height, GameConfig.TimingBarGoodColor);
            perfectZone = MakeZone(root, 2f * ZoneHalfWidthPx(GameConfig.PerfectWindowSec), height, GameConfig.TimingBarPerfectColor);

            leftLine = MakeLine(root);
            rightLine = MakeLine(root);
            SetLinesVisible(false);

            iconSprite = PixelArt.Make(SpriteData.BeatIcon, new Vector2(0.5f, 0.5f));
            Image iconImage = UiFactory.MakeImage(root, iconSprite, iconSprite.rect.size,
                new Vector2(0f, GameConfig.BeatIconY - GameConfig.TimingBarY)); // 부모(바) 가운데 기준이므로 바 y를 뺀다
            iconImage.name = "BeatIcon";
            icon = (RectTransform)iconImage.transform;
        }

        static RectTransform MakeZone(Transform parent, float width, float height, Color color)
        {
            Image zone = UiFactory.MakeImage(parent, PixelArt.WhitePixel, new Vector2(width, height), Vector2.zero);
            zone.color = color;
            return (RectTransform)zone.transform;
        }

        static RectTransform MakeLine(Transform parent)
        {
            Image line = UiFactory.MakeImage(parent, PixelArt.WhitePixel,
                new Vector2(GameConfig.TimingBarLineWidth, GameConfig.TimingBarLineHeight), Vector2.zero);
            line.color = GameConfig.TimingBarLineColor;
            line.name = "TimingLine";
            return (RectTransform)line.transform;
        }

        void OnDestroy()
        {
            if (iconSprite != null)
            {
                Destroy(iconSprite.texture);
                Destroy(iconSprite);
                iconSprite = null;
            }
        }
    }
}
