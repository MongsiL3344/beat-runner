using System.Collections.Generic;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 문자 격자 → Sprite 팩토리(구현명세 4.2, 9.1). 첫 줄이 맨 위, 문자 하나가 팔레트 색 하나, '.'은 투명.
    /// 격자가 잘못되면(행 길이 불일치, 팔레트 밖 문자) Debug.LogError 후 마젠타 오류 스프라이트를 돌려준다.
    /// </summary>
    public static class PixelArt
    {
        static readonly Dictionary<char, Color32> PaletteMap = new Dictionary<char, Color32>
        {
            { '.', new Color32(0, 0, 0, 0) },              // 투명
            { 'k', new Color32(0x1a, 0x1c, 0x2c, 0xff) },  // 외곽선
            { 'w', new Color32(0xf4, 0xf4, 0xf4, 0xff) },  // 흰색
            { 's', new Color32(0xff, 0xcd, 0x75, 0xff) },  // 피부
            { 'h', new Color32(0x5d, 0x27, 0x5d, 0xff) },  // 머리카락
            { 'b', new Color32(0xb8, 0x79, 0x4a, 0xff) },  // 골판지
            { 'd', new Color32(0x8a, 0x55, 0x30, 0xff) },  // 골판지 그림자, 빗자루 자루
            { 't', new Color32(0xe8, 0xd5, 0xa0, 0xff) },  // 테이프
            { 'g', new Color32(0x38, 0xb7, 0x64, 0xff) },  // 비상구 초록
            { 'r', new Color32(0xb1, 0x3e, 0x53, 0xff) },  // 방화문 빨강, 눈 빨강
            { 'c', new Color32(0x41, 0xa6, 0xf6, 0xff) },  // 빨래 하늘색
            { 'm', new Color32(0x56, 0x6c, 0x86, 0xff) },  // 벽 회색
            { 'n', new Color32(0x33, 0x3c, 0x57, 0xff) },  // 어두운 회색
            { 'y', new Color32(0xe0, 0xa0, 0x40, 0xff) },  // 빗자루 솔(짚 색)
            { 'v', new Color32(0xa7, 0xf0, 0x70, 0xff) },  // 슬라임 몸(독성 연두)
            { 'u', new Color32(0x25, 0x71, 0x79, 0xff) },  // 슬라임 그림자(어두운 청록)
        };

        static readonly Color32 ErrorColor = new Color32(0xff, 0x00, 0xff, 0xff);

        static Sprite whitePixel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            whitePixel = null;
        }

        /// <summary>격자 문자 → 색. 테스트가 격자 검증에 쓴다.</summary>
        public static IReadOnlyDictionary<char, Color32> Palette => PaletteMap;

        /// <summary>1×1 흰 픽셀 스프라이트(Point 필터). 처음 쓸 때 만들고 캐시한다.</summary>
        public static Sprite WhitePixel
        {
            get
            {
                if (whitePixel == null) whitePixel = CreateWhitePixel();
                return whitePixel;
            }
        }

        /// <summary>
        /// 문자 격자 하나로 Sprite를 만든다. pivot은 0~1 정규화 좌표(캐릭터 (0.5, 0), 빨래 (0.5, 1)).
        /// 호출할 때마다 새 텍스처를 만들므로 결과는 호출자가 캐시한다.
        /// </summary>
        public static Sprite Make(string[] rows, Vector2 pivot)
        {
            if (rows == null || rows.Length == 0 || rows[0] == null || rows[0].Length == 0)
            {
                Debug.LogError("PixelArt.Make: 빈 격자입니다(행 " + (rows == null ? 0 : rows.Length) + "개)");
                return MakeError(1, 1, pivot);
            }

            int height = rows.Length;
            int width = rows[0].Length;
            for (int row = 0; row < height; row++)
            {
                int len = rows[row] == null ? 0 : rows[row].Length;
                if (len != width)
                {
                    Debug.LogError("PixelArt.Make: 행 길이가 다릅니다(격자 " + width + "×" + height
                                   + ", " + row + "번 행 길이 " + len + ")");
                    return MakeError(width, height, pivot);
                }
            }

            var pixels = new Color32[width * height];
            for (int row = 0; row < height; row++)
            {
                string line = rows[row];
                int y = height - 1 - row; // 첫 줄이 텍스처 맨 위
                for (int x = 0; x < width; x++)
                {
                    char ch = line[x];
                    if (!PaletteMap.TryGetValue(ch, out Color32 color))
                    {
                        Debug.LogError("PixelArt.Make: 팔레트에 없는 문자 '" + ch + "'(격자 " + width + "×" + height
                                       + ", " + row + "번 행 " + x + "번 열)");
                        return MakeError(width, height, pivot);
                    }
                    pixels[y * width + x] = color;
                }
            }
            return CreateSprite(width, height, pixels, pivot, "PixelArt");
        }

        /// <summary>프레임마다 Make를 부른다. frames가 null이면 빈 배열.</summary>
        public static Sprite[] MakeFrames(string[][] frames, Vector2 pivot)
        {
            if (frames == null)
            {
                Debug.LogError("PixelArt.MakeFrames: 프레임 배열이 null입니다");
                return new Sprite[0];
            }

            var sprites = new Sprite[frames.Length];
            for (int i = 0; i < frames.Length; i++) sprites[i] = Make(frames[i], pivot);
            return sprites;
        }

        static Sprite MakeError(int width, int height, Vector2 pivot)
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = ErrorColor;
            return CreateSprite(width, height, pixels, pivot, "PixelArtError");
        }

        static Sprite CreateSprite(int width, int height, Color32[] pixels, Vector2 pivot, string name)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.name = name;
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels);
            tex.Apply(false, false); // 테스트가 GetPixel로 읽으므로 읽기 가능하게 둔다

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), pivot, GameConfig.PixelsPerUnit);
            sprite.name = name;
            return sprite;
        }

        static Sprite CreateWhitePixel()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.name = "WhitePixel";
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), GameConfig.PixelsPerUnit);
            sprite.name = "WhitePixel";
            return sprite;
        }
    }
}
