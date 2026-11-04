using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// After the piece was ended with the hands, nothing plays until the next visit has started (the restart lock, then an open hand): not the notes of a hand whose pose stays unsure, not a strike.
    /// A visit that ended because the hands left is not held back.
    /// </summary>
    public class CutoffSilenceTests
    {
        const float Palm = 0.15f;
        const double PlayUntil = 3.0;
        const double SpreadAt = 4.0;
        const double SpreadDoneAt = 4.4;

        static PosedHand Hand(int id, double x, double y, HandPose pose) => PoseHand(id, (float)x, (float)y, Palm, pose);

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>An open hand plays for 3 s, then both hands are fists that are pulled apart.</summary>
        static PosedHand[] PlayThenSpread(double t)
        {
            if (t < PlayUntil) return new[] { Hand(0, 0.5, 0.75, HandPose.Open) };
            var apart = Ramp(t, 1.3, 6.0, SpreadAt, SpreadDoneAt) * Palm;
            return new[] { Hand(0, 0.5 - apart / 2.0, 0.75, HandPose.Fist), Hand(1, 0.5 + apart / 2.0, 0.75, HandPose.Fist) };
        }

        static PerformanceRig RigThatEndedThePiece(int fps, out double cutAt)
        {
            var rig = new PerformanceRig();
            Assert.IsTrue(rig.RunUntil(fps, PlayThenSpread, () => rig.Cuts.Count == 1, 10.0), "the piece was ended");
            cutAt = rig.Cuts[0];
            Assert.IsNotEmpty(rig.NoteTimes, "it was played before");
            Assert.IsTrue(rig.Experience.AwaitsOpenHand, "and now the meadow waits for an open hand");
            return rig;
        }

        static List<NoteIntent> After(PerformanceRig rig, double sec) => rig.Played.Where((n, i) => rig.NoteTimes[i] > sec).ToList();

        [TestCase(30)]
        [TestCase(6)]
        public void AfterCutoff_AmbiguousHandStaysSilent_UntilAnOpenHandStartsTheVisit(int fps)
        {
            var rig = RigThatEndedThePiece(fps, out var cutAt);
            var unsure = Hand(0, 0.17, 0.25, HandPose.Unknown);   // the fists relax: one hand stays up, half open, in the upper left cell

            rig.Run(6.0, fps, t => new[] { unsure });

            Assert.IsTrue(rig.Motion.Hand(0).CanPlay, "this hand is one that plays by the rule (its pose is confirmed and it is not a fist): without the hold it would sound");
            Assert.IsEmpty(After(rig, cutAt), "six seconds, the restart lock long over, and not a note");
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State, "and the meadow is as quiet as the sound");
            Assert.AreEqual(1, rig.Started.Count);

            var openAt = rig.Elapsed;
            rig.Run(2.0, fps, t => new[] { unsure, Hand(1, 0.5, 0.75, HandPose.Open) });

            Assert.AreEqual(2, rig.Started.Count, "an open hand starts the next visit");
            var played = After(rig, openAt - 1e-6).Select(n => n.Part).Distinct().ToList();
            CollectionAssert.Contains(played, Part.Marimba, "the open hand plays");
            CollectionAssert.Contains(played, Part.Harp, "and from then on the unsure hand plays again");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterCutoff_OpenHandAfterLock_PlaysAtOnce(int fps)
        {
            // how soon a hand that is open sounds when nothing was held back: from the picture that confirms its pose to its first note
            var fresh = new PerformanceRig();
            fresh.Run(3.0, fps, t => new[] { Hand(0, 0.5, 0.75, HandPose.Open) });
            var freshDelay = fresh.NoteTimes[0] - 1.0 / fps;

            var rig = RigThatEndedThePiece(fps, out var cutAt);
            var lockEnd = cutAt + rig.Settings.RestartLockSec;
            Func<double, PosedHand[]> openAfterAHalfSecond = t => t < cutAt + 0.5 ? PlayThenSpread(t) : new[] { Hand(0, 0.5, 0.75, HandPose.Open) };   // an open hand inside the lock

            rig.Run(lockEnd + 2.0 - rig.Elapsed, fps, openAfterAHalfSecond);

            var notes = rig.NoteTimes.Where(t => t > cutAt).ToList();
            Assert.IsNotEmpty(notes, "it plays");
            Assert.GreaterOrEqual(notes[0], lockEnd - 1e-6, "not a note inside the lock");
            Assert.LessOrEqual(notes[0], lockEnd + freshDelay + 0.1, $"and the first one comes as soon as it would for a hand that just came in ({freshDelay:0.00} s) plus a frame or two");
            Assert.AreEqual(2, rig.Started.Count);
            Assert.IsFalse(rig.Experience.AwaitsOpenHand);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void HandsLeavingEnd_IsNotGated(int fps)
        {
            var rig = new PerformanceRig();
            rig.Run(PlayUntil, fps, t => new[] { Hand(0, 0.5, 0.75, HandPose.Open) });
            Assert.IsTrue(rig.RunUntil(fps, t => new PosedHand[0], () => rig.Ended.Count == 1, 60.0), "the visit goes quiet by itself");
            Assert.IsEmpty(rig.Cuts);
            Assert.IsFalse(rig.Experience.AwaitsOpenHand, "the hands left, nobody ended it with the fists");
            var endedAt = rig.Elapsed;

            rig.Run(1.5, fps, t => new[] { Hand(0, 0.17, 0.25, HandPose.Unknown) });   // a hand with an unsure pose comes back: today's rule, no lock, no open hand needed

            Assert.IsNotEmpty(After(rig, endedAt), "it plays at once");
            Assert.AreEqual(2, rig.Started.Count, "and starts the next visit");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AfterCutoff_OpenHandThatLeavesAndComesBack_StillWaitsForTheLock(int fps)
        {
            var rig = RigThatEndedThePiece(fps, out var cutAt);

            rig.Run(0.4, fps, t => new[] { Hand(0, 0.5, 0.75, HandPose.Open) });   // inside the lock
            rig.Run(0.3, fps, t => new PosedHand[0]);                              // gone ...
            rig.Run(0.5, fps, t => new[] { Hand(0, 0.5, 0.75, HandPose.Open) });   // ... and back, still inside it

            Assert.IsEmpty(After(rig, cutAt));
            Assert.AreEqual(1, rig.Started.Count);
        }

        // ---- a fist whose pose is not sure for a picture ----------------------------------------------------------------------------

        /// <summary>One hand at (x 0.5, y 0.75) that is a fist in every picture but those named, where it reads as half open (a noisy picture).</summary>
        static Func<double, PosedHand[]> FistWithNoisyPictures(int fps, params int[] noisy) =>
            t => new[] { Hand(0, 0.5, 0.75, noisy.Contains((int)Math.Round(t * fps)) ? HandPose.Unknown : HandPose.Fist) };

        /// <summary>
        /// WHAT THE RULE ALLOWS (HandPlayRule + the tracker's ConfirmFrames = 2): a hand is a fist only after two pictures in a row say so, and plays from its second picture on unless it is a fist then. A fist that
        /// arrives as Fist, Unknown, Fist, Fist... therefore plays for two pictures (the second and the third: its pose is still Unknown, not Fist) and is a fist from the fourth on. In that moment it can make a
        /// note, and so can wake an Idle meadow; its voice then fades over conduct.crossfadeSec as that of a hand that turns into a fist during play does. After that it is silent, however long it stays. This is
        /// the most the rule allows and the test says no more than that: a note for every slot of the time the hand plays and fades (plus one), nothing later, one visit at the most.
        /// </summary>
        [TestCase(30)]
        [TestCase(6)]
        public void AFistThatArrivesWithANoisyMiddlePicture_PlaysOnlyWhatTheRuleAllows_AndIsSilentThen(int fps)
        {
            const double SlotSec = 60.0 / 96.0 / 4.0;
            var crossfade = ConductSettings.Default.CrossfadeSec;
            var rig = new PerformanceRig();

            rig.Run(10.0, fps, FistWithNoisyPictures(fps, 1));   // Fist, Unknown, Fist, Fist, Fist ...

            var playsFor = 2.0 / fps;                            // from the second picture to the fourth
            var lastNoteMayBeAt = 3.0 / fps + crossfade + SlotSec + 2.0 * PerformanceRig.FrameSec;
            Assert.AreEqual(HandPose.Fist, rig.Motion.Hand(0).Pose, "two pictures in a row settled it");
            Assert.IsFalse(rig.Motion.Hand(0).CanPlay);
            Assert.IsEmpty(rig.NoteTimes.Where(t => t > lastNoteMayBeAt), "nothing is played after its voice has faded, however long the fist stays up");
            Assert.LessOrEqual(rig.NoteTimes.Count, Math.Ceiling((playsFor + crossfade) / SlotSec) + 1, "and before that no more than the slots of the time it plays and fades");
            Assert.LessOrEqual(rig.Started.Count, 1, "it may have woken the meadow, once");
            Assert.IsEmpty(rig.Cuts);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AFistThatIsSettled_IsNotMovedByANoisyPictureAfterwards(int fps)
        {
            var rig = new PerformanceRig();

            rig.Run(10.0, fps, FistWithNoisyPictures(fps, 2, 7, 12));   // Fist, Fist, Unknown, Fist, ... one noisy picture now and then

            Assert.AreEqual(HandPose.Fist, rig.Motion.Hand(0).Pose);
            Assert.IsEmpty(rig.NoteTimes, "a fist that two pictures confirmed plays nothing, noise or not");
            Assert.IsEmpty(rig.Started);
            Assert.AreEqual(ExperienceState.Idle, rig.Experience.State);
        }

        // ---- strikes --------------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheGate_AlsoHoldsBackStrikes_AndTheyComeBackWhenItOpens()
        {
            var settings = new HandSettingsHolder();
            var motion = new HandMotionTracker(settings);
            var source = new FakeHandSource { State = TrackerState.Running };
            var range = new PlayableRange();
            range.Set(10);
            var conduct = new ConductPlayer(() => 0.0);
            conduct.Configure(TestContent.LoadShipped(), ConductSettings.StrikeName);
            var open = false;
            conduct.SetPlayGate(() => open);
            var input = new HandPlayInput(source, range, settings, motion, new HandInterpreter(), conduct);

            source.Pending.AddRange(StrikeFrames(0.5f, 0.6f, 7f, stillFrames: 8));
            input.Tick(0.0);
            var held = input.TryDequeue(out _);

            open = true;
            source.Pending.AddRange(StrikeFrames(0.5f, 0.6f, 7f, stillFrames: 8, firstSeq: 100, firstMs: 2000));
            input.Tick(1.0);
            var back = input.TryDequeue(out var intent);

            Assert.IsFalse(conduct.IsConducting, "strike mode");
            Assert.IsFalse(held, "no strike while the gate is closed");
            Assert.IsTrue(back, "and a strike after it opened");
            Assert.AreEqual(Part.Marimba, intent.Part);
        }

        [Test]
        public void TheGate_IsOpenByDefault_AndAConductPlayerWithoutOneAlwaysPlays()
        {
            var conduct = new ConductPlayer(() => 0.0);

            Assert.IsTrue(conduct.MayPlay);
            conduct.SetPlayGate(() => false);
            Assert.IsFalse(conduct.MayPlay);
            conduct.SetPlayGate(null);
            Assert.IsTrue(conduct.MayPlay);
        }

        [Test]
        public void AwaitsOpenHand_IsTrueFromEndNowUntilTheNextVisitStarts_AndOnlyThen()
        {
            var machine = new ExperienceStateMachine(new ExperienceSettings(1.0, 3.0, 0.05f, 6.0, 1.5));
            Assert.IsFalse(machine.AwaitsOpenHand);
            machine.Update(0.0, true, true, 0.5f);
            machine.EndNow(1.0);
            Assert.IsTrue(machine.AwaitsOpenHand);

            machine.Update(1.2, true, false, 0f, true);   // inside the lock
            Assert.IsTrue(machine.AwaitsOpenHand);
            machine.Update(2.6, true, false, 0f, true);   // after it, an open hand
            Assert.IsFalse(machine.AwaitsOpenHand);
            Assert.AreEqual(ExperienceState.Waking, machine.State);
        }
    }
}
