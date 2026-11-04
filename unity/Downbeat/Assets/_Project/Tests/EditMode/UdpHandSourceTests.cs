using System;
using System.Collections.Generic;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    sealed class FakePacketReceiver : IPacketReceiver
    {
        Action<byte[], int> _onPacket;

        public bool CanStart = true;
        public int StartCalls;
        public int StartedPort;
        public bool Disposed;

        public bool TryStart(int port, Action<byte[], int> onPacket, out string error)
        {
            StartCalls++;
            StartedPort = port;
            if (!CanStart)
            {
                error = "address already in use";
                return false;
            }
            _onPacket = onPacket;
            error = null;
            return true;
        }

        public void Stop() { }

        public void Dispose() => Disposed = true;

        public void Deliver(byte[] packet) => _onPacket(packet, packet.Length);
    }

    public class UdpHandSourceTests
    {
        const double TimeoutMs = 2000.0;

        double _nowMs;
        FakePacketReceiver _receiver;
        HandSettingsHolder _settings;
        UdpHandSource _source;
        readonly List<HandFrame> _frames = new List<HandFrame>();

        [SetUp]
        public void SetUp()
        {
            _nowMs = 0.0;
            _receiver = new FakePacketReceiver();
            _settings = new HandSettingsHolder();
            _settings.Set(new HandSettingsDto { udpPort = 50123, trackerTimeoutSec = 2f, mirrorX = false });
            _source = new UdpHandSource(_receiver, _settings, () => _nowMs);
        }

        List<HandFrame> Poll()
        {
            _frames.Clear();
            _source.Poll(_frames);
            return _frames;
        }

        static byte[] Packet(long seq, double captureMs, TrackerState state = TrackerState.Running, params HandTestData.Pose[] hands) =>
            HandUdpTestPackets.Build(seq, captureMs, state, hands);

        [Test]
        public void Open_ListensOnTheConfiguredPort_AndStartsInTheStartingState()
        {
            _source.Open();

            Assert.AreEqual(50123, _receiver.StartedPort);
            Assert.AreEqual(TrackerState.Starting, _source.State);
            Assert.AreEqual(0, _source.Generation);
        }

        [Test]
        public void Poll_TurnsPacketsIntoHandFrames_WithTheTimeTheyWereReceived()
        {
            _source.Open();
            _receiver.Deliver(Packet(0, 100.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            _nowMs = 117.0;

            var frames = Poll();

            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(0, frames[0].Seq);
            Assert.AreEqual(100.0, frames[0].CaptureMs);
            Assert.AreEqual(117.0, frames[0].ReceivedMs);
            Assert.AreEqual(1, frames[0].HandCount);
            Assert.AreEqual(0.3f, frames[0].Hand(0).X, 1e-5f);
            Assert.AreEqual(TrackerState.Running, _source.State);
        }

        [Test]
        public void Poll_ReadsOpenHandOrFistFromThe21Points_WithTheThresholdsOfTheLoadedContent()
        {
            var hands = new[] { new HandTestData.Pose(0, 0.3f, 0.4f, shape: HandPose.Fist), new HandTestData.Pose(1, 0.7f, 0.4f, shape: HandPose.Unknown) };
            _source.Open();
            _receiver.Deliver(Packet(0, 100.0, TrackerState.Running, hands));
            _nowMs = 110.0;
            var byDefault = new List<HandFrame>(Poll());

            _settings.Set(new HandSettingsDto { udpPort = 50123, trackerTimeoutSec = 2f, mirrorX = false }, new PoseDto { openRatio = 1.1f, fistRatio = 0.5f });
            _receiver.Deliver(Packet(1, 133.0, TrackerState.Running, hands));
            _nowMs = 140.0;
            var lenient = new List<HandFrame>(Poll());

            Assert.AreEqual(HandPose.Fist, byDefault[0].Hand(0).Pose);
            Assert.AreEqual(HandPose.Unknown, byDefault[0].Hand(1).Pose, "half curled under the default thresholds");
            Assert.AreEqual(HandPose.Unknown, lenient[0].Hand(0).Pose, "a fist (0.9) is neither under thresholds of 1.1 for open and 0.5 for a fist");
            Assert.AreEqual(HandPose.Open, lenient[0].Hand(1).Pose, "half curled (1.3) is open under 1.1: the source reads the holder for every packet");
        }

        [Test]
        public void PacketAge_IsHowLongTheNewestPacketWaitedBetweenTheSocketAndThePoll()
        {
            var arrivalMs = 0.0;
            var source = new UdpHandSource(_receiver, _settings, () => _nowMs, () => arrivalMs);
            source.Open();
            arrivalMs = 100.0;
            _receiver.Deliver(Packet(0, 100.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            arrivalMs = 108.0;
            _receiver.Deliver(Packet(1, 108.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            arrivalMs = 130.0;   // the main thread comes back after a long frame

            _frames.Clear();
            source.Poll(_frames);

            Assert.AreEqual(2, _frames.Count);
            Assert.AreEqual(22.0, source.PacketAgeMs, 1e-9, "the newest packet of the poll, 130 - 108");
        }

        [Test]
        public void PacketAge_IsNaN_WhenThePollBroughtNoPacket()
        {
            var arrivalMs = 0.0;
            var source = new UdpHandSource(_receiver, _settings, () => _nowMs, () => arrivalMs);
            source.Open();
            arrivalMs = 5.0;
            _receiver.Deliver(Packet(0, 5.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            arrivalMs = 9.0;
            source.Poll(new List<HandFrame>());
            Assert.AreEqual(4.0, source.PacketAgeMs, 1e-9);

            arrivalMs = 40.0;
            source.Poll(new List<HandFrame>());

            Assert.IsTrue(double.IsNaN(source.PacketAgeMs), "nothing arrived: there is no age, and the old one is not repeated");
        }

        [Test]
        public void PacketAge_IsNaN_ForABadPacket()
        {
            var arrivalMs = 0.0;
            var source = new UdpHandSource(_receiver, _settings, () => _nowMs, () => arrivalMs);
            source.Open();
            _receiver.Deliver(new byte[] { 1, 2, 3 });
            arrivalMs = 10.0;

            source.Poll(new List<HandFrame>());

            Assert.IsTrue(double.IsNaN(source.PacketAgeMs));
        }

        [Test]
        public void Poll_MirrorsX_WhenTheSettingsSayLeftIsRight()
        {
            _settings.Set(new HandSettingsDto { udpPort = 50123, trackerTimeoutSec = 2f, mirrorX = true });
            _source.Open();
            _receiver.Deliver(Packet(0, 100.0, TrackerState.Running, new HandTestData.Pose(0, 0.2f, 0.4f)));

            var frames = Poll();

            Assert.AreEqual(0.8f, frames[0].Hand(0).X, 1e-5f);
            Assert.AreEqual(0.4f, frames[0].Hand(0).Y, 1e-5f);
        }

        [Test]
        public void Source_ReportsCameraMissing_AfterTimeout()
        {
            _source.Open();
            _receiver.Deliver(Packet(0, 0.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            _nowMs = 10.0;
            Poll();

            _nowMs = 10.0 + TimeoutMs - 1.0;
            var before = Poll();
            Assert.AreEqual(0, before.Count);
            Assert.AreEqual(TrackerState.Running, _source.State);

            _nowMs = 10.0 + TimeoutMs;
            var after = Poll();

            Assert.AreEqual(TrackerState.CameraMissing, _source.State);
            Assert.AreEqual(1, after.Count, "one frame without hands tells the motion tracker that the hands are gone");
            Assert.AreEqual(0, after[0].HandCount);
            Assert.AreEqual(TrackerState.CameraMissing, after[0].State);
        }

        [Test]
        public void Source_SendsTheEmptyFrameOnlyOnce_PerTimeout()
        {
            _source.Open();
            _nowMs = TimeoutMs;
            Assert.AreEqual(1, Poll().Count);

            _nowMs = TimeoutMs * 3;

            Assert.AreEqual(0, Poll().Count);
            Assert.AreEqual(TrackerState.CameraMissing, _source.State);
        }

        [Test]
        public void Source_TimesOutFromTheLastPacket_NotFromTheStart()
        {
            _source.Open();
            _receiver.Deliver(Packet(0, 0.0));
            _nowMs = 1500.0;
            _receiver.Deliver(Packet(1, 1500.0));
            Poll();

            _nowMs = 3499.0;
            Assert.AreEqual(0, Poll().Count);
            _nowMs = 3500.0;
            Assert.AreEqual(1, Poll().Count);
        }

        [Test]
        public void Source_ReturnsToRunning_WhenPacketsComeBack()
        {
            _source.Open();
            _nowMs = TimeoutMs;
            Poll();
            Assert.AreEqual(TrackerState.CameraMissing, _source.State);

            _nowMs = TimeoutMs + 500.0;
            _receiver.Deliver(Packet(0, 900.0, TrackerState.Running, new HandTestData.Pose(0, 0.3f, 0.4f)));
            var frames = Poll();

            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(TrackerState.Running, _source.State);
        }

        [Test]
        public void Source_BumpsGeneration_WhenSeqResets()
        {
            _source.Open();
            _receiver.Deliver(Packet(100, 5000.0));
            _receiver.Deliver(Packet(101, 5033.0));
            Poll();
            Assert.AreEqual(0, _source.Generation);

            _receiver.Deliver(Packet(0, 20.0)); // the tracker was restarted: its counter and clock start again
            var frames = Poll();

            Assert.AreEqual(1, _source.Generation);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(0, frames[0].Seq);
        }

        [Test]
        public void Source_TreatsASmallSeqReversalAsALatePacket_NotARestart()
        {
            _source.Open();
            _receiver.Deliver(Packet(100, 3000.0));
            _receiver.Deliver(Packet(101, 3033.0));
            Poll();

            _receiver.Deliver(Packet(90, 2700.0)); // 11 behind: a datagram that overtook nothing but arrived late
            var frames = Poll();

            Assert.AreEqual(0, _source.Generation);
            Assert.AreEqual(0, frames.Count, "a late packet is dropped, not played");
            Assert.AreEqual(1, _source.LatePackets);

            _receiver.Deliver(Packet(102, 3066.0));
            Assert.AreEqual(1, Poll().Count, "the stream goes on");
        }

        [Test]
        public void Source_BumpsGeneration_WhenSeqFallsBelow16_EvenWhenNotFarBehind()
        {
            _source.Open();
            _receiver.Deliver(Packet(20, 600.0));
            Poll();

            _receiver.Deliver(Packet(15, 30.0)); // only 5 behind, but a counter that starts again is at 0..15
            var frames = Poll();

            Assert.AreEqual(1, _source.Generation);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(0, _source.LatePackets);
        }

        [Test]
        public void Source_RestartThreshold_IsMoreThan64BehindTheLastSeq()
        {
            _source.Open();
            _receiver.Deliver(Packet(200, 6000.0));
            Poll();

            _receiver.Deliver(Packet(136, 4000.0)); // exactly 64 behind: still late
            Poll();
            Assert.AreEqual(0, _source.Generation);
            Assert.AreEqual(1, _source.LatePackets);

            _receiver.Deliver(Packet(135, 3900.0)); // 65 behind: a new stream
            var frames = Poll();

            Assert.AreEqual(1, _source.Generation);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(135, frames[0].Seq);
        }

        [Test]
        public void Poll_ReadsAtMostOneQueueOfPackets_WhenTheSocketKeepsFeedingIt()
        {
            const int Packets = 20000;
            _source.Open();
            var packets = new byte[Packets][];
            for (var i = 0; i < Packets; i++) packets[i] = Packet(i, i * 33.0);
            var stop = false;
            var producer = new System.Threading.Thread(() =>
            {
                for (var i = 0; i < Packets && !stop; i++) _receiver.Deliver(packets[i]);
            });
            producer.Start();
            try
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (_source.DroppedPackets == 0 && deadline.ElapsedMilliseconds < 2000) System.Threading.Thread.Yield(); // let the producer fill the ring

                var frames = Poll();

                Assert.LessOrEqual(frames.Count, UdpHandSource.QueueCapacity, "one frame of the app must not spend its time draining an endless socket");
            }
            finally
            {
                stop = true;
                producer.Join();
            }
        }

        [Test]
        public void Source_DropsTheFramesOfTheOldStream_WhenARestartArrivesInTheSameBatch()
        {
            _source.Open();
            _receiver.Deliver(Packet(100, 5000.0));
            Poll();

            _receiver.Deliver(Packet(101, 5033.0));
            _receiver.Deliver(Packet(0, 20.0));
            _receiver.Deliver(Packet(1, 53.0));
            var frames = Poll();

            Assert.AreEqual(1, _source.Generation);
            CollectionAssert.AreEqual(new long[] { 0, 1 }, new[] { frames[0].Seq, frames[1].Seq });
        }

        [Test]
        public void Source_KeepsTheGeneration_WhenSeqKeepsGrowing()
        {
            _source.Open();
            for (long seq = 0; seq < 50; seq++) _receiver.Deliver(Packet(seq, seq * 33.0));

            Poll();

            Assert.AreEqual(0, _source.Generation);
        }

        [Test]
        public void Source_IgnoresARepeatedSeq()
        {
            _source.Open();
            _receiver.Deliver(Packet(7, 100.0));
            _receiver.Deliver(Packet(7, 100.0));

            Assert.AreEqual(1, Poll().Count);
            Assert.AreEqual(0, _source.Generation);
        }

        [Test]
        public void Source_DropsOldest_WhenMoreThan8Queued()
        {
            _source.Open();
            for (long seq = 0; seq < 12; seq++) _receiver.Deliver(Packet(seq, seq * 33.0));

            var frames = Poll();

            Assert.AreEqual(8, frames.Count);
            Assert.AreEqual(4, frames[0].Seq);
            Assert.AreEqual(11, frames[7].Seq);
            Assert.AreEqual(4, _source.DroppedPackets);
        }

        [Test]
        public void Source_IgnoresPacketsThatAreNotHandUdp_AndCountsThem()
        {
            _source.Open();
            _receiver.Deliver(new byte[] { 1, 2, 3 });
            var wrongMagic = Packet(0, 0.0);
            wrongMagic[0] = (byte)'X';
            _receiver.Deliver(wrongMagic);
            _receiver.Deliver(Packet(1, 33.0));

            var frames = Poll();

            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(2, _source.BadPackets);
        }

        [Test]
        public void Source_PassesOnTheStateTheTrackerReports()
        {
            _source.Open();
            _receiver.Deliver(Packet(0, 0.0, TrackerState.CameraMissing));

            var frames = Poll();

            Assert.AreEqual(TrackerState.CameraMissing, _source.State);
            Assert.AreEqual(TrackerState.CameraMissing, frames[0].State);
            Assert.AreEqual(0, frames[0].HandCount);
        }

        [Test]
        public void Source_ReportsCameraError_WhenThePortCannotBeOpened_AndTriesAgainEvery5Seconds()
        {
            _receiver.CanStart = false;
            _source.Open();
            Assert.AreEqual(TrackerState.CameraError, _source.State);
            Assert.AreEqual(1, _receiver.StartCalls);

            _nowMs = 4999.0;
            Poll();
            Assert.AreEqual(1, _receiver.StartCalls, "not yet");

            _nowMs = 5000.0;
            Poll();
            Assert.AreEqual(2, _receiver.StartCalls);
            Assert.AreEqual(TrackerState.CameraError, _source.State);

            _receiver.CanStart = true;
            _nowMs = 10000.0;
            Poll();

            Assert.AreEqual(3, _receiver.StartCalls);
            Assert.AreEqual(TrackerState.Starting, _source.State);
        }

        [Test]
        public void Source_NeverOpensThePortTwice_WhenItIsListening()
        {
            _source.Open();
            _nowMs = 60000.0;

            Poll();

            Assert.AreEqual(1, _receiver.StartCalls);
        }

        [Test]
        public void RequestTimeFor_IsNaN_BecauseAHandNoteIsRequestedNow()
        {
            Assert.IsTrue(double.IsNaN(_source.RequestTimeFor(123.0)));
        }

        [Test]
        public void Dispose_ReleasesTheReceiver()
        {
            _source.Open();

            _source.Dispose();

            Assert.IsTrue(_receiver.Disposed);
        }
    }
}
