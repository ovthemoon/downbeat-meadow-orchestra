using System;
using System.Threading;

namespace Downbeat.Core
{
    /// <summary>
    /// The decision of the backstop against a frozen main thread: has the main loop stood still for longer than the limit? The main thread calls <see cref="Beat"/> every frame; another thread calls
    /// <see cref="IsStalled"/> about once a second (<see cref="CheckIntervalSec"/>). It does not start a thread, kill anything or read the real clock (the clock is passed in), so every rule is tested without waiting.
    /// <para>
    /// It watches nothing until <see cref="Arm"/>: the app arms it when it is running, so that a slow boot is not mistaken for a freeze (the supervisor has its own grace period for that). The silence is counted from
    /// the arming or from the last beat, whichever is later.
    /// </para>
    /// <para>
    /// A suspended process (the PC slept, a debugger stopped it) is no freeze: when two looks are further apart than <see cref="ProcessPauseSec"/> the silence of the main thread says nothing. The count does not start
    /// again at once, because a PC that wakes up often needs its main thread many seconds to get going (display, sound, graphics driver); it starts again at the first beat after the wake-up. The wait for that first beat
    /// is not for ever: a graphics driver that resets while the PC wakes up leaves the main thread frozen for good, which is the very case this class is for, so after <see cref="ResumeWindowFactor"/> times the limit
    /// without a beat the main thread counts as stalled.
    /// </para>
    /// </summary>
    public sealed class StallWatchdog
    {
        /// <summary>How often the thread that calls <see cref="IsStalled"/> should look.</summary>
        public const double CheckIntervalSec = 1.0;
        /// <summary>If two looks of the watchdog thread are further apart than this, the whole process was stopped in between, not only the main thread.</summary>
        public const double ProcessPauseSec = 5.0;
        /// <summary>After a suspend the main thread may take this many times the limit to run its first frame.</summary>
        public const double ResumeWindowFactor = 2.0;

        const double MillisecondsPerSecond = 1000.0;
        const long NotResuming = -1L;

        readonly long _stallMs;
        readonly long _resumeWindowMs;
        readonly Func<long> _nowMs;
        long _lastBeatMs;                 // written by the main thread (Beat, Arm), read by the watchdog thread
        long _lastLookMs;                 // written by the watchdog thread at every look and by Arm, which the main thread also calls while armed (the app starts to quit): a stale value costs one look a wrong gap, nothing more
        long _resumedAtMs = NotResuming;  // the watchdog thread; since when a suspend is being waited out, or NotResuming
        int _armed;

        /// <param name="stallSec">The limit in seconds; 0 or less: the watchdog is off.</param>
        /// <param name="nowMs">A clock that only goes forward, in milliseconds.</param>
        public StallWatchdog(double stallSec, Func<long> nowMs)
        {
            _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            if (double.IsNaN(stallSec) || double.IsInfinity(stallSec)) throw new ArgumentOutOfRangeException(nameof(stallSec), stallSec, "must be a number");
            StallSec = stallSec;
            _stallMs = (long)(stallSec * MillisecondsPerSecond);
            _resumeWindowMs = (long)(stallSec * ResumeWindowFactor * MillisecondsPerSecond);
        }

        public double StallSec { get; }
        public bool IsEnabled => StallSec > 0.0;
        public bool IsArmed => Volatile.Read(ref _armed) != 0;
        /// <summary>The process was found suspended and the main thread has not run a frame since (the longer window applies).</summary>
        public bool IsWaitingAfterSuspend => Volatile.Read(ref _resumedAtMs) != NotResuming;

        /// <summary>From now on a main thread that does not <see cref="Beat"/> for the limit is a stall. Nothing happens when the watchdog is off. Also starts the count again when already armed.</summary>
        public void Arm()
        {
            if (!IsEnabled) return;
            var now = _nowMs();
            Volatile.Write(ref _lastBeatMs, now);
            Volatile.Write(ref _lastLookMs, now);
            Volatile.Write(ref _resumedAtMs, NotResuming);
            Volatile.Write(ref _armed, 1);
        }

        public void Disarm() => Volatile.Write(ref _armed, 0);

        /// <summary>The main loop ran a frame (or the app is about to leave and the clock should start from here). Cheap: one clock read and one write.</summary>
        public void Beat() => Volatile.Write(ref _lastBeatMs, _nowMs());

        /// <summary>One look from the watchdog thread. True when the main loop has been silent for the limit; <paramref name="silentSec"/> says for how long.</summary>
        public bool IsStalled(out double silentSec)
        {
            silentSec = 0.0;
            if (!IsArmed) return false;
            var now = _nowMs();
            var sinceLastLookMs = now - Volatile.Read(ref _lastLookMs);
            Volatile.Write(ref _lastLookMs, now);
            if (sinceLastLookMs > ProcessPauseSec * MillisecondsPerSecond)
            {
                Volatile.Write(ref _resumedAtMs, now);   // a suspend (again): wait for a frame
                return false;
            }

            var lastBeatMs = Volatile.Read(ref _lastBeatMs);
            var resumedAtMs = Volatile.Read(ref _resumedAtMs);
            if (resumedAtMs != NotResuming)
            {
                if (lastBeatMs < resumedAtMs) return SilentFor(now - resumedAtMs, _resumeWindowMs, out silentSec);   // no frame since the wake-up
                Volatile.Write(ref _resumedAtMs, NotResuming);   // the first frame after it: the ordinary count goes on from that beat
            }
            return SilentFor(now - lastBeatMs, _stallMs, out silentSec);
        }

        static bool SilentFor(long silentMs, long limitMs, out double silentSec)
        {
            silentSec = silentMs / MillisecondsPerSecond;
            return silentMs >= limitMs;
        }
    }
}
