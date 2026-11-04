using System.Threading;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class PacketRingTests
    {
        static byte[] Packet(byte marker, int length = 4)
        {
            var packet = new byte[length];
            for (var i = 0; i < length; i++) packet[i] = marker;
            return packet;
        }

        [Test]
        public void Pop_ReturnsPacketsOldestFirst()
        {
            var ring = new PacketRing(8);
            ring.Push(Packet(1), 4);
            ring.Push(Packet(2), 4);
            var destination = new byte[PacketRing.SlotBytes];

            Assert.IsTrue(ring.TryPop(destination, out var length));
            Assert.AreEqual(4, length);
            Assert.AreEqual(1, destination[0]);
            Assert.IsTrue(ring.TryPop(destination, out _));
            Assert.AreEqual(2, destination[0]);
            Assert.IsFalse(ring.TryPop(destination, out _));
        }

        [Test]
        public void Pop_ReturnsTheMomentEachPacketArrived()
        {
            var ring = new PacketRing(4);
            ring.Push(Packet(1), 4, 1000.5);
            ring.Push(Packet(2), 4, 1016.25);
            var destination = new byte[PacketRing.SlotBytes];

            Assert.IsTrue(ring.TryPop(destination, out _, out var first));
            Assert.IsTrue(ring.TryPop(destination, out _, out var second));

            Assert.AreEqual(1000.5, first);
            Assert.AreEqual(1016.25, second);
        }

        [Test]
        public void ADroppedPacket_TakesItsArrivalTimeWithIt()
        {
            var ring = new PacketRing(2);
            ring.Push(Packet(1), 4, 10.0);
            ring.Push(Packet(2), 4, 20.0);
            ring.Push(Packet(3), 4, 30.0);   // overwrites the first
            var destination = new byte[PacketRing.SlotBytes];

            ring.TryPop(destination, out _, out var arrived);

            Assert.AreEqual(2, destination[0]);
            Assert.AreEqual(20.0, arrived, "the time that belongs to the packet that is returned");
        }

        [Test]
        public void Push_DropsTheOldest_WhenFull()
        {
            var ring = new PacketRing(3);
            for (byte i = 1; i <= 5; i++) ring.Push(Packet(i), 4);
            var destination = new byte[PacketRing.SlotBytes];

            Assert.AreEqual(3, ring.Count);
            Assert.AreEqual(2, ring.Dropped);
            ring.TryPop(destination, out _);
            Assert.AreEqual(3, destination[0], "packets 1 and 2 were dropped");
        }

        [Test]
        public void Push_CopiesTheBytes_SoTheSenderCanReuseItsBuffer()
        {
            var ring = new PacketRing(2);
            var shared = Packet(7);
            ring.Push(shared, 4);
            shared[0] = 99;
            var destination = new byte[PacketRing.SlotBytes];

            ring.TryPop(destination, out _);

            Assert.AreEqual(7, destination[0]);
        }

        [Test]
        public void Push_OnlyCopiesTheGivenLength()
        {
            var ring = new PacketRing(2);
            ring.Push(Packet(5, 100), 10);
            var destination = new byte[PacketRing.SlotBytes];

            ring.TryPop(destination, out var length);

            Assert.AreEqual(10, length);
        }

        [Test]
        public void Push_IgnoresAPacketLargerThanASlot_AndCountsItAsOversize()
        {
            var ring = new PacketRing(2);

            ring.Push(new byte[PacketRing.SlotBytes + 1], PacketRing.SlotBytes + 1);

            Assert.AreEqual(0, ring.Count);
            Assert.AreEqual(1, ring.Oversize);
        }

        [Test]
        public void ConcurrentProducerAndConsumer_LoseNothingSilently()
        {
            var ring = new PacketRing(8);
            const int total = 20000;
            var producer = new Thread(() =>
            {
                var packet = Packet(1);
                for (var i = 0; i < total; i++) ring.Push(packet, 4);
            });
            var popped = 0;
            var destination = new byte[PacketRing.SlotBytes];
            producer.Start();

            while (producer.IsAlive || ring.Count > 0)
            {
                if (ring.TryPop(destination, out _)) popped++;
            }
            producer.Join();

            Assert.AreEqual(total, popped + ring.Dropped, "every packet was either popped or counted as dropped");
        }
    }
}
