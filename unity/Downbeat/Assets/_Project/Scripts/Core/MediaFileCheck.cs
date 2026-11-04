using System;
using System.IO;

namespace Downbeat.Core
{
    /// <summary>
    /// Looks at the bytes of a picture or a sound that content.json refers to, without decoding it, and says what is wrong when it cannot be one: not that kind of file, cut short, or a header
    /// that makes no sense. A copy that stopped half way, a file that was saved as something else, an empty file: these are what a content folder that was edited by hand or copied over the
    /// network really suffers from, and they are found before the show starts rather than as a missing sound in the middle of it. The caller supplies the file length and a function that reads
    /// <c>count</c> bytes at <c>offset</c> (fewer at the end of the file).
    /// </summary>
    public static class MediaFileCheck
    {
        const int WavFormatChunkMinBytes = 16;
        const int MinSampleRate = 8000;
        const int MaxSampleRate = 192000;
        const int MaxChannels = 8;
        const ushort PcmFormat = 1;
        const ushort FloatFormat = 3;
        const ushort ExtensibleFormat = 0xFFFE;
        const int JpegTailWindow = 64;
        const int PngTailBytes = 12;

        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        static readonly byte[] PngEnd = { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };   // the "IEND" chunk and its checksum, which end every PNG
        static readonly byte[] JpegStart = { 0xFF, 0xD8, 0xFF };

        /// <summary>Null when the file looks like a whole WAV with at least one sample; otherwise what is wrong.</summary>
        public static string CheckWav(long length, Func<long, int, byte[]> read)
        {
            if (length <= 0) return "the file is empty";
            var head = read(0, 12);
            if (head.Length < 12 || !Matches(head, 0, "RIFF") || !Matches(head, 8, "WAVE")) return "not a WAV file (no RIFF/WAVE header)";

            long offset = 12;
            var formatSeen = false;
            while (offset + 8 <= length)
            {
                var header = read(offset, 8);
                if (header.Length < 8) break;
                long size = BitConverter.ToUInt32(header, 4);
                var body = offset + 8;
                if (Matches(header, 0, "fmt "))
                {
                    var problem = CheckWavFormat(read, body, size);
                    if (problem != null) return problem;
                    formatSeen = true;
                }
                else if (Matches(header, 0, "data"))
                {
                    if (!formatSeen) return "the data chunk comes before the fmt chunk";
                    if (size == 0) return "no sound in the file (the data chunk is empty)";
                    if (body + size > length) return "the file was cut short (the data chunk is longer than the file)";
                    return null;
                }
                offset = body + size + (size & 1);   // chunks are padded to an even size
            }
            return formatSeen ? "no data chunk (the file ends before the sound starts)" : "no fmt chunk";
        }

        static string CheckWavFormat(Func<long, int, byte[]> read, long body, long size)
        {
            if (size < WavFormatChunkMinBytes) return "the fmt chunk is too short";
            var format = read(body, WavFormatChunkMinBytes);
            if (format.Length < WavFormatChunkMinBytes) return "the file was cut short inside the fmt chunk";
            var tag = BitConverter.ToUInt16(format, 0);
            var channels = BitConverter.ToUInt16(format, 2);
            var rate = BitConverter.ToUInt32(format, 4);
            var bits = BitConverter.ToUInt16(format, 14);
            if (tag != PcmFormat && tag != FloatFormat && tag != ExtensibleFormat) return $"unsupported WAV format {tag} (PCM or float expected)";
            if (channels < 1 || channels > MaxChannels) return $"{channels} channels is not plausible";
            if (rate < MinSampleRate || rate > MaxSampleRate) return $"sample rate {rate} is not plausible";
            if (bits != 8 && bits != 16 && bits != 24 && bits != 32) return $"{bits} bits per sample is not supported";
            return null;
        }

        /// <summary>Null when a .png or .jpg/.jpeg file starts and ends the way a whole one does; otherwise what is wrong.</summary>
        public static string CheckImage(string fileName, long length, Func<long, int, byte[]> read)
        {
            var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            if (length <= 0) return "the file is empty";
            if (extension == ".png") return CheckPng(length, read);
            if (extension == ".jpg" || extension == ".jpeg") return CheckJpeg(length, read);
            return "not a kind of picture the meadow reads (.png or .jpg)";
        }

        static string CheckPng(long length, Func<long, int, byte[]> read)
        {
            if (length < PngSignature.Length + PngTailBytes) return "not a PNG file (too short)";
            if (!StartsWith(read(0, PngSignature.Length), PngSignature)) return "not a PNG file (wrong start)";
            var tail = read(length - PngTailBytes, PngTailBytes);
            return tail.Length == PngTailBytes && StartsWith(Slice(tail, 4, PngEnd.Length), PngEnd) ? null : "the file was cut short (a PNG ends with an IEND chunk)";
        }

        static string CheckJpeg(long length, Func<long, int, byte[]> read)
        {
            if (length < JpegStart.Length + 2) return "not a JPEG file (too short)";
            if (!StartsWith(read(0, JpegStart.Length), JpegStart)) return "not a JPEG file (wrong start)";
            var window = (int)Math.Min(JpegTailWindow, length);
            var tail = read(length - window, window);
            var end = tail.Length;
            while (end > 0 && tail[end - 1] == 0) end--;   // some writers pad a file with zeros
            return end >= 2 && tail[end - 2] == 0xFF && tail[end - 1] == 0xD9 ? null : "the file was cut short (a JPEG ends with the marker FF D9)";
        }

        static bool Matches(byte[] bytes, int offset, string text)
        {
            if (bytes.Length < offset + text.Length) return false;
            for (var i = 0; i < text.Length; i++)
            {
                if (bytes[offset + i] != (byte)text[i]) return false;
            }
            return true;
        }

        static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i]) return false;
            }
            return true;
        }

        static byte[] Slice(byte[] bytes, int offset, int count)
        {
            var slice = new byte[Math.Max(0, Math.Min(count, bytes.Length - offset))];
            if (slice.Length > 0) Array.Copy(bytes, offset, slice, 0, slice.Length);
            return slice;
        }
    }
}
