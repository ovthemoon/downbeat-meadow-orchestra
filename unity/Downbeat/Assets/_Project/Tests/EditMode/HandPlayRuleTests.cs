using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The one rule for whether a hand plays: tracked for the confirming pictures and not a fist; and how the music keeps a hand that the tracker lost for a moment.</summary>
    public class HandPlayRuleTests
    {
        static HandMotion Hand(HandPose pose, bool? canPlay) =>
            new HandMotion(0, true, 1f, 0.5f, 0.5f, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, pose, canPlay);

        [TestCase(HandPose.Open, 1, 2, false)]
        [TestCase(HandPose.Open, 2, 2, true)]
        [TestCase(HandPose.Unknown, 1, 2, false)]
        [TestCase(HandPose.Unknown, 2, 2, true)]
        [TestCase(HandPose.Unknown, 500, 2, true)]
        [TestCase(HandPose.Fist, 1, 2, false)]
        [TestCase(HandPose.Fist, 2, 2, false)]
        [TestCase(HandPose.Fist, 500, 2, false)]
        [TestCase(HandPose.Open, 1, 1, true)]
        [TestCase(HandPose.Open, 2, 3, false)]
        [TestCase(HandPose.Open, 3, 3, true)]
        [TestCase(HandPose.Open, 1, 0, true)]
        public void IsEligible_NeedsConfirmFramesPictures_AndNoFist(HandPose pose, int pictures, int confirmFrames, bool expected)
        {
            Assert.AreEqual(expected, HandPlayRule.IsEligible(pose, pictures, confirmFrames));
        }

        [Test]
        public void AHandMadeByHand_PlaysUnlessItIsAFist()
        {
            Assert.IsTrue(Hand(HandPose.Open, null).CanPlay);
            Assert.IsTrue(Hand(HandPose.Unknown, null).CanPlay, "a test or a capture that says nothing about it: an unsure hand plays");
            Assert.IsFalse(Hand(HandPose.Fist, null).CanPlay);
            Assert.IsFalse(Hand(HandPose.Open, false).CanPlay, "and one that is told not to, does not");
        }

        [Test]
        public void Resolve_ATrackerThatSaysPlay_Plays()
        {
            Assert.IsTrue(HandPlayRule.Resolve(Hand(HandPose.Open, true), continuing: false, wasPlaying: false));
        }

        [Test]
        public void Resolve_AHandThatJustCameIn_DoesNotPlay_ButOneThatWasPlayingAndCameBackWithinTheHoldTimeDoes()
        {
            var fresh = Hand(HandPose.Unknown, false);

            Assert.IsFalse(HandPlayRule.Resolve(fresh, continuing: false, wasPlaying: false), "a new hand");
            Assert.IsFalse(HandPlayRule.Resolve(fresh, continuing: false, wasPlaying: true), "it was away for longer than the hold time: a new hand, whatever the old one did");
            Assert.IsFalse(HandPlayRule.Resolve(fresh, continuing: true, wasPlaying: false), "the same hand, but it was not playing (a fist before the gap)");
            Assert.IsTrue(HandPlayRule.Resolve(fresh, continuing: true, wasPlaying: true), "the same hand, still playing: a gap of the tracker does not stop the music");
        }

        [Test]
        public void Resolve_AFistNeverPlays_HoweverTheHandWasBefore()
        {
            Assert.IsFalse(HandPlayRule.Resolve(Hand(HandPose.Fist, false), continuing: true, wasPlaying: true));
        }
    }
}
