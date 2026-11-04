using System;

namespace Downbeat.Core
{
    /// <summary>How visible the word that says the piece is over is, at a time after it was shown: it appears quickly, stays, and fades out slowly (it is there for <c>cutoff.endWordSec</c> in all).</summary>
    public static class EndWordTiming
    {
        /// <summary>The share of the time in which the word comes in.</summary>
        public const double FadeInShare = 0.15;
        /// <summary>The share of the time, at the end, in which the word goes out.</summary>
        public const double FadeOutShare = 0.45;

        /// <returns>0..1; 0 before the start and from the end on.</returns>
        public static float Alpha(double elapsedSec, double durationSec)
        {
            if (!(durationSec > 0.0) || !(elapsedSec > 0.0) || elapsedSec >= durationSec) return 0f;
            var progress = elapsedSec / durationSec;
            if (progress < FadeInShare) return Smooth(progress / FadeInShare);
            if (progress > 1.0 - FadeOutShare) return Smooth((1.0 - progress) / FadeOutShare);
            return 1f;
        }

        static float Smooth(double t)
        {
            var c = Math.Max(0.0, Math.Min(1.0, t));
            return (float)(c * c * (3.0 - 2.0 * c));
        }
    }
}
