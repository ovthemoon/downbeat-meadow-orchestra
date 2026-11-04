using UnityEngine;

namespace Downbeat.Record
{
    /// <summary>
    /// The picture of the idle motes of the record screen, made in code like the flower icons: a soft round glow (a firefly) on the left and a petal on the right, side by side in one
    /// small texture, so that all motes are one mesh and one draw call. White with an alpha shape; the colour comes from the vertices.
    /// </summary>
    public static class MoteAtlas
    {
        public const int CellSize = 64;
        const float Edge = 1.6f;                 // anti-aliasing width in pixels
        const float PetalHalfLength = 0.92f;     // of the half cell
        const float PetalHalfWidth = 0.46f;

        /// <summary>The texture area of the firefly glow.</summary>
        public static void GlowUv(out Vector2 min, out Vector2 max)
        {
            min = new Vector2(0f, 0f);
            max = new Vector2(0.5f, 1f);
        }

        /// <summary>The texture area of the petal: it lies along the horizontal axis, so turning the quad turns the petal.</summary>
        public static void PetalUv(out Vector2 min, out Vector2 max)
        {
            min = new Vector2(0.5f, 0f);
            max = new Vector2(1f, 1f);
        }

        public static Texture2D Create(bool keepReadable = false)
        {
            var texture = new Texture2D(CellSize * 2, CellSize, TextureFormat.RGBA32, false, true) { name = "RecordMotes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[CellSize * CellSize * 2];
            for (var y = 0; y < CellSize; y++)
            {
                for (var x = 0; x < CellSize; x++)
                {
                    var u = (x + 0.5f) / CellSize * 2f - 1f;
                    var v = (y + 0.5f) / CellSize * 2f - 1f;
                    pixels[y * CellSize * 2 + x] = Alpha(Glow(u, v));
                    pixels[y * CellSize * 2 + CellSize + x] = Alpha(Petal(u, v));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, !keepReadable);
            return texture;
        }

        /// <summary>1 in the middle, smoothly 0 at the edge: bright core, soft halo.</summary>
        public static float Glow(float u, float v)
        {
            var t = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
            return t * t * (3f - 2f * t);
        }

        /// <summary>An egg-shaped petal, broader at one end, a little brighter along its middle line.</summary>
        public static float Petal(float u, float v)
        {
            var along = u / PetalHalfLength;                          // -1 (the broad end) .. 1 (the tip)
            if (along <= -1f || along >= 1f) return 0f;
            var half = PetalHalfWidth * Mathf.Sqrt(1f - along * along) * (1f - 0.3f * along);
            var distance = Mathf.Abs(v) - half;                       // signed distance to the edge, about
            var coverage = Mathf.Clamp01(0.5f - distance * CellSize * 0.5f / Edge);
            if (coverage <= 0f) return 0f;
            var middle = 1f - Mathf.Clamp01(Mathf.Abs(v) / Mathf.Max(half, 1e-4f));
            return coverage * (0.7f + 0.3f * middle);
        }

        static Color32 Alpha(float a) => new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
    }
}
