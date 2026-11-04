using System;
using System.Text;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The click of the clock measurement is the same signal as tools/gen_click.mjs (3 kHz burst, 3 ms decay, 30 ms, 48 kHz).</summary>
    public class ClickWavTests
    {
        const int Rate = 48000;

        [Test]
        public void Click_Is30MsLong_AndPeaksNearItsAmplitude()
        {
            var click = ClickSignal.Render(Rate);

            Assert.AreEqual(1440, click.Length);
            var peak = 0f;
            foreach (var v in click) peak = Math.Max(peak, Math.Abs(v));
            Assert.That(peak, Is.GreaterThan(0.4f).And.LessThanOrEqualTo(0.6f));
        }

        [Test]
        public void Click_StartsAtZeroAndDecays()
        {
            var click = ClickSignal.Render(Rate);

            Assert.AreEqual(0f, click[0], 1e-9f);
            var tail = 0f;
            for (var i = click.Length - 100; i < click.Length; i++) tail = Math.Max(tail, Math.Abs(click[i]));
            Assert.Less(tail, 1e-3f, "the burst has died away by the end of the 30 ms");
        }

        [Test]
        public void Wav_HasAPcm16MonoHeaderAndTheRightSizes()
        {
            var wav = WavEncoder.EncodePcm16Mono(new[] { 0f, 0.5f, -0.5f, 1f }, Rate);

            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 20), "PCM");
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22), "mono");
            Assert.AreEqual(Rate, BitConverter.ToInt32(wav, 24));
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));
            Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
            Assert.AreEqual(8, BitConverter.ToInt32(wav, 40));
            Assert.AreEqual(44 + 8, wav.Length);
            Assert.AreEqual(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        }

        [Test]
        public void Wav_ClampsAndScalesSamplesToSigned16Bit()
        {
            var wav = WavEncoder.EncodePcm16Mono(new[] { 0.5f, -2f, 2f }, Rate);

            Assert.AreEqual(16384, BitConverter.ToInt16(wav, 44), 1);
            Assert.AreEqual(-32767, BitConverter.ToInt16(wav, 46), 1);
            Assert.AreEqual(32767, BitConverter.ToInt16(wav, 48), 1);
        }
    }
}
