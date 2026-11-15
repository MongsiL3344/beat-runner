using System.Collections.Generic;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 주인공(구현명세 4.2, 8.1, 6.3). 달리기·대기 애니메이션과 점프·슬라이드·음파 동작 연출, 판정 반응(맞음·휘청)을 맡는다.
    /// 위치는 외부(GameFlow)가 SetX로 매 프레임 넣고, 이 클래스는 동작 중 y(점프 곡선)와 프레임만 정한다.
    /// 판정(RhythmJudge)과의 연결은 GameFlow가 한다(T6): 동작은 DoAction, Miss는 PlayHit, 헛박자는 PlayStumble.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(SpriteAnimator))]
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>판정 반응 연출(6.3). 반응 중에는 동작이 없다(CurrentAction == null).</summary>
        public enum Reaction { None, Hit, Stumble }

        static readonly Vector2 FootPivot = new Vector2(0.5f, 0f);

        readonly List<Sprite> made = new List<Sprite>();
        SpriteRenderer body;
        SpriteAnimator animator;
        SpriteRenderer waveRenderer;
        SpriteAnimator waveAnimator;

        Sprite[] idle;
        Sprite[] run;
        Sprite[] jump;
        Sprite[] slide;
        Sprite[] blast;
        Sprite[] soundWave;
        Sprite hitFrame;
        Sprite stumbleFrame;

        bool running;
        bool facingRight = true;
        float x;
        ActionType? action;
        float actionDuration;
        float actionElapsed;
        bool jumpFalling;
        Reaction reaction;
        float reactionDuration;
        float reactionElapsed;

        /// <summary>parent 아래에 주인공을 만든다. 처음에는 대기 프레임이다.</summary>
        public static PlayerController Create(Transform parent)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            return go.AddComponent<PlayerController>();
        }

        /// <summary>진행 중인 동작. 동작이 없으면 null.</summary>
        public ActionType? CurrentAction => action;

        /// <summary>진행 중인 판정 반응. 없으면 None.</summary>
        public Reaction CurrentReaction => reaction;

        /// <summary>동작 연출 시간 = min(ActionMaxSec, 반박자 × ActionBeatFactor) (7.3).</summary>
        public static float ActionDuration(double halfBeatSec)
        {
            return Mathf.Min(GameConfig.ActionMaxSec, (float)(halfBeatSec * GameConfig.ActionBeatFactor));
        }

        void Awake()
        {
            body = GetComponent<SpriteRenderer>();
            animator = GetComponent<SpriteAnimator>();
            body.sortingOrder = GameConfig.SortOrderPlayer;

            idle = new[] { Make(SpriteData.PlayerIdle) };
            run = MakeAll(SpriteData.PlayerRun);
            jump = MakeAll(SpriteData.PlayerJump);
            slide = MakeAll(SpriteData.PlayerSlide);
            blast = MakeAll(SpriteData.PlayerBlast);
            soundWave = MakeAll(SpriteData.SoundWave);
            hitFrame = Make(SpriteData.PlayerHit);
            stumbleFrame = Make(SpriteData.PlayerStumble);

            var waveGo = new GameObject("SoundWave");
            waveGo.transform.SetParent(transform, false);
            waveRenderer = waveGo.AddComponent<SpriteRenderer>();
            waveRenderer.sortingOrder = GameConfig.SortOrderEffect;
            waveAnimator = waveGo.AddComponent<SpriteAnimator>();
            waveGo.SetActive(false);

            x = transform.position.x;
            ShowBaseAnimation();
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

        void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>동작·반응 시간을 dt초 진행한다. Update가 Time.deltaTime으로 부르며, 테스트는 직접 부를 수 있다.</summary>
        public void Advance(float dt)
        {
            if (reaction != Reaction.None)
            {
                reactionElapsed += dt;
                if (reactionElapsed >= reactionDuration) EndAction();
                return;
            }

            if (action == null) return;

            actionElapsed += dt;
            if (actionElapsed >= actionDuration)
            {
                EndAction();
                return;
            }

            if (action == ActionType.Jump)
            {
                float u = actionElapsed / actionDuration;
                ApplyJumpHeight(u);
                // 정점을 지나면 하강 프레임으로 바꾼다(프레임이 바뀔 때만 SetFrame).
                if (!jumpFalling && u >= JumpApexU() && jump.Length > 1)
                {
                    jumpFalling = true;
                    animator.SetFrame(jump[jump.Length - 1]);
                }
            }
        }

        /// <summary>월드 x. y는 바닥에 두고, 점프 중이면 곡선 높이를 더한다.</summary>
        public void SetX(float x)
        {
            this.x = x;
            ApplyPosition(CurrentJumpHeight());
        }

        public void Face(bool right)
        {
            facingRight = right;
            body.flipX = !right;
            ApplyWaveFacing();
        }

        /// <summary>달리기(RunAnimFps) ↔ 대기. 동작·반응 중이면 그것이 끝난 뒤에 반영한다.</summary>
        public void SetRunning(bool running)
        {
            if (this.running == running) return;
            this.running = running;
            if (action == null && reaction == Reaction.None) ShowBaseAnimation();
        }

        /// <summary>
        /// 동작을 처음부터 시작한다. 이미 동작 중이면 그 동작을, 반응(맞음·휘청) 중이면 그 반응을 끊고 새로 시작한다.
        /// 점프는 JumpHeight까지 오르는 곡선, 슬라이드는 슬라이드 프레임, 음파는 붐박스 프레임 + 앞쪽 음파 이펙트.
        /// duration초 뒤에 달리기(또는 대기)로 돌아간다.
        /// </summary>
        public void DoAction(ActionType a, float duration)
        {
            reaction = Reaction.None;
            reactionElapsed = 0f;
            action = a;
            actionDuration = Mathf.Max(duration, Mathf.Epsilon);
            actionElapsed = 0f;
            jumpFalling = false;
            waveRenderer.gameObject.SetActive(false);

            switch (a)
            {
                case ActionType.Jump:
                    animator.SetFrame(jump[0]);
                    ApplyJumpHeight(0f);
                    break;
                case ActionType.Slide:
                    PlayOnce(animator, slide, actionDuration, true);
                    ApplyPosition(0f);
                    break;
                case ActionType.Wave:
                    PlayOnce(animator, blast, actionDuration, false);
                    ApplyPosition(0f);
                    waveRenderer.gameObject.SetActive(true);
                    ApplyWaveFacing();
                    PlayOnce(waveAnimator, soundWave, actionDuration, false);
                    break;
            }
        }

        /// <summary>Miss(틀린 키·지나침) 반응: 진행 중인 동작을 끊고 맞음 프레임을 HitFlashSec 동안 보여 준다(6.3, 사용자 결정).</summary>
        public void PlayHit()
        {
            StartReaction(Reaction.Hit, hitFrame, GameConfig.HitFlashSec);
        }

        /// <summary>헛박자 반응: 진행 중인 동작을 끊고 휘청 프레임을 StumbleSec 동안 보여 준다(6.3).</summary>
        public void PlayStumble()
        {
            StartReaction(Reaction.Stumble, stumbleFrame, GameConfig.StumbleSec);
        }

        /// <summary>동작을 끊고(음파 숨김, y는 바닥) 한 장짜리 반응 프레임을 고정한다. Play는 프레임을 처음부터 다시 돌리므로 SetFrame을 쓴다.</summary>
        void StartReaction(Reaction r, Sprite frame, float duration)
        {
            action = null;
            actionElapsed = 0f;
            jumpFalling = false;
            waveRenderer.gameObject.SetActive(false);
            ApplyPosition(0f);

            reaction = r;
            reactionDuration = Mathf.Max(duration, Mathf.Epsilon);
            reactionElapsed = 0f;
            animator.SetFrame(frame);
        }

        /// <summary>동작·반응을 끝내고 바닥의 달리기(또는 대기)로 돌아간다.</summary>
        void EndAction()
        {
            action = null;
            actionElapsed = 0f;
            jumpFalling = false;
            reaction = Reaction.None;
            reactionElapsed = 0f;
            waveRenderer.gameObject.SetActive(false);
            ApplyPosition(0f);
            ShowBaseAnimation();
        }

        /// <summary>진행 중인 동작·반응을 끊고 음파 이펙트를 숨기며 y를 바닥에 두고 대기 프레임으로 돌린다(레벨 정리 때 GameFlow가 부른다).</summary>
        public void ResetPose()
        {
            running = false;
            EndAction();
        }

        void ShowBaseAnimation()
        {
            if (running) animator.Play(run, GameConfig.RunAnimFps, true);
            else animator.Play(idle, 0f, false);
        }

        /// <summary>프레임 전체가 duration에 걸쳐 한 번 지나가도록 fps를 정한다(마지막 프레임은 loop=false면 절반 시점에 도달).</summary>
        static void PlayOnce(SpriteAnimator target, Sprite[] frames, float duration, bool loop)
        {
            target.Play(frames, frames.Length / duration, loop);
        }

        float JumpApexU()
        {
            // sin(π × u^e)가 1이 되는 u^e = 0.5 지점.
            return Mathf.Pow(0.5f, 1f / GameConfig.JumpRiseExponent);
        }

        float CurrentJumpHeight()
        {
            if (action != ActionType.Jump) return 0f;
            return JumpCurve(actionElapsed / actionDuration);
        }

        static float JumpCurve(float u)
        {
            u = Mathf.Clamp01(u);
            return GameConfig.JumpHeight * Mathf.Sin(Mathf.PI * Mathf.Pow(u, GameConfig.JumpRiseExponent));
        }

        void ApplyJumpHeight(float u)
        {
            ApplyPosition(JumpCurve(u));
        }

        void ApplyPosition(float lift)
        {
            transform.position = new Vector3(x, GameConfig.FloorY + lift, transform.position.z);
        }

        void ApplyWaveFacing()
        {
            float dir = facingRight ? 1f : -1f;
            waveRenderer.flipX = !facingRight;
            waveRenderer.transform.localPosition = new Vector3(dir * GameConfig.WaveEffectOffsetUnits, 0f, 0f);
        }

        Sprite Make(string[] rows)
        {
            Sprite sprite = PixelArt.Make(rows, FootPivot);
            made.Add(sprite);
            return sprite;
        }

        Sprite[] MakeAll(string[][] frames)
        {
            Sprite[] sprites = PixelArt.MakeFrames(frames, FootPivot);
            made.AddRange(sprites);
            return sprites;
        }
    }
}
