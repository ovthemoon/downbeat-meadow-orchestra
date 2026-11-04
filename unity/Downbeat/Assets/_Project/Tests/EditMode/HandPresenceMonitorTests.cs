using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HandPresenceMonitorTests
    {
        static HandPresenceMonitor Monitor() => new HandPresenceMonitor(0.5, 2.0);

        [Test]
        public void ARunningTrackerThatKeepsDeliveringFramesWithAHand_HasHands()
        {
            var monitor = Monitor();
            var present = true;
            for (var i = 0; i < 100; i++) present &= monitor.Update(i * 0.033, TrackerState.Running, i, 1, false);

            Assert.IsTrue(present);
        }

        [Test]
        public void ARunningTrackerThatSeesNobody_HasNoHands()
        {
            var monitor = Monitor();

            Assert.IsFalse(monitor.Update(1.0, TrackerState.Running, 5, 0, false));
        }

        [Test]
        public void FramesThatStop_CountAsNoHands_AfterTheStaleTime_EvenWithAHandInTheLastFrame()
        {
            var monitor = Monitor();
            monitor.Update(10.0, TrackerState.Running, 7, 1, false);

            Assert.IsTrue(monitor.Update(10.4, TrackerState.Running, 7, 1, false), "0.4 s without a new frame: still there");
            Assert.IsFalse(monitor.Update(10.6, TrackerState.Running, 7, 1, false), "0.6 s: the tracker is gone (a recording that ended, a camera that was pulled)");
        }

        [Test]
        public void ALoopingRecordingThatStartsOver_IsNotMistakenForAStall()
        {
            var monitor = Monitor();
            monitor.Update(10.0, TrackerState.Running, 480, 1, false);

            Assert.IsTrue(monitor.Update(10.3, TrackerState.Running, 1, 1, false), "the sequence number went back to 1: that is a new frame");
        }

        [Test]
        public void ATrackerThatIsNotRunning_HasNoHands()
        {
            var monitor = Monitor();
            foreach (var state in new[] { TrackerState.Off, TrackerState.Starting, TrackerState.CameraDenied, TrackerState.CameraMissing, TrackerState.CameraError, TrackerState.ModelFailed })
            {
                Assert.IsFalse(monitor.Update(1.0, state, 9, 2, false), state.ToString());
            }
        }

        [Test]
        public void ANoteFromTheMouseOrKeyboard_CountsAsPresenceForTheInputTime()
        {
            var monitor = Monitor();

            Assert.IsTrue(monitor.Update(5.0, TrackerState.Off, 0, 0, true));
            Assert.IsTrue(monitor.Update(6.9, TrackerState.Off, 0, 0, false), "1.9 s after the note");
            Assert.IsFalse(monitor.Update(7.1, TrackerState.Off, 0, 0, false), "2.1 s after the note");
        }

        [Test]
        public void Constructor_RejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HandPresenceMonitor(0.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HandPresenceMonitor(0.5, -1.0));
        }
    }
}
