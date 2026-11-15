using UnityEngine;

namespace NoiseRun
{
    /// <summary>
    /// 문자 격자 → Sprite 팩토리(구현명세 4.2). T3 범위: 선·사각형을 늘려 그리는 1×1 흰 픽셀만.
    /// Make·MakeFrames와 팔레트는 T4에서 추가한다.
    /// </summary>
    public static class PixelArt
    {
        static Sprite whitePixel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            whitePixel = null;
        }

        /// <summary>1×1 흰 픽셀 스프라이트(Point 필터). 처음 쓸 때 만들고 캐시한다.</summary>
        public static Sprite WhitePixel
        {
            get
            {
                if (whitePixel == null) whitePixel = CreateWhitePixel();
                return whitePixel;
            }
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
