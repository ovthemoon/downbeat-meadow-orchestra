using System;

namespace Downbeat.Core
{
    /// <summary>A point of a bent blade: where it is relative to the foot (x and z along the ground, y up, in the unit of the blade's height) and the direction the blade goes in there (a unit vector).</summary>
    public readonly struct BladePoint
    {
        public BladePoint(float x, float y, float z, float tangentX, float tangentY, float tangentZ)
        {
            X = x; Y = y; Z = z; TangentX = tangentX; TangentY = tangentY; TangentZ = tangentZ;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float TangentX { get; }
        public float TangentY { get; }
        public float TangentZ { get; }
    }

    /// <summary>
    /// How a blade of grass (and a flower stem) bends when the wind pushes it: along a circular arc as long as the blade is, never longer. The push is a "bend per height" vector (what the wind says,
    /// before this): its direction is the way the blade leans and its length <c>s</c> says how hard. The old shader moved the tip sideways by s heights without limit, so near a fast hand (s of 1.5 to 3)
    /// a blade came out two or three times as long, a streak. Now the total bend angle <c>theta</c> grows with s like <see cref="RestSlope"/> x s, which is what the resting meadow has always looked like
    /// (the chord of the arc leans within 5 degrees of the old formula's tip up to s = 0.6), and saturates smoothly at the most a blade bends (<c>meadow.wind.maxBendDeg</c>, 120): near a hand the blades lie
    /// down towards that angle instead of stretching, a harder hand further (78, 107, 117, 119, 120 degrees at s = 0.6, 1, 1.5, 2, 3). The knee is soft (power 4) on purpose: a blade that already leans
    /// 0.5 to 0.9 on its own still has room to bend when a gust comes, so the grass sways. The same numbers, line for line, are in MeadowWind.hlsl (<c>MeadowBladeCurve</c>), which draws the grass;
    /// BladeBendGpuParityPlayModeTests holds the two together.
    /// </summary>
    public static class BladeBend
    {
        public const float DefaultMaxBendDeg = 120f;
        /// <summary>The total bend in radians per unit of strength for a push that is still gentle: 2.4 puts the chord of the arc within 5 degrees of the old formula's tip for every s up to 0.6 (3.1 at worst) when the maximum is 100 degrees or more.</summary>
        public const float RestSlope = 2.4f;
        /// <summary>No push is read as harder than this (a wave and a hand on top of each other do not get near it): it keeps the arithmetic far from overflow.</summary>
        public const float MaxStrength = 40f;
        /// <summary>Below this total bend (radians, times the height up the blade) the sine series is used: it avoids dividing a rounding error by a tiny number.</summary>
        const float SeriesBelow = 1e-3f;
        const float TinyStrength = 1e-20f;

        public static float MaxBendRadians(float degrees) => degrees * (float)(Math.PI / 180.0);

        /// <summary>
        /// The total bend of a blade (radians) for a push of <paramref name="strength"/>: <c>min(RestSlope x s, max)</c> with a soft knee,
        /// <c>max x u / (1 + u^4)^(1/4)</c> with <c>u = RestSlope x s / max</c>. Zero for no push, growing with s, always below the maximum (and 0.89 of it at s = 1).
        /// </summary>
        public static float Angle(float strength, float maxBendRadians)
        {
            if (!(strength > 0f) || !(maxBendRadians > 0f)) return 0f;
            var u = Math.Min(strength, MaxStrength) * RestSlope / maxBendRadians;
            var u2 = u * u;
            var u4 = u2 * u2;
            var knee = MathF.Sqrt(MathF.Sqrt(1f + u4));
            return maxBendRadians * u / knee;
        }

        /// <summary>
        /// The point at <paramref name="t"/> (0 at the foot, 1 at the tip) of a blade <paramref name="height"/> long that is pushed by (<paramref name="bendX"/>, <paramref name="bendZ"/>) blade heights:
        /// a circular arc of length <paramref name="height"/> and total bend <see cref="Angle"/>, leaning the way of the push. Never NaN: no push is a straight blade.
        /// </summary>
        public static BladePoint At(float bendX, float bendZ, float t, float height, float maxBendRadians)
        {
            if (float.IsNaN(bendX) || float.IsNaN(bendZ)) { bendX = 0f; bendZ = 0f; }
            bendX = Clamp(bendX, MaxStrength);
            bendZ = Clamp(bendZ, MaxStrength);
            var strength = MathF.Sqrt(bendX * bendX + bendZ * bendZ);
            var theta = Angle(strength, maxBendRadians);
            var inverse = strength > TinyStrength ? 1f / strength : 0f;
            var dirX = bendX * inverse;
            var dirZ = bendZ * inverse;

            var a = theta * t;                    // the angle of the tangent at this point
            var half = 0.5f * a;
            var sinHalf = MathF.Sin(half);
            var cosHalf = MathF.Cos(half);
            // (1 - cos a) / theta = t sin(a/2) sinc(a/2), sin a / theta = t cos(a/2) sinc(a/2), with sinc(x) = sin x / x (the series 1 - x^2/6 where x is tiny)
            var sinc = half > SeriesBelow ? sinHalf / half : 1f - half * half * (1f / 6f);
            var sideways = height * t * sinHalf * sinc;
            var up = height * t * cosHalf * sinc;
            var lean = 2f * sinHalf * cosHalf;                  // sin a and cos a from the half angle (one sine and one cosine for the whole point, as in the shader)
            var cosA = cosHalf * cosHalf - sinHalf * sinHalf;
            return new BladePoint(sideways * dirX, up, sideways * dirZ, lean * dirX, cosA, lean * dirZ);
        }

        static float Clamp(float value, float limit) => value < -limit ? -limit : (value > limit ? limit : value);
    }
}
