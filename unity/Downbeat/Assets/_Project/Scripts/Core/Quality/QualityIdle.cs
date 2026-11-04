namespace Downbeat.Core
{
    /// <summary>
    /// When the meadow counts as idle for the automatic quality, which may build the grass again only then. A hand in the picture or a note this frame is somebody: the frame in which a visit
    /// begins is not idle, even if the state of the visit has not caught up with it yet.
    /// </summary>
    public static class QualityIdle
    {
        public static bool Is(ExperienceState state, int handCount, bool noteThisFrame) => state == ExperienceState.Idle && handCount == 0 && !noteThisFrame;
    }
}
