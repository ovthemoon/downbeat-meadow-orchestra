using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Meadow
{
    /// <summary>Content colours ("#RRGGBB", sRGB) as Unity colours. The content was validated, so a bad string is a bug and throws.</summary>
    static class MeadowColors
    {
        public static Color Srgb(string hex)
        {
            if (!ColorHex.TryParse(hex, out var rgb)) throw new System.FormatException($"'{hex}' is not a colour like #RRGGBB");
            return new Color(rgb.R, rgb.G, rgb.B, 1f);
        }

        public static Color Linear(string hex) => Srgb(hex).linear;

        /// <summary>Linear RGB as a plain vector: shaders get exactly these numbers (SetColor would convert them a second time in a linear project).</summary>
        public static Vector4 LinearVector(string hex)
        {
            var c = Linear(hex);
            return new Vector4(c.r, c.g, c.b, 1f);
        }

        public static Vector4 AsVector(Color linear) => new Vector4(linear.r, linear.g, linear.b, 1f);
    }
}
