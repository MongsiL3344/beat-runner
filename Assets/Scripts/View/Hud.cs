using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>
    /// 플레이 중 HUD(T6). 오른쪽 위에 점수와 콤보, 판정 문구(PERFECT / GOOD / MISS / 헛박자)는 주인공 머리 위에 떠서
    /// GameConfig.JudgeTextSec 동안 보인다. 판정 문구는 월드 좌표(주인공 x, GameConfig.JudgeTextWorldY)를 화면으로 옮겨
    /// Canvas(ScreenSpaceOverlay) 위치를 잡는다. 480×270 기준 픽셀, 화면 중앙이 0이고 위쪽이 +.
    /// </summary>
    [DefaultExecutionOrder(GameConfig.ViewAfterGameplayExecutionOrder)] // 카메라 LateUpdate 뒤에 문구 위치를 잡는다(리뷰 L1)
    public sealed class Hud : MonoBehaviour
    {
        const string ScorePrefix = "SCORE ";
        const string ComboSuffix = " COMBO";
        const string PerfectLabel = "PERFECT";
        const string GoodLabel = "GOOD";
        const string MissLabel = "MISS";
        const string OffBeatLabel = "헛박자";

        Text scoreText;
        Text comboText;
        Text judgementText;
        RectTransform canvasRect;
        Transform followTarget;
        Camera followCamera;
        float judgementTimer;
        string judgementLabel;

        /// <summary>점수 문구(예: "SCORE 1200").</summary>
        public string ScoreLabel => scoreText.text;

        /// <summary>콤보 문구(예: "12 COMBO"). 콤보 0이면 숨겨져 빈 문자열.</summary>
        public string ComboLabel => comboText.text;

        /// <summary>떠 있는 판정 문구. 보이는 문구가 없으면 null.</summary>
        public string JudgementLabel => judgementLabel;

        /// <summary>Canvas 아래에 숨긴 상태로 만든다. Bootstrap이 호출한다.</summary>
        public static Hud Create(Transform canvasRoot)
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvasRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var hud = go.AddComponent<Hud>();
            hud.canvasRect = canvasRoot as RectTransform;
            hud.Build();
            go.SetActive(false);
            return hud;
        }

        void Build()
        {
            float rightX = GameConfig.RefWidth / 2f - GameConfig.UiMargin;
            scoreText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeBody, TextAnchor.UpperRight,
                new Vector2(rightX, GameConfig.UiScoreY));
            comboText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeBody, TextAnchor.UpperRight,
                new Vector2(rightX, GameConfig.UiComboY));
            judgementText = UiFactory.MakeText(transform, string.Empty, GameConfig.TextSizeHeader, TextAnchor.MiddleCenter,
                Vector2.zero);
            judgementText.gameObject.SetActive(false);
            Clear();
        }

        public void SetScore(int s)
        {
            scoreText.text = ScorePrefix + s.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>0이면 콤보 줄을 비운다(숨김).</summary>
        public void SetCombo(int c)
        {
            comboText.text = c > 0 ? c.ToString(CultureInfo.InvariantCulture) + ComboSuffix : string.Empty;
        }

        /// <summary>판정 문구를 띄운다. EmptyBeat는 아무것도 보이지 않는다(이미 떠 있는 문구도 그대로 둔다).</summary>
        public void ShowJudgement(Judgement j)
        {
            Color color;
            switch (j)
            {
                case Judgement.Perfect: judgementLabel = PerfectLabel; color = GameConfig.JudgePerfectColor; break;
                case Judgement.Good: judgementLabel = GoodLabel; color = GameConfig.JudgeGoodColor; break;
                case Judgement.Miss: judgementLabel = MissLabel; color = GameConfig.JudgeMissColor; break;
                case Judgement.OffBeat: judgementLabel = OffBeatLabel; color = GameConfig.JudgeOffBeatColor; break;
                default: return;
            }

            judgementText.text = judgementLabel;
            judgementText.color = color;
            judgementTimer = GameConfig.JudgeTextSec;
            judgementText.gameObject.SetActive(true);
            PlaceJudgement();
        }

        /// <summary>판정 문구가 따라갈 대상(주인공)과 월드 → 화면 변환에 쓸 카메라.</summary>
        public void Follow(Transform target, Camera cam)
        {
            followTarget = target;
            followCamera = cam;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// <summary>점수 0, 콤보 0, 판정 문구 숨김.</summary>
        public void Clear()
        {
            SetScore(0);
            SetCombo(0);
            HideJudgement();
        }

        /// <summary>판정 문구 타이머를 dt초 진행한다. 다 되면 문구를 숨긴다.</summary>
        public void Advance(float dt)
        {
            if (judgementLabel == null) return;
            judgementTimer -= dt;
            if (judgementTimer <= 0f) HideJudgement();
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        void LateUpdate()
        {
            PlaceJudgement(); // 카메라가 Update/LateUpdate에서 움직이므로 매 프레임 따라간다
        }

        void HideJudgement()
        {
            judgementLabel = null;
            judgementTimer = 0f;
            judgementText.gameObject.SetActive(false);
        }

        /// <summary>월드 점(대상 x, JudgeTextWorldY) → 화면 → Canvas 로컬. 픽셀이 뭉개지지 않게 정수로 반올림한다.</summary>
        void PlaceJudgement()
        {
            if (judgementLabel == null || followTarget == null || followCamera == null || canvasRect == null) return;

            Vector3 world = new Vector3(followTarget.position.x, GameConfig.JudgeTextWorldY, followTarget.position.z);
            Vector3 screen = followCamera.WorldToScreenPoint(world);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local)) return;

            ((RectTransform)judgementText.transform).anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }
    }
}
