using System;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// The one start rule (<see cref="HandPlayRule"/>): a hand plays, wakes the meadow and starts a visit once it has been tracked for the confirming pictures and is not a fist - an open hand, and a hand whose pose
    /// stays unsure (sideways, half open). A fist never does. After the piece was ended with the fists only an open hand starts the next visit. Driven through <see cref="PerformanceRig"/> (the engine, the presence, the
    /// visit and the tutorial as the app wires them), at 30 and at 6 pictures a second.
    /// </summary>
    public class StartRuleTests
    {
        const float PalmSize = 0.15f;
        const float Row = 0.75f;
        const double FrameSec = PerformanceRig.FrameSec;

        static PosedHand Hand(int id, double x, HandPose pose, double y = Row) => PoseHand(id, (float)x, (float)y, PalmSize, pose);

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>The sideways distance (in palm lengths) between two hands centred at 0.5.</summary>
        static (double left, double right) Around(double apartPalms) => (0.5 - apartPalms * PalmSize / 2.0, 0.5 + apartPalms * PalmSize / 2.0);

        // ---- who starts ---------------------------------------------------------------------------------------------------------------

        [TestCase(30)]
        [TestCase(6)]
        public void AmbiguousHand_StartsAVisitAfterTheConfirmWindow(int fps)
        {
            const double ArrivesAfter = 1.05;
            var rig = new PerformanceRig();
            Func<double, PosedHand[]> halfOpenHand = t => t >= ArrivesAfter ? new[] { Hand(0, 0.5, HandPose.Unknown) } : new PosedHand[0];
            var firstPicture = Math.Ceiling(ArrivesAfter * fps) / fps;
            var confirmedAt = firstPicture + (PoseSettings.DefaultConfirmFrames - 1.0) / fps;

            rig.Run(firstPicture + 0.25 / fps, fps, halfOpenHand);   // the hand is in view, for fewer pictures than the pose needs
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "a hand that has just come in wakes nothing");
            Assert.IsEmpty(rig.Started);
            Assert.IsEmpty(rig.Played, "and plays nothing");

            rig.Run(confirmedAt + 2 * FrameSec - rig.Elapsed, fps, halfOpenHand);
            Assert.AreEqual(1, rig.Started.Count, "the picture that confirms it starts the visit, though the pose is still unsure");
            Assert.AreNotEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.That(rig.Started[0], Is.InRange(confirmedAt - 1e-6, confirmedAt + 2 * FrameSec), "in that very frame, not later");

            rig.Run(6.0, fps, halfOpenHand);
            Assert.AreEqual(ExperienceState.Playing, rig.Experience.State);
            Assert.IsNotEmpty(rig.Played, "and it plays: the music and the meadow are awake together");
            Assert.AreEqual(1, rig.Started.Count);
            Assert.Greater(rig.NoteTimes.First(), rig.Started[0] - 1e-9, "never a note before the visit");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void HandArrivingAsAFist_NeverSounds(int fps)
        {
            var rig = new PerformanceRig();
            // twelve arrivals, half a second each, the beat grid under them at a different phase every time: the first pictures of a hand are the ones that must stay silent
            Func<double, PosedHand[]> comingAndGoing = t => t % 1.0 < 0.5 ? new[] { Hand(0, 0.5, HandPose.Fist) } : new PosedHand[0];

            rig.Run(12.0, fps, comingAndGoing);

            Assert.IsEmpty(rig.Played, "a hand that arrives as a fist never makes a sound");
            Assert.IsEmpty(rig.Started, "and wakes nothing");
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void HandArrivingAsAFist_ThatOpens_PlaysFromTheOpeningOn(int fps)
        {
            var rig = new PerformanceRig();
            const double OpensAt = 3.0;
            Func<double, PosedHand[]> hand = t => new[] { Hand(0, 0.5, t < OpensAt ? HandPose.Fist : HandPose.Open) };

            rig.Run(OpensAt, fps, hand);
            Assert.IsEmpty(rig.Played);
            Assert.IsEmpty(rig.Started);

            rig.Run(4.0, fps, hand);
            Assert.AreEqual(1, rig.Started.Count);
            Assert.IsNotEmpty(rig.Played);
            Assert.GreaterOrEqual(rig.Started[0], OpensAt, "not before the hand opened");
            Assert.LessOrEqual(rig.Started[0], OpensAt + 2.0 * (PoseSettings.DefaultConfirmFrames + 1) / fps, "and soon after");
        }

        // ---- fists --------------------------------------------------------------------------------------------------------------------

        [TestCase(30)]
        [TestCase(6)]
        public void FistsAlone_KeepTheMeadowIdle_AndTheTutorialStepThreeChecks(int fps)
        {
            var rig = new PerformanceRig();
            Func<double, PosedHand[]> twoFists = t => new[] { Hand(0, 0.3, HandPose.Fist), Hand(1, 0.7, HandPose.Fist) };
            var idleDelay = rig.TutorialSettings.IdleDelaySec;
            var stepSec = rig.TutorialSettings.StepSec;

            rig.Run(idleDelay + 0.5 * stepSec, fps, twoFists);   // in the middle of the first step
            Assert.IsTrue(rig.Tutorial.IsVisible, "the meadow has been quiet: the tutorial shows");
            Assert.AreEqual(0, rig.Tutorial.Step);
            Assert.IsFalse(rig.Tutorial.IsChecked(0), "fists are not an open hand: the first step is not done");

            rig.Run(2 * stepSec, fps, twoFists);   // in the middle of the third step
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "two fists never wake the meadow");
            Assert.IsEmpty(rig.Started);
            Assert.IsEmpty(rig.Played);
            Assert.IsEmpty(rig.Cuts, "two fists that stay where they are are no ending");
            Assert.IsTrue(rig.Tutorial.IsVisible, "and fists never fade the tutorial");
            Assert.AreEqual(2, rig.Tutorial.Step);
            Assert.IsTrue(rig.Tutorial.IsChecked(2), "the third step asks for two fists: the tick shows");
            Assert.IsFalse(rig.Tutorial.IsChecked(0));
            Assert.IsFalse(rig.Tutorial.IsChecked(1));
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AnOpenHand_FadesTheTutorial_AndStartsTheVisitAtTheSameTime(int fps)
        {
            var rig = new PerformanceRig();
            Func<double, PosedHand[]> openHand = t => new[] { Hand(0, 0.5, HandPose.Open) };
            rig.Run(rig.TutorialSettings.IdleDelaySec + 1.5, fps, t => new PosedHand[0]);
            Assert.IsTrue(rig.Tutorial.IsVisible);

            rig.Run(0.4, fps, openHand);
            Assert.AreEqual(1, rig.Started.Count, "the visit starts without waiting for the tutorial to be gone");
            Assert.That(rig.Tutorial.Alpha, Is.GreaterThan(0f).And.LessThan(1f), "and the tutorial is on its way out");

            rig.Run(2.0, fps, openHand);
            Assert.IsNotEmpty(rig.Played, "it plays");
            Assert.IsFalse(rig.Tutorial.IsVisible, "and the tutorial is gone");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AnAmbiguousHand_StartsAVisit_SoTheTutorialLeaves(int fps)
        {
            var rig = new PerformanceRig();
            rig.Run(rig.TutorialSettings.IdleDelaySec + 1.5, fps, t => new PosedHand[0]);
            Assert.IsTrue(rig.Tutorial.IsVisible);

            rig.Run(rig.TutorialSettings.FadeOutSec + 1.0, fps, t => new[] { Hand(0, 0.5, HandPose.Unknown) });

            Assert.AreNotEqual(ExperienceState.Idle, rig.Experience.State, "the state leaves Idle");
            Assert.IsFalse(rig.Tutorial.IsVisible, "so the tutorial does not stay over the music");
        }

        // ---- after the piece was ended ------------------------------------------------------------------------------------------------

        static Func<double, PosedHand[]> OpenThenSpreadThen(Func<double, double, PosedHand[]> afterwards)
        {
            return t =>
            {
                if (t < 3.0) return new[] { Hand(0, 0.5, HandPose.Open) };
                if (t < 7.0)
                {
                    var (left, right) = Around(Ramp(t, 1.3, 6.0, 4.0, 4.4));
                    return new[] { Hand(0, left, HandPose.Fist), Hand(1, right, HandPose.Fist) };
                }
                return afterwards(t, t - 7.0);
            };
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterCutoff_AmbiguousHandDoesNotRestart_OpenHandDoes(int fps)
        {
            const double AmbiguousFrom = 7.3;   // the fists have left, a half open hand takes their place
            const double OpensAt = 15.0;        // and many seconds later it opens
            var rig = new PerformanceRig();
            var hands = OpenThenSpreadThen((t, since) =>
                since < AmbiguousFrom - 7.0 ? new[] { Hand(0, 0.5, HandPose.Fist) }
                : t < OpensAt ? new[] { Hand(0, 0.5, HandPose.Unknown) }
                : new[] { Hand(0, 0.5, HandPose.Open) });

            rig.Run(7.0, fps, hands);
            Assert.AreEqual(1, rig.Cuts.Count, "the piece was ended");
            Assert.AreEqual(1, rig.Started.Count);

            rig.Run(14.0 - rig.Elapsed, fps, hands);   // the lock (1.5 s) is long over, a hand that is not known to be open has been there for six seconds
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "an ambiguous hand does not start the next visit");
            Assert.AreEqual(1, rig.Started.Count);

            rig.Run(OpensAt + 2.0 * (PoseSettings.DefaultConfirmFrames + 1) / fps + 0.1 - rig.Elapsed, fps, hands);
            Assert.AreEqual(2, rig.Started.Count, "an open hand does");
            Assert.AreNotEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.GreaterOrEqual(rig.Started[1], OpensAt, "not before it opened");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterCutoff_FistsAloneNeverRestart(int fps)
        {
            var rig = new PerformanceRig();
            var hands = OpenThenSpreadThen((t, since) => new[] { Hand(0, 0.2, HandPose.Fist), Hand(1, 0.8, HandPose.Fist) });

            rig.Run(25.0, fps, hands);

            Assert.AreEqual(1, rig.Cuts.Count);
            Assert.AreEqual(1, rig.Started.Count);
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
            Assert.IsTrue(rig.Tutorial.IsVisible, "and the tutorial comes back after the quiet seconds, fists or not");
        }
    }
}
