using System;
using System.IO;
using System.Text;

namespace Downbeat.App
{
    /// <summary>Writes mono float samples as a 16-bit PCM WAV (what <c>--record-audio</c> saves). Samples beyond full scale are clipped and counted.</summary>
    public static class Pcm16Wav
    {
        const int HeaderBytes = 44;
        const int BytesPerSample = 2;
        const float PositiveScale = 32767f;
        const float NegativeScale = 32768f;

        public static byte[] Encode(float[] samples, int count, int sampleRate, out int clipped)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count), count, "0..samples.Length");
            if (sampleRate < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "must be positive");

            var dataBytes = count * BytesPerSample;
            var bytes = new byte[HeaderBytes + dataBytes];
            using (var stream = new MemoryStream(bytes))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);                        // PCM
                writer.Write((short)1);                        // mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * BytesPerSample);
                writer.Write((short)BytesPerSample);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                clipped = 0;
                for (var i = 0; i < count; i++)
                {
                    var v = samples[i];
                    if (v > 1f || v < -1f) clipped++;
                    var clamped = float.IsNaN(v) ? 0f : Math.Max(-1f, Math.Min(1f, v));
                    writer.Write((short)Math.Round(clamped * (clamped < 0f ? NegativeScale : PositiveScale)));
                }
            }
            return bytes;
        }
    }
}
