namespace Downbeat.Core
{
    /// <summary>The numbers of the hand pose reading, as an immutable snapshot of <see cref="PoseDto"/>.</summary>
    public sealed class PoseSettings
    {
        public const float DefaultOpenRatio = 1.35f;   // a relaxed raised hand measures about 1.3 to 1.5; the fist stays at 1.15 and below
        public const float DefaultFistRatio = 1.15f;
        public const int DefaultConfirmFrames = 2;

        public PoseSettings() : this(DefaultOpenRatio, DefaultFistRatio, DefaultConfirmFrames) { }

        public PoseSettings(float openRatio, float fistRatio, int confirmFrames)
        {
            OpenRatio = openRatio;
            FistRatio = fistRatio;
            ConfirmFrames = confirmFrames;
        }

        /// <summary>A finger whose tip is at least this many times the wrist-to-middle-knuckle length away from the wrist is stretched out.</summary>
        public float OpenRatio { get; }
        /// <summary>A finger whose tip is at most this many times that length away from the wrist is curled.</summary>
        public float FistRatio { get; }
        /// <summary>How many pictures in a row must show a new pose before the tracker takes it over.</summary>
        public int ConfirmFrames { get; }

        public static PoseSettings Default { get; } = new PoseSettings();

        /// <summary>From the content block (null: the defaults). Values that the content validator would have refused are not checked again here.</summary>
        public static PoseSettings From(PoseDto dto) => dto == null ? Default : new PoseSettings(dto.openRatio, dto.fistRatio, dto.confirmFrames);
    }
}
