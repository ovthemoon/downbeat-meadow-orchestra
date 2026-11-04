using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HandRibbonFollowTests
    {
        // The shipped ribbon.follow* values, drawn at 60 frames a second.
        const float MinHz = 0.6f;
        const float Beta = 6f;
        const float SpeedHz = 1.5f;
        const float FrameSec = 1f / 60f;
        const double FrameMs = 1000.0 / 60.0;

        static FieldPoint Step(HandRibbonFollow follow, float x, float z, double captureMs, float minHz = MinHz) =>
            follow.Step(new FieldPoint(x, z), captureMs, FrameSec, minHz, Beta, SpeedHz);

        /// <summary>Normal noise (Box-Muller) with a fixed seed, so a run is always the same.</summary>
        static float Gauss(Random random, float sd) =>
            sd * (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));

        [Test]
        public void The_first_place_of_a_hand_is_drawn_as_it_is()
        {
            var place = Step(new HandRibbonFollow(), 1.5f, 4f, 1000.0);

            Assert.AreEqual(1.5f, place.X, 1e-6f);
            Assert.AreEqual(4f, place.Z, 1e-6f);
        }

        [Test]
        public void Between_tracker_frames_the_ribbon_glides_instead_of_jumping()
        {
            var follow = new HandRibbonFollow();
            Step(follow, 0f, 4f, 1000.0);

            var first = Step(follow, 0.05f, 4f, 1050.0).X; // a tracker frame 5 cm to the right (1 m/s)
            var second = Step(follow, 0.05f, 4f, 1050.0).X; // no new tracker frame in these two drawn frames
            var third = Step(follow, 0.05f, 4f, 1050.0).X;

            Assert.That(first, Is.GreaterThan(0f).And.LessThan(0.05f), "part of the way in the first drawn frame");
            Assert.That(second, Is.GreaterThan(first), "it keeps moving while the tracker is silent");
            Assert.That(third, Is.GreaterThan(second).And.LessThan(0.05f), "and never passes the hand");
        }

        [Test]
        public void A_still_hand_with_jitter_is_drawn_at_least_twice_as_steady_as_its_raw_places()
        {
            // A palm centre that jitters by 0.003 of the picture is about 2 cm across and 1 cm in depth on the field at mid height;
            // 20 tracker frames a second, drawn at 60.
            var follow = new HandRibbonFollow();
            var random = new Random(7);
            float rawX = 0f, rawZ = 4f, lastRawX = 0f, lastRawZ = 4f, lastX = 0f, lastZ = 4f;
            double rawMoves = 0.0, drawnMoves = 0.0, captureMs = 0.0;
            for (var frame = 0; frame < 4 * 60; frame++)
            {
                if (frame % 3 == 0)
                {
                    rawX = Gauss(random, 0.02f);
                    rawZ = 4f + Gauss(random, 0.01f);
                    captureMs = frame * FrameMs;
                }
                var drawn = Step(follow, rawX, rawZ, captureMs);
                if (frame >= 60) // after a second, when the filter has settled
                {
                    rawMoves += Math.Sqrt((rawX - lastRawX) * (rawX - lastRawX) + (rawZ - lastRawZ) * (rawZ - lastRawZ));
                    drawnMoves += Math.Sqrt((drawn.X - lastX) * (drawn.X - lastX) + (drawn.Z - lastZ) * (drawn.Z - lastZ));
                }
                lastRawX = rawX;
                lastRawZ = rawZ;
                lastX = drawn.X;
                lastZ = drawn.Z;
            }

            Assert.Less(drawnMoves, 0.5 * rawMoves, "the ribbon of a still hand moves less than half as much as the raw places do");
        }

        [Test]
        public void A_fast_hand_is_followed_within_a_few_centimetres()
        {
            // 4 m/s across the field (a strike) with 30 tracker frames a second.
            var follow = new HandRibbonFollow();
            const double trackerMs = 1000.0 / 30.0;
            double nextCaptureMs = 0.0, captureMs = 0.0;
            float target = 0f, worstLag = 0f;
            for (var frame = 0; frame < 60; frame++)
            {
                var nowMs = frame * FrameMs;
                if (nowMs >= nextCaptureMs - 1e-6)
                {
                    captureMs = nextCaptureMs;
                    target = (float)(4.0 * captureMs / 1000.0);
                    nextCaptureMs += trackerMs;
                }
                var x = Step(follow, target, 4f, captureMs).X;
                if (frame >= 20) worstLag = Math.Max(worstLag, target - x);
            }

            Assert.Less(worstLag, 0.05f, "never more than 5 cm behind the newest tracker place");
        }

        [Test]
        public void After_Reset_the_next_place_is_drawn_as_it_is()
        {
            var follow = new HandRibbonFollow();
            Step(follow, 0f, 4f, 1000.0);
            Step(follow, 0.1f, 4f, 1050.0);

            follow.Reset();
            var place = Step(follow, -2f, 6f, 1100.0);

            Assert.AreEqual(-2f, place.X, 1e-6f, "the other hand in this slot does not glide over from the first one");
            Assert.AreEqual(6f, place.Z, 1e-6f);
            Assert.AreEqual(0f, follow.Speed, 1e-6f);
        }

        [Test]
        public void A_followMinHz_of_0_turns_the_smoothing_off()
        {
            var follow = new HandRibbonFollow();
            Step(follow, 0f, 4f, 1000.0, minHz: 0f);

            var place = Step(follow, 0.3f, 5f, 1050.0, minHz: 0f);

            Assert.AreEqual(0.3f, place.X, 1e-6f);
            Assert.AreEqual(5f, place.Z, 1e-6f);
        }

        [Test]
        public void The_same_capture_time_again_is_not_a_new_tracker_frame()
        {
            var follow = new HandRibbonFollow();
            Step(follow, 0f, 4f, 1000.0);
            Step(follow, 0.2f, 4f, 1050.0);
            var speed = follow.Speed;

            Step(follow, 0.2f, 4f, 1050.0);
            Step(follow, 0.2f, 4f, 1050.0);

            Assert.Greater(speed, 0f);
            Assert.AreEqual(speed, follow.Speed, 1e-6f, "the speed only changes with a new tracker frame");
        }

        [Test]
        public void A_tracker_that_starts_again_moves_the_target_without_a_speed_jump()
        {
            var follow = new HandRibbonFollow();
            Step(follow, 0f, 4f, 90_000.0);
            Step(follow, 0.02f, 4f, 90_050.0);
            var speed = follow.Speed;

            var place = Step(follow, 0.5f, 4f, 30.0); // the tracker counts from 0 again

            Assert.AreEqual(speed, follow.Speed, 1e-6f, "a capture time that goes back gives no speed");
            Assert.Greater(place.X, 0.02f, "the ribbon still heads for the new place");
        }

        [Test]
        public void The_follower_reports_the_smoothed_velocity_of_a_moving_hand()
        {
            var follow = new HandRibbonFollow();
            double captureMs = 0.0;
            var x = 0f;
            Step(follow, x, 4f, captureMs);
            for (var frame = 1; frame <= 90; frame++)   // 1.5 s at 2 m/s to the right and a little away, tracker frames at 30 a second
            {
                if (frame % 2 == 0)
                {
                    captureMs += 1000.0 / 30.0;
                    x += 2f / 30f;
                }
                Step(follow, x, 4f + x * 0.25f, captureMs);
            }

            Assert.AreEqual(2f, follow.Velocity.X, 0.25f);
            Assert.AreEqual(0.5f, follow.Velocity.Z, 0.15f);
        }

        [Test]
        public void A_still_hand_with_jitter_has_a_velocity_close_to_zero_and_Reset_forgets_it()
        {
            var follow = new HandRibbonFollow();
            var random = new Random(3);
            double captureMs = 0.0;
            for (var frame = 0; frame < 120; frame++)
            {
                if (frame % 3 == 0) captureMs += 50.0;
                Step(follow, Gauss(random, 0.02f), 4f + Gauss(random, 0.01f), captureMs);
            }
            var still = follow.Velocity;

            Assert.Less(Math.Abs(still.X) + Math.Abs(still.Z), 0.25f, "the jitter back and forth cancels out in the smoothed velocity");

            follow.Reset();
            Step(follow, 1f, 4f, captureMs + 100.0);
            Assert.AreEqual(0f, follow.Velocity.X, 1e-6f);
            Assert.AreEqual(0f, follow.Velocity.Z, 1e-6f);
        }
    }
}
