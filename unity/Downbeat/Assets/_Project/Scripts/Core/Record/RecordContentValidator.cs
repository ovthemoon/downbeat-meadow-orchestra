using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>display</c> and <c>record</c> blocks of content.json (called by <see cref="ContentValidator"/>).</summary>
    public static class RecordContentValidator
    {
        public static void Check(DisplayDto display, RecordDto record, List<string> errors)
        {
            CheckDisplay(display, errors);
            CheckRecord(record, errors);
        }

        static void CheckDisplay(DisplayDto d, List<string> errors)
        {
            if (d == null) { errors.Add("display is missing"); return; }
            var mode = (d.mode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode != "auto" && mode != "dual" && mode != "single") errors.Add("display.mode must be auto, dual or single");
            if (d.meadowIndex < 0 || d.meadowIndex > 7) errors.Add("display.meadowIndex must be 0..7");
            if (d.recordIndex < 0 || d.recordIndex > 7 || d.recordIndex == d.meadowIndex) errors.Add("display.recordIndex must be 0..7 and not the meadow's screen (display.meadowIndex)");
            var fullscreen = (d.fullscreenMode ?? string.Empty).Trim().ToLowerInvariant();
            if (fullscreen != "borderless" && fullscreen != "exclusive") errors.Add("display.fullscreenMode must be borderless or exclusive");
            if (!(d.overlayFraction >= 0.15f && d.overlayFraction <= 0.5f)) errors.Add("display.overlayFraction must be 0.15..0.5");
        }

        static void CheckRecord(RecordDto r, List<string> errors)
        {
            if (r == null) { errors.Add("record is missing"); return; }
            if (!(r.windowSec >= 2f && r.windowSec <= 60f)) errors.Add("record.windowSec must be 2..60");
            if (r.scoreCapacity < 16 || r.scoreCapacity > 4096) errors.Add("record.scoreCapacity must be 16..4096");
            if (r.cardsShown < 1 || r.cardsShown > 12) errors.Add("record.cardsShown must be 1..12");
            if (r.minNotes < 1 || r.minNotes > 100) errors.Add("record.minNotes must be 1..100");
            if (r.cardMaxMarks < 1 || r.cardMaxMarks > SessionCard.MaxStoredMarks) errors.Add($"record.cardMaxMarks must be 1..{SessionCard.MaxStoredMarks}");
            if (string.IsNullOrWhiteSpace(r.cardFolder) || r.cardFolder.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || r.cardFolder.Contains("..")) errors.Add("record.cardFolder must be a plain folder name");
            if (!(r.dateCheckSec >= 1f && r.dateCheckSec <= 3600f)) errors.Add("record.dateCheckSec must be 1..3600");
            if (r.thumbnailWidth < 64 || r.thumbnailWidth > 1920 || r.thumbnailHeight < 36 || r.thumbnailHeight > 1080) errors.Add("record.thumbnail size must be 64x36 to 1920x1080");
            if (r.idleMotes < 0 || r.idleMotes > IdleMoteField.MaxCount) errors.Add($"record.idleMotes must be 0..{IdleMoteField.MaxCount}");
            if (!(r.idleMoteSpeed >= 0.005f && r.idleMoteSpeed <= 0.05f)) errors.Add("record.idleMoteSpeed must be 0.005..0.05 (screen heights per second)");
            if (!(r.idleMoteAlpha >= 0.05f && r.idleMoteAlpha <= 1f)) errors.Add("record.idleMoteAlpha must be 0.05..1");
            if (!(r.idleMoteDimmed >= 0f && r.idleMoteDimmed <= 1f)) errors.Add("record.idleMoteDimmed must be 0..1");
            if (r.fontNames == null || r.fontNames.Length == 0) errors.Add("record.fontNames needs at least one font");
            CheckColors(r.colors, errors);
            CheckText(r.text, errors);
        }

        static void CheckColors(RecordColorsDto c, List<string> errors)
        {
            if (c == null) { errors.Add("record.colors is missing"); return; }
            foreach (var (name, value) in new[] { ("background", c.background), ("backgroundBottom", c.backgroundBottom), ("ink", c.ink), ("muted", c.muted), ("staff", c.staff), ("accent", c.accent), ("card", c.card) })
            {
                if (!ColorHex.TryParse(value, out _)) errors.Add($"record.colors.{name} must be #RRGGBB");
            }
        }

        static void CheckText(RecordTextDto t, List<string> errors)
        {
            if (t == null) { errors.Add("record.text is missing"); return; }
            if (string.IsNullOrWhiteSpace(t.title)) errors.Add("record.text.title is empty");
            if (string.IsNullOrWhiteSpace(t.hint)) errors.Add("record.text.hint is empty");
            if (t.layers == null || t.layers.Length != NatureLayers.Count) errors.Add($"record.text.layers must list {NatureLayers.Count} names");
            TryFormat(t.durationFormat, "record.text.durationFormat", errors, 1, 12);
            TryFormat(t.natureFormat, "record.text.natureFormat", errors, 3);
            TryFormat(t.dateFormat, "record.text.dateFormat", errors, 10, 29);
            TryFormat(t.timeFormat, "record.text.timeFormat", errors, "오전", 3, 42);
            if (t.meridiem == null || t.meridiem.Length != 2 || string.IsNullOrWhiteSpace(t.meridiem[0]) || string.IsNullOrWhiteSpace(t.meridiem[1])) errors.Add("record.text.meridiem must be the two words for before and after noon");
        }

        static void TryFormat(string format, string name, List<string> errors, params object[] sample)
        {
            if (string.IsNullOrEmpty(format)) { errors.Add(name + " is empty"); return; }
            try { string.Format(System.Globalization.CultureInfo.InvariantCulture, format, sample); }
            catch (FormatException) { errors.Add(name + " is not a valid format"); }
        }
    }
}
