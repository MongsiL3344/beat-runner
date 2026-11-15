using System.Collections.Generic;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 분석된 BeatMap으로 복도·장애물·비상구를 월드에 깐다(구현명세 4.2, 9.3, T5). 월드 x = 곡 시간 × ScrollSpeed.
    /// 복도는 CeilingHeight(48px) 띠 하나이고, 화면 위아래는 같은 층 구조를 어둡게(`n` 색 곱) 반복해 아파트 단면처럼 보이게 한다.
    /// 타일 줄마다 SpriteRenderer 하나를 Tiled 모드로 곡 전체 길이만큼 늘린다. 격자당 Sprite는 1개만 만들어 공유하고(메모 M1),
    /// Clear에서 그 texture·sprite를 Destroy한다.
    /// </summary>
    public sealed class LevelBuilder : MonoBehaviour
    {
        static readonly Vector2 FootPivot = new Vector2(0.5f, 0f);
        static readonly Vector2 HangPivot = new Vector2(0.5f, 1f);

        readonly List<Sprite> made = new List<Sprite>();
        readonly Dictionary<int, Transform> obstacles = new Dictionary<int, Transform>();
        GameObject levelRoot;
        BeatMap map;

        /// <summary>parent 아래에 새 GameObject를 만든다. 빈 상태이며 Build를 불러야 레벨이 생긴다.</summary>
        public static LevelBuilder Create(Transform parent)
        {
            var go = new GameObject("LevelBuilder");
            go.transform.SetParent(parent, false);
            return go.AddComponent<LevelBuilder>();
        }

        /// <summary>곡 끝 위치 + ExitDistanceAfterEnd. Build 전에는 0.</summary>
        public float ExitX => map == null ? 0f : TimeToX(map.songLength) + GameConfig.ExitDistanceAfterEnd;

        /// <summary>비상구(닫힘) 오브젝트. 표지판은 자식이다. Build 전·Clear 뒤에는 null.</summary>
        public Transform Exit { get; private set; }

        public float TimeToX(double t) => (float)(t * GameConfig.ScrollSpeed);

        /// <summary>map.notes[noteIndex]의 장애물. 장애물이 없는 노트·범위 밖·Build 전이면 null.</summary>
        public Transform ObstacleFor(int noteIndex)
        {
            return obstacles.TryGetValue(noteIndex, out Transform t) ? t : null;
        }

        /// <summary>이전 레벨을 지우고 복도 타일(곡 전체 + 앞뒤 여백), 장애물, 비상구를 만든다. map이 null이면 지우기만 한다.</summary>
        public void Build(BeatMap map)
        {
            Clear();
            if (map == null) return;
            this.map = map;

            levelRoot = new GameObject("Level");
            levelRoot.transform.SetParent(transform, false);

            BuildCorridor();
            BuildObstacles();
            BuildExit();
        }

        /// <summary>만든 오브젝트와 texture·sprite를 모두 제거한다. 오브젝트는 즉시 비활성화되고 프레임 끝에 파괴된다.</summary>
        public void Clear()
        {
            if (levelRoot != null)
            {
                levelRoot.SetActive(false);
                Destroy(levelRoot);
            }
            levelRoot = null;
            obstacles.Clear();
            Exit = null;
            map = null;
            ReleaseSprites();
        }

        void OnDestroy()
        {
            // 자식 오브젝트는 이 오브젝트와 함께 파괴되므로 에셋만 정리한다.
            obstacles.Clear();
            ReleaseSprites();
        }

        void ReleaseSprites()
        {
            foreach (Sprite sprite in made)
            {
                if (sprite == null) continue;
                Destroy(sprite.texture);
                Destroy(sprite);
            }
            made.Clear();
        }

        // ── 복도 ──

        void BuildCorridor()
        {
            float ppu = GameConfig.PixelsPerUnit;

            Sprite floor = MakeTileable(SpriteData.FloorTile);
            Sprite ceiling = MakeTileable(SpriteData.CeilingTile);
            Sprite railing = MakeTileable(SpriteData.Railing);
            Sprite walls = MakeTileable(JoinHorizontally(SpriteData.WallModules)); // 모듈을 번갈아 반복
            Sprite plainWall = MakeTileable(PlainColumn(SpriteData.WallModules[0]));  // 비상구 뒤 벽: 문·창 없는 1px 세로줄

            // 모든 줄의 폭이 각 타일 폭의 정수배가 되도록 가장 긴 반복 단위(벽 모듈 묶음)의 배수로 맞춘다.
            float period = walls.rect.width / ppu;
            float left = -(GameConfig.LeadInDistanceUnits + GameConfig.LevelMarginUnits);
            float right = ExitX + GameConfig.LevelMarginUnits;
            float width = Mathf.Ceil((right - left) / period) * period;

            // 비상구가 벽 모듈의 문·창과 겹치지 않도록, 비상구 왼쪽 끝 직전의 모듈 경계부터는 문·창 없는 벽을 깐다.
            float moduleWidth = period / SpriteData.WallModules.Length;
            float exitHalfWidth = SpriteData.ExitClosed[0].Length / ppu / 2f;
            float wallEndX = left + Mathf.Floor((ExitX - exitHalfWidth - left) / moduleWidth) * moduleWidth;

            float floorHeight = floor.rect.height / ppu;
            float ceilingTileHeight = ceiling.rect.height / ppu;
            float wallHeight = walls.rect.height / ppu;
            float soffitHeight = GameConfig.CeilingHeight - wallHeight;
            // 한 층 = 바닥 타일 + 복도 띠 + 천장 타일. 위·아래층을 같은 구조로 반복한다.
            float storyHeight = floorHeight + GameConfig.CeilingHeight + ceilingTileHeight;
            // 카메라 중심은 복도 띠 안이므로 화면 위아래 끝(중심에서 CameraOrthoSize)까지 덮는 층 수.
            int storiesEachSide = Mathf.CeilToInt((GameConfig.CameraOrthoSize + GameConfig.CeilingHeight) / storyHeight);

            Color soffitColor = PixelArt.Palette['n'];
            Transform root = levelRoot.transform;

            for (int k = -storiesEachSide; k <= storiesEachSide; k++)
            {
                Color tint = k == 0 ? Color.white : GameConfig.OtherStoryTint;
                float y0 = GameConfig.FloorY + k * storyHeight; // 이 층의 바닥선
                string story = "Story " + k;

                TiledRow(story + " Wall", walls, left, y0, wallEndX - left, GameConfig.SortOrderBackground, tint, root);
                TiledRow(story + " Wall Plain", plainWall, wallEndX, y0, left + width - wallEndX, GameConfig.SortOrderBackground, tint, root);
                if (soffitHeight > 0f)
                    SolidRect(story + " Soffit", left, y0 + wallHeight, width, soffitHeight,
                              GameConfig.SortOrderBackground, soffitColor * tint, root);
                TiledRow(story + " Railing", railing, left, y0, width, GameConfig.SortOrderCorridor, tint, root);
                TiledRow(story + " Floor", floor, left, y0 - floorHeight, width, GameConfig.SortOrderCorridor, tint, root);
                TiledRow(story + " Ceiling", ceiling, left, y0 + GameConfig.CeilingHeight, width, GameConfig.SortOrderCorridor, tint, root);
            }
        }

        static string[] JoinHorizontally(string[][] modules)
        {
            var rows = new string[modules[0].Length];
            for (int r = 0; r < rows.Length; r++)
            {
                var line = new System.Text.StringBuilder();
                foreach (string[] module in modules) line.Append(module[r]);
                rows[r] = line.ToString();
            }
            return rows;
        }

        /// <summary>모듈의 세로줄 하나(문·창이 없는 벽 바탕 열)를 1px 폭 스프라이트 원본으로 만든다.</summary>
        static string[] PlainColumn(string[] module)
        {
            var rows = new string[module.Length];
            for (int r = 0; r < rows.Length; r++) rows[r] = module[r].Substring(1, 1);
            return rows;
        }

        static void TiledRow(string name, Sprite sprite, float left, float bottom, float width, int order, Color tint, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(left, bottom, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(width, sprite.rect.height / GameConfig.PixelsPerUnit);
            sr.sortingOrder = order;
            sr.color = tint;
        }

        /// <summary>WhitePixel(1px, 중앙 피벗)을 늘린 단색 사각형. WhitePixel은 PixelArt가 공유하므로 여기서 파괴하지 않는다.</summary>
        static void SolidRect(string name, float left, float bottom, float width, float height, int order, Color color, Transform parent)
        {
            float ppu = GameConfig.PixelsPerUnit;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(left + width / 2f, bottom + height / 2f, 0f);
            go.transform.localScale = new Vector3(width * ppu, height * ppu, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelArt.WhitePixel;
            sr.sortingOrder = order;
            sr.color = color;
        }

        // ── 장애물·비상구 ──

        void BuildObstacles()
        {
            if (map.notes == null) return;

            Sprite box = Make(SpriteData.Box, FootPivot);
            Sprite laundry = Make(SpriteData.Laundry, HangPivot);
            Sprite door = Make(SpriteData.FireDoor, FootPivot);
            Transform parent = new GameObject("Obstacles").transform;
            parent.SetParent(levelRoot.transform, false);

            for (int i = 0; i < map.notes.Count; i++)
            {
                Note note = map.notes[i];
                // E1 조합은 T5 범위 밖: 가장 낮은 플래그(상자 → 빨래 → 방화문) 하나만 놓는다.
                Sprite sprite;
                float y;
                ObstacleKind shown;
                if ((note.kind & ObstacleKind.Box) != 0) { sprite = box; y = GameConfig.FloorY; shown = ObstacleKind.Box; }
                else if ((note.kind & ObstacleKind.Laundry) != 0) { sprite = laundry; y = GameConfig.FloorY + GameConfig.CeilingHeight; shown = ObstacleKind.Laundry; }
                else if ((note.kind & ObstacleKind.Door) != 0) { sprite = door; y = GameConfig.FloorY; shown = ObstacleKind.Door; }
                else continue;

                Transform t = SpawnSprite("Obstacle " + i + " " + shown, sprite, TimeToX(note.time), y,
                                          GameConfig.SortOrderObstacle, parent);
                obstacles[i] = t;
            }
        }

        void BuildExit()
        {
            Sprite closed = Make(SpriteData.ExitClosed, FootPivot);
            Sprite sign = Make(SpriteData.ExitSign, FootPivot);
            Exit = SpawnSprite("Exit", closed, ExitX, GameConfig.FloorY, GameConfig.SortOrderObstacle, levelRoot.transform);
            SpawnSprite("ExitSign", sign, 0f, closed.rect.height / GameConfig.PixelsPerUnit, GameConfig.SortOrderObstacle, Exit);
        }

        static Transform SpawnSprite(string name, Sprite sprite, float x, float y, int order, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return go.transform;
        }

        Sprite Make(string[] rows, Vector2 pivot)
        {
            Sprite sprite = PixelArt.Make(rows, pivot);
            made.Add(sprite);
            return sprite;
        }

        /// <summary>
        /// Tiled 그리기용 스프라이트. PixelArt.Make는 Tight 메시라 타일링 경고가 나므로 같은 텍스처로 FullRect 스프라이트를 다시 만든다.
        /// 피벗은 왼쪽 아래(줄의 왼쪽 끝·밑변이 위치가 된다).
        /// </summary>
        Sprite MakeTileable(string[] rows)
        {
            Sprite source = PixelArt.Make(rows, Vector2.zero);
            Sprite sprite = Sprite.Create(source.texture, source.rect, Vector2.zero, GameConfig.PixelsPerUnit,
                                          0, SpriteMeshType.FullRect);
            sprite.name = source.name;
            Destroy(source); // 텍스처는 새 스프라이트가 쓰므로 남긴다
            made.Add(sprite);
            return sprite;
        }
    }
}
