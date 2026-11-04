using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// A UDP socket bound to the loopback address only (the hand tracker runs on the same PC; nothing from the network is accepted) and a
    /// background thread that hands every datagram to the callback.
    /// </summary>
    public sealed class UdpSocketReceiver : IPacketReceiver
    {
        const int PollMicroseconds = 250_000;
        const int ThreadStopMs = 1000;
        const string ThreadName = "UdpHandReceiver";
        const double WarningIntervalSec = 5.0;
        const int ErrorBackoffMs = 10;

        Socket _socket;
        Thread _thread;
        volatile bool _stopping;

        public IPAddress BoundAddress => IPAddress.Loopback;

        public bool TryStart(int port, Action<byte[], int> onPacket, out string error)
        {
            Stop();
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException e)
            {
                socket?.Close();
                error = $"{e.SocketErrorCode}: {e.Message}";
                return false;
            }

            _stopping = false;
            _socket = socket;
            _thread = new Thread(() => ReceiveLoop(socket, onPacket)) { IsBackground = true, Name = ThreadName };
            _thread.Start();
            error = null;
            return true;
        }

        public void Stop()
        {
            if (_socket == null) return;
            _stopping = true;
            _socket.Close();
            _thread?.Join(ThreadStopMs);
            _socket = null;
            _thread = null;
        }

        public void Dispose() => Stop();

        void ReceiveLoop(Socket socket, Action<byte[], int> onPacket)
        {
            var buffer = new byte[PacketRing.SlotBytes];
            var warnings = new WarningRateLimiter(WarningIntervalSec); // a failing socket can fail thousands of times a second
            var clock = Stopwatch.StartNew();
            while (!_stopping)
            {
                try
                {
                    if (!socket.Poll(PollMicroseconds, SelectMode.SelectRead)) continue;
                    var length = socket.Receive(buffer);
                    if (length > 0) onPacket(buffer, length);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException e)
                {
                    if (_stopping) return;
                    // a datagram bigger than the buffer, or a transient error: keep listening, but say so at most every few seconds
                    if (warnings.TryAcquire(clock.Elapsed.TotalSeconds, out var held)) UnityEngine.Debug.LogWarning($"[UdpSocketReceiver] receive failed: {e.SocketErrorCode}" + (held > 0 ? $" ({held} more failures since the last message)" : string.Empty));
                    Thread.Sleep(ErrorBackoffMs); // a socket that keeps failing must not turn this thread into a busy loop
                }
            }
        }
    }
}
