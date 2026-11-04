namespace Downbeat.Core
{
    /// <summary>
    /// Lets a warning through at most once per interval and counts the ones it held back, so that a fault repeated thousands of times
    /// a second (a socket that keeps failing) writes one line instead of filling the log. Not thread safe: use it from one thread.
    /// </summary>
    public sealed class WarningRateLimiter
    {
        readonly double _intervalSec;
        double _lastAllowedSec = double.NegativeInfinity;

        public WarningRateLimiter(double intervalSec)
        {
            _intervalSec = intervalSec;
        }

        /// <summary>Warnings held back since the last one let through.</summary>
        public int Suppressed { get; private set; }

        /// <summary>True when the caller may log now; <paramref name="suppressedSinceLast"/> is how many were held back before this one.</summary>
        public bool TryAcquire(double nowSec, out int suppressedSinceLast)
        {
            if (nowSec - _lastAllowedSec < _intervalSec)
            {
                Suppressed++;
                suppressedSinceLast = 0;
                return false;
            }

            suppressedSinceLast = Suppressed;
            Suppressed = 0;
            _lastAllowedSec = nowSec;
            return true;
        }
    }
}
