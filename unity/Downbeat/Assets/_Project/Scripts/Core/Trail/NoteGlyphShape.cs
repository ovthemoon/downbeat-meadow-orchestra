using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The picture of one trail note, a quaver, worked out from its outline so that no font has to hold the character (♪ is not in every operating-system font). The glyph sits on a unit square
    /// (u to the right, v up): a tilted oval head at the bottom left, a stem going up from its right side and a flag curving away from the top of the stem. <see cref="Alpha"/> is the coverage of a point of
    /// the square, 0..1: 1 inside the glyph and in a thin dark outline around it, anti-aliased over about a pixel, and a soft dark halo around that which is gone before the edge of the square.
    /// <see cref="Sample"/> also gives the grey of the point: 1 in the glyph, <see cref="OutlineGrey"/> in the outline and the halo. The view paints the grey into a texture and tints it with the colour of the
    /// cell, so the note is the colour of its cell inside a nearly black edge and still reads when that colour is a light one on a bright sky.
    /// </summary>
    public static class NoteGlyphShape
    {
        const float HeadU = 0.36f, HeadV = 0.22f;
        const float HeadLong = 0.155f, HeadShort = 0.108f;
        const float HeadTilt = 0.38f; // radians, the long axis rises to the right
        const float StemU = 0.492f, StemHalfWidth = 0.028f, StemBottom = 0.24f, StemTop = 0.80f;
        /// <summary>The thickness of the dark outline, as a share of the square (about 3 pixels of a note drawn 90 pixels high).</summary>
        public const float OutlineWidth = 0.034f;
        /// <summary>The grey (1 = the colour of the cell) of the outline and the halo: nearly black once tinted.</summary>
        public const float OutlineGrey = 0.03f;
        const float HaloStrength = 0.4f, HaloReach = 0.04f;
        const int FlagSegments = 24;

        // the flag: a curve from the top of the stem out to the right and down, thick at the stem and thin at its tip (u, v and half the width)
        static readonly float[] FlagU = { 0.492f, 0.505f, 0.60f, 0.700f };
        static readonly float[] FlagV = { 0.80f, 0.66f, 0.70f, 0.44f };
        const float FlagStartHalfWidth = 0.056f, FlagEndHalfWidth = 0.026f;

        /// <summary>The coverage at (<paramref name="u"/>, <paramref name="v"/>) of the unit square, for a texture whose pixels are <paramref name="pixel"/> wide (as a share of the square).</summary>
        public static float Alpha(float u, float v, float pixel = 1f / 128f) => Sample(u, v, pixel, out _);

        /// <summary>The coverage as <see cref="Alpha"/>, and the grey of the point (<see cref="OutlineGrey"/>..1: 1 where the glyph is, the outline's grey where only the outline or the halo is).</summary>
        public static float Sample(float u, float v, float pixel, out float grey)
        {
            var distance = Math.Min(HeadDistance(u, v), Math.Min(StemDistance(u, v), FlagDistance(u, v)));
            var fill = Clamp01(0.5f - distance / pixel);
            var outlined = Clamp01(0.5f - (distance - OutlineWidth) / pixel);
            var halo = HaloStrength * (float)Math.Exp(-Math.Max(0f, distance - OutlineWidth) / HaloReach);
            var alpha = outlined + (1f - outlined) * halo;
            grey = alpha > 1e-4f ? (fill + (alpha - fill) * OutlineGrey) / alpha : 1f;
            return alpha;
        }

        // the approximate distance to an ellipse (negative inside)
        static float HeadDistance(float u, float v)
        {
            var cos = (float)Math.Cos(HeadTilt);
            var sin = (float)Math.Sin(HeadTilt);
            var px = u - HeadU;
            var py = v - HeadV;
            var qx = px * cos + py * sin;
            var qy = -px * sin + py * cos;
            var k0 = (float)Math.Sqrt(qx * qx / (HeadLong * HeadLong) + qy * qy / (HeadShort * HeadShort));
            var k1 = (float)Math.Sqrt(qx * qx / (HeadLong * HeadLong * HeadLong * HeadLong) + qy * qy / (HeadShort * HeadShort * HeadShort * HeadShort));
            return k1 < 1e-6f ? -HeadShort : k0 * (k0 - 1f) / k1;
        }

        static float StemDistance(float u, float v)
        {
            var dx = Math.Abs(u - StemU) - StemHalfWidth;
            var dy = Math.Abs(v - (StemBottom + StemTop) * 0.5f) - (StemTop - StemBottom) * 0.5f;
            var outside = (float)Math.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f));
            return outside + Math.Min(Math.Max(dx, dy), 0f);
        }

        static float FlagDistance(float u, float v)
        {
            var best = float.MaxValue;
            var previousU = FlagU[0];
            var previousV = FlagV[0];
            for (var i = 1; i <= FlagSegments; i++)
            {
                var t = i / (float)FlagSegments;
                var nextU = Bezier(FlagU, t);
                var nextV = Bezier(FlagV, t);
                var radiusA = Lerp(FlagStartHalfWidth, FlagEndHalfWidth, (i - 1) / (float)FlagSegments);
                var radiusB = Lerp(FlagStartHalfWidth, FlagEndHalfWidth, t);
                best = Math.Min(best, SegmentDistance(u, v, previousU, previousV, nextU, nextV, radiusA, radiusB));
                previousU = nextU;
                previousV = nextV;
            }
            return best;
        }

        static float SegmentDistance(float u, float v, float ax, float ay, float bx, float by, float radiusA, float radiusB)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var length2 = dx * dx + dy * dy;
            var t = length2 < 1e-12f ? 0f : Clamp01(((u - ax) * dx + (v - ay) * dy) / length2);
            var cx = ax + dx * t - u;
            var cy = ay + dy * t - v;
            return (float)Math.Sqrt(cx * cx + cy * cy) - Lerp(radiusA, radiusB, t);
        }

        static float Bezier(float[] p, float t)
        {
            var s = 1f - t;
            return s * s * s * p[0] + 3f * s * s * t * p[1] + 3f * s * t * t * p[2] + t * t * t * p[3];
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
