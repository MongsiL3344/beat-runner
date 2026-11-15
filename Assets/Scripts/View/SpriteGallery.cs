#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 개발용 스프라이트 갤러리(DevOptions gallery=true, Editor 전용). SpriteData의 모든 격자·프레임을
    /// 1배 크기(480×270 기준 픽셀)로 줄마다 나열하고, 마지막 줄에는 SpriteAnimator로 순환 재생하는 애니메이션을 둔다.
    /// 어두운 외곽선이 보이도록 중간 회색 배경을 깔고, 모든 스프라이트를 정수 픽셀 위치에 놓는다.
    /// </summary>
    public sealed class SpriteGallery : MonoBehaviour
    {
        struct Cell
        {
            public string Name;
            public Sprite[] Frames;
            public float Fps;       // 0이면 정지 그림, 아니면 SpriteAnimator로 순환 재생
            public int GapBefore;   // 같은 줄 왼쪽 이웃과의 간격(px)
            public int Width;       // 프레임 중 가장 넓은 폭(px)
            public int Height;      // 프레임 중 가장 높은 높이(px)
        }

        static readonly Vector2 FootPivot = new Vector2(0.5f, 0f);
        static readonly Vector2 HangPivot = new Vector2(0.5f, 1f);

        readonly List<Sprite> made = new List<Sprite>();

        public static SpriteGallery Create(Transform parent)
        {
            var go = new GameObject("SpriteGallery");
            go.transform.SetParent(parent, false);
            var gallery = go.AddComponent<SpriteGallery>();
            gallery.Build();
            return gallery;
        }

        void OnDestroy()
        {
            foreach (Sprite sprite in made)
            {
                if (sprite == null) continue;
                Destroy(sprite.texture);
                Destroy(sprite);
            }
        }

        void Build()
        {
            Camera cam = Camera.main;
            Vector3 center = cam != null ? cam.transform.position : Vector3.zero;
            float ppu = GameConfig.PixelsPerUnit;
            // 카메라 중심에 맞추되 픽셀 격자에 걸치도록 반올림한다.
            transform.position = new Vector3(Mathf.Round(center.x * ppu) / ppu, Mathf.Round(center.y * ppu) / ppu, 0f);

            CreateBackground(cam);

            int left = -GameConfig.RefWidth / 2 + GameConfig.GalleryMarginPx;
            int right = GameConfig.RefWidth / 2 - GameConfig.GalleryMarginPx;
            int top = GameConfig.RefHeight / 2 - GameConfig.GalleryMarginPx;
            int bottom = -GameConfig.RefHeight / 2 + GameConfig.GalleryMarginPx;

            // 같은 스프라이트를 정지 그림 줄과 애니메이션 줄이 함께 쓴다.
            Sprite playerIdle = Make(SpriteData.PlayerIdle, FootPivot);
            Sprite[] playerRun = MakeAll(SpriteData.PlayerRun, FootPivot);
            Sprite[] playerJump = MakeAll(SpriteData.PlayerJump, FootPivot);
            Sprite[] playerSlide = MakeAll(SpriteData.PlayerSlide, FootPivot);
            Sprite[] playerBlast = MakeAll(SpriteData.PlayerBlast, FootPivot);
            Sprite playerHit = Make(SpriteData.PlayerHit, FootPivot);
            Sprite playerStumble = Make(SpriteData.PlayerStumble, FootPivot);
            Sprite[] soundWave = MakeAll(SpriteData.SoundWave, FootPivot);
            Sprite chaserIdle = Make(SpriteData.ChaserIdle, FootPivot);
            Sprite[] chaserRun = MakeAll(SpriteData.ChaserRun, FootPivot);
            Sprite[] chaserSwing = MakeAll(SpriteData.ChaserSwing, FootPivot);

            // 1~2줄: 주인공·이웃 크기 비교(같은 바닥선) + 이웃 프레임 + 순환 재생 칸.
            // 이웃 프레임은 높이가 46이라 줄 하나가 넓게 남으므로, 애니메이션 칸을 바로 이어 붙여 그 빈 공간을 쓴다.
            var line = new List<Cell>();
            Still(line, nameof(SpriteData.PlayerIdle), playerIdle);
            Still(line, nameof(SpriteData.ChaserIdle), chaserIdle);
            Still(line, nameof(SpriteData.ChaserRun), chaserRun);
            Still(line, nameof(SpriteData.ChaserSwing), chaserSwing);
            Animated(line, nameof(SpriteData.PlayerRun), playerRun, GameConfig.RunAnimFps);
            Animated(line, nameof(SpriteData.ChaserRun), chaserRun, GameConfig.RunAnimFps);
            Animated(line, nameof(SpriteData.SoundWave), soundWave, GameConfig.GalleryAnimFps);
            Animated(line, nameof(SpriteData.ChaserSwing), chaserSwing, GameConfig.GalleryAnimFps);
            Animated(line, nameof(SpriteData.PlayerJump), playerJump, GameConfig.GalleryAnimFps);
            PlaceLine(line, left, right, ref top, bottom);

            // 3줄: 주인공 나머지 프레임과 음파
            line.Clear();
            Still(line, nameof(SpriteData.PlayerRun), playerRun);
            Still(line, nameof(SpriteData.PlayerJump), playerJump);
            Still(line, nameof(SpriteData.PlayerSlide), playerSlide);
            Still(line, nameof(SpriteData.PlayerBlast), playerBlast);
            Still(line, nameof(SpriteData.PlayerHit), playerHit);
            Still(line, nameof(SpriteData.PlayerStumble), playerStumble);
            Still(line, nameof(SpriteData.SoundWave), soundWave);
            PlaceLine(line, left, right, ref top, bottom);

            // 4줄: 장애물·비상구·UI 아이콘
            line.Clear();
            Still(line, nameof(SpriteData.Box), Make(SpriteData.Box, FootPivot));
            Still(line, nameof(SpriteData.Laundry), Make(SpriteData.Laundry, HangPivot));
            Still(line, nameof(SpriteData.FireDoor), Make(SpriteData.FireDoor, FootPivot));
            Still(line, nameof(SpriteData.FireDoorBlown), Make(SpriteData.FireDoorBlown, FootPivot));
            Still(line, nameof(SpriteData.ExitClosed), Make(SpriteData.ExitClosed, FootPivot));
            Still(line, nameof(SpriteData.ExitOpen), Make(SpriteData.ExitOpen, FootPivot));
            Still(line, nameof(SpriteData.ExitSign), Make(SpriteData.ExitSign, FootPivot));
            Still(line, nameof(SpriteData.Railing), Make(SpriteData.Railing, FootPivot));
            Still(line, nameof(SpriteData.BeatIcon), Make(SpriteData.BeatIcon, FootPivot));
            Still(line, nameof(SpriteData.BeatTickMain), Make(SpriteData.BeatTickMain, FootPivot));
            Still(line, nameof(SpriteData.BeatTickHalf), Make(SpriteData.BeatTickHalf, FootPivot));
            PlaceLine(line, left, right, ref top, bottom);

            // 5줄: 타일·벽 모듈
            line.Clear();
            Still(line, nameof(SpriteData.FloorTile), Make(SpriteData.FloorTile, FootPivot));
            Still(line, nameof(SpriteData.CeilingTile), Make(SpriteData.CeilingTile, FootPivot));
            Still(line, nameof(SpriteData.WallModules), MakeAll(SpriteData.WallModules, FootPivot));
            PlaceLine(line, left, right, ref top, bottom);
        }

        void CreateBackground(Camera cam)
        {
            float aspect = cam != null ? cam.aspect : GameConfig.RefWidth / (float)GameConfig.RefHeight;
            // 게임 뷰가 480×270 비율이 아니어도 화면 전체를 덮는다.
            float widthPx = Mathf.Max(GameConfig.RefWidth, Mathf.Ceil(GameConfig.RefHeight * aspect));
            float heightPx = Mathf.Max(GameConfig.RefHeight, Mathf.Ceil(GameConfig.RefWidth / aspect));

            var go = new GameObject("Background");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(widthPx, heightPx, 1f); // WhitePixel 1px 스프라이트를 늘린다
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelArt.WhitePixel;
            sr.color = GameConfig.GalleryBackgroundColor;
            sr.sortingOrder = GameConfig.GalleryBackgroundSortingOrder;
        }

        Sprite Make(string[] rows, Vector2 pivot)
        {
            Sprite sprite = PixelArt.Make(rows, pivot);
            made.Add(sprite);
            return sprite;
        }

        Sprite[] MakeAll(string[][] frames, Vector2 pivot)
        {
            Sprite[] sprites = PixelArt.MakeFrames(frames, pivot);
            made.AddRange(sprites);
            return sprites;
        }

        /// <summary>프레임마다 칸 하나. 같은 그룹 프레임 사이는 FrameGap, 그룹 앞은 GroupGap.</summary>
        static void Still(List<Cell> line, string name, params Sprite[] frames)
        {
            for (int i = 0; i < frames.Length; i++)
            {
                Rect rect = frames[i].rect;
                line.Add(new Cell
                {
                    Name = frames.Length == 1 ? name : name + "[" + i + "]",
                    Frames = new[] { frames[i] },
                    Fps = 0f,
                    GapBefore = i == 0 ? GameConfig.GalleryGroupGapPx : GameConfig.GalleryFrameGapPx,
                    Width = (int)rect.width,
                    Height = (int)rect.height,
                });
            }
        }

        /// <summary>프레임 전체가 칸 하나. 칸 크기는 가장 큰 프레임에 맞춘다.</summary>
        static void Animated(List<Cell> line, string name, Sprite[] frames, float fps)
        {
            int width = 0;
            int height = 0;
            foreach (Sprite frame in frames)
            {
                width = Mathf.Max(width, (int)frame.rect.width);
                height = Mathf.Max(height, (int)frame.rect.height);
            }
            line.Add(new Cell
            {
                Name = name + " (anim)",
                Frames = frames,
                Fps = fps,
                GapBefore = GameConfig.GalleryGroupGapPx,
                Width = width,
                Height = height,
            });
        }

        /// <summary>한 줄(논리적 묶음)을 가로로 놓고, 폭이 넘치면 줄을 더 나눈다. top은 다음 줄의 윗변으로 갱신된다.</summary>
        void PlaceLine(List<Cell> cells, int left, int right, ref int top, int bottom)
        {
            var row = new List<Cell>();
            int rowWidth = 0;
            foreach (Cell cell in cells)
            {
                int add = row.Count == 0 ? cell.Width : cell.GapBefore + cell.Width;
                if (row.Count > 0 && left + rowWidth + add > right)
                {
                    PlaceRow(row, left, right, ref top, bottom);
                    row.Clear();
                    rowWidth = 0;
                    add = cell.Width;
                }
                row.Add(cell);
                rowWidth += add;
            }
            if (row.Count > 0) PlaceRow(row, left, right, ref top, bottom);
        }

        void PlaceRow(List<Cell> row, int left, int right, ref int top, int bottom)
        {
            int height = 0;
            foreach (Cell cell in row) height = Mathf.Max(height, cell.Height);
            int baseline = top - height;

            int x = left;
            for (int i = 0; i < row.Count; i++)
            {
                if (i > 0) x += row[i].GapBefore;
                Spawn(row[i], x, baseline);
                x += row[i].Width;
            }

            if (x > right || baseline < bottom)
                Debug.LogWarning("SpriteGallery: 화면 밖으로 넘칩니다(" + row[0].Name + " 줄, 오른쪽 끝 " + x + " > " + right
                                 + " 또는 아래 " + baseline + " < " + bottom + ")");

            top = baseline - GameConfig.GalleryLineGapPx;
        }

        void Spawn(Cell cell, int x, int baseline)
        {
            var go = new GameObject(cell.Name);
            go.transform.SetParent(transform, false);

            // 피벗 x는 가운데, y는 바닥(0) 또는 윗변(Laundry). 칸의 바닥선에 맞춘다.
            Vector2 pivotPx = cell.Frames[0].pivot;
            float ppu = GameConfig.PixelsPerUnit;
            go.transform.localPosition = new Vector3((x + cell.Width * 0.5f) / ppu, (baseline + pivotPx.y) / ppu, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cell.Frames[0];
            if (cell.Fps > 0f) go.AddComponent<SpriteAnimator>().Play(cell.Frames, cell.Fps, true);
        }
    }
}
#endif
