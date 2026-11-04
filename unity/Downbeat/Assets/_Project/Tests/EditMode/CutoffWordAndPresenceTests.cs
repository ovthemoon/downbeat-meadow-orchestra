using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>What counts as somebody being there when fists do not, and how the word that ends the piece comes and goes.</summary>
    public class CutoffWordAndPresenceTests
    {
        static HandMotionTracker TrackerWith(params PosedHand[] hands)
        {
            var settings = new HandSettingsHolder();
            var tracker = new HandMotionTracker(settings);
            for (var i = 0; i < 6; i++) tracker.Update(PosedFrame(i + 1, i * FrameMs, settings.Pose, hands));   // enough pictures for the pose to be sure
            return tracker;
        }

        [Test]
        public void CountPlayers_AFistIsNotSomebodyBeingThere_AnUnsureHandIs_AnOpenHandIsAndIsOpen()
        {
            HandPresenceMonitor.CountPlayers(TrackerWith(PoseHand(0, 0.3f, 0.5f, 0.15f, HandPose.Fist), PoseHand(1, 0.7f, 0.5f, 0.15f, HandPose.Fist)), out var fistsPlaying, out var fistsOpen);
            HandPresenceMonitor.CountPlayers(TrackerWith(PoseHand(0, 0.3f, 0.5f, 0.15f, HandPose.Fist), PoseHand(1, 0.7f, 0.5f, 0.15f, HandPose.Open)), out var mixedPlaying, out var mixedOpen);
            HandPresenceMonitor.CountPlayers(TrackerWith(PoseHand(0, 0.3f, 0.5f, 0.15f, HandPose.Unknown)), out var unsurePlaying, out var unsureOpen);
            HandPresenceMonitor.CountPlayers(TrackerWith(), out var nonePlaying, out var noneOpen);

            Assert.AreEqual((0, 0), (fistsPlaying, fistsOpen), "two fists");
            Assert.AreEqual((1, 1), (mixedPlaying, mixedOpen), "one fist, one open hand");
            Assert.AreEqual((1, 0), (unsurePlaying, unsureOpen), "a half open hand that has been there for the confirming pictures is somebody, and it is not open");
            Assert.AreEqual((0, 0), (nonePlaying, noneOpen), "nobody");
        }

        [Test]
        public void CountPlayers_AHandThatHasJustComeIn_IsNotSomebodyYet()
        {
            var settings = new HandSettingsHolder();
            var tracker = new HandMotionTracker(settings);
            var hand = PoseHand(0, 0.3f, 0.5f, 0.15f, HandPose.Open);

            tracker.Update(PosedFrame(1, 0, settings.Pose, hand));
            HandPresenceMonitor.CountPlayers(tracker, out var first, out _);
            for (var i = 1; i < settings.Pose.ConfirmFrames; i++) tracker.Update(PosedFrame(i + 1, i * FrameMs, settings.Pose, hand));
            HandPresenceMonitor.CountPlayers(tracker, out var confirmed, out var confirmedOpen);

            Assert.AreEqual(0, first, "the first picture of a hand: it plays nothing yet");
            Assert.AreEqual((1, 1), (confirmed, confirmedOpen), "after the confirming pictures it plays");
        }

        [Test]
        public void TheMonitor_SaysWhoIsThere_AndWhetherAnOpenHandIs()
        {
            var monitor = new HandPresenceMonitor(0.5, 2.0);

            var notOpen = monitor.Update(1.0, TrackerState.Running, 1, handCount: 1, openHandCount: 0, inputNote: false, out var notOpenOpen);
            var open = monitor.Update(1.1, TrackerState.Running, 2, handCount: 1, openHandCount: 1, inputNote: false, out var openOpen);
            var nobody = monitor.Update(1.2, TrackerState.Running, 3, handCount: 0, openHandCount: 0, inputNote: false, out var nobodyOpen);
            var mouse = monitor.Update(1.3, TrackerState.Running, 4, handCount: 0, openHandCount: 0, inputNote: true, out var mouseOpen);

            Assert.IsTrue(notOpen);
            Assert.IsFalse(notOpenOpen);
            Assert.IsTrue(open && openOpen);
            Assert.IsFalse(nobody || nobodyOpen, "nobody is there and the mouse has not been used");
            Assert.IsTrue(mouse && mouseOpen, "the mouse has no hand to open: its player counts as an open hand");
        }

        [Test]
        public void TheMonitor_ATrackerThatStopsDeliveringHasNoOpenHand_Either()
        {
            var monitor = new HandPresenceMonitor(0.5, 2.0);
            monitor.Update(1.0, TrackerState.Running, 1, 1, 1, false, out _);

            var has = monitor.Update(2.0, TrackerState.Running, 1, 1, 1, false, out var open);   // no new picture for a second

            Assert.IsFalse(has);
            Assert.IsFalse(open);
        }

        [Test]
        public void TheOldUpdate_IsTheSameWithTheOpenCountEqualToTheHandCount()
        {
            var monitor = new HandPresenceMonitor(0.5, 2.0);

            Assert.IsTrue(monitor.Update(1.0, TrackerState.Running, 1, 2, false));
            Assert.IsFalse(monitor.Update(1.1, TrackerState.Running, 2, 0, false));
        }

        [Test]
        public void EndWordAlpha_ComesInQuickly_StaysAndFadesOutSlowlyToNothing()
        {
            const double Duration = 1.5;

            Assert.AreEqual(0f, EndWordTiming.Alpha(0.0, Duration), "not there before it starts");
            Assert.AreEqual(0f, EndWordTiming.Alpha(-1.0, Duration));
            Assert.Greater(EndWordTiming.Alpha(Duration * EndWordTiming.FadeInShare * 0.5, Duration), 0f);
            Assert.Less(EndWordTiming.Alpha(Duration * EndWordTiming.FadeInShare * 0.5, Duration), 1f);
            Assert.AreEqual(1f, EndWordTiming.Alpha(Duration * EndWordTiming.FadeInShare, Duration), 1e-5f);
            Assert.AreEqual(1f, EndWordTiming.Alpha(Duration * 0.5, Duration), "it stays");
            Assert.AreEqual(1f, EndWordTiming.Alpha(Duration * (1.0 - EndWordTiming.FadeOutShare), Duration), 1e-5f);
            Assert.Greater(EndWordTiming.Alpha(Duration * 0.9, Duration), 0f);
            Assert.Less(EndWordTiming.Alpha(Duration * 0.9, Duration), 1f);
            Assert.AreEqual(0f, EndWordTiming.Alpha(Duration, Duration), "gone at the end");
            Assert.AreEqual(0f, EndWordTiming.Alpha(Duration + 5.0, Duration));
        }

        [Test]
        public void EndWordAlpha_NeverRisesWhileFadingOut_AndStaysInRange()
        {
            const double Duration = 1.5;
            var last = 1f;
            for (var t = Duration * (1.0 - EndWordTiming.FadeOutShare); t < Duration; t += 0.005)
            {
                var alpha = EndWordTiming.Alpha(t, Duration);
                Assert.That(alpha, Is.InRange(0f, 1f));
                Assert.LessOrEqual(alpha, last + 1e-6f);
                last = alpha;
            }
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        public void EndWordAlpha_WithNoDuration_IsNothing(double duration)
        {
            Assert.AreEqual(0f, EndWordTiming.Alpha(0.5, duration));
        }
    }
}
