using System;
using System.IO;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>Reads the datagrams of the Python tracker. The .bin/.json fixtures are written by tracker/tests/test_protocol.py and shared by both sides.</summary>
    public class HandUdpCodecTests
    {
        [Serializable]
        class FixtureHand
        {
            public int id;
            public float score;
            public float[] landmarks;
        }

        [Serializable]
        class FixtureFrame
        {
            public long seq;
            public int state;
            public double capture_ms;
            public double infer_end_ms;
            public double send_ms;
            public float aspect;
            public FixtureHand[] hands;
        }

        static string FixtureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "tests", "fixtures", "hand-udp-v1"));

        static byte[] Bin(string name) => File.ReadAllBytes(Path.Combine(FixtureDir, name + ".bin"));

        static FixtureFrame Json(string name) => JsonUtility.FromJson<FixtureFrame>(File.ReadAllText(Path.Combine(FixtureDir, name + ".json")));

        static double[] NewBuffer() => new double[HandBufferLayout.Length];

        [Test]
        public void Decode_TwoHandsFixture_MatchesJson()
        {
            var buffer = NewBuffer();
            var expected = Json("two-hands");

            var ok = HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, buffer, out var seq);

            Assert.IsTrue(ok);
            Assert.AreEqual(expected.seq, seq);
            Assert.AreEqual(expected.seq, (long)buffer[HandBufferLayout.SeqIndex]);
            Assert.AreEqual(expected.capture_ms, buffer[HandBufferLayout.CaptureMsIndex]);
            Assert.AreEqual(expected.infer_end_ms, buffer[HandBufferLayout.InferEndMsIndex]);
            Assert.AreEqual(expected.state, (int)buffer[HandBufferLayout.StateIndex]);
            Assert.AreEqual(2, (int)buffer[HandBufferLayout.HandCountIndex]);
            Assert.AreEqual((double)expected.aspect, buffer[HandBufferLayout.AspectIndex]);
            for (var h = 0; h < 2; h++)
            {
                var b = HandBufferLayout.HandBase(h);
                Assert.AreEqual(expected.hands[h].id, (int)buffer[b]);
                Assert.AreEqual((double)expected.hands[h].score, buffer[b + 1]);
                for (var i = 0; i < HandBufferLayout.LandmarksPerHand * 3; i++) Assert.AreEqual((double)expected.hands[h].landmarks[i], buffer[b + 2 + i], $"hand {h} value {i}");
            }
        }

        [Test]
        public void Decode_NoHandsFixture_GivesAnEmptyFrame()
        {
            var buffer = NewBuffer();
            var expected = Json("no-hands");

            var ok = HandUdpCodec.TryDecodeToBuffer(Bin("no-hands"), false, buffer, out var seq);

            Assert.IsTrue(ok);
            Assert.AreEqual(expected.seq, seq);
            Assert.AreEqual(0, (int)buffer[HandBufferLayout.HandCountIndex]);
            Assert.AreEqual(expected.capture_ms, buffer[HandBufferLayout.CaptureMsIndex]);
        }

        [Test]
        public void Decode_MirrorsX_WhenRequested()
        {
            var plain = NewBuffer();
            var mirrored = NewBuffer();
            var packet = Bin("two-hands");

            HandUdpCodec.TryDecodeToBuffer(packet, false, plain, out _);
            HandUdpCodec.TryDecodeToBuffer(packet, true, mirrored, out _);

            for (var h = 0; h < 2; h++)
            {
                var b = HandBufferLayout.HandBase(h);
                for (var k = 0; k < HandBufferLayout.LandmarksPerHand; k++)
                {
                    Assert.AreEqual(1.0 - plain[b + 2 + k * 3], mirrored[b + 2 + k * 3], 1e-12, "x is 1 - x");
                    Assert.AreEqual(plain[b + 3 + k * 3], mirrored[b + 3 + k * 3], "y is unchanged");
                    Assert.AreEqual(plain[b + 4 + k * 3], mirrored[b + 4 + k * 3], "z is unchanged");
                }
            }
        }

        [Test]
        public void Decode_ThenHandFrameReader_GivesTheTwoHandsWithTheirIds()
        {
            var buffer = NewBuffer();
            HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, buffer, out _);

            var ok = HandFrameReader.TryRead(buffer, 5000.0, out var frame);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, frame.HandCount);
            Assert.AreEqual(TrackerState.Running, frame.State);
            Assert.AreEqual(Json("two-hands").seq, frame.Seq);
            Assert.AreEqual(0, frame.Hand(0).Id);
            Assert.AreEqual(1, frame.Hand(1).Id);
            Assert.AreEqual(0.875f, frame.Hand(0).Score, 1e-6f);
        }

        [Test]
        public void Decode_RejectsBadMagic()
        {
            var packet = Bin("two-hands");
            packet[0] = (byte)'X';

            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(packet, false, NewBuffer(), out _));
        }

        [Test]
        public void Decode_RejectsAnUnknownVersion()
        {
            var packet = Bin("two-hands");
            packet[4] = 2;

            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(packet, false, NewBuffer(), out _));
        }

        [Test]
        public void Decode_RejectsTruncatedAndTooLongPackets()
        {
            var packet = Bin("two-hands");
            var longer = new byte[packet.Length + 1];
            Array.Copy(packet, longer, packet.Length);

            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(new ReadOnlySpan<byte>(packet, 0, packet.Length - 1), false, NewBuffer(), out _));
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(new ReadOnlySpan<byte>(packet, 0, 10), false, NewBuffer(), out _));
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(ReadOnlySpan<byte>.Empty, false, NewBuffer(), out _));
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(longer, false, NewBuffer(), out _));
        }

        [Test]
        public void Decode_RejectsMoreThanTwoHandsAndUnknownStates()
        {
            var threeHands = Bin("two-hands");
            threeHands[6] = 3;
            var unknownState = Bin("two-hands");
            unknownState[5] = 99;

            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(threeHands, false, NewBuffer(), out _));
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(unknownState, false, NewBuffer(), out _));
        }

        [Test]
        public void Decode_RejectsABufferThatIsTooSmall()
        {
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, new double[10], out _));
            Assert.IsFalse(HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, null, out _));
        }

        [Test]
        public void Decode_LeavesTheBufferUntouched_WhenThePacketIsInvalid()
        {
            var buffer = NewBuffer();
            HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, buffer, out _);
            var snapshot = (double[])buffer.Clone();
            var bad = Bin("two-hands");
            bad[0] = 0;

            HandUdpCodec.TryDecodeToBuffer(bad, false, buffer, out _);

            CollectionAssert.AreEqual(snapshot, buffer);
        }

        [Test]
        public void Decode_ClearsTheHandsOfThePreviousFrame()
        {
            var buffer = NewBuffer();
            HandUdpCodec.TryDecodeToBuffer(Bin("two-hands"), false, buffer, out _);

            HandUdpCodec.TryDecodeToBuffer(Bin("no-hands"), false, buffer, out _);

            Assert.AreEqual(0, (int)buffer[HandBufferLayout.HandCountIndex]);
            for (var i = HandBufferLayout.HeaderLength; i < HandBufferLayout.AspectIndex; i++) Assert.AreEqual(0.0, buffer[i], $"index {i}");
        }

        [Test]
        public void TestPackets_AreDecodedLikeTheRealOnes()
        {
            var packet = HandUdpTestPackets.Build(42, 1234.5, TrackerState.Running, new HandTestData.Pose(1, 0.25f, 0.5f));
            var buffer = NewBuffer();

            Assert.IsTrue(HandUdpCodec.TryDecodeToBuffer(packet, false, buffer, out var seq));
            Assert.IsTrue(HandFrameReader.TryRead(buffer, 0, out var frame));

            Assert.AreEqual(42, seq);
            Assert.AreEqual(1234.5, frame.CaptureMs);
            Assert.AreEqual(0.25f, frame.Hand(0).X, 1e-5f);
            Assert.AreEqual(0.5f, frame.Hand(0).Y, 1e-5f);
        }
    }
}
