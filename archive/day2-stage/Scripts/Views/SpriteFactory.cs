using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>Small generated sprites (no image files needed): a white pixel for lines, a soft disc for glows and dots.</summary>
    public static class SpriteFactory
    {
        const int PixelSize = 4;
        const int DiscSize = 128;

        public static Sprite WhitePixel()
        {
            var texture = new Texture2D(PixelSize, PixelSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[PixelSize * PixelSize];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, PixelSize, PixelSize), new Vector2(0.5f, 0.5f), PixelSize); // 1 world unit
        }

        /// <summary>White disc: opaque core that falls off smoothly to transparent at the rim. Tint with SpriteRenderer.color.</summary>
        public static Sprite SoftDisc(float coreRadius)
        {
            var texture = new Texture2D(DiscSize, DiscSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[DiscSize * DiscSize];
            var center = (DiscSize - 1) * 0.5f;
            for (var y = 0; y < DiscSize; y++)
            {
                for (var x = 0; x < DiscSize; x++)
                {
                    var r = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                    var t = Mathf.Clamp01((1f - r) / Mathf.Max(0.0001f, 1f - coreRadius));
                    var alpha = t * t * (3f - 2f * t);
                    pixels[y * DiscSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, DiscSize, DiscSize), new Vector2(0.5f, 0.5f), DiscSize); // 1 world unit across
        }

        public static Sprite FromImage(Texture2D texture, float pixelsPerUnit, Vector2 pivot) =>
            Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }
}
