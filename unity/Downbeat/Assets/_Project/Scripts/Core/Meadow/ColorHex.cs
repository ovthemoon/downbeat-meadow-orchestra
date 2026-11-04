using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>A colour as three channels in 0..1, exactly as written (sRGB-encoded when it comes from a hex string).</summary>
    public readonly struct Rgb
    {
        public float R { get; }
        public float G { get; }
        public float B { get; }

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb Lerp(in Rgb a, in Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    /// <summary>Colours in content.json are written as "#RRGGBB".</summary>
    public static class ColorHex
    {
        const int HexLength = 6;
        const float ChannelMax = 255f;

        public static bool TryParse(string text, out Rgb color)
        {
            color = default;
            if (string.IsNullOrEmpty(text)) return false;
            var digits = text[0] == '#' ? text.Substring(1) : text;
            if (digits.Length != HexLength) return false;
            if (!int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
            color = new Rgb(((value >> 16) & 0xFF) / ChannelMax, ((value >> 8) & 0xFF) / ChannelMax, (value & 0xFF) / ChannelMax);
            return true;
        }
    }
}
