using System;
using System.Buffers.Binary;

namespace Downbeat.Core
{
    /// <summary>
    /// Reads a hand-udp-v1 datagram (docs/protocol/hand-udp-v1.md) into the double[136] hand buffer that <see cref="HandFrameReader"/> reads.
    /// The datagram comes from another process, so everything is checked first and the buffer is only written for a valid packet.
    /// </summary>
    public static class HandUdpCodec
    {
        public const int HeaderSize = 44;
        public const int HandSize = 260;
        public const byte Version = 1;

        const int StateOffset = 5;
        const int HandCountOffset = 6;
        const int SeqOffset = 8;
        const int CaptureMsOffset = 16;
        const int InferEndMsOffset = 24;
        const int AspectOffset = 40;
        const int HandIdOffset = 0;
        const int HandScoreOffset = 4;
        const int LandmarksOffset = 8;
        const int LandmarkStride = 12;
        const int FloatBytes = 4;

        /// <summary>
        /// False when <paramref name="packet"/> is not a valid hand-udp-v1 datagram or the buffer is too small; the buffer is then untouched.
        /// <paramref name="mirrorX"/> flips x (a webcam image is a mirror of what the person sees); y and z are kept.
        /// </summary>
        public static bool TryDecodeToBuffer(ReadOnlySpan<byte> packet, bool mirrorX, double[] buffer, out long seq)
        {
            seq = 0;
            if (buffer == null || buffer.Length < HandBufferLayout.Length) return false;
            if (!IsValidHeader(packet, out var handCount)) return false;
            var rawSeq = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(SeqOffset));
            if (rawSeq > long.MaxValue) return false;

            seq = (long)rawSeq;
            Array.Clear(buffer, 0, buffer.Length);
            buffer[HandBufferLayout.SeqIndex] = seq;
            buffer[HandBufferLayout.CaptureMsIndex] = ReadDouble(packet, CaptureMsOffset);
            buffer[HandBufferLayout.InferEndMsIndex] = ReadDouble(packet, InferEndMsOffset);
            buffer[HandBufferLayout.StateIndex] = packet[StateOffset];
            buffer[HandBufferLayout.HandCountIndex] = handCount;
            buffer[HandBufferLayout.AspectIndex] = ReadFloat(packet, AspectOffset);
            for (var slot = 0; slot < handCount; slot++) CopyHand(packet, HeaderSize + HandSize * slot, mirrorX, buffer, HandBufferLayout.HandBase(slot));
            return true;
        }

        static bool IsValidHeader(ReadOnlySpan<byte> packet, out int handCount)
        {
            handCount = 0;
            if (packet.Length < HeaderSize) return false;
            if (packet[0] != (byte)'D' || packet[1] != (byte)'B' || packet[2] != (byte)'H' || packet[3] != (byte)'1') return false;
            if (packet[4] != Version) return false;
            if (packet[StateOffset] > (byte)TrackerState.ModelFailed) return false;
            handCount = packet[HandCountOffset];
            if (handCount > HandBufferLayout.MaxHands) return false;
            return packet.Length == HeaderSize + HandSize * handCount;
        }

        static void CopyHand(ReadOnlySpan<byte> packet, int offset, bool mirrorX, double[] buffer, int target)
        {
            buffer[target] = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset + HandIdOffset));
            buffer[target + 1] = ReadFloat(packet, offset + HandScoreOffset);
            for (var k = 0; k < HandBufferLayout.LandmarksPerHand; k++)
            {
                var from = offset + LandmarksOffset + k * LandmarkStride;
                var x = ReadFloat(packet, from);
                buffer[target + 2 + k * 3] = mirrorX ? 1.0 - x : x;
                buffer[target + 3 + k * 3] = ReadFloat(packet, from + FloatBytes);
                buffer[target + 4 + k * 3] = ReadFloat(packet, from + 2 * FloatBytes);
            }
        }

        static double ReadDouble(ReadOnlySpan<byte> packet, int offset) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(offset)));

        static float ReadFloat(ReadOnlySpan<byte> packet, int offset) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset)));
    }
}
