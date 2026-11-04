using Downbeat.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class VoiceFaderTests
    {
        const int Channels = 2;
        const int FadeSamples = 100;

        GameObject _host;
        VoiceFader _fader;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("fader test");
            _fader = _host.AddComponent<VoiceFader>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        static float[] Ones(int frames)
        {
            var buffer = new float[frames * Channels];
            for (var i = 0; i < buffer.Length; i++) buffer[i] = 1f;
            return buffer;
        }

        [Test]
        public void Process_LeavesTheSoundAlone_WhenNoFadeWasStarted()
        {
            var buffer = Ones(64);

            _fader.Process(buffer, Channels);

            CollectionAssert.AreEqual(Ones(64), buffer);
        }

        [Test]
        public void Process_RampsTheGainDownLinearlyToZero_OverTheFadeLength()
        {
            var buffer = Ones(FadeSamples + 50);
            _fader.BeginFade(FadeSamples);

            _fader.Process(buffer, Channels);

            Assert.AreEqual(1f, buffer[0], 1e-6f, "starts at full gain");
            var previous = 1f;
            for (var frame = 0; frame < FadeSamples; frame++)
            {
                var gain = buffer[frame * Channels];
                Assert.LessOrEqual(gain, previous + 1e-6f, $"frame {frame} must not be louder than the one before");
                Assert.LessOrEqual(previous - gain, 1.5f / FadeSamples, $"no step bigger than one fade sample at frame {frame}: no click");
                Assert.AreEqual(gain, buffer[frame * Channels + 1], 1e-6f, "both channels get the same gain");
                previous = gain;
            }
            for (var frame = FadeSamples; frame < FadeSamples + 50; frame++) Assert.AreEqual(0f, buffer[frame * Channels], "silent after the fade");
        }

        [Test]
        public void Process_ContinuesTheFadeAcrossBuffers_AndStaysSilentUntilReset()
        {
            _fader.BeginFade(FadeSamples);
            var first = Ones(60);
            var second = Ones(60);
            var third = Ones(60);

            _fader.Process(first, Channels);
            _fader.Process(second, Channels);
            _fader.Process(third, Channels);

            Assert.Greater(first[0], second[0], "the second buffer continues where the first stopped");
            Assert.AreEqual(0f, second[(FadeSamples - 60) * Channels], "reaches zero at the end of the fade length");
            Assert.AreEqual(0f, third[0]);
            Assert.AreEqual(0f, third[third.Length - 1]);
        }

        [Test]
        public void AResetDuringTheCallback_IsNotOverwrittenWhenTheCallbackEnds()
        {
            _fader.BeginFade(FadeSamples);

            _fader.Process(Ones(40), Channels, () => _fader.Reset()); // the main thread starts the next note while the audio thread is half way through the old fade
            var next = Ones(8);
            _fader.Process(next, Channels);

            CollectionAssert.AreEqual(Ones(8), next, "the next note is not faded: the old fade's state was not written back over the reset");
        }

        [Test]
        public void AFadeThatEndsDuringACallback_DoesNotSilenceTheNoteThatReplacedIt()
        {
            _fader.BeginFade(30);

            _fader.Process(Ones(60), Channels, () => _fader.Reset()); // the fade runs out inside this buffer, and the voice is reused before the callback returns
            var next = Ones(8);
            _fader.Process(next, Channels);

            CollectionAssert.AreEqual(Ones(8), next, "no stale 'fade done' flag: the new note is audible");
        }

        [Test]
        public void ANewFadeStartedDuringTheCallback_KeepsItsOwnLength()
        {
            _fader.BeginFade(FadeSamples);

            _fader.Process(Ones(40), Channels, () => _fader.BeginFade(200));
            var next = Ones(10);
            _fader.Process(next, Channels);

            Assert.AreEqual(1f, next[0], 1e-6f, "the new fade starts from full gain");
            Assert.AreEqual(1f - 9f / 200f, next[9 * Channels], 1e-4f, "and runs over its own 200 samples, not the remainder of the old one");
        }

        [Test]
        public void Reset_BringsBackFullGainForTheNextNote()
        {
            _fader.BeginFade(10);
            _fader.Process(Ones(64), Channels);
            _fader.Reset();
            var buffer = Ones(8);

            _fader.Process(buffer, Channels);

            CollectionAssert.AreEqual(Ones(8), buffer);
        }
    }
}
