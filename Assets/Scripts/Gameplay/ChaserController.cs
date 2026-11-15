using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 추격자(아랫집 슬라임 이웃, 구현명세 4.2·7절). 주인공과의 거리 d를 계산하고, 화면에는 표시 거리(d를 ChaserLerpTau로 따라가는 값)만큼
    /// 주인공 왼쪽에 그린다. 판정 연결은 GameFlow가 한다: Miss·헛박자는 Approach, 콤보 변화는 OnComboChanged, 매 프레임 Tick.
    /// 잡힘 시퀀스(T8)는 GameFlow가 타임라인을 가진다: PlaceAt으로 위치를 직접 두고 SetSwingFrame으로 빗자루 휘두르기 프레임을 고른다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(SpriteAnimator))]
    public sealed class ChaserController : MonoBehaviour
    {
        static readonly Vector2 FootPivot = new Vector2(0.5f, 0f);

        readonly List<Sprite> made = new List<Sprite>();
        SpriteAnimator animator;
        Sprite[] idle;
        Sprite[] run;
        Sprite[] swing;
        int swingFrame = -1;

        float distance = GameConfig.ChaserStartDistance;
        float displayDistance = GameConfig.ChaserStartDistance;
        int lastCombo;
        bool caught;
        bool running;
        double lastSongTime;
        double nextLogTime;

        /// <summary>d ≤ ChaserCatchDistance가 된 첫 Tick에 한 번 발생한다. NoCatch면 발생하지 않는다. ResetChase 뒤 다시 발생할 수 있다.</summary>
        public event Action Caught;

        /// <summary>주인공과의 거리 d(유닛). ChaserCatchDistance 아래로 내려가지 않는다.</summary>
        public float Distance => distance;

        /// <summary>화면에 쓰는 거리. Tick마다 Distance를 지수적으로 따라간다(7.1 표시용 식).</summary>
        public float DisplayDistance => displayDistance;

        /// <summary>이번 판에 이미 잡혔는지. 잡힌 뒤에는 거리가 바뀌지 않는다.</summary>
        public bool IsCaught => caught;

        /// <summary>지금 고정해 보여 주는 휘두르기 프레임(0/1/2). 휘두르기 중이 아니면 −1(테스트가 읽음).</summary>
        public int SwingFrame => swingFrame;

        /// <summary>true면 잡히지 않는다(DevOptions.NoCatch, 개발·검증용). Bootstrap이 정하고 테스트가 직접 바꾼다.</summary>
        public bool NoCatch { get; set; }

        /// <summary>parent 아래에 이웃을 만든다. 처음에는 대기 프레임이다.</summary>
        public static ChaserController Create(Transform parent)
        {
            var go = new GameObject("Chaser");
            go.transform.SetParent(parent, false);
            return go.AddComponent<ChaserController>();
        }

        void Awake()
        {
            var body = GetComponent<SpriteRenderer>();
            animator = GetComponent<SpriteAnimator>();
            body.sortingOrder = GameConfig.SortOrderChaser;

            idle = new[] { Track(PixelArt.Make(SpriteData.ChaserIdle, FootPivot)) };
            run = PixelArt.MakeFrames(SpriteData.ChaserRun, FootPivot);
            made.AddRange(run);
            swing = PixelArt.MakeFrames(SpriteData.ChaserSwing, FootPivot); // 72px 폭, 몸 중심 = 가로 중앙이라 transform x = 몸 중심
            made.AddRange(swing);
            animator.Play(idle, 0f, false);
        }

        void OnDestroy()
        {
            foreach (Sprite sprite in made)
            {
                if (sprite == null) continue;
                Destroy(sprite.texture);
                Destroy(sprite);
            }
            made.Clear();
        }

        /// <summary>새 판: 거리·표시 거리를 시작 거리로, 잡힘·콤보 기록을 지운다. 휘두르기 프레임은 해제한다(달리기 상태에 맞는 대기·달리기로 복귀). NoCatch는 그대로 둔다.</summary>
        public void ResetChase()
        {
            distance = GameConfig.ChaserStartDistance;
            displayDistance = distance;
            lastCombo = 0;
            caught = false;
            lastSongTime = 0.0;
            nextLogTime = 0.0;
            if (swingFrame >= 0) ReleaseSwing();
        }

        /// <summary>
        /// 한 프레임. 곡 시간이 0 이상이고 곡 길이 미만일 때만 진행률에 비례한 속도로 다가온다(리드인·곡 끝 뒤에는 멈춤, 8.2).
        /// 표시 거리를 갱신해 x = playerX − 표시 거리에 서고, d ≤ ChaserCatchDistance면 Caught를 한 번 낸다.
        /// </summary>
        public void Tick(double songTime, double songLength, float dt, float playerX)
        {
            lastSongTime = songTime;
            if (!caught && songTime >= 0.0 && songTime < songLength)
            {
                float p = Mathf.Clamp01((float)(songTime / songLength));
                float closeSpeed = Mathf.Lerp(GameConfig.ChaserCloseSpeedStart, GameConfig.ChaserCloseSpeedEnd, p);
                SetDistance(distance - closeSpeed * dt);
            }

            displayDistance += (distance - displayDistance) * (1f - Mathf.Exp(-dt / GameConfig.ChaserLerpTau));
            transform.position = new Vector3(playerX - displayDistance, GameConfig.FloorY, transform.position.z);

            if (songTime >= nextLogTime)
            {
                Log("tick");
                nextLogTime = (Math.Floor(songTime / GameConfig.ChaserLogIntervalSec) + 1.0) * GameConfig.ChaserLogIntervalSec;
            }

            if (!caught && !NoCatch && distance <= GameConfig.ChaserCatchDistance)
            {
                caught = true;
                Log("caught");
                Caught?.Invoke();
            }
        }

        /// <summary>즉시 amount만큼 다가온다(Miss·헛박자). 잡힌 뒤에는 무시한다. 표시 거리는 Tick에서 따라간다.</summary>
        public void Approach(float amount)
        {
            if (caught) return;
            SetDistance(distance - amount);
            Log("approach " + amount.ToString("F3", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 판정 뒤의 콤보를 받는다. 콤보가 올라서 마일스톤(IsMilestone)에 닿았을 때만 ChaserMilestonePush만큼 밀어낸다(상한 ChaserMaxDistance).
        /// 같은 콤보로 다시 불려도(EmptyBeat 등) 두 번 밀지 않는다.
        /// </summary>
        public void OnComboChanged(int combo)
        {
            bool reached = combo > lastCombo && IsMilestone(combo);
            lastCombo = combo;
            if (!reached || caught) return;
            distance = Mathf.Min(distance + GameConfig.ChaserMilestonePush, GameConfig.ChaserMaxDistance);
            Log("push combo " + combo);
        }

        /// <summary>combo가 ComboMilestones 중 하나이거나, 마지막 마일스톤 이상에서 ComboMilestoneRepeat의 배수인지(7.1).</summary>
        public static bool IsMilestone(int combo)
        {
            if (combo <= 0) return false;
            int[] milestones = GameConfig.ComboMilestones;
            if (Array.IndexOf(milestones, combo) >= 0) return true;
            return combo >= milestones[milestones.Length - 1] && combo % GameConfig.ComboMilestoneRepeat == 0;
        }

        /// <summary>달리기(RunAnimFps) ↔ 대기. 바뀔 때만 애니메이션을 다시 건다. 휘두르기 프레임을 보여 주는 중이면 값이 같아도 풀고 다시 건다.</summary>
        public void SetRunning(bool running)
        {
            if (swingFrame >= 0)
            {
                this.running = running;
                ReleaseSwing();
                return;
            }

            if (this.running == running) return;
            this.running = running;
            ShowBaseAnimation();
        }

        /// <summary>
        /// 이웃의 월드 x(몸 중심)를 직접 둔다(걸어 들어오기·잡힘 연출, T8). y는 바닥이다. Distance·DisplayDistance는 바꾸지 않으며,
        /// 다음 Tick이 다시 일반 배치(playerX − 표시 거리)를 한다.
        /// </summary>
        public void PlaceAt(float x)
        {
            transform.position = new Vector3(x, GameConfig.FloorY, transform.position.z);
        }

        /// <summary>
        /// 빗자루 휘두르기 프레임을 고정해 보여 준다(T8 잡힘): 0 = 들어올림, 1 = 내리침, 2 = 내려친 뒤. −1이면 대기 프레임(달리기 중이면 달리기)으로 돌아간다.
        /// 이미 −1이면 아무것도 하지 않는다. SetRunning·ResetChase도 휘두르기를 해제한다.
        /// </summary>
        public void SetSwingFrame(int frame)
        {
            if (frame < -1 || frame >= swing.Length)
                throw new ArgumentOutOfRangeException(nameof(frame), frame, "휘두르기 프레임은 -1 또는 0~" + (swing.Length - 1));

            if (frame < 0)
            {
                if (swingFrame >= 0) ReleaseSwing();
                return;
            }

            swingFrame = frame;
            animator.SetFrame(swing[frame]);
        }

        void ReleaseSwing()
        {
            swingFrame = -1;
            ShowBaseAnimation();
        }

        void ShowBaseAnimation()
        {
            if (running) animator.Play(run, GameConfig.RunAnimFps, true);
            else animator.Play(idle, 0f, false);
        }

        void SetDistance(float d)
        {
            distance = Mathf.Max(d, GameConfig.ChaserCatchDistance);
        }

        void Log(string reason)
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Chaser] d={0:F3} shown={1:F3} t={2:F2} combo={3} ({4})",
                distance, displayDistance, lastSongTime, lastCombo, reason));
        }

        Sprite Track(Sprite sprite)
        {
            made.Add(sprite);
            return sprite;
        }
    }
}
