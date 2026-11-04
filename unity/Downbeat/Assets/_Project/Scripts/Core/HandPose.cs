namespace Downbeat.Core
{
    /// <summary>
    /// The shape of a hand as far as the game cares: fingers stretched out, fingers curled into a fist, or neither clearly (a half open hand, or a picture the classifier cannot read).
    /// The numeric values are fixed: <c>Unknown</c> is what a default struct holds.
    /// </summary>
    public enum HandPose
    {
        Unknown = 0,
        Open = 1,
        Fist = 2,
    }

    /// <summary>
    /// Tells an open hand from a fist in one picture, from the x/y of the 21 hand points (x is scaled by the aspect, so lengths are in video heights and do not depend on the direction the hand points).
    /// For each of the four fingers (index, middle, ring, little; the thumb folds in too many ways to say anything) it measures the distance from the wrist to the fingertip in wrist-to-middle-knuckle lengths:
    /// at least <see cref="PoseSettings.OpenRatio"/> is a stretched finger, at most <see cref="PoseSettings.FistRatio"/> a curled one. Three stretched fingers make an open hand, three curled ones a fist,
    /// anything else <see cref="HandPose.Unknown"/> (a finger in between, or one whose tip cannot be read, counts for neither side). The picture of a single hand can be wrong now and then; the motion tracker
    /// takes a pose over only after a few pictures in a row agree.
    /// A fingertip that lies outside the picture (x or y beyond 0..1: a hand held high or to the side, cut off by the edge) is a guess of the tracker, nearer to the wrist than the finger really is, so the hand
    /// would read as a fist: such a hand is <see cref="HandPose.Unknown"/>, neither open nor a fist.
    /// </summary>
    public static class HandPoseClassifier
    {
        const int FingersNeeded = 3;
        /// <summary>A palm shorter than this (in video heights) is a hand squeezed into a point: there is nothing to measure the fingers against.</summary>
        const float MinPalmLength = 1e-4f;
        static readonly int[] FingerTips = { 8, 12, 16, 20 };

        /// <param name="buffer">The hand buffer (<see cref="HandBufferLayout"/>), read with the aspect it names.</param>
        /// <param name="handBase">Where the hand starts in the buffer (<see cref="HandBufferLayout.HandBase"/>).</param>
        /// <param name="settings">null reads with <see cref="PoseSettings.Default"/>.</param>
        public static HandPose Classify(double[] buffer, int handBase, PoseSettings settings) =>
            Classify(buffer, handBase, HandFrameReader.ReadAspect(buffer), settings);

        public static HandPose Classify(double[] buffer, int handBase, float aspect, PoseSettings settings)
        {
            if (buffer == null || handBase < 0 || handBase + HandBufferLayout.HandStride > buffer.Length) return HandPose.Unknown;
            if (!HandFrameReader.IsUsableLandmark(buffer, handBase, HandBufferLayout.Wrist) || !HandFrameReader.IsUsableLandmark(buffer, handBase, HandBufferLayout.MiddleBase)) return HandPose.Unknown;
            var palm = HandFrameReader.Distance(buffer, handBase, HandBufferLayout.Wrist, HandBufferLayout.MiddleBase, aspect);
            if (!(palm >= MinPalmLength)) return HandPose.Unknown;

            var thresholds = settings ?? PoseSettings.Default;
            var stretched = 0;
            var curled = 0;
            foreach (var tip in FingerTips)
            {
                if (!HandFrameReader.IsUsableLandmark(buffer, handBase, tip)) continue;
                if (IsOutsideThePicture(buffer, handBase, tip)) return HandPose.Unknown;
                var ratio = HandFrameReader.Distance(buffer, handBase, HandBufferLayout.Wrist, tip, aspect) / palm;
                if (ratio >= thresholds.OpenRatio) stretched++;
                else if (ratio <= thresholds.FistRatio) curled++;
            }

            if (stretched >= FingersNeeded) return HandPose.Open;
            return curled >= FingersNeeded ? HandPose.Fist : HandPose.Unknown;
        }

        /// <summary>
        /// The four ratios that <see cref="Classify(double[], int, float, PoseSettings)"/> decides by, for the diagnostic <c>--log-pose</c>. A default value (<see cref="FingerRatios.IsKnown"/> false) when the wrist, the middle
        /// knuckle or the palm cannot be read; a tip that cannot be read is NaN. Unlike the classification this also gives the ratio of a tip outside the picture.
        /// </summary>
        public static FingerRatios Measure(double[] buffer, int handBase, float aspect)
        {
            if (buffer == null || handBase < 0 || handBase + HandBufferLayout.HandStride > buffer.Length) return default;
            if (!HandFrameReader.IsUsableLandmark(buffer, handBase, HandBufferLayout.Wrist) || !HandFrameReader.IsUsableLandmark(buffer, handBase, HandBufferLayout.MiddleBase)) return default;
            var palm = HandFrameReader.Distance(buffer, handBase, HandBufferLayout.Wrist, HandBufferLayout.MiddleBase, aspect);
            if (!(palm >= MinPalmLength)) return default;
            return new FingerRatios(
                RatioOf(buffer, handBase, FingerTips[0], aspect, palm), RatioOf(buffer, handBase, FingerTips[1], aspect, palm),
                RatioOf(buffer, handBase, FingerTips[2], aspect, palm), RatioOf(buffer, handBase, FingerTips[3], aspect, palm));
        }

        static float RatioOf(double[] buffer, int handBase, int tip, float aspect, float palm) =>
            HandFrameReader.IsUsableLandmark(buffer, handBase, tip) ? HandFrameReader.Distance(buffer, handBase, HandBufferLayout.Wrist, tip, aspect) / palm : float.NaN;

        static bool IsOutsideThePicture(double[] buffer, int handBase, int landmark)
        {
            var x = buffer[handBase + 2 + landmark * 3];
            var y = buffer[handBase + 3 + landmark * 3];
            return x < 0.0 || x > 1.0 || y < 0.0 || y > 1.0;
        }
    }
}
