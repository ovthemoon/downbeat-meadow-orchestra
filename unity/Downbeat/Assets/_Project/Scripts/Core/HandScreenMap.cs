namespace Downbeat.Core
{
    /// <summary>
    /// The six cells' place on the screen: where a hand at (x01, y01) of the tracker's picture is drawn in the picture of the meadow, for everything the visitor sees (the ribbon of light, the wind of the hand,
    /// the note trail, the lit cell, the effects). The tracker's picture is split in two rows at <c>cells.rowSplit</c>, and the two rows are laid on the two halves of the meadow's picture that meet at the horizon
    /// (the far edge of the playing field): the top row (the sky: harp, recorder, bell) spreads over the picture from its top down to the horizon, the bottom row (the ground: low string, marimba, hand drum) from the horizon
    /// down to the top of the record strip. Each row is mapped in a straight line, so a hand that goes up is drawn higher, whatever the row, and the two rows meet exactly at the horizon.
    /// </summary>
    public static class HandScreenMap
    {
        const float SmallestRowSplit = 0.01f;     // a row split of 0 or 1 would leave one of the rows without a height
        const float SmallestBand = 0.01f;         // the share of the picture that each of the two bands keeps at least (the horizon cannot reach the strip or the top)
        const float LargestStrip = 1f - 2f * SmallestBand;   // a strip taller than this would leave no room for the horizon between its top and the top of the picture: the lowest horizon (strip + band) would pass the highest (1 - band)

        /// <summary>
        /// The viewport point (both 0..1, y counted from the bottom as a Unity viewport is) where a hand at <paramref name="x01"/> (0 left .. 1 right, as the player sees it) and <paramref name="y01"/> (0 top .. 1 bottom)
        /// is drawn. <paramref name="overlayFraction"/> is the share of the picture that the record strip covers from the bottom (0 with two screens; a strip taller than 0.98 is taken as 0.98), <paramref name="rowSplit"/> the hand height (0 top .. 1 bottom) where the
        /// rows meet, <paramref name="horizonVy"/> the viewport y of the horizon (NaN: half way between the strip and the top, which draws the two rows as one straight line when <paramref name="rowSplit"/> is 0.5).
        /// </summary>
        public static (float vx, float vy) Viewport(float x01, float y01, float overlayFraction, float rowSplit, float horizonVy)
        {
            Resolve(overlayFraction, rowSplit, horizonVy, out var strip, out var split, out var horizon);
            var y = Clamp01(y01);
            var vy = y <= split
                ? 1f - y / split * (1f - horizon)
                : horizon - (y - split) / (1f - split) * (horizon - strip);
            return (Clamp01(x01), vy);
        }

        /// <summary>The other way: the hand place (x 0 left .. 1 right, y 0 top .. 1 bottom) that is drawn at the viewport point (<paramref name="vx"/>, <paramref name="vy"/>); a point outside the picture or over the strip is put on the nearest edge.</summary>
        public static (float x01, float y01) HandOf(float vx, float vy, float overlayFraction, float rowSplit, float horizonVy)
        {
            Resolve(overlayFraction, rowSplit, horizonVy, out var strip, out var split, out var horizon);
            var v = vy < strip ? strip : (vy > 1f ? 1f : vy);
            var y = v >= horizon
                ? (1f - v) / (1f - horizon) * split
                : split + (horizon - v) / (horizon - strip) * (1f - split);
            return (Clamp01(vx), Clamp01(y));
        }

        /// <summary>The viewport y of the horizon that is used for <paramref name="horizonVy"/>: itself kept inside the picture above the strip, or half way between the strip and the top when it is not a number.</summary>
        public static float HorizonOf(float horizonVy, float overlayFraction)
        {
            Resolve(overlayFraction, 0.5f, horizonVy, out _, out _, out var horizon);
            return horizon;
        }

        static void Resolve(float overlayFraction, float rowSplit, float horizonVy, out float strip, out float split, out float horizon)
        {
            strip = Clamp01(overlayFraction);
            if (strip > LargestStrip) strip = LargestStrip;
            split = rowSplit < SmallestRowSplit ? SmallestRowSplit : (rowSplit > 1f - SmallestRowSplit ? 1f - SmallestRowSplit : rowSplit);
            var lowest = strip + SmallestBand;
            var highest = 1f - SmallestBand;
            if (float.IsNaN(horizonVy)) horizon = (1f + strip) * 0.5f;
            else horizon = horizonVy < lowest ? lowest : (horizonVy > highest ? highest : horizonVy);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
