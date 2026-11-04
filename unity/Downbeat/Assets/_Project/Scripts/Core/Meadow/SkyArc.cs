namespace Downbeat.Core
{
    /// <summary>
    /// The sky as an arc of three pictures: quiet blue hour (t 0), the playing dusk (t 0.5) and the climax sunset (t 1). Between two stops only those two
    /// neighbours are mixed, so the blue-hour plate never shows in the second half and the sunset plate never in the first.
    /// </summary>
    public static class SkyArc
    {
        public const float MidT = 0.5f;

        /// <summary>Weights of the (blueHour, mid, sunset) stops; they sum to 1. t is clamped to 0..1 and NaN counts as 0.</summary>
        public static (float blueHour, float mid, float sunset) Weights(float t01)
        {
            var t = float.IsNaN(t01) ? 0f : (t01 < 0f ? 0f : (t01 > 1f ? 1f : t01));
            if (t <= MidT)
            {
                var u = t / MidT;
                return (1f - u, u, 0f);
            }

            var v = (t - MidT) / (1f - MidT);
            return (0f, 1f - v, v);
        }

        /// <summary>The three stop values mixed with <see cref="Weights"/>.</summary>
        public static float Mix(float t01, float blueHour, float mid, float sunset)
        {
            var (a, b, c) = Weights(t01);
            return a * blueHour + b * mid + c * sunset;
        }
    }
}
