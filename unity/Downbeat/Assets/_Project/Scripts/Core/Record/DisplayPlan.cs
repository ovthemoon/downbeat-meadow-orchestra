using System;

namespace Downbeat.Core
{
    /// <summary>Dual: the meadow on one screen and the record on the other. SingleOverlay: one screen, the record is a strip over the lower part of the meadow.</summary>
    public enum DisplayMode { Dual, SingleOverlay }

    public static class DisplayPlan
    {
        /// <summary>
        /// <paramref name="preference"/> is <c>display.mode</c>: "auto" (anything else too), "dual" or "single". Two screens cannot be had on one: one screen is always a single overlay.
        /// On a Mac "auto" is a single overlay whatever Unity counts (this Mac reports three screens that are not monitors); "dual" asked for by name still gives two screens.
        /// </summary>
        public static DisplayMode Resolve(int displayCount, string preference, bool isMac = false)
        {
            var text = (preference ?? string.Empty).Trim();
            if (string.Equals(text, "single", StringComparison.OrdinalIgnoreCase)) return DisplayMode.SingleOverlay;
            var asked = string.Equals(text, "dual", StringComparison.OrdinalIgnoreCase);
            if (isMac && !asked) return DisplayMode.SingleOverlay;
            return displayCount >= 2 ? DisplayMode.Dual : DisplayMode.SingleOverlay;
        }
    }

    /// <summary>Which screen (index in the list of screens) shows the meadow and which the record.</summary>
    public readonly struct DisplayAssignment
    {
        public DisplayAssignment(int meadowDisplay, int recordDisplay)
        {
            MeadowDisplay = meadowDisplay;
            RecordDisplay = recordDisplay;
        }

        public int MeadowDisplay { get; }
        public int RecordDisplay { get; }

        public static DisplayAssignment For(DisplayMode mode, int meadowIndex, int recordIndex, int displayCount)
        {
            if (displayCount < 1) throw new ArgumentOutOfRangeException(nameof(displayCount), displayCount, "at least one screen");
            var meadow = Math.Max(0, Math.Min(displayCount - 1, meadowIndex));
            if (mode == DisplayMode.SingleOverlay || displayCount < 2) return new DisplayAssignment(meadow, meadow);
            var record = Math.Max(0, Math.Min(displayCount - 1, recordIndex));
            if (record == meadow) record = meadow == 0 ? 1 : 0;   // the record never shares the meadow's screen
            return new DisplayAssignment(meadow, record);
        }
    }

    /// <summary>How the player goes full screen when it is started full screen: borderless (default) or exclusive.</summary>
    public enum FullscreenChoice { Borderless, Exclusive }

    public static class FullscreenChoices
    {
        public static FullscreenChoice Parse(string value) =>
            string.Equals((value ?? string.Empty).Trim(), "exclusive", StringComparison.OrdinalIgnoreCase) ? FullscreenChoice.Exclusive : FullscreenChoice.Borderless;

        /// <summary>The kind of full screen is only chosen for a Windows player that was started full screen: a window that was asked for stays a window, and the editor and the Mac are left alone.</summary>
        public static bool ShouldApply(bool isWindowsPlayer, bool isFullScreen) => isWindowsPlayer && isFullScreen;
    }
}
