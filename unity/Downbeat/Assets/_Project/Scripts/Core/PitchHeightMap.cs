namespace Downbeat.Core
{
    /// <summary>
    /// Height (0 = bottom, 1 = top of the playing area) to pitch step. The top and bottom 15% are not used, so the extreme
    /// steps are easy to reach. Shared by mouse play and hand play.
    /// </summary>
    public static class PitchHeightMap
    {
        public const float VerticalMargin = 0.15f;
        public const float UsableHeight = 1f - 2f * VerticalMargin;

        /// <summary>Continuous position on the pitch ladder: 0 at the lowest usable height, <paramref name="pitchSteps"/> at the highest.</summary>
        public static float Position(float normalizedHeight, int pitchSteps)
        {
            var t = (normalizedHeight - VerticalMargin) / UsableHeight;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * pitchSteps;
        }

        public static int StepForHeight(float normalizedHeight, int pitchSteps)
        {
            var step = (int)Position(normalizedHeight, pitchSteps);
            return step >= pitchSteps ? pitchSteps - 1 : step;
        }
    }
}
