using System;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class ExperienceLogTests
    {
        static void Observe(ExperienceLog log, double t, float energy, int level, ExperienceState state, bool hands = true) =>
            log.Observe(t, energy, level, state, energy, energy, hands, 0, 0, 0);

        [Test]
        public void Log_SamplesEveryInterval_AndRecordsStateAndLevelChanges()
        {
            var log = new ExperienceLog(0.5f, "test");

            Observe(log, 100.0, 0f, 0, ExperienceState.Idle);
            Observe(log, 100.2, 0.1f, 0, ExperienceState.Waking);
            Observe(log, 100.6, 0.25f, 1, ExperienceState.Playing);
            Observe(log, 101.2, 0.5f, 2, ExperienceState.Playing);
            Observe(log, 102.0, 0.8f, 3, ExperienceState.Playing);

            var data = log.Data;
            Assert.AreEqual(4, data.samples.Count, "100.0, 100.6, 101.2 and 102.0 (100.2 is within the interval of the first)");
            Assert.AreEqual(0f, data.samples[0].t, 1e-4f);
            Assert.AreEqual(2.0f, data.samples[3].t, 1e-4f, "run time starts at the first observation");
            Assert.AreEqual(5, data.events.Count, "two state changes and three level changes");
            Assert.AreEqual("Waking", data.events[0].to);
            Assert.AreEqual("Playing", data.events[1].to);
            Assert.AreEqual(3, data.summary.maxLevel);
            Assert.AreEqual(0.8f, data.summary.peakEnergy, 1e-6f);
            Assert.AreEqual(0.6f, data.summary.firstReachedLevel[1], 1e-4f);
            Assert.AreEqual(1.2f, data.summary.firstReachedLevel[2], 1e-4f);
            Assert.AreEqual(2.0f, data.summary.firstReachedLevel[3], 1e-4f);
        }

        [Test]
        public void Log_RemembersWhenTheLastVisitEnded()
        {
            var log = new ExperienceLog(0.5f, "test");
            Observe(log, 10.0, 0f, 0, ExperienceState.Idle);
            log.SessionStarted(10.1, 0f);
            log.SessionEnded(40.0, 0.01f);

            Assert.AreEqual(1, log.Data.summary.sessions);
            Assert.AreEqual(30.0f, log.Data.summary.returnedToIdleAt, 1e-4f);
            Assert.IsTrue(log.Data.events.Exists(e => e.kind == "session-started"));
            Assert.IsTrue(log.Data.events.Exists(e => e.kind == "session-ended"));
        }

        [Test]
        public void Log_WritesJsonThatReadsBack()
        {
            var log = new ExperienceLog(0.5f, "a note");
            Observe(log, 1.0, 0.3f, 1, ExperienceState.Playing);

            var back = JsonUtility.FromJson<ExperienceLogDto>(log.ToJson());

            Assert.AreEqual("a note", back.note);
            Assert.AreEqual(1, back.samples.Count);
            Assert.AreEqual(1, back.summary.maxLevel);
        }

        [Test]
        public void Constructor_RejectsAZeroInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExperienceLog(0f, "x"));
        }
    }

    public class Pcm16WavTests
    {
        [Test]
        public void Encode_WritesAValidMonoHeader_AndTheSamples()
        {
            var bytes = Pcm16Wav.Encode(new[] { 0f, 0.5f, -0.5f, 1f, -1f }, 5, 48000, out var clipped);

            Assert.AreEqual(44 + 10, bytes.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.AreEqual(1, BitConverter.ToInt16(bytes, 22), "mono");
            Assert.AreEqual(48000, BitConverter.ToInt32(bytes, 24));
            Assert.AreEqual(16, BitConverter.ToInt16(bytes, 34));
            Assert.AreEqual(10, BitConverter.ToInt32(bytes, 40));
            Assert.AreEqual(16384, BitConverter.ToInt16(bytes, 46), 1);
            Assert.AreEqual(32767, BitConverter.ToInt16(bytes, 50));
            Assert.AreEqual(-32768, BitConverter.ToInt16(bytes, 52));
            Assert.AreEqual(0, clipped, "full scale itself is not clipping");
        }

        [Test]
        public void Encode_ClipsAndCountsSamplesBeyondFullScale_AndOnlyWritesTheRecordedPart()
        {
            var bytes = Pcm16Wav.Encode(new[] { 1.5f, -2f, 0.1f, 9f, 9f }, 3, 44100, out var clipped);

            Assert.AreEqual(2, clipped);
            Assert.AreEqual(44 + 6, bytes.Length, "count 3: the last two samples are not part of the recording");
            Assert.AreEqual(32767, BitConverter.ToInt16(bytes, 44));
            Assert.AreEqual(-32768, BitConverter.ToInt16(bytes, 46));
        }

        [Test]
        public void Encode_RejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Pcm16Wav.Encode(new float[2], 3, 48000, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pcm16Wav.Encode(new float[2], 2, 0, out _));
            Assert.Throws<ArgumentNullException>(() => Pcm16Wav.Encode(null, 0, 48000, out _));
        }
    }
}
