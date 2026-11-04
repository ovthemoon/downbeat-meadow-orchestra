namespace Downbeat.Core
{
    /// <summary>
    /// The four numbers that the hand pose is made of, for one picture of one hand: how far the tip of the index, middle, ring and little finger is from the wrist, in wrist-to-middle-knuckle lengths
    /// (<see cref="HandPoseClassifier"/>). A tip that cannot be read is <see cref="float.NaN"/>. A default value (nothing was measured) says <see cref="IsKnown"/> false. Used by <c>--log-pose</c>.
    /// </summary>
    public readonly struct FingerRatios
    {
        public bool IsKnown { get; }
        public float Index { get; }
        public float Middle { get; }
        public float Ring { get; }
        public float Pinky { get; }

        public FingerRatios(float index, float middle, float ring, float pinky)
        {
            IsKnown = true;
            Index = index; Middle = middle; Ring = ring; Pinky = pinky;
        }
    }
}
