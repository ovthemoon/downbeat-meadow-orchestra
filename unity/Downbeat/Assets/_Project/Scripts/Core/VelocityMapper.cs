using System;

namespace Downbeat.Core
{
    /// <summary>Turns click speed or a modifier key into a 0..1 note strength, and a strength into an amplitude.</summary>
    public static class VelocityMapper
    {
        public const float SoftVelocity = 0.4f;
        public const float LoudVelocity = 1.0f;
        public const float SlowClickVelocity = 0.5f;
        public const double SlowIntervalSec = 0.6;
        public const double FastIntervalSec = 0.1;

        const float MinGain = 0.3f;

        /// <summary>Slow clicks (>= 0.6 s apart) give 0.5, fast clicks (<= 0.1 s apart) give 1.0, linear in between.</summary>
        public static float FromClickInterval(double intervalSec)
        {
            if (double.IsNaN(intervalSec) || intervalSec >= SlowIntervalSec) return SlowClickVelocity;
            if (intervalSec <= FastIntervalSec) return LoudVelocity;
            var t = (SlowIntervalSec - intervalSec) / (SlowIntervalSec - FastIntervalSec);
            return (float)(SlowClickVelocity + (LoudVelocity - SlowClickVelocity) * t);
        }

        /// <summary>Strike speed (palm lengths per second) to strength: the trigger speed gives <paramref name="minVelocity"/>, full speed gives 1.0.</summary>
        public static float FromStrikeSpeed(float speed, float triggerSpeed, float fullSpeed, float minVelocity)
        {
            if (!(fullSpeed > triggerSpeed)) return LoudVelocity;
            var t = (speed - triggerSpeed) / (fullSpeed - triggerSpeed);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return minVelocity + (LoudVelocity - minVelocity) * t;
        }

        /// <summary>Amplitude factor: even the softest note is audible (0.3), the loudest is 1.0.</summary>
        public static float ToGain(float velocity)
        {
            var v = Math.Min(1f, Math.Max(0f, velocity));
            return MinGain + (1f - MinGain) * v;
        }
    }
}
