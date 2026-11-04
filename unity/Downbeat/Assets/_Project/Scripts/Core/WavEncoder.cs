using System;
using System.IO;
using System.Text;

namespace Downbeat.Core
{
    /// <summary>Writes 16-bit PCM mono WAV files (the clock measurement writes its click this way).</summary>
    public static class WavEncoder
    {
        const int HeaderBytes = 44;
        const int FormatChunkBytes = 16;
        const short PcmFormat = 1;
        const short Mono = 1;
        const short BitsPerSample = 16;
        const float FullScale = 32767f;

        public static byte[] EncodePcm16Mono(float[] samples, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var dataBytes = samples.Length * 2;
            using (var stream = new MemoryStream(HeaderBytes + dataBytes))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(HeaderBytes - 8 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(FormatChunkBytes);
                writer.Write(PcmFormat);
                writer.Write(Mono);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write(BitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                foreach (var sample in samples)
                {
                    var clamped = Math.Max(-1f, Math.Min(1f, sample));
                    writer.Write((short)Math.Round(clamped * FullScale));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
