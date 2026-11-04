using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    public class HandMotionTrackerTests
    {
        static HandMotionTracker NewTracker(HandSettingsDto dto = null)
        {
            var holder = new HandSettingsHolder();
            holder.Set(dto);
            return new HandMotionTracker(holder);
        }

        static void Feed(HandMotionTracker tracker, IEnumerable<HandFrame> frames)
        {
            foreach (var frame in frames) tracker.Update(frame);
        }

        [Test]
        public void The_first_frame_of_a_hand_has_a_position_but_no_velocity()
        {
            var tracker = NewTracker();
            tracker.Update(Frame(1, 0, new Pose(0, 0.3f, 0.6f)));

            Assert.AreEqual(1, tracker.Count);
            var hand = tracker.Hand(0);
            Assert.AreEqual(0.3f, hand.X, 1e-5f);
            Assert.AreEqual(0.6f, hand.Y, 1e-5f);
            Assert.IsFalse(hand.HasVelocity);
            Assert.IsTrue(hand.IsTracked);
        }

        [Test]
        public void A_hand_that_stays_still_has_zero_velocity()
        {
            var tracker = NewTracker();
            for (var i = 0; i < 6; i++) tracker.Update(Frame(i + 1, i * FrameMs, new Pose(0, 0.5f, 0.5f)));

            var hand = tracker.Hand(0);
            Assert.IsTrue(hand.HasVelocity);
            Assert.AreEqual(0f, hand.SpeedPalms, 1e-4f);
        }

        [Test]
        public void Downward_motion_gives_the_speed_in_palm_lengths_per_second()
        {
            var tracker = NewTracker();
            var perFrame = 7f * PalmSize * (float)(FrameMs / 1000.0);
            for (var i = 0; i < 8; i++) tracker.Update(Frame(i + 1, i * FrameMs, new Pose(0, 0.5f, 0.2f + i * perFrame)));

            var hand = tracker.Hand(0);
            Assert.AreEqual(7f, hand.DownSpeedPalms, 0.05f);
            Assert.AreEqual(7f, hand.SpeedPalms, 0.05f);
            Assert.AreEqual(7f * PalmSize, hand.VelocityY, 0.01f, "video heights per second");
            Assert.AreEqual(0f, hand.VelocityX, 1e-3f);
        }

        [Test]
        public void Moving_up_is_a_negative_down_speed_and_moving_right_is_a_positive_x_velocity()
        {
            var tracker = NewTracker();
            for (var i = 0; i < 8; i++) tracker.Update(Frame(i + 1, i * FrameMs, new Pose(0, 0.2f + i * 0.02f, 0.6f - i * 0.02f)));

            var hand = tracker.Hand(0);
            Assert.Less(hand.DownSpeedPalms, 0f);
            Assert.Greater(hand.VelocityX, 0f);
        }

        [Test]
        public void Two_hands_are_listed_in_id_order_with_their_own_motion()
        {
            var tracker = NewTracker();
            var perFrame = 6f * PalmSize * (float)(FrameMs / 1000.0);
            for (var i = 0; i < 6; i++) tracker.Update(Frame(i + 1, i * FrameMs, new Pose(1, 0.8f, 0.3f + i * perFrame), new Pose(0, 0.2f, 0.5f)));

            Assert.AreEqual(2, tracker.Count);
            Assert.AreEqual(0, tracker.Hand(0).Id);
            Assert.AreEqual(1, tracker.Hand(1).Id);
            Assert.AreEqual(0f, tracker.Hand(0).DownSpeedPalms, 1e-3f);
            Assert.AreEqual(6f, tracker.Hand(1).DownSpeedPalms, 0.05f);
        }

        [Test]
        public void A_hand_missing_for_a_moment_is_still_known_but_not_tracked_and_is_dropped_after_the_gap()
        {
            var tracker = NewTracker();
            tracker.Update(Frame(1, 0, new Pose(0, 0.5f, 0.5f)));
            tracker.Update(Frame(2, FrameMs, new Pose(0, 0.5f, 0.5f)));

            tracker.Update(Frame(3, 2 * FrameMs));
            Assert.AreEqual(0, tracker.Count);
            Assert.IsTrue(tracker.TryGetById(0, out var held));
            Assert.IsFalse(held.IsTracked);

            tracker.Update(Frame(4, 2 * FrameMs + 400));
            Assert.IsFalse(tracker.TryGetById(0, out _));
        }

        [Test]
        public void A_hand_that_comes_back_after_a_long_gap_is_a_new_epoch_without_a_jump_in_velocity()
        {
            var tracker = NewTracker();
            tracker.Update(Frame(1, 0, new Pose(0, 0.5f, 0.2f)));
            tracker.Update(Frame(2, FrameMs, new Pose(0, 0.5f, 0.2f)));
            tracker.TryGetById(0, out var before);
            tracker.Update(Frame(3, 600));
            tracker.Update(Frame(4, 633, new Pose(0, 0.5f, 0.8f)));

            tracker.TryGetById(0, out var after);
            Assert.AreNotEqual(before.Epoch, after.Epoch);
            Assert.IsFalse(after.HasVelocity, "the jump from y 0.2 to 0.8 is not a movement");
        }

        [Test]
        public void Low_confidence_hands_are_ignored_and_Reset_forgets_everything()
        {
            var tracker = NewTracker();
            tracker.Update(Frame(1, 0, new Pose(0, 0.5f, 0.5f, score: 0.1f)));
            Assert.AreEqual(0, tracker.Count);

            tracker.Update(Frame(2, FrameMs, new Pose(0, 0.5f, 0.5f)));
            Assert.AreEqual(1, tracker.Count);
            tracker.Reset();
            Assert.AreEqual(0, tracker.Count);
            Assert.IsFalse(tracker.TryGetById(0, out _));
            Assert.AreEqual(0L, tracker.FrameSeq);
        }

        [Test]
        public void Settings_changes_apply_to_the_next_frame()
        {
            var holder = new HandSettingsHolder();
            var tracker = new HandMotionTracker(holder);
            tracker.Update(Frame(1, 0, new Pose(0, 0.5f, 0.5f, score: 0.6f)));
            Assert.AreEqual(1, tracker.Count);

            holder.Set(new HandSettingsDto { minScore = 0.9f });
            tracker.Update(Frame(2, FrameMs, new Pose(0, 0.5f, 0.5f, score: 0.6f)));
            Assert.AreEqual(0, tracker.Count);
        }
    }
}
