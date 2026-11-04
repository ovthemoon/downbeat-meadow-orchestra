using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ExperienceStateMachineTests
    {
        static ExperienceSettings Settings() => new ExperienceSettings(1.0, 3.0, 0.05f, 6.0);

        sealed class Run
        {
            public readonly ExperienceStateMachine Machine = new ExperienceStateMachine(Settings());
            public readonly List<double> Started = new List<double>();
            public readonly List<double> Ended = new List<double>();
            public double Now;

            public Run()
            {
                Machine.SessionStarted += t => Started.Add(t);
                Machine.SessionEnded += t => Ended.Add(t);
            }

            public void Step(double dt, bool hands, bool note = false, float energy = 0f)
            {
                Now += dt;
                Machine.Update(Now, hands, note, energy);
            }

            public void Hold(double seconds, bool hands, float energy = 0f)
            {
                var end = Now + seconds;
                while (Now < end - 1e-9) Step(0.1, hands, false, energy);
            }

            /// <summary>An update that says whether the hand that is there is open (a fist is a hand that is there but not an open one).</summary>
            public void Look(double dt, bool hands, bool openHand, float energy = 0f)
            {
                Now += dt;
                Machine.Update(Now, hands, false, energy, openHand);
            }

            /// <summary>Looks every 0.1 s for this long.</summary>
            public void Keep(double seconds, bool hands, bool openHand, float energy = 0f)
            {
                var end = Now + seconds;
                while (Now < end - 1e-9) Look(0.1, hands, openHand, energy);
            }

            public void EndNow() => Machine.EndNow(Now);

            public void StartPlaying()
            {
                Step(0.1, true, note: true);
                Step(0.1, true, note: true);
            }
        }

        [Test]
        public void State_IdleToWakingOnHand()
        {
            var run = new Run();
            run.Step(0.1, false);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);

            run.Step(0.1, true);

            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
            Assert.AreEqual(1, run.Started.Count);
            Assert.AreEqual(0.2, run.Started[0], 1e-9, "the session starts when the hand comes in");
        }

        [Test]
        public void State_WakingToPlayingOnTheFirstNote_OrAfterOneSecond()
        {
            var byNote = new Run();
            byNote.Step(0.1, true);
            byNote.Step(0.1, true, note: true);
            Assert.AreEqual(ExperienceState.Playing, byNote.Machine.State);

            var byTime = new Run();
            byTime.Step(0.1, true);
            byTime.Hold(0.8, true);
            Assert.AreEqual(ExperienceState.Waking, byTime.Machine.State, "not yet after 0.9 s");
            byTime.Hold(0.3, true);
            Assert.AreEqual(ExperienceState.Playing, byTime.Machine.State, "after 1 s");
        }

        [Test]
        public void State_SettlesAfter3sWithoutHands()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Step(0.1, true, note: true);
            Assert.AreEqual(ExperienceState.Playing, run.Machine.State);

            run.Hold(2.8, false, 0.5f);
            Assert.AreEqual(ExperienceState.Playing, run.Machine.State, "2.8 s without hands: still playing");
            run.Hold(0.4, false, 0.5f);

            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);
        }

        [Test]
        public void State_ShortLossOfTheHand_DoesNotSettle()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Hold(2.0, false, 0.5f);
            run.Hold(1.0, true, 0.5f);
            run.Hold(2.0, false, 0.5f);

            Assert.AreEqual(ExperienceState.Playing, run.Machine.State, "the count of hand-less time starts again when the hand comes back");
        }

        [Test]
        public void State_SettlingToPlayingWhenTheHandComesBack_WithoutANewSession()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Hold(3.5, false, 0.5f);
            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);

            run.Step(0.1, true, energy: 0.4f);

            Assert.AreEqual(ExperienceState.Playing, run.Machine.State);
            Assert.AreEqual(1, run.Started.Count, "the same visit goes on");
            Assert.AreEqual(0, run.Ended.Count);
        }

        [Test]
        public void State_EndsSessionWhenEnergyLowAfter6s()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Hold(3.5, false, 0.5f);
            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);
            var settlingStart = run.Machine.StateSince;

            run.Hold(5.0, false, 0.01f); // low energy but Settling has not lasted 6 s yet
            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);
            run.Hold(1.2, false, 0.01f);

            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
            Assert.AreEqual(1, run.Ended.Count);
            Assert.AreEqual(settlingStart + 6.0, run.Ended[0], 0.15, "ended when 6 s of Settling had passed");
        }

        [Test]
        public void State_StaysSettlingWhileTheEnergyIsStillHigh_EvenAfter6s()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Hold(3.5, false, 0.8f);

            run.Hold(20.0, false, 0.3f);

            Assert.AreEqual(ExperienceState.Settling, run.Machine.State, "the energy has not faded yet: the meadow is still alive");
            run.Hold(1.0, false, 0.02f);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
        }

        [Test]
        public void State_TrackerLossCountsAsNoHands()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);

            // the tracker stops (nothing is running, no hand is counted): the caller derives "no hands" from that
            var hands = ExperiencePresence.HasHands(trackerRunning: false, handCount: 2, inputRecent: false);
            Assert.IsFalse(hands, "a tracker that is not running has no hands, whatever its last frame said");
            run.Hold(3.2, hands, 0.6f);

            Assert.AreEqual(ExperienceState.Settling, run.Machine.State, "within 3 s of the tracker timeout (2 s) = within 5 s of the loss");
        }

        [Test]
        public void Presence_NeedsARunningTrackerWithAHand_OrRecentMouseOrKeyboardInput()
        {
            Assert.IsTrue(ExperiencePresence.HasHands(true, 1, false));
            Assert.IsTrue(ExperiencePresence.HasHands(true, 2, false));
            Assert.IsFalse(ExperiencePresence.HasHands(true, 0, false), "the tracker runs but sees nobody");
            Assert.IsFalse(ExperiencePresence.HasHands(false, 1, false), "the tracker was lost");
            Assert.IsTrue(ExperiencePresence.HasHands(false, 0, true), "the mouse and the keyboard have no hand: a recent note of theirs counts");
        }

        [Test]
        public void State_WakingWithoutHandsForTooLong_GoesToSettlingAndEnds()
        {
            var run = new Run();
            run.Step(0.1, true);
            run.Hold(3.3, false, 0f);
            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);

            run.Hold(6.2, false, 0f);

            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
            Assert.AreEqual(1, run.Ended.Count);
        }

        [Test]
        public void State_CanHostASecondVisitAfterTheFirst()
        {
            var run = new Run();
            run.Step(0.1, true, note: true);
            run.Hold(3.5, false, 0.5f);
            run.Hold(7.0, false, 0f);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);

            run.Step(0.1, true);

            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
            Assert.AreEqual(2, run.Started.Count);
        }

        [Test]
        public void EndNow_RaisesSessionEndedAtOnce()
        {
            var run = new Run();
            run.StartPlaying();
            run.Hold(2.0, true, 0.8f);
            Assert.AreEqual(ExperienceState.Playing, run.Machine.State);

            run.EndNow();

            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "no waiting for the hands to leave, for the settling time or for the energy to drop");
            Assert.AreEqual(1, run.Ended.Count);
            Assert.AreEqual(run.Now, run.Ended[0], 1e-9, "the card is made for this moment");
            Assert.AreEqual(run.Now, run.Machine.StateSince, 1e-9);
        }

        [Test]
        public void EndNow_EndsAWakingAndASettlingVisitToo()
        {
            var waking = new Run();
            waking.Step(0.1, true);
            Assert.AreEqual(ExperienceState.Waking, waking.Machine.State);
            waking.EndNow();
            Assert.AreEqual(ExperienceState.Idle, waking.Machine.State);
            Assert.AreEqual(1, waking.Ended.Count);

            var settling = new Run();
            settling.StartPlaying();
            settling.Hold(3.5, false, 0.5f);
            Assert.AreEqual(ExperienceState.Settling, settling.Machine.State);
            settling.EndNow();
            Assert.AreEqual(ExperienceState.Idle, settling.Machine.State);
            Assert.AreEqual(1, settling.Ended.Count);
        }

        [Test]
        public void EndNow_WithNoVisit_RaisesNothing_ButTheLockHolds()
        {
            var run = new Run();
            run.Step(0.1, false);

            run.EndNow();

            Assert.AreEqual(0, run.Ended.Count, "no visit, nothing ended");
            run.Look(0.5, true, openHand: true);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "and an open hand does not start one inside the lock");
            run.Keep(1.5, true, openHand: true);
            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
        }

        [Test]
        public void AfterEndNow_NoVisitDuringLock_ThenOnlyWithAnOpenHand()
        {
            var run = new Run();
            run.StartPlaying();
            run.EndNow();

            run.Keep(1.4, true, openHand: true);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "inside the lock (1.5 s) even an open hand starts nothing");
            Assert.AreEqual(1, run.Started.Count);

            run.Keep(6.0, true, openHand: false);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "after the lock a hand that is not known to be open (a fist that is relaxing, a sideways hand) starts nothing either");
            Assert.AreEqual(1, run.Started.Count);

            run.Look(0.1, true, openHand: true);
            Assert.AreEqual(ExperienceState.Waking, run.Machine.State, "an open hand starts a new visit");
            Assert.AreEqual(2, run.Started.Count);
        }

        [Test]
        public void AfterEndNow_FistsThatLeftAndCameBack_StillNeedAnOpenHand()
        {
            var run = new Run();
            run.StartPlaying();
            run.EndNow();
            run.Keep(10.0, false, openHand: false);

            run.Keep(3.0, true, openHand: false);

            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "the rule stays until an open hand has started a visit");
            run.Look(0.1, true, openHand: true);
            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
        }

        [Test]
        public void AfterEndNow_TheFourArgumentUpdate_CountsEveryHandAsOpen()
        {
            var run = new Run();
            run.StartPlaying();
            run.EndNow();
            run.Hold(1.4, true);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "the lock holds for a caller that cannot tell hands apart as well");

            run.Step(0.2, true);

            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
        }

        [Test]
        public void HandsLeaving_StillEndsTheOldWay()
        {
            var run = new Run();
            run.StartPlaying();
            run.Hold(3.5, false, 0.5f);
            Assert.AreEqual(ExperienceState.Settling, run.Machine.State);
            run.Hold(6.2, false, 0f);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
            Assert.AreEqual(1, run.Ended.Count);

            run.Look(0.1, true, openHand: true);

            Assert.AreEqual(ExperienceState.Waking, run.Machine.State, "a visit that ended by the hands leaving starts when an open hand is seen again, at once: there is no lock");
            Assert.AreEqual(2, run.Started.Count);
        }

        [Test]
        public void ARestartWithAnOpenHand_EndsTheRule_ForTheNextVisit()
        {
            var run = new Run();
            run.StartPlaying();
            run.EndNow();
            run.Keep(2.0, true, openHand: false);
            run.Look(0.1, true, openHand: true);
            Assert.AreEqual(ExperienceState.Waking, run.Machine.State);
            run.Step(0.1, true, note: true);
            run.Hold(3.5, false, 0.5f);
            run.Hold(7.0, false, 0f);
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State, "this one ended the old way");

            run.Look(0.1, true, openHand: true);

            Assert.AreEqual(ExperienceState.Waking, run.Machine.State, "so the next one starts at once again, with no lock");
        }

        [Test]
        public void AHandThatPlays_StartsAVisit_WhetherOrNotItIsKnownToBeOpen()
        {
            var run = new Run();

            run.Keep(2.0, false, openHand: false);   // fists, or hands that have just come in: nobody who plays
            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
            Assert.IsEmpty(run.Started);

            run.Look(0.1, true, openHand: false);   // a hand that plays and whose pose stays unsure (sideways, half open)
            Assert.AreEqual(ExperienceState.Waking, run.Machine.State, "it plays, so the meadow wakes: music never plays over an Idle meadow");
            Assert.AreEqual(1, run.Started.Count);
            run.Keep(10.0, true, openHand: false);   // and the visit goes on, it does not settle
            Assert.AreEqual(ExperienceState.Playing, run.Machine.State);
            Assert.IsEmpty(run.Ended);
        }

        [Test]
        public void AfterEndNow_AHandWhoseOpenessIsNotKnown_NeverStartsAVisit_NoMatterHowLong()
        {
            var run = new Run();
            run.StartPlaying();
            run.EndNow();

            run.Keep(20.0, true, openHand: false);

            Assert.AreEqual(ExperienceState.Idle, run.Machine.State);
            Assert.AreEqual(1, run.Started.Count);
        }

        [Test]
        public void TheFourArgumentUpdate_StillStartsAVisitWithANote_ThatNobodyPlaysByHand()
        {
            var run = new Run();

            run.Step(0.1, false, note: true);   // the keyboard

            Assert.AreEqual(1, run.Started.Count);
            Assert.AreEqual(ExperienceState.Playing, run.Machine.State, "a visit with a note in its first update is playing at once");
        }

        [Test]
        public void TheLockIsASettingOfTheMachine()
        {
            var longer = new ExperienceStateMachine(Settings().WithRestartLockSec(3.0));
            longer.Update(0.0, true, true, 0.5f);
            longer.EndNow(1.0);
            longer.Update(3.9, true, false, 0f, true);
            Assert.AreEqual(ExperienceState.Idle, longer.State, "3.0 s of lock: not at 2.9 s after the end");
            longer.Update(4.1, true, false, 0f, true);
            Assert.AreEqual(ExperienceState.Waking, longer.State);

            var none = new ExperienceStateMachine(Settings().WithRestartLockSec(0.0));
            none.Update(0.0, true, true, 0.5f);
            none.EndNow(1.0);
            none.Update(1.0, true, false, 0f, false);
            Assert.AreEqual(ExperienceState.Idle, none.State, "no lock, but fists are not an open hand");
            none.Update(1.0, true, false, 0f, true);
            Assert.AreEqual(ExperienceState.Waking, none.State, "no lock: an open hand starts at once");
        }

        [Test]
        public void Settings_TheRestartLockDefaultsToOneAndAHalfSeconds_AndMustNotBeNegative()
        {
            Assert.AreEqual(1.5, Settings().RestartLockSec, 1e-9);
            Assert.AreEqual(2.5, Settings().WithRestartLockSec(2.5).RestartLockSec, 1e-9);
            Assert.AreEqual(6.0, Settings().WithRestartLockSec(2.5).SettlingMinSec, 1e-9, "the rest is kept");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ExperienceSettings(1.0, 3.0, 0.05f, 6.0, -0.1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Settings().WithRestartLockSec(double.NaN));
        }

        [Test]
        public void Settings_RejectNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ExperienceSettings(-1.0, 3.0, 0.05f, 6.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ExperienceSettings(1.0, 0.0, 0.05f, 6.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ExperienceSettings(1.0, 3.0, 1.5f, 6.0));
        }
    }
}
