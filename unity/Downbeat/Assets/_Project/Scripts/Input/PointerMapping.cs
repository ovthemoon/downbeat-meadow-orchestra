using Downbeat.Core;

namespace Downbeat.Controls
{
    /// <summary>
    /// Pure mapping from a pointer position inside the stage view to a part and a pitch step.
    /// The six cells of the hand's place: the horizontal thirds pick the column, the part of the view above the horizon is the sky row and the part below it the ground row (harp / recorder / bell above, low string /
    /// marimba / hand drum below), the way the lit cell is drawn on the screen; height also picks the pitch (top = highest). The top and bottom 15% of the view are not used for the pitch, so the extreme steps are easy to hit.
    /// </summary>
    public static class PointerMapping
    {
        public const float VerticalMargin = PitchHeightMap.VerticalMargin;
        /// <summary>Where the horizon is in the view when nothing says (half way up): the sky row is the upper half of the view exactly. The mouse has no dead band.</summary>
        public const float DefaultHorizon = 0.5f;

        public static bool TryMap(RectF stage, float pixelX, float pixelY, int pitchSteps, out Part part, out int pitchStep) =>
            TryMap(stage, pixelX, pixelY, pitchSteps, DefaultHorizon, out part, out pitchStep);

        /// <param name="horizonVy">How far up the view the horizon is, 0 at the bottom .. 1 at the top: above it is the sky row.</param>
        public static bool TryMap(RectF stage, float pixelX, float pixelY, int pitchSteps, float horizonVy, out Part part, out int pitchStep)
        {
            part = Part.Harp;
            pitchStep = 0;
            if (stage.Width <= 0f || stage.Height <= 0f || pitchSteps < 1 || !stage.Contains(pixelX, pixelY)) return false;
            part = PartAt(stage, pixelX, pixelY, horizonVy);
            pitchStep = StepForHeight((pixelY - stage.Y) / stage.Height, pitchSteps);
            return true;
        }

        /// <summary>The cell under the pointer; a pointer outside the view is taken to the nearest cell.</summary>
        public static Part PartAt(RectF stage, float pixelX, float pixelY) => PartAt(stage, pixelX, pixelY, DefaultHorizon);

        /// <param name="horizonVy">How far up the view the horizon is, 0 at the bottom .. 1 at the top: above it is the sky row.</param>
        public static Part PartAt(RectF stage, float pixelX, float pixelY, float horizonVy)
        {
            if (stage.Width <= 0f || stage.Height <= 0f) return Part.Harp;
            var column = (int)(Clamp01((pixelX - stage.X) / stage.Width) * Parts.Columns);
            var row = Clamp01((pixelY - stage.Y) / stage.Height) >= horizonVy ? 0 : 1;   // pixels count from the bottom: above the horizon is the sky
            return Parts.At(row, column >= Parts.Columns ? Parts.Columns - 1 : column);
        }

        /// <summary>How far down the view the pointer is, 0 at the top .. 1 at the bottom (the way a hand's y is counted); NaN for a view without a size.</summary>
        public static float Y01(RectF stage, float pixelY) => stage.Height <= 0f ? float.NaN : 1f - Clamp01((pixelY - stage.Y) / stage.Height);

        /// <summary>
        /// The place a hand would have to be at to be drawn under the pointer (<see cref="HandScreenMap.HandOf"/>): the y that goes with the notes of the pointer, so that what the notes make on the screen (the ground under them, the effects in the air) is
        /// where the pointer is. 0 at the top .. 1 at the bottom of the space above the record strip; NaN for a view without a size.
        /// </summary>
        public static float HandY01(RectF stage, float pixelX, float pixelY, float overlayFraction, float rowSplit, float horizonVy)
        {
            if (stage.Width <= 0f || stage.Height <= 0f) return float.NaN;
            var viewportY = Clamp01((pixelY - stage.Y) / stage.Height);
            return HandScreenMap.HandOf(0f, viewportY, overlayFraction, rowSplit, horizonVy).y01;
        }

        /// <summary>How far across the view the pointer is, 0 at the left .. 1 at the right; NaN for a view without a size.</summary>
        public static float X01(RectF stage, float pixelX) => stage.Width <= 0f ? float.NaN : Clamp01((pixelX - stage.X) / stage.Width);

        /// <summary>normalizedY is 0 at the bottom and 1 at the top of the view.</summary>
        public static int StepForHeight(float normalizedY, int pitchSteps) => PitchHeightMap.StepForHeight(normalizedY, pitchSteps);

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
