using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The one rule for "this hand plays", asked by everything that needs to know: the conducting engine (whose notes sound), the cell guide (the lit cell is the instrument that plays), the presence of a visitor
    /// (a visit starts and goes on while a hand that plays is there) and the follow-along tutorial. The motion tracker decides it once per picture (<see cref="HandMotion.CanPlay"/>); the rest read the answer.
    /// A hand plays once it has been tracked for <see cref="PoseSettings.ConfirmFrames"/> pictures and is not a fist: an open hand plays, and so does a hand whose pose stays unsure (sideways, half open, a finger
    /// out of sight). A hand that has just come in does not play for those first pictures, so a hand that arrives as a fist never makes a sound.
    /// </summary>
    public static class HandPlayRule
    {
        /// <param name="pose">The pose the tracker has confirmed.</param>
        /// <param name="pictures">How many pictures the hand has been tracked for since its history began.</param>
        /// <param name="confirmFrames"><see cref="PoseSettings.ConfirmFrames"/>.</param>
        public static bool IsEligible(HandPose pose, int pictures, int confirmFrames) => pictures >= Math.Max(1, confirmFrames) && pose != HandPose.Fist;

        /// <summary>
        /// Whether the music goes on playing a hand after its newest picture. A hand the tracker lost for a moment and found again within the hold time is the same hand for the music (it plays on through the
        /// pictures that confirm its pose, so a short loss does not stop it); one that was not playing before, or a fist, is not.
        /// </summary>
        /// <param name="hand">The newest picture of the hand.</param>
        /// <param name="continuing">The hand was last seen within the hold time (or has been there all along).</param>
        /// <param name="wasPlaying">The hand was playing at the picture before.</param>
        public static bool Resolve(in HandMotion hand, bool continuing, bool wasPlaying) =>
            hand.CanPlay || (continuing && wasPlaying && hand.Pose != HandPose.Fist);
    }
}
