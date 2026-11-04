using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>A round soft gradient made in code (no picture file): alpha 1 in the middle, falling smoothly to 0 at the edge.</summary>
    static class SoftDiscTexture
    {
        const int Size = 64;

        public static Texture2D Create(float hardness = 0.35f)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "SoftDisc", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f) / Size * 2f - 1f;
                    var dy = (y + 0.5f) / Size * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var t = Mathf.Clamp01((1f - r) / (1f - hardness * 0.5f));
                    var a = t * t * (3f - 2f * t);
                    var b = (byte)Mathf.RoundToInt(a * 255f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, b);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A point of light with a halo: alpha falls off with a power of the distance from the middle (a bright small core and a long soft tail), unlike the flat-topped <see cref="Create"/>.</summary>
        public static Texture2D CreateGlow(float exponent = 1.7f)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "SoftGlow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f) / Size * 2f - 1f;
                    var dy = (y + 0.5f) / Size * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Pow(Mathf.Clamp01(1f - r), exponent);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A four-pointed sparkle (an upright cross of thin rays around a bright small core), for the star dust of the bell.</summary>
        public static Texture2D CreateStar(float rayWidth = 14f)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true) { name = "SoftStar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x + 0.5f) / Size * 2f - 1f;
                    var dy = (y + 0.5f) / Size * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var inside = Mathf.Clamp01(1f - r);
                    var core = Mathf.Pow(inside, 2.2f);
                    var ray = Mathf.Exp(-Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) * rayWidth) * inside;   // the nearer to either axis, the stronger; fading towards the edge
                    var a = Mathf.Clamp01(core + 0.8f * ray);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
