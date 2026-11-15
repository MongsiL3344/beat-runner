using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>T4: 문자 격자 데이터(SpriteData)의 형식·크기·프레임 수, PixelArt.Make 결과, SpriteAnimator 재생.</summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class PixelArtTests
    {
        const int Free = 0;                   // 크기를 정하지 않은 값
        const int PlayerSize = 16;
        const int ChaserHeight = 46;
        const int HalfTickWidth = 1;          // 짝수 폭 규칙의 유일한 예외(구현명세 9.3)
        const float AnimFps = 10f;
        const float FramePeriod = 1f / AnimFps;
        static readonly Color32 Magenta = new Color32(0xff, 0x00, 0xff, 0xff);
        static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // 필드 이름, 프레임 수, 폭, 높이(Free면 검사 안 함)
        static readonly (string Name, int Frames, int Width, int Height)[] Sizes =
        {
            (nameof(SpriteData.PlayerIdle), 1, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerRun), 4, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerJump), 2, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerSlide), 2, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerBlast), 2, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerHit), 1, PlayerSize, PlayerSize),
            (nameof(SpriteData.PlayerStumble), 1, PlayerSize, PlayerSize),
            (nameof(SpriteData.SoundWave), 3, PlayerSize, PlayerSize),
            (nameof(SpriteData.ChaserIdle), 1, Free, ChaserHeight),
            (nameof(SpriteData.ChaserRun), 4, Free, ChaserHeight),
            (nameof(SpriteData.ChaserSwing), 3, Free, ChaserHeight),
            (nameof(SpriteData.Box), 1, 12, 10),
            (nameof(SpriteData.Laundry), 1, 16, 38),
            (nameof(SpriteData.FireDoor), 1, 12, 24),
            (nameof(SpriteData.FireDoorBlown), 1, Free, Free),
            (nameof(SpriteData.ExitClosed), 1, 20, 32),
            (nameof(SpriteData.ExitOpen), 1, 20, 32),
            (nameof(SpriteData.ExitSign), 1, 12, 6),
            (nameof(SpriteData.FloorTile), 1, 16, 8),
            (nameof(SpriteData.CeilingTile), 1, 16, 8),
            (nameof(SpriteData.WallModules), 2, 64, 40),
            (nameof(SpriteData.Railing), 1, 16, 10),
            (nameof(SpriteData.BeatIcon), 1, 16, 16),
            (nameof(SpriteData.BeatTickMain), 1, 2, 8),
            (nameof(SpriteData.BeatTickHalf), 1, HalfTickWidth, 5),
        };

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (Object o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        // ── 데이터 ──

        static IEnumerable<FieldInfo> GridFields()
        {
            foreach (FieldInfo f in typeof(SpriteData).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(string[]) || f.FieldType == typeof(string[][])) yield return f;
        }

        static string[][] FramesOf(FieldInfo field)
        {
            object value = field.GetValue(null);
            if (value is string[][] frames) return frames;
            return new[] { (string[])value };
        }

        static string[][] FramesOf(string fieldName)
        {
            FieldInfo field = typeof(SpriteData).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "SpriteData." + fieldName + " 필드가 없습니다");
            return FramesOf(field);
        }

        [Test]
        public void SpriteData_AllGrids_AreRectangularAndUsePaletteCharsOnly()
        {
            int fieldCount = 0;
            foreach (FieldInfo field in GridFields())
            {
                fieldCount++;
                string[][] frames = FramesOf(field);
                Assert.IsNotEmpty(frames, field.Name + ": 프레임이 없습니다");
                for (int f = 0; f < frames.Length; f++)
                {
                    string[] rows = frames[f];
                    string where = field.Name + "[" + f + "]";
                    Assert.IsNotNull(rows, where + ": 격자가 null입니다");
                    Assert.IsNotEmpty(rows, where + ": 행이 없습니다");
                    int width = rows[0].Length;
                    Assert.Greater(width, 0, where + ": 폭이 0입니다");
                    for (int r = 0; r < rows.Length; r++)
                    {
                        Assert.AreEqual(width, rows[r].Length, where + ": " + r + "번 행 길이가 첫 행과 다릅니다");
                        foreach (char ch in rows[r])
                            Assert.IsTrue(PixelArt.Palette.ContainsKey(ch), where + ": 팔레트에 없는 문자 '" + ch + "' (" + r + "번 행)");
                    }
                }
            }
            Assert.GreaterOrEqual(fieldCount, Sizes.Length, "SpriteData 격자 필드가 표보다 적습니다");
        }

        [Test]
        public void SpriteData_FrameCountsAndSizes_MatchSpecTable()
        {
            foreach (var (name, frameCount, width, height) in Sizes)
            {
                string[][] frames = FramesOf(name);
                Assert.AreEqual(frameCount, frames.Length, name + ": 프레임 수");
                for (int f = 0; f < frames.Length; f++)
                {
                    string where = name + "[" + f + "]";
                    int actualWidth = frames[f][0].Length;
                    if (width != Free) Assert.AreEqual(width, actualWidth, where + ": 폭");
                    if (height != Free) Assert.AreEqual(height, frames[f].Length, where + ": 높이");
                    if (name != nameof(SpriteData.BeatTickHalf)) Assert.AreEqual(0, actualWidth % 2, where + ": 폭은 짝수여야 합니다");
                }
            }
        }

        [Test]
        public void SpriteData_ChaserRunAndSwing_KeepOneWidthAcrossFrames()
        {
            foreach (string name in new[] { nameof(SpriteData.ChaserRun), nameof(SpriteData.ChaserSwing) })
            {
                string[][] frames = FramesOf(name);
                for (int f = 1; f < frames.Length; f++)
                    Assert.AreEqual(frames[0][0].Length, frames[f][0].Length, name + "[" + f + "]: 폭이 첫 프레임과 다릅니다(몸 중심이 흔들립니다)");
            }
        }

        [Test]
        public void Make_EverySpriteDataGrid_BuildsWithoutErrorAtGridSize()
        {
            foreach (FieldInfo field in GridFields())
            {
                string[][] frames = FramesOf(field);
                for (int f = 0; f < frames.Length; f++)
                {
                    Sprite sprite = Track(PixelArt.Make(frames[f], new Vector2(0.5f, 0f)));
                    Assert.AreEqual(frames[f][0].Length, (int)sprite.rect.width, field.Name + "[" + f + "] 폭");
                    Assert.AreEqual(frames[f].Length, (int)sprite.rect.height, field.Name + "[" + f + "] 높이");
                }
            }
        }

        // ── Make ──

        Sprite Track(Sprite sprite)
        {
            created.Add(sprite);
            created.Add(sprite.texture);
            return sprite;
        }

        [Test]
        public void Make_SetsPointFilterPixelsPerUnitAndPivot()
        {
            Sprite sprite = Track(PixelArt.Make(new[] { "kws", "..h" }, new Vector2(0.5f, 1f)));

            Assert.AreEqual(FilterMode.Point, sprite.texture.filterMode);
            Assert.AreEqual(TextureWrapMode.Clamp, sprite.texture.wrapMode);
            Assert.AreEqual(GameConfig.PixelsPerUnit, sprite.pixelsPerUnit);
            Assert.AreEqual(3, sprite.texture.width);
            Assert.AreEqual(2, sprite.texture.height);
            Assert.AreEqual(new Vector2(1.5f, 2f), sprite.pivot);
        }

        [Test]
        public void Make_FirstRowIsTopOfTexture()
        {
            Sprite sprite = Track(PixelArt.Make(new[] { "kws", "..h" }, new Vector2(0.5f, 0f)));
            Texture2D tex = sprite.texture;

            Assert.AreEqual(PixelArt.Palette['k'], (Color32)tex.GetPixel(0, 1));
            Assert.AreEqual(PixelArt.Palette['w'], (Color32)tex.GetPixel(1, 1));
            Assert.AreEqual(PixelArt.Palette['s'], (Color32)tex.GetPixel(2, 1));
            Assert.AreEqual(Transparent, (Color32)tex.GetPixel(0, 0));
            Assert.AreEqual(PixelArt.Palette['h'], (Color32)tex.GetPixel(2, 0));
        }

        [Test]
        public void MakeFrames_ReturnsOneSpritePerFrameInOrder()
        {
            Sprite[] sprites = PixelArt.MakeFrames(new[] { new[] { "k" }, new[] { "w" }, new[] { "s" } }, new Vector2(0.5f, 0f));
            foreach (Sprite s in sprites) Track(s);

            Assert.AreEqual(3, sprites.Length);
            Assert.AreEqual(PixelArt.Palette['k'], (Color32)sprites[0].texture.GetPixel(0, 0));
            Assert.AreEqual(PixelArt.Palette['w'], (Color32)sprites[1].texture.GetPixel(0, 0));
            Assert.AreEqual(PixelArt.Palette['s'], (Color32)sprites[2].texture.GetPixel(0, 0));
        }

        static void AssertAllMagenta(Sprite sprite, int width, int height)
        {
            Assert.AreEqual(width, sprite.texture.width);
            Assert.AreEqual(height, sprite.texture.height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Assert.AreEqual(Magenta, (Color32)sprite.texture.GetPixel(x, y), "(" + x + "," + y + ")");
        }

        [Test]
        public void Make_RowsOfDifferentLength_LogsErrorAndReturnsMagentaGridSizedSprite()
        {
            LogAssert.Expect(LogType.Error, new Regex("행 길이"));

            Sprite sprite = Track(PixelArt.Make(new[] { "kk", "k", "kk" }, new Vector2(0.5f, 0f)));

            AssertAllMagenta(sprite, 2, 3);
        }

        [Test]
        public void Make_CharOutsidePalette_LogsErrorWithCharAndPositionAndReturnsMagenta()
        {
            LogAssert.Expect(LogType.Error, new Regex("팔레트에 없는 문자 'x'.*1번 행 1번 열"));

            Sprite sprite = Track(PixelArt.Make(new[] { "kk", "kx" }, new Vector2(0.5f, 0f)));

            AssertAllMagenta(sprite, 2, 2);
        }

        [Test]
        public void Make_EmptyGrid_LogsErrorAndReturnsOnePixelMagenta()
        {
            LogAssert.Expect(LogType.Error, new Regex("빈 격자"));

            Sprite sprite = Track(PixelArt.Make(new string[0], new Vector2(0.5f, 0f)));

            AssertAllMagenta(sprite, 1, 1);
        }

        // ── SpriteAnimator ──

        SpriteRenderer renderer;
        SpriteAnimator animator;
        Sprite[] frames;

        void CreateAnimator()
        {
            frames = PixelArt.MakeFrames(new[] { new[] { "k" }, new[] { "w" }, new[] { "s" } }, new Vector2(0.5f, 0f));
            foreach (Sprite s in frames) Track(s);

            var go = new GameObject("AnimatorTest");
            created.Add(go);
            renderer = go.AddComponent<SpriteRenderer>();
            animator = go.AddComponent<SpriteAnimator>();
        }

        // 시간은 Advance(dt)로 직접 흘려 보낸다. AnimFps=10이므로 프레임은 0.1초마다 바뀐다(k→w→s→k).

        [Test]
        public void SpriteAnimator_Loop_ChangesFrameAtEachPeriodAndWrapsAround()
        {
            CreateAnimator();
            animator.Play(frames, AnimFps, true);
            Assert.AreSame(frames[0], renderer.sprite);

            animator.Advance(FramePeriod * 0.5f);
            Assert.AreSame(frames[0], renderer.sprite, "한 프레임 시간이 지나기 전에는 바뀌면 안 됩니다");
            animator.Advance(FramePeriod * 0.6f);
            Assert.AreSame(frames[1], renderer.sprite);
            animator.Advance(FramePeriod);
            Assert.AreSame(frames[2], renderer.sprite);
            animator.Advance(FramePeriod);
            Assert.AreSame(frames[0], renderer.sprite, "마지막 프레임 다음에는 처음으로 돌아와야 합니다");
            animator.Advance(FramePeriod);
            Assert.AreSame(frames[1], renderer.sprite);
        }

        [Test]
        public void SpriteAnimator_Loop_LargeStepSkipsWholeCycles()
        {
            CreateAnimator();
            animator.Play(frames, AnimFps, true);

            animator.Advance(FramePeriod * (frames.Length * 5 + 1.5f)); // 5바퀴 + 1.5프레임 → 둘째 프레임

            Assert.AreSame(frames[1], renderer.sprite);
        }

        [Test]
        public void SpriteAnimator_NoLoop_AdvancesInOrderAndStaysOnLastFrame()
        {
            CreateAnimator();
            animator.Play(frames, AnimFps, false);
            Assert.AreSame(frames[0], renderer.sprite);

            animator.Advance(FramePeriod * 0.5f);
            Assert.AreSame(frames[0], renderer.sprite);
            animator.Advance(FramePeriod);
            Assert.AreSame(frames[1], renderer.sprite);
            animator.Advance(FramePeriod);
            Assert.AreSame(frames[2], renderer.sprite);
            animator.Advance(FramePeriod * frames.Length * 10f);
            Assert.AreSame(frames[2], renderer.sprite, "마지막 프레임에서 멈춰야 합니다");
        }

        [Test]
        public void SpriteAnimator_Play_RestartsFromFirstFrame()
        {
            CreateAnimator();
            animator.Play(frames, AnimFps, true);
            animator.Advance(FramePeriod * 1.5f);
            Assert.AreSame(frames[1], renderer.sprite);

            animator.Play(frames, AnimFps, true);

            Assert.AreSame(frames[0], renderer.sprite);
            animator.Advance(FramePeriod * 0.5f);
            Assert.AreSame(frames[0], renderer.sprite, "이전 재생의 경과 시간이 남아 있으면 안 됩니다");
        }

        [Test]
        public void SpriteAnimator_SetFrame_StopsPlaybackOnThatSprite()
        {
            CreateAnimator();
            animator.Play(frames, AnimFps, true);
            animator.Advance(FramePeriod * 1.5f);

            animator.SetFrame(frames[2]);
            animator.Advance(FramePeriod * frames.Length * 5f);

            Assert.AreSame(frames[2], renderer.sprite);
        }

        [Test]
        public void SpriteAnimator_ZeroFps_StaysOnFirstFrame()
        {
            CreateAnimator();

            animator.Play(frames, 0f, true);
            animator.Advance(1f);

            Assert.AreSame(frames[0], renderer.sprite);
        }

        [Test]
        public void SpriteAnimator_PlayNullOrEmpty_KeepsCurrentSpriteAndIgnoresAdvance()
        {
            CreateAnimator();
            animator.SetFrame(frames[1]);

            animator.Play(null, AnimFps, true);
            animator.Advance(1f);
            Assert.AreSame(frames[1], renderer.sprite);

            animator.Play(new Sprite[0], AnimFps, true);
            animator.Advance(1f);
            Assert.AreSame(frames[1], renderer.sprite);
        }
    }
}
