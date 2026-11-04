using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HandFrameReaderTests
    {
        [Test]
        public void Layout_numbers_match_the_page_format()
        {
            Assert.AreEqual(5, HandBufferLayout.HeaderLength);
            Assert.AreEqual(65, HandBufferLayout.HandStride);
            Assert.AreEqual(135, HandBufferLayout.AspectIndex);
            Assert.AreEqual(136, HandBufferLayout.Length);
        }

        [Test]
        public void Palm_center_and_size_come_from_the_five_palm_landmarks()
        {
            var frame = HandTestData.Frame(7, 123.5, new HandTestData.Pose(0, 0.3f, 0.6f, 0.9f, 0.25f));

            Assert.AreEqual(1, frame.HandCount);
            var hand = frame.Hand(0);
            Assert.AreEqual(0.3f, hand.X, 1e-5f);
            Assert.AreEqual(0.6f, hand.Y, 1e-5f);
            Assert.AreEqual(0.25f, hand.Size, 1e-5f);
            Assert.AreEqual(0.9f, hand.Score, 1e-5f);
            Assert.AreEqual(7L, frame.Seq);
            Assert.AreEqual(123.5, frame.CaptureMs, 1e-9);
            Assert.AreEqual(TrackerState.Running, frame.State);
        }

        [Test]
        public void Two_hands_keep_their_ids()
        {
            var frame = HandTestData.Frame(1, 0, new HandTestData.Pose(1, 0.8f, 0.4f), new HandTestData.Pose(0, 0.2f, 0.5f));

            Assert.AreEqual(2, frame.HandCount);
            Assert.AreEqual(1, frame.Hand(0).Id);
            Assert.AreEqual(0, frame.Hand(1).Id);
        }

        [Test]
        public void Palm_size_is_measured_in_video_heights_so_the_aspect_ratio_matters()
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.HandCountIndex] = 1;
            buffer[HandBufferLayout.AspectIndex] = 2.0;
            buffer[HandBufferLayout.SeqIndex] = 1;
            var b = HandBufferLayout.HandBase(0);
            buffer[b + 1] = 0.9;
            // wrist and middle base 0.1 apart horizontally: 0.1 * aspect 2 = 0.2 video heights
            foreach (var landmark in new[] { HandBufferLayout.IndexBase, HandBufferLayout.RingBase, HandBufferLayout.PinkyBase }) SetXY(buffer, b, landmark, 0.5, 0.5);
            SetXY(buffer, b, HandBufferLayout.Wrist, 0.5, 0.5);
            SetXY(buffer, b, HandBufferLayout.MiddleBase, 0.6, 0.5);

            Assert.IsTrue(HandFrameReader.TryRead(buffer, 0, out var frame));
            Assert.AreEqual(0.2f, frame.Hand(0).Size, 1e-5f);
        }

        [Test]
        public void Missing_or_short_buffers_are_rejected()
        {
            Assert.IsFalse(HandFrameReader.TryRead(null, 0, out _));
            Assert.IsFalse(HandFrameReader.TryRead(new double[10], 0, out _));
            Assert.AreEqual(TrackerState.Off, HandFrameReader.ReadState(null));
        }

        [Test]
        public void Hands_with_non_finite_coordinates_or_bad_ids_are_skipped_not_trusted()
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.SeqIndex] = 1;
            buffer[HandBufferLayout.HandCountIndex] = 2;
            buffer[HandBufferLayout.AspectIndex] = 1.3;
            buffer[HandBufferLayout.HandBase(0) + 1] = 0.9;
            buffer[HandBufferLayout.HandBase(0) + 2] = double.NaN; // wrist x
            buffer[HandBufferLayout.HandBase(1)] = 5; // id out of range
            buffer[HandBufferLayout.HandBase(1) + 1] = 0.9;

            Assert.IsTrue(HandFrameReader.TryRead(buffer, 0, out var frame));
            Assert.AreEqual(0, frame.HandCount);
        }

        [Test]
        public void Unknown_tracker_states_read_as_an_error_not_as_running()
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.StateIndex] = 99;
            Assert.AreEqual(TrackerState.CameraError, HandFrameReader.ReadState(buffer));
            buffer[HandBufferLayout.StateIndex] = double.NaN;
            Assert.AreEqual(TrackerState.CameraError, HandFrameReader.ReadState(buffer));
        }

        [Test]
        public void A_nonsense_aspect_falls_back_to_four_by_three()
        {
            var buffer = new double[HandBufferLayout.Length];
            buffer[HandBufferLayout.SeqIndex] = 1;
            buffer[HandBufferLayout.AspectIndex] = 0;
            Assert.IsTrue(HandFrameReader.TryRead(buffer, 0, out var frame));
            Assert.AreEqual(4f / 3f, frame.Aspect, 1e-5f);
        }

        static void SetXY(double[] buffer, int handBase, int landmark, double x, double y)
        {
            buffer[handBase + 2 + landmark * 3] = x;
            buffer[handBase + 3 + landmark * 3] = y;
        }
    }
}
