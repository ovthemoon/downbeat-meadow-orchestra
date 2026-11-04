using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// <see cref="CutoffDirector.EndPiece"/> is the one way a piece ends: the two fists and the last bar of a song do the same things (the visit ends and makes the card, the voices fade out, the conducting
    /// engine forgets, the energy drops, <see cref="CutoffDirector.Cut"/> is raised), and asking twice for the same moment ends it once. The gesture itself is in CutoffGestureTests and CutoffEndToEndTests.
    /// </summary>
    public class CutoffDirectorTests
    {
        const int Fps = 30;
        const double PlayUntil = 3.0;
        const double SpreadAt = 4.0;
        const double SpreadDoneAt = 4.4;
        const float Palm = 0.15f;
        const float Row = 0.75f;
        const double GridOriginOffset = ConductRig.GridOrigin - 1.0;   // PerformanceRig reports times seconds into the run

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>One open hand in the middle for 3 s (a visit), then two fists a palm and a third apart that are pulled apart between 4.0 and 4.4 s.</summary>
        static PosedHand[] PlayThenSpread(double t)
        {
            if (t < PlayUntil) return new[] { PoseHand(0, 0.5f, Row, Palm, HandPose.Open) };
            var apart = (float)Ramp(t, 1.3, 6.0, SpreadAt, SpreadDoneAt) * Palm;
            return new[] { PoseHand(0, 0.5f - apart / 2f, Row, Palm, HandPose.Fist), PoseHand(1, 0.5f + apart / 2f, Row, Palm, HandPose.Fist) };
        }

        static PerformanceRig PlayedForThreeSeconds()
        {
            var rig = new PerformanceRig();
            rig.Run(PlayUntil, Fps, PlayThenSpread);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State, "somebody plays");
            return rig;
        }

        [Test]
        public void EndPiece_SongEnd_DoesWhatTheFistsDo()
        {
            var rig = PlayedForThreeSeconds();
            rig.Energy.Set(0.8f);
            var now = rig.Audio.Now;

            var ended = rig.Director.EndPiece(now, PieceEndReason.SongEnd);

            Assert.IsTrue(ended);
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "the visit is over now");
            Assert.AreEqual(1, rig.Ended.Count, "and it made the card (SessionEnded)");
            Assert.AreEqual(now - GridOriginOffset, rig.Ended[0], 1e-9);
            Assert.IsTrue(rig.Experience.AwaitsOpenHand, "the meadow waits for an open hand, as after the fists");
            Assert.AreEqual(new[] { rig.Settings.FadeSec }, rig.Audio.FadeOutAllCalls, "the voices fade out over cutoff.fadeSec, once");
            Assert.AreEqual(0f, rig.Energy.Value, 1e-6f, "the nature layers go down with the energy");
            Assert.AreEqual(1, rig.Cuts.Count, "Cut is raised once (the app shows the word of the end)");
            Assert.AreEqual(rig.Ended[0], rig.Cuts[0], 1e-9, "in the same frame");
            Assert.AreEqual(0, rig.Director.CutCount, "the hands did not do it, so it is not counted as a cut");
            Assert.AreEqual(PieceEndReason.SongEnd, rig.Director.LastEndReason);
        }

        [Test]
        public void EndPiece_TwiceInOneFrame_EndsOnce()
        {
            var rig = PlayedForThreeSeconds();
            var now = rig.Audio.Now;

            var first = rig.Director.EndPiece(now, PieceEndReason.SongEnd);
            var second = rig.Director.EndPiece(now, PieceEndReason.SongEnd);
            var third = rig.Director.EndPiece(now, PieceEndReason.Hands);

            Assert.IsTrue(first);
            Assert.IsFalse(second, "the same moment again: nothing is done");
            Assert.IsFalse(third, "whoever asks, and for whatever reason");
            Assert.AreEqual(1, rig.Audio.FadeOutAllCalls.Count, "the fade is asked for once");
            Assert.AreEqual(1, rig.Cuts.Count, "and so is Cut");
            Assert.AreEqual(1, rig.Ended.Count);
            Assert.AreEqual(0, rig.Director.CutCount, "a refused ask does not count either");
            Assert.AreEqual(PieceEndReason.SongEnd, rig.Director.LastEndReason, "the reason is the one of the end that happened");
        }

        [Test]
        public void EndPiece_AtALaterMoment_EndsAgain()
        {
            var rig = PlayedForThreeSeconds();
            var now = rig.Audio.Now;
            rig.Director.EndPiece(now, PieceEndReason.SongEnd);

            var again = rig.Director.EndPiece(now + PerformanceRig.FrameSec, PieceEndReason.Hands);

            Assert.IsTrue(again, "the guard is for one moment, not for good");
            Assert.AreEqual(2, rig.Audio.FadeOutAllCalls.Count);
            Assert.AreEqual(2, rig.Cuts.Count);
            Assert.AreEqual(1, rig.Director.CutCount, "only the one made by the hands counts");
            Assert.AreEqual(PieceEndReason.Hands, rig.Director.LastEndReason);
        }

        [Test]
        public void EndPiece_AtTimeZero_IsNotMistakenForAnEndThatAlreadyHappened()
        {
            var rig = new PerformanceRig();

            var ended = rig.Director.EndPiece(0.0, PieceEndReason.SongEnd);

            Assert.IsTrue(ended, "no piece was ended yet, whatever the clock says");
        }

        [Test]
        public void NothingEndedYet_ReasonIsHands_AndNothingIsCounted()
        {
            var rig = new PerformanceRig();

            Assert.AreEqual(0, rig.Director.CutCount);
            Assert.AreEqual(PieceEndReason.Hands, rig.Director.LastEndReason);
        }

        [Test]
        public void EndPiece_WithNoVisitGoingOn_StillCutsButMakesNoCard()
        {
            var rig = new PerformanceRig();

            var ended = rig.Director.EndPiece(rig.Audio.Now, PieceEndReason.SongEnd);

            Assert.IsTrue(ended);
            Assert.IsEmpty(rig.Ended, "no visit, no card");
            Assert.AreEqual(1, rig.Cuts.Count);
            Assert.AreEqual(1, rig.Audio.FadeOutAllCalls.Count);
        }

        [Test]
        public void Update_Fists_StillCountsAsACut()
        {
            var rig = PlayedForThreeSeconds();

            Assert.IsTrue(rig.RunUntil(Fps, PlayThenSpread, () => rig.Cuts.Count == 1, SpreadDoneAt + 2.0), "the fists end the piece");

            Assert.AreEqual(1, rig.Director.CutCount);
            Assert.AreEqual(PieceEndReason.Hands, rig.Director.LastEndReason);
            Assert.AreEqual(1, rig.Ended.Count, "the card is made at once");
            Assert.AreEqual(new[] { rig.Settings.FadeSec }, rig.Audio.FadeOutAllCalls);
            Assert.AreEqual(0f, rig.Energy.Value, 1e-6f);
            Assert.IsTrue(rig.Experience.AwaitsOpenHand);
        }
    }
}
