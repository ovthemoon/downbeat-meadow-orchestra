using System;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>
    /// <c>--stall-exit-sec N</c>: how long the main thread may stand still before the player ends itself (<see cref="StallWatchdog"/>). 0 turns the whole self-exit off, the exit on a lost graphics device
    /// (<see cref="DeviceLostLog"/>) too: one switch for "this player never ends itself", for a debugger or a machine whose freezes are known and harmless. Other values are held to <see cref="MinSec"/>..<see cref="MaxSec"/>.
    /// </summary>
    public static class StallExitSetting
    {
        public const string Flag = "stall-exit-sec";

        /// <summary>
        /// 20 s, between two numbers that were measured or configured. Below it: the worst frame of the Windows soak (6 h 57 min, Intel UHD, 2026-11-03) was 0.87 s, so 20 s is 23 times that; a frame that takes longer than
        /// a few seconds is no hitch but a freeze, and a monitor that is plugged in again or a display mode that is switched can hold the main thread for several seconds without being broken. Above it: the supervisor
        /// (<c>supervisor/configs/windows.json</c>, <c>heartbeatTimeoutSec</c> 30) restarts a player whose heartbeat is 30 s old, and the heartbeat is written every 5 s, so after a freeze it may come as early as
        /// 25 s later. The player is gone 20 s plus the one-second look of the watchdog thread plus the two steps of <c>SelfExit</c> (1 s each at most) after the freeze at the latest: 23 s, earlier than that in every phase
        /// of the beat, so the reason on record is the app's own and not a bare <c>heartbeat_stale</c>. A test (<c>StallExitSettingTests</c>) reads the supervisor's number and keeps the two apart.
        /// </summary>
        public const double DefaultSec = 20.0;

        /// <summary>Below this a healthy player would be ended by the longest frames it has (0.87 s on the soak, plus the look of the watchdog thread).</summary>
        public const double MinSec = 2.0;
        /// <summary>An hour: above this the setting is a typo (the supervisor restarts a frozen player after 30 s anyway).</summary>
        public const double MaxSec = 3600.0;

        /// <summary>
        /// The limit in seconds: <see cref="DefaultSec"/> without the flag; 0 = off; a number outside <see cref="MinSec"/>..<see cref="MaxSec"/> is held to the nearest end; a value that is not a number gives
        /// <see cref="DefaultSec"/>. In the last two cases <paramref name="problem"/> says what was done.
        /// </summary>
        public static double Resolve(LaunchArgs launch, out string problem)
        {
            problem = null;
            if (launch == null || !launch.Has(Flag)) return DefaultSec;
            if (!launch.TryGetDouble(Flag, out var seconds))
            {
                problem = $"--{Flag} '{launch.Get(Flag)}' is not a number of seconds (0 = off): using {Format(DefaultSec)}";
                return DefaultSec;
            }
            if (seconds == 0.0) return 0.0;
            var held = Math.Min(MaxSec, Math.Max(MinSec, seconds));
            if (held != seconds) problem = $"--{Flag} {Format(seconds)} is outside {Format(MinSec)}..{Format(MaxSec)} s (0 = off): using {Format(held)}";
            return held;
        }

        /// <summary>The self-exit is on: in a player (never in the editor, which a game must not end) and with a limit above 0.</summary>
        public static bool IsActive(bool isEditor, double stallSec) => !isEditor && stallSec > 0.0;

        static string Format(double seconds) => seconds.ToString("G6", CultureInfo.InvariantCulture);
    }
}
