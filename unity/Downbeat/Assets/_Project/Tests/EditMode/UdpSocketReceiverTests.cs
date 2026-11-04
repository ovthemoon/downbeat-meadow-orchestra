using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Downbeat.App;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The real loopback socket (no mocks): a datagram sent to 127.0.0.1 reaches the callback.</summary>
    public class UdpSocketReceiverTests
    {
        const int WaitMs = 3000;

        static int FreePort()
        {
            using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)probe.LocalEndPoint).Port;
            }
        }

        static void SendLoopback(int port, byte[] data)
        {
            using (var client = new UdpClient())
            {
                client.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, port));
            }
        }

        [Test]
        public void Receiver_DeliversADatagramSentToLoopback()
        {
            var received = new ConcurrentQueue<byte[]>();
            var port = FreePort();
            using (var receiver = new UdpSocketReceiver())
            {
                Assert.IsTrue(receiver.TryStart(port, (buffer, length) => received.Enqueue(Slice(buffer, length)), out var error), error);

                SendLoopback(port, new byte[] { 1, 2, 3, 4, 5 });
                var waited = 0;
                while (received.IsEmpty && waited < WaitMs) { Thread.Sleep(5); waited += 5; }

                Assert.IsTrue(received.TryDequeue(out var packet), "no datagram arrived");
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, packet);
            }
        }

        [Test]
        public void Receiver_FailsToStart_WhenThePortIsTaken()
        {
            var port = FreePort();
            using (var holder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (var receiver = new UdpSocketReceiver())
            {
                holder.Bind(new IPEndPoint(IPAddress.Loopback, port));

                var ok = receiver.TryStart(port, (b, l) => { }, out var error);

                Assert.IsFalse(ok);
                Assert.IsNotEmpty(error);
            }
        }

        [Test]
        public void Receiver_CanBeStoppedAndStartedAgainOnTheSamePort()
        {
            var port = FreePort();
            using (var receiver = new UdpSocketReceiver())
            {
                Assert.IsTrue(receiver.TryStart(port, (b, l) => { }, out _));
                receiver.Stop();

                Assert.IsTrue(receiver.TryStart(port, (b, l) => { }, out var error), error);
            }
        }

        [Test]
        public void Receiver_OnlyListensOnTheLoopbackAddress()
        {
            var port = FreePort();
            using (var receiver = new UdpSocketReceiver())
            {
                receiver.TryStart(port, (b, l) => { }, out _);

                Assert.AreEqual(IPAddress.Loopback, receiver.BoundAddress);
            }
        }

        static byte[] Slice(byte[] buffer, int length)
        {
            var copy = new byte[length];
            System.Array.Copy(buffer, copy, length);
            return copy;
        }
    }
}
