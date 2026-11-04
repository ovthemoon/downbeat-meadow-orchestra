using System;
using System.Globalization;
using System.Text;

namespace Downbeat.Core
{
    /// <summary>Why the player ended itself (see <see cref="SelfExitReasons"/>).</summary>
    public enum SelfExitReason
    {
        /// <summary>A log line that reached the log hook says the graphics device is lost (<see cref="DeviceLostLog"/>).</summary>
        GraphicsDeviceLost,
        /// <summary>The main thread has not run a frame for longer than the limit (<see cref="StallWatchdog"/>).</summary>
        MainThreadStalled,
    }

    /// <summary>
    /// The words, the process exit codes and the one-line form of a self-exit. The exit code is what a supervisor logs when the player is gone (<c>exit code=72</c>), so the two reasons have codes of their own
    /// that are not 0 (a normal quit) and not 1 (what a killed or crashed player usually gives); <c>exit-reasons.txt</c> in the health folder and the player log say the same in words.
    /// </summary>
    public static class SelfExitReasons
    {
        public const int GraphicsDeviceLostExitCode = 71;
        public const int MainThreadStalledExitCode = 72;
        /// <summary>The detail of a line is cut here: it is a line in a log, not a dump of what the driver printed.</summary>
        public const int MaxDetailChars = 240;

        const string Ellipsis = "...";

        public static string Word(this SelfExitReason reason) => reason == SelfExitReason.GraphicsDeviceLost ? "graphics_device_lost" : "main_thread_stalled";

        public static int ExitCode(this SelfExitReason reason) => reason == SelfExitReason.GraphicsDeviceLost ? GraphicsDeviceLostExitCode : MainThreadStalledExitCode;

        /// <summary><c>reason=main_thread_stalled exit_code=72 uptime_s=25012.3 detail=…</c>: one line, the detail last, with line breaks and tabs turned into spaces.</summary>
        public static string Describe(SelfExitReason reason, double uptimeSec, string detail)
        {
            return $"reason={reason.Word()} exit_code={reason.ExitCode()} uptime_s={uptimeSec.ToString("0.0", CultureInfo.InvariantCulture)} detail={OneLine(detail)}";
        }

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var builder = new StringBuilder(Math.Min(text.Length, MaxDetailChars + Ellipsis.Length));
            for (var i = 0; i < text.Length && i < MaxDetailChars; i++) builder.Append(char.IsControl(text[i]) ? ' ' : text[i]);
            if (text.Length > MaxDetailChars) builder.Append(Ellipsis);
            return builder.ToString();
        }
    }
}
