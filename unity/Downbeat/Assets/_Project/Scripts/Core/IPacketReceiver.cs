using System;

namespace Downbeat.Core
{
    /// <summary>Where hand datagrams come from. The real one is a UDP socket on a background thread; tests deliver packets by hand.</summary>
    public interface IPacketReceiver : IDisposable
    {
        /// <summary>Starts listening on <paramref name="port"/>; every datagram is handed to <paramref name="onPacket"/> (data, length) on any thread. False when the port cannot be used.</summary>
        bool TryStart(int port, Action<byte[], int> onPacket, out string error);

        void Stop();
    }
}
