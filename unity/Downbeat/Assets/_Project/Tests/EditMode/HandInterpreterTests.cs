using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    public class HandInterpreterTests
    {
        const float MidX = 0.5f;
        const float MidY = 0.5f;

        [Test]
        public void A_hand_that_stays_still_plays_nothing()
        {
            var frames = new List<HandFrame>();
            for (var i = 0; i < 60; i++) frames.Add(Frame(i + 1, i * FrameMs, new Pose(0, MidX, MidY)));

            Assert.AreEqual(0, Run(NewInterpreter(), frames).Count);
        }

        [Test]
        public void A_fist_does_not_strike_but_the_same_fall_of_an_open_hand_does()
        {
            Assert.AreEqual(1, Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 7f, stillFrames: 8)).Count, "an open hand");
            Assert.AreEqual(0, Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 7f, stillFrames: 8, shape: HandPose.Fist)).Count, "a fist (its pose is known after two pictures, before the fall)");
        }

        [Test]
        public void A_slow_downward_drift_is_not_a_strike()
        {
            Assert.AreEqual(0, Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 1.5f, moveFrames: 20)).Count);
        }

        [Test]
        public void A_fast_downward_strike_plays_exactly_one_note_even_if_the_motion_keeps_going()
        {
            Assert.AreEqual(1, Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 7f, moveFrames: 8)).Count);
        }

        [Test]
        public void Upward_motion_never_triggers_but_the_next_downward_strike_does()
        {
            var interpreter = NewInterpreter();
            Assert.AreEqual(0, Run(interpreter, StrikeFrames(MidX, 0.5f, -7f)).Count);

            Assert.AreEqual(1, Run(interpreter, StrikeFrames(MidX, 0.3f, 7f, firstSeq: 100, firstMs: 1000)).Count);
        }

        [Test]
        public void After_a_strike_the_hand_must_slow_down_before_the_next_one_and_then_plays_again()
        {
            var interpreter = NewInterpreter();
            var frames = StrikeFrames(MidX, 0.25f, 7f, tailFrames: 8);
            frames.AddRange(StrikeFrames(MidX, 0.25f, 7f, firstSeq: 100, firstMs: 1000));

            Assert.AreEqual(2, Run(interpreter, frames).Count);
        }

        [Test]
        public void Without_slowing_down_the_hand_cannot_strike_twice()
        {
            // one long continuous fast fall: it triggers once and never re-arms
            Assert.AreEqual(1, Run(NewInterpreter(), StrikeFrames(MidX, 0.1f, 8f, moveFrames: 20, tailFrames: 0)).Count);
        }

        [TestCase(0.10f, 0.20f, Part.Harp)]
        [TestCase(0.50f, 0.20f, Part.Recorder)]
        [TestCase(0.90f, 0.20f, Part.Bell)]
        [TestCase(0.10f, 0.70f, Part.LowString)]
        [TestCase(0.50f, 0.70f, Part.Marimba)]
        [TestCase(0.90f, 0.70f, Part.HandDrum)]
        public void The_thirds_across_and_the_two_halves_up_and_down_pick_the_part(float x, float y, Part expected)
        {
            var strikes = Run(NewInterpreter(), StrikeFrames(x, y, 7f));

            Assert.AreEqual(1, strikes.Count);
            Assert.AreEqual(expected, strikes[0].Part);
            Assert.AreEqual(x, strikes[0].X01, 1e-3f);
            Assert.That(strikes[0].Y01, Is.EqualTo(y).Within(0.1f), "the strike knows where up and down the hand was");
        }

        [Test]
        public void A_higher_hand_gives_a_higher_pitch_step_and_the_extremes_are_reachable()
        {
            var high = Run(NewInterpreter(), StrikeFrames(MidX, 0.10f, 7f))[0].PitchStep;
            var middle = Run(NewInterpreter(), StrikeFrames(MidX, 0.45f, 7f))[0].PitchStep;
            var low = Run(NewInterpreter(), StrikeFrames(MidX, 0.80f, 7f))[0].PitchStep;

            Assert.AreEqual(9, high);
            Assert.Less(low, middle);
            Assert.Less(middle, high);
            Assert.AreEqual(0, low);
        }

        [Test]
        public void A_harder_strike_is_stronger_and_a_very_hard_one_is_full_strength()
        {
            var gentle = Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 4.5f))[0].Velocity;
            var medium = Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 7f))[0].Velocity;
            var hard = Run(NewInterpreter(), StrikeFrames(MidX, 0.3f, 20f))[0].Velocity;

            Assert.Less(gentle, medium);
            Assert.Less(medium, hard);
            Assert.AreEqual(VelocityMapper.LoudVelocity, hard, 1e-5f);
            Assert.GreaterOrEqual(gentle, new HandSettingsDto().minVelocity);
        }

        [Test]
        public void Strength_comes_from_the_peak_speed_not_from_the_first_frame_over_the_threshold()
        {
            var frames = AcceleratingStrike(new[] { 0f, 0f, 0f, 0f, 3.5f, 6f, 10f, 14f, 14f, 2f, 0f, 0f, 0f });

            var strike = Run(NewInterpreter(), frames)[0];

            Assert.Greater(strike.PeakSpeed, 8f);
            Assert.Greater(strike.Velocity, 0.6f);
            Assert.LessOrEqual(strike.PeakWaitMs, new HandSettingsDto().peakWaitMs + FrameMs);
        }

        [Test]
        public void The_same_gesture_gives_the_same_note_near_or_far_from_the_camera()
        {
            // speeds are divided by the palm length: a hand half the size moves half the distance for the same gesture
            var near = StrikeFramesWithSize(0.30f, 7f);
            var far = StrikeFramesWithSize(0.15f, 7f);

            var a = Run(NewInterpreter(), near)[0];
            var b = Run(NewInterpreter(), far)[0];

            Assert.AreEqual(a.Velocity, b.Velocity, 0.05f);
        }

        [Test]
        public void Two_hands_strike_independently_with_their_own_ids_and_parts()
        {
            var interpreter = NewInterpreter();
            var frames = new List<HandFrame>();
            var perFrame = 7f * PalmSize * (float)(FrameMs / 1000.0);
            float left = 0.3f, right = 0.3f;
            for (var i = 0; i < 20; i++)
            {
                if (i >= 6 && i < 12) left += perFrame;           // left hand strikes first
                if (i >= 10 && i < 16) right += perFrame;         // right hand a little later
                frames.Add(Frame(i + 1, i * FrameMs, new Pose(0, 0.15f, left), new Pose(1, 0.85f, right)));
            }

            var strikes = Run(interpreter, frames);

            Assert.AreEqual(2, strikes.Count);
            Assert.AreEqual(0, strikes[0].HandId);
            Assert.AreEqual(Part.Harp, strikes[0].Part);
            Assert.AreEqual(1, strikes[1].HandId);
            Assert.AreEqual(Part.Bell, strikes[1].Part);
        }

        [Test]
        public void A_hand_that_disappears_right_after_the_trigger_still_gets_its_note()
        {
            var frames = StrikeFrames(MidX, 0.3f, 8f, stillFrames: 4, moveFrames: 2, tailFrames: 0);
            var seq = frames.Count + 1L;
            var t = frames.Count * FrameMs;
            for (var i = 0; i < 4; i++) frames.Add(Frame(seq++, t + i * FrameMs));

            Assert.AreEqual(1, Run(NewInterpreter(), frames).Count);
        }

        [Test]
        public void A_hand_below_the_confidence_threshold_is_ignored()
        {
            var frames = new List<HandFrame>();
            var y = 0.3f;
            for (var i = 0; i < 16; i++)
            {
                if (i >= 6) y += 0.05f;
                frames.Add(Frame(i + 1, i * FrameMs, new Pose(0, MidX, y, score: 0.2f)));
            }

            Assert.AreEqual(0, Run(NewInterpreter(), frames).Count);
        }

        [Test]
        public void Reappearing_in_another_place_after_a_gap_is_not_a_strike()
        {
            var frames = new List<HandFrame>();
            long seq = 1;
            for (var i = 0; i < 6; i++) frames.Add(Frame(seq++, i * FrameMs, new Pose(0, MidX, 0.2f)));
            for (var i = 6; i < 22; i++) frames.Add(Frame(seq++, i * FrameMs)); // 500 ms without a hand
            for (var i = 22; i < 30; i++) frames.Add(Frame(seq++, i * FrameMs, new Pose(0, MidX, 0.7f)));

            Assert.AreEqual(0, Run(NewInterpreter(), frames).Count);
        }

        [Test]
        public void The_same_hand_cannot_trigger_again_within_the_retrigger_time()
        {
            var settings = new HandSettingsDto { minRetriggerMs = 1000f };
            var frames = StrikeFrames(MidX, 0.25f, 7f, tailFrames: 8);
            frames.AddRange(StrikeFrames(MidX, 0.25f, 7f, firstSeq: 100, firstMs: 700));

            Assert.AreEqual(1, Run(NewInterpreter(10, settings), frames).Count);
        }

        [Test]
        public void The_same_frames_always_give_the_same_strikes()
        {
            var frames = StrikeFrames(0.2f, 0.25f, 9f, tailFrames: 8);
            frames.AddRange(StrikeFrames(0.5f, 0.35f, 5f, firstSeq: 100, firstMs: 1000, tailFrames: 8));
            frames.AddRange(StrikeFrames(0.8f, 0.55f, 12f, firstSeq: 200, firstMs: 2000));

            var first = Run(NewInterpreter(), frames);
            var second = Run(NewInterpreter(), frames);
            var reused = NewInterpreter();
            Run(reused, frames);
            reused.Reset();
            var third = Run(reused, frames);

            Assert.AreEqual(3, first.Count);
            AssertSame(first, second);
            AssertSame(first, third);
        }

        [Test]
        public void Reset_forgets_hands_so_a_looping_replay_starts_clean()
        {
            var interpreter = NewInterpreter();
            Run(interpreter, StrikeFrames(MidX, 0.3f, 7f, tailFrames: 0)); // ends mid-strike (disarmed)
            interpreter.Reset();

            Assert.AreEqual(1, Run(interpreter, StrikeFrames(MidX, 0.3f, 7f)).Count);
        }

        [TestCase(2.95f, 3, 0.2f, 3)]   // just under the boundary, previous band wins inside the dead band
        [TestCase(2.70f, 3, 0.2f, 2)]   // clearly in the lower band
        [TestCase(4.15f, 3, 0.2f, 3)]   // just over the upper boundary, previous band wins
        [TestCase(4.30f, 3, 0.2f, 4)]
        [TestCase(7.50f, 0, 0.2f, 7)]   // a big jump is never held back
        [TestCase(5.00f, -1, 0.2f, 5)]  // no previous band
        [TestCase(10.0f, 9, 0.2f, 9)]   // clamped to the last band
        public void Dead_bands_keep_a_hand_on_a_boundary_from_flipping(float position, int previous, float margin, int expected)
        {
            Assert.AreEqual(expected, HandInterpreter.PickBand(position, 10, previous, margin));
        }

        static void AssertSame(List<HandStrike> a, List<HandStrike> b)
        {
            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].HandId, b[i].HandId);
                Assert.AreEqual(a[i].Part, b[i].Part);
                Assert.AreEqual(a[i].PitchStep, b[i].PitchStep);
                Assert.AreEqual(a[i].Velocity, b[i].Velocity);
                Assert.AreEqual(a[i].TriggerCaptureMs, b[i].TriggerCaptureMs);
                Assert.AreEqual(a[i].Timing.CaptureMs, b[i].Timing.CaptureMs);
            }
        }

        /// <summary>Speeds (palm lengths per second, one per frame, may differ frame to frame) applied to a hand starting at (MidX, 0.3).</summary>
        static List<HandFrame> AcceleratingStrike(float[] speeds)
        {
            var frames = new List<HandFrame>();
            var y = 0.3f;
            for (var i = 0; i < speeds.Length; i++)
            {
                y += speeds[i] * PalmSize * (float)(FrameMs / 1000.0);
                frames.Add(Frame(i + 1, i * FrameMs, new Pose(0, MidX, y)));
            }
            return frames;
        }

        static List<HandFrame> StrikeFramesWithSize(float size, float speed)
        {
            var frames = new List<HandFrame>();
            var y = 0.3f;
            for (var i = 0; i < 24; i++)
            {
                if (i >= 6 && i < 12) y += speed * size * (float)(FrameMs / 1000.0);
                frames.Add(Frame(i + 1, i * FrameMs, new Pose(0, MidX, y, size: size)));
            }
            return frames;
        }
    }
}
