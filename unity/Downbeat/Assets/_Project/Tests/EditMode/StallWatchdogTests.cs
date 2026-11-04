using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class StallWatchdogTests
    {
        const double StallSec = 20.0;
        const long MsPerSec = 1000;

        /// <summary>A clock the test moves by hand (milliseconds).</summary>
        sealed class Clock
        {
            public long Ms;
            public long Now() => Ms;
            public void Advance(double seconds) => Ms += (long)(seconds * MsPerSec);
        }

        static StallWatchdog Make(Clock clock, double stallSec = StallSec) => new StallWatchdog(stallSec, clock.Now);

        /// <summary>The watchdog thread: looks once a second for <paramref name="seconds"/> seconds; true as soon as it sees a stall.</summary>
        static bool LooksFor(StallWatchdog watchdog, Clock clock, double seconds, out double silentSec)
        {
            silentSec = 0.0;
            for (var i = 0; i < (int)(seconds / StallWatchdog.CheckIntervalSec); i++)
            {
                clock.Advance(StallWatchdog.CheckIntervalSec);
                if (watchdog.IsStalled(out silentSec)) return true;
            }
            return false;
        }

        [Test]
        public void ANewWatchdog_IsNotArmed_AndNeverSeesAStall_HoweverLongTheSilence()
        {
            var clock = new Clock();
            var watchdog = Make(clock);

            var stalled = LooksFor(watchdog, clock, 600.0, out _);

            Assert.IsFalse(watchdog.IsArmed);
            Assert.IsFalse(stalled, "a slow boot is the supervisor's business (its grace period), not the watchdog's");
        }

        [Test]
        public void Beating_WithoutBeingArmed_ArmsNothing()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            watchdog.Beat();

            Assert.IsFalse(watchdog.IsArmed);
            Assert.IsFalse(LooksFor(watchdog, clock, 100.0, out _));
        }

        [Test]
        public void Armed_ASilentMainThread_IsAStall_AtTheLimit_AndNotBefore()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            watchdog.Arm();

            var justBefore = LooksFor(watchdog, clock, StallSec - StallWatchdog.CheckIntervalSec, out _);
            var atTheLimit = LooksFor(watchdog, clock, StallWatchdog.CheckIntervalSec, out var silentSec);

            Assert.IsFalse(justBefore);
            Assert.IsTrue(atTheLimit);
            Assert.AreEqual(StallSec, silentSec, 0.001);
        }

        [Test]
        public void TheClockOfTheSilence_StartsWhenArmed_NotWhenTheWatchdogWasMade()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            clock.Advance(90.0);   // a slow boot
            watchdog.Arm();

            Assert.IsFalse(watchdog.IsStalled(out _), "armed this very moment: no silence yet");
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 2.0, out _));
        }

        [Test]
        public void EveryBeat_StartsTheSilenceAgain()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            watchdog.Arm();

            for (var frame = 0; frame < 100; frame++)
            {
                Assert.IsFalse(LooksFor(watchdog, clock, StallSec * 0.9, out _), "frame " + frame);   // a very long frame, but a frame: it ends with a beat
                watchdog.Beat();
            }
        }

        [Test]
        public void TheWorstFrameOfTheSoak_0_87Seconds_IsNowhereNearAStall()
        {
            var clock = new Clock();
            var watchdog = Make(clock, StallExitSetting.DefaultSec);
            watchdog.Arm();

            clock.Advance(0.87);
            Assert.IsFalse(watchdog.IsStalled(out var silentSec));
            Assert.Less(silentSec * 10.0, StallExitSetting.DefaultSec, "the limit is more than ten times the worst frame that was measured");
        }

        [Test]
        public void ABeatAfterAnAlmostStall_StartsAgain_AndTheNextSilenceIsNewlyCounted()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            watchdog.Arm();
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 1.0, out _));

            watchdog.Beat();

            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 1.0, out _));
            Assert.IsTrue(LooksFor(watchdog, clock, 2.0, out _));
        }

        [Test]
        public void Disarm_StopsTheWatch_AndArmStartsItFromNow()
        {
            var clock = new Clock();
            var watchdog = Make(clock);
            watchdog.Arm();
            watchdog.Disarm();

            Assert.IsFalse(watchdog.IsArmed);
            Assert.IsFalse(LooksFor(watchdog, clock, 100.0, out _));

            watchdog.Arm();

            Assert.IsTrue(watchdog.IsArmed);
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 2.0, out _));
            Assert.IsTrue(LooksFor(watchdog, clock, 3.0, out _));
        }

        [TestCase(0.0)]
        [TestCase(-5.0)]
        public void ALimitOfZeroOrLess_IsOff_ArmDoesNothing(double stallSec)
        {
            var clock = new Clock();
            var watchdog = Make(clock, stallSec);

            watchdog.Arm();

            Assert.IsFalse(watchdog.IsEnabled);
            Assert.IsFalse(watchdog.IsArmed);
            Assert.IsFalse(LooksFor(watchdog, clock, 1000.0, out _));
        }

        // ---- a suspended process (the PC slept) --------------------------------------------------------------------------------------------

        /// <summary>The PC sleeps for an hour in the middle of an ordinary run: the watchdog's next look is an hour after its last one.</summary>
        static StallWatchdog SuspendedAndWokenUp(Clock clock)
        {
            var watchdog = Make(clock);
            watchdog.Arm();
            Assert.IsFalse(LooksFor(watchdog, clock, 3.0, out _));
            watchdog.Beat();
            clock.Advance(3600.0);
            Assert.IsFalse(watchdog.IsStalled(out _), "the look that finds the wake-up says nothing about the main thread");
            return watchdog;
        }

        [Test]
        public void WhenTheWholeProcessWasPaused_ByASleepingPc_ThatIsNoStall()
        {
            var clock = new Clock();
            var watchdog = SuspendedAndWokenUp(clock);

            Assert.IsTrue(watchdog.IsWaitingAfterSuspend);
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec + 5.0, out _), "the main thread of a woken PC may need longer than the limit to run its first frame");
        }

        [Test]
        public void AfterASuspend_TheFirstBeat_EndsTheWait_AndTheOrdinaryLimitCountsFromThatBeat()
        {
            var clock = new Clock();
            var watchdog = SuspendedAndWokenUp(clock);
            Assert.IsFalse(LooksFor(watchdog, clock, 30.0, out _), "slow to wake up: 30 s without a frame, more than the limit");

            watchdog.Beat();

            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 1.0, out _));
            Assert.IsFalse(watchdog.IsWaitingAfterSuspend, "the first frame ended the wait");
            Assert.IsTrue(LooksFor(watchdog, clock, 1.0, out var silentSec));
            Assert.AreEqual(StallSec, silentSec, 0.001, "counted from the beat, not from the wake-up");
        }

        [Test]
        public void AfterASuspend_AMainThreadThatNeverRunsAFrame_IsCaught_AtTwiceTheLimit_ButNotBefore()
        {
            // a graphics driver that resets while the PC wakes up leaves the main thread frozen for good: waiting for a first beat for ever would be the very hang this class is for
            var clock = new Clock();
            var watchdog = SuspendedAndWokenUp(clock);
            var window = StallSec * StallWatchdog.ResumeWindowFactor;

            var before = LooksFor(watchdog, clock, window - 1.0, out _);
            var atTheEnd = LooksFor(watchdog, clock, 1.0, out var silentSec);

            Assert.IsFalse(before);
            Assert.IsTrue(atTheEnd);
            Assert.AreEqual(window, silentSec, 0.001, "counted from the wake-up");
            Assert.IsTrue(watchdog.IsWaitingAfterSuspend, "and it still says that the window after a suspend was the one that ran out");
        }

        [Test]
        public void ASecondSuspend_StartsTheWaitAgain()
        {
            var clock = new Clock();
            var watchdog = SuspendedAndWokenUp(clock);
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec * StallWatchdog.ResumeWindowFactor - 2.0, out _));

            clock.Advance(7200.0);   // asleep again before the window ran out
            Assert.IsFalse(watchdog.IsStalled(out _));

            Assert.IsFalse(LooksFor(watchdog, clock, StallSec * StallWatchdog.ResumeWindowFactor - 2.0, out _));
        }

        [Test]
        public void Arm_AfterASuspend_StartsAllAgain()
        {
            var clock = new Clock();
            var watchdog = SuspendedAndWokenUp(clock);

            watchdog.Arm();   // as when the app starts to quit

            Assert.IsFalse(watchdog.IsWaitingAfterSuspend);
            Assert.IsFalse(LooksFor(watchdog, clock, StallSec - 1.0, out _));
            Assert.IsTrue(LooksFor(watchdog, clock, 1.0, out _), "the ordinary limit again, not the longer window");
        }

        [Test]
        public void ThePauseLimit_IsShorterThanTheStallLimit_SoThatItMeansSomething()
        {
            Assert.Less(StallWatchdog.CheckIntervalSec, StallWatchdog.ProcessPauseSec);
            Assert.Less(StallWatchdog.ProcessPauseSec, StallExitSetting.DefaultSec);
        }

        [Test]
        public void ConstructorRejects_ANullClock_AndALimitThatIsNotANumber()
        {
            Assert.Throws<ArgumentNullException>(() => new StallWatchdog(StallSec, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StallWatchdog(double.NaN, () => 0L));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StallWatchdog(double.PositiveInfinity, () => 0L));
        }
    }
}
