using UnityEngine;

namespace Downbeat.Record
{
    /// <summary>
    /// The three flowers of the score, painted into one small texture (three icons side by side) by evaluating their shapes pixel by pixel: the bellflower (harp), the dandelion
    /// (recorder) and the poppy (marimba), seen from the front like the flowers of the meadow. No picture files; the colours are the open colours of <c>meadow.flowers</c>.
    /// </summary>
    public static class FlowerIconPainter
    {
        public const int IconSize = 128;
        public const int IconCount = 3;
        const float Edge = 1.6f; // anti-aliasing width in pixels

        /// <summary>The texture area of icon <paramref name="index"/> (0 bellflower, 1 dandelion, 2 poppy).</summary>
        public static void Uv(int index, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(index / (float)IconCount, 0f);
            max = new Vector2((index + 1) / (float)IconCount, 1f);
        }

        /// <param name="open">Open colour of each of the three flowers (sRGB).</param>
        /// <param name="center">Centre colour of each (sRGB).</param>
        /// <param name="keepReadable">Keep the pixels readable by code (tests); the app does not need them once they are on the graphics card.</param>
        public static Texture2D Create(Color[] open, Color[] center, bool keepReadable = false)
        {
            var texture = new Texture2D(IconSize * IconCount, IconSize, TextureFormat.RGBA32, true, true) { name = "FlowerIcons", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[IconSize * IconSize * IconCount];
            for (var icon = 0; icon < IconCount; icon++)
            {
                for (var y = 0; y < IconSize; y++)
                {
                    for (var x = 0; x < IconSize; x++)
                    {
                        var u = (x + 0.5f) / IconSize * 2f - 1f;
                        var v = (y + 0.5f) / IconSize * 2f - 1f;
                        var c = Shade(icon, u, v, open[icon].linear, center[icon].linear);
                        pixels[y * IconSize * IconCount + icon * IconSize + x] = (Color32)c;
                    }
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, !keepReadable);
            return texture;
        }

        static Color Shade(int icon, float u, float v, Color open, Color center)
        {
            switch (icon)
            {
                case 0: return Bellflower(u, v, open, center);
                case 1: return Dandelion(u, v, open, center);
                default: return Poppy(u, v, open, center);
            }
        }

        static float Coverage(float signedDistance) => Mathf.Clamp01(0.5f - signedDistance * IconSize * 0.5f / Edge);

        /// <summary>Five pointed lobes of a bell, darker at the rim, a pale heart with a few stamens.</summary>
        static Color Bellflower(float u, float v, Color open, Color center)
        {
            var r = Mathf.Sqrt(u * u + v * v);
            var angle = Mathf.Atan2(v, u) + Mathf.PI * 0.5f;
            var lobe = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2.5f)), 0.55f);
            var outline = 0.93f * (0.55f + 0.45f * lobe);
            var body = Coverage(r - outline);
            var rim = Mathf.SmoothStep(0.35f, 1f, r / Mathf.Max(outline, 0.01f));
            var color = Color.Lerp(Color.Lerp(open, center, 0.35f), open * 0.55f, rim);
            var heart = Coverage(r - 0.27f);
            color = Color.Lerp(color, center, heart);
            var seam = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2.5f)), 12f) * Mathf.SmoothStep(0.2f, 0.45f, r) * 0.35f; // the folds between the lobes
            color = Color.Lerp(color, open * 0.4f, seam * (1f - heart));
            return new Color(color.r, color.g, color.b, body);
        }

        /// <summary>A puff of fine rays around a pale core.</summary>
        static Color Dandelion(float u, float v, Color open, Color center)
        {
            var r = Mathf.Sqrt(u * u + v * v);
            var angle = Mathf.Atan2(v, u);
            const int Rays = 26;
            var ray = angle / (Mathf.PI * 2f) * Rays;
            var offset = Mathf.Abs(ray - Mathf.Round(ray)) * (Mathf.PI * 2f / Rays) * r;   // distance to the nearest ray, in picture units
            var length = 0.9f - 0.08f * Mathf.Sin(angle * 7f);
            var spoke = Coverage(offset - 0.035f) * Coverage(r - length) * Mathf.Clamp01((r - 0.12f) * 8f);
            var tip = Coverage(Mathf.Sqrt(Mathf.Pow(r - length, 2f) + offset * offset) - 0.07f);   // a small dot at the end of each ray
            var core = Coverage(r - 0.36f) * 0.9f;
            var a = Mathf.Max(Mathf.Max(spoke * 0.9f, tip), core);
            var color = Color.Lerp(open, center, Mathf.Clamp01(1.2f - r * 2f));
            return new Color(color.r, color.g, color.b, a);
        }

        /// <summary>Four broad overlapping petals with a dark heart and a ring of stamens.</summary>
        static Color Poppy(float u, float v, Color open, Color center)
        {
            var a = 0f;
            var shade = 0f;
            for (var i = 0; i < 4; i++)
            {
                var angle = (i * 90f + 45f) * Mathf.Deg2Rad;
                var dx = u - Mathf.Cos(angle) * 0.4f;
                var dy = v - Mathf.Sin(angle) * 0.4f;
                var petal = Coverage(Mathf.Sqrt(dx * dx + dy * dy) - 0.52f);
                if (petal > a) { a = petal; shade = Mathf.Sqrt(dx * dx + dy * dy) / 0.52f; }
            }
            var r = Mathf.Sqrt(u * u + v * v);
            var color = Color.Lerp(open * 1.1f, open * 0.62f, Mathf.SmoothStep(0.25f, 1f, shade) * 0.8f + (1f - Mathf.Clamp01(r * 1.6f)) * 0.25f);
            var heart = Coverage(r - 0.2f);
            color = Color.Lerp(color, center, heart);
            var angleHeart = Mathf.Atan2(v, u);
            var stamen = Coverage(Mathf.Abs(r - 0.3f) - 0.025f) * Mathf.Pow(Mathf.Abs(Mathf.Sin(angleHeart * 8f)), 3f);
            color = Color.Lerp(color, center * 1.5f, stamen * 0.8f);
            return new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), a);
        }
    }
}
