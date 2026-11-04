using System;
using System.Buffers.Binary;
using Downbeat.Core;

namespace Downbeat.Tests
{
    /// <summary>Builds hand-udp-v1 datagrams the way the Python tracker does (docs/protocol/hand-udp-v1.md): little endian, 44-byte header, 260 bytes per hand.</summary>
    public static class HandUdpTestPackets
    {
        public const int HeaderSize = 44;
        public const int HandSize = 260;
        const int Landmarks = 21;

        public static byte[] Build(long seq, double captureMs, TrackerState state, params HandTestData.Pose[] hands) =>
            Build(seq, captureMs, (byte)state, hands);

        public static byte[] Build(long seq, double captureMs, byte state, params HandTestData.Pose[] hands)
        {
            var packet = new byte[HeaderSize + HandSize * hands.Length];
            packet[0] = (byte)'D'; packet[1] = (byte)'B'; packet[2] = (byte)'H'; packet[3] = (byte)'1';
            packet[4] = 1;
            packet[5] = state;
            packet[6] = (byte)hands.Length;
            BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8), (ulong)seq);
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(16), BitConverter.DoubleToInt64Bits(captureMs));
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(24), BitConverter.DoubleToInt64Bits(captureMs + 10.0));
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(32), BitConverter.DoubleToInt64Bits(captureMs + 11.0));
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(40), BitConverter.SingleToInt32Bits(1f)); // aspect 1: palm length = pose size
            for (var i = 0; i < hands.Length; i++) WriteHand(packet, HeaderSize + HandSize * i, hands[i]);
            return packet;
        }

        static void WriteHand(byte[] packet, int offset, HandTestData.Pose pose)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset), pose.Id);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset + 4), BitConverter.SingleToInt32Bits(pose.Score));
            var points = HandTestData.Landmarks(pose.X, pose.Y, pose.Size, pose.Shape);   // all 21 points, as the Python tracker sends them
            for (var k = 0; k < Landmarks; k++) SetLandmark(packet, offset, k, points[k * 2], points[k * 2 + 1]);
        }

        static void SetLandmark(byte[] packet, int handOffset, int landmark, float x, float y)
        {
            var at = handOffset + 8 + landmark * 12;
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(at), BitConverter.SingleToInt32Bits(x));
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(at + 4), BitConverter.SingleToInt32Bits(y));
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(at + 8), BitConverter.SingleToInt32Bits(0f));
        }
    }
}
