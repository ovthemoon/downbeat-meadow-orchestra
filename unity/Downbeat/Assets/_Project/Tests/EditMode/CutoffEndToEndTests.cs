using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The two-fists ending from the picture of a hand to the card, with the real pieces the app wires together (tracker pictures at 30 or 6 a second -> pose -> conducting engine, cutoff director,
    /// presence, the visit's state machine, the energy) and a fake sound. The frame logic of <see cref="PerformanceRig"/> is the one of DownbeatApp.Tick / TickNature; the real scene is in CutoffPlayModeTests.
    /// </summary>
    public class CutoffEndToEndTests
    {
        const float PalmSize = 0.15f;
        const float Row = 0.75f;

        // ---- the hands people make (x across, y down; palm 0.15 of the picture's height, the picture square) -------------------------------

        static PosedHand Open(int id, double x, double y = Row) => PoseHand(id, (float)x, (float)y, PalmSize, HandPose.Open);
        static PosedHand Fist(int id, double x, double y = Row) => PoseHand(id, (float)x, (float)y, PalmSize, HandPose.Fist);

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>The sideways distance (in palm lengths) between two hands centred at 0.5.</summary>
        static (double left, double right) Around(double apartPalms) => (0.5 - apartPalms * PalmSize / 2.0, 0.5 + apartPalms * PalmSize / 2.0);

        const double PlayUntil = 3.0;      // an open hand plays until here
        const double SpreadAt = 4.0;       // two fists, a palm and a half apart, are pulled apart from here
        const double SpreadDoneAt = 4.4;
        const double FistsUntil = 12.0;

        /// <summary>One open hand in the middle of the ground row for 3 s (a visit), then both hands as fists a palm and a third apart, pulled apart to six palms between 4.0 and 4.4 s, and held there.</summary>
        static PosedHand[] PlayThenSpread(double t)
        {
            if (t < PlayUntil) return new[] { Open(0, 0.5) };
            var (left, right) = Around(Ramp(t, 1.3, 6.0, SpreadAt, SpreadDoneAt));
            return new[] { Fist(0, left), Fist(1, right) };
        }

        static PerformanceRig PlayedFor3Seconds(int fps)
        {
            var rig = new PerformanceRig();
            rig.Run(PlayUntil, fps, PlayThenSpread);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State, "somebody plays");
            Assert.IsNotEmpty(rig.NoteTimes, "and notes are made");
            return rig;
        }

        // ---- the end ------------------------------------------------------------------------------------------------------------------

        [TestCase(30)]
        [TestCase(6)]
        public void TwoFistsSpread_EndsTheVisit_AndWritesTheCardAtOnce(int fps)
        {
            var rig = PlayedFor3Seconds(fps);
            var notesBefore = rig.NoteTimes.Count;

            rig.Run(FistsUntil - PlayUntil, fps, PlayThenSpread);

            Assert.AreEqual(1, rig.Cuts.Count, "the piece was ended once");
            Assert.That(rig.Cuts[0], Is.InRange(SpreadAt, SpreadDoneAt + 0.2 + 3.0 / fps + 0.1), "within a moment of the end of the spread");
            Assert.AreEqual(1, rig.Ended.Count, "the visit ended");
            Assert.AreEqual(rig.Cuts[0], rig.Ended[0], 1e-9, "in the same frame: SessionEnded is what makes the card, there is no waiting for the hands to leave and the energy to drop");
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.AreEqual(new[] { rig.Settings.FadeSec }, rig.Audio.FadeOutAllCalls, "the players fade out over cutoff.fadeSec, once");
            Assert.AreEqual(0f, rig.Energy.Value, 1e-6f, "the nature layers go down with the energy");
            Assert.AreEqual(1, rig.Director.CutCount);
            Assert.Greater(notesBefore, 0);
            Assert.IsEmpty(rig.NoteTimes.Where(t => t > rig.Cuts[0] - 1e-9), "not a single note after the end, however long the fists stay up");
            Assert.AreEqual(1, rig.Started.Count, "no second visit");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TwoFistsSpread_WithOneDroppedPictureMidway_StillEnds(int fps)
        {
            // a tracker at 6 pictures a second now and then misses a hand for a picture: here the second fist, in the middle of the spread
            var rig = PlayedFor3Seconds(fps);
            var dropped = Math.Round(4.2 * fps);

            rig.Run(FistsUntil - PlayUntil, fps, t => Math.Round(t * fps) == dropped ? PlayThenSpread(t).Where(h => h.Id != 1).ToArray() : PlayThenSpread(t));

            Assert.AreEqual(1, rig.Cuts.Count, "the piece was ended");
            Assert.LessOrEqual(rig.Cuts[0], SpreadDoneAt + rig.Settings.HoldSec + 4.0 / fps + 0.1, "and about as soon as without the dropped picture");
            Assert.AreEqual(1, rig.Ended.Count);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TwoFistsSpread_WithAHandMissingForAWhile_DoesNotEnd_TheGestureIsDoneAgain(int fps)
        {
            // a hand that is missing for more than the hold time (0.8 s) is gone: here from 3.2 s to 5.5 s, so the spread (4.0 to 4.4 s) is made with one fist only and the fists are back already far apart
            var rig = PlayedFor3Seconds(fps);

            rig.Run(FistsUntil - PlayUntil, fps, t => t >= 3.2 && t < 5.5 ? PlayThenSpread(t).Where(h => h.Id != 1).ToArray() : PlayThenSpread(t));

            Assert.IsEmpty(rig.Cuts, "far apart and not moving: no spread");

            // doing it again works: the fists come together and fly apart
            var from = rig.Elapsed;
            rig.Run(6.0, fps, t => { var (left, right) = Around(Ramp(t, 1.3, 6.0, from + 2.0, from + 2.4)); return new[] { Fist(0, left), Fist(1, right) }; });
            Assert.AreEqual(1, rig.Cuts.Count);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TheEnd_ComesAboutAsSoonAtSixPicturesASecond_AsAt30(int fps)
        {
            var rig = PlayedFor3Seconds(fps);

            rig.Run(FistsUntil - PlayUntil, fps, PlayThenSpread);

            var slowest = SpreadDoneAt + rig.Settings.HoldSec + 3.0 / fps;
            Assert.LessOrEqual(rig.Cuts[0], slowest + 0.1, $"{fps} pictures a second: within {slowest - SpreadAt:0.00} s of the start of the spread");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OneFistOneOpen_NeverEnds(int fps)
        {
            var rig = new PerformanceRig();

            rig.Run(30.0, fps, t =>
            {
                var (left, right) = Around(Ramp(t % 6.0, 1.3, 6.0, 4.0, 4.4));
                return new[] { Fist(0, left), Open(1, right) };
            });

            Assert.IsEmpty(rig.Cuts);
            Assert.AreEqual(0, rig.Audio.FadeOutAllCalls.Count);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State, "the open hand goes on playing");
            Assert.IsEmpty(rig.Ended);
            Assert.IsNotEmpty(rig.NoteTimes, "and only that hand plays");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OpenHandsWavingWildly_ForThirtySeconds_NeverEnds(int fps)
        {
            var rig = new PerformanceRig();

            rig.Run(30.0, fps, t =>
            {
                var (left, right) = Around(3.5 + 2.5 * Math.Sin(2.0 * Math.PI * 3.0 * t));
                var y = 0.5 + 0.3 * Math.Sin(2.0 * Math.PI * 1.7 * t);
                var hidden = (long)(t * 50) % 23 == 0;   // now and then a hand is not seen in a picture
                return hidden ? new[] { Open(0, left, y) } : new[] { Open(0, left, y), Open(1, right, y) };
            });

            Assert.IsEmpty(rig.Cuts);
            Assert.AreEqual(0, rig.Audio.FadeOutAllCalls.Count);
            Assert.IsEmpty(rig.Ended, "the piece goes on");
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void FistsTogetherWithoutSpreading_NeverCut(int fps)
        {
            var rig = PlayedFor3Seconds(fps);

            rig.Run(20.0, fps, t =>
            {
                var (left, right) = Around(1.3 + 0.2 * Math.Sin(t * 9.0));
                return new[] { Fist(0, left), Fist(1, right) };
            });

            Assert.IsEmpty(rig.Cuts, "no spread, no end");
            Assert.AreEqual(0, rig.Audio.FadeOutAllCalls.Count);
        }

        // ---- afterwards ---------------------------------------------------------------------------------------------------------------

        [TestCase(30)]
        [TestCase(6)]
        public void AfterTheEnd_FistsStillUp_NoNewVisit(int fps)
        {
            var rig = PlayedFor3Seconds(fps);

            rig.Run(FistsUntil - PlayUntil, fps, PlayThenSpread);
            rig.Run(10.0, fps, t => PlayThenSpread(FistsUntil));   // the fists stay where they are, far apart

            Assert.AreEqual(1, rig.Cuts.Count);
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.AreEqual(1, rig.Started.Count, "fists alone start nothing");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterTheEnd_OpenHandWithinLock_NoVisit_AfterLock_Visit(int fps)
        {
            var rig = PlayedFor3Seconds(fps);
            rig.RunUntil(fps, PlayThenSpread, () => rig.Cuts.Count == 1, 5.0);
            var cutAt = rig.Cuts[0];
            Func<double, PosedHand[]> openHandsBack = t => t < cutAt + 0.3 ? PlayThenSpread(t) : new[] { Open(0, 0.5), Open(1, 0.6) };

            rig.Run(cutAt + 1.3 - rig.Elapsed, fps, openHandsBack);   // 1.3 s after the end, open hands have been there for a second
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "inside the lock (1.5 s) an open hand starts nothing");
            Assert.AreEqual(1, rig.Started.Count);

            rig.Run(0.4 + 2.0 / fps, fps, openHandsBack);
            Assert.AreNotEqual(ExperienceState.Idle, rig.Experience.State, "after it the open hand starts a new visit");
            Assert.AreEqual(2, rig.Started.Count);
            Assert.GreaterOrEqual(rig.Started[1], cutAt + rig.Settings.RestartLockSec - 1e-6, "not before the lock was over");
            Assert.LessOrEqual(rig.Started[1], cutAt + rig.Settings.RestartLockSec + 2.0 / fps + 0.05, "and at once when it was");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterTheEnd_AnOpenHandStartsAnotherVisitThatPlays_AndCanBeEndedAgain(int fps)
        {
            var rig = PlayedFor3Seconds(fps);
            rig.RunUntil(fps, PlayThenSpread, () => rig.Cuts.Count == 1, 5.0);
            var second = rig.Elapsed + 3.0;   // after the lock the hands open, play for 3 s, then do it again
            Func<double, PosedHand[]> again = t =>
            {
                if (t < second) return new[] { Fist(0, 0.2), Fist(1, 0.8) };
                if (t < second + 3.0) return new[] { Open(0, 0.5) };
                var (left, right) = Around(Ramp(t, 1.3, 6.0, second + 4.0, second + 4.4));
                return new[] { Fist(0, left), Fist(1, right) };
            };

            rig.Run(second + 3.0 - rig.Elapsed, fps, again);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State, "an open hand plays again");
            rig.Run(4.0, fps, again);

            Assert.AreEqual(2, rig.Cuts.Count, "and the gesture works again: the fists were open in between");
            Assert.AreEqual(2, rig.Ended.Count);
            Assert.AreEqual(2, rig.Audio.FadeOutAllCalls.Count);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void HandsLeaving_StillEndsTheOldWay(int fps)
        {
            var rig = PlayedFor3Seconds(fps);

            rig.Run(8.0, fps, t => t < PlayUntil ? new[] { Open(0, 0.5) } : new PosedHand[0]);   // 3 s without a hand: Settling (it goes quiet when the energy has dropped, after 6 s at the least)
            Assert.AreEqual(ExperienceState.Settling, rig.Experience.State);
            Assert.IsTrue(rig.RunUntil(fps, t => new PosedHand[0], () => rig.Ended.Count == 1, 60.0), "it goes quiet by itself");

            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.AreEqual(1, rig.Ended.Count, "the visit ended by itself");
            Assert.IsEmpty(rig.Cuts, "not by the hands");
            Assert.AreEqual(0, rig.Audio.FadeOutAllCalls.Count, "the voices fade as they always did");
            Assert.Greater(rig.Ended[0], PlayUntil + 3.0 + 5.0, "after the settling time");

            // today's rule: a hand that is seen again starts a visit at once (no lock, no open hand needed: its pose is not known for a picture or two)
            rig.Run(0.1 + 2.0 / fps, fps, t => new[] { Open(0, 0.5) });
            Assert.AreNotEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.AreEqual(2, rig.Started.Count);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TwoFistsAlone_NeverStartAVisit(int fps)
        {
            var rig = new PerformanceRig();

            rig.Run(10.0, fps, t => new[] { Fist(0, 0.3), Fist(1, 0.7) });

            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "fists do not wake the meadow");
            Assert.IsEmpty(rig.Started);
            Assert.IsEmpty(rig.NoteTimes, "and a fist plays nothing");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void ASpreadOfFistsWithNobodyPlaying_ShowsTheEnd_ButHasNoVisitToEnd(int fps)
        {
            var rig = new PerformanceRig();

            rig.Run(8.0, fps, t => { var (left, right) = Around(Ramp(t, 1.3, 6.0, 4.0, 4.4)); return new[] { Fist(0, left), Fist(1, right) }; });

            Assert.AreEqual(1, rig.Cuts.Count, "the follow-along guide asks for this gesture: the word and the event come");
            Assert.IsEmpty(rig.Ended, "but there is no visit, so no card");
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
        }
    }
}
