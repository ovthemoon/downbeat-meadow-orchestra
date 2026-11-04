using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Recognises the log lines that say the graphics device is gone. On the Windows soak (2026-11-03, Intel UHD, D3D11) the driver timed out after 6 h 57 min and the last lines of the player log were
    /// <c>d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]</c>; after that the player never drew, logged or answered again. A lost device cannot be used any more, so the player ends itself
    /// when the log hook receives such a line and lets whatever started it start it again (<see cref="SelfExitReason.GraphicsDeviceLost"/>). Unity may write its own native lines (these d3d11 ones are likely among them)
    /// straight to Player.log without passing them to <c>Application.logMessageReceived*</c>: then the hook never sees them and the 20 s watchdog (<see cref="StallWatchdog"/>) is what ends the player. Whether the
    /// lines reach the hook is to be read from the first Windows run: <c>exit-reasons.txt</c> says <c>graphics_device_lost</c> (71) if they did, <c>main_thread_stalled</c> (72) if only the watchdog did.
    /// </summary>
    public static class DeviceLostLog
    {
        /// <summary>
        /// What a device-lost line contains, in any letter case: the DXGI error numbers DEVICE_REMOVED (the driver was reset or the GPU went away: the soak), DEVICE_HUNG and DEVICE_RESET, and their names.
        /// They are long on purpose: a pattern must not appear by chance in an ordinary line.
        /// </summary>
        public static readonly IReadOnlyList<string> Patterns = new[]
        {
            "0x887A0005",
            "0x887A0006",
            "0x887A0007",
            "DEVICE_REMOVED",
            "DEVICE_HUNG",
            "DEVICE_RESET",
        };

        public static bool IsDeviceLost(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            for (var i = 0; i < Patterns.Count; i++)
            {
                if (message.IndexOf(Patterns[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
