using System;
using System.Globalization;
using System.IO;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The parts of the app's own recorder that need no running audio: how it writes the clock, and that it stays quiet until it was started. (The quit and destroy messages of the runner need play mode: RunRecorderEarlyEndPlayModeTests.)</summary>
    public class RunRecorderTests
    {
        [Test]
        public void TheStartTime_IsWrittenAsIsoUtcWithMilliseconds_AndParsesBackToTheSameInstant()
        {
            var instant = new DateTime(2026, 11, 2, 1, 23, 45, 678, DateTimeKind.Utc);

            var text = RunRecorder.FormatUtc(instant);

            Assert.AreEqual("2026-11-02T01:23:45.678Z", text);
            var parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            Assert.AreEqual(instant, parsed);
        }

        [Test]
        public void TheStartTime_OfALocalClockReading_IsConvertedToUtcFirst()
        {
            var local = new DateTime(2026, 11, 2, 10, 0, 0, 5, DateTimeKind.Local);

            var text = RunRecorder.FormatUtc(local);

            StringAssert.EndsWith("Z", text);
            Assert.AreEqual(local.ToUniversalTime(), DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        }

        [Test]
        public void SecondZeroOfTheVideo_IsTheStartPlusTheOutputLatency_ToTheTick()
        {
            var started = new DateTime(2026, 11, 2, 1, 23, 45, 678, DateTimeKind.Utc);

            var videoZero = RunRecorder.VideoZeroUtc(started, 0.0427);

            Assert.AreEqual(started.AddTicks(427000), videoZero);
            Assert.AreEqual(DateTimeKind.Utc, videoZero.Kind);
            Assert.AreEqual("2026-11-02T01:23:45.721Z", RunRecorder.FormatUtc(RunRecorder.VideoZeroUtc(started, 0.043)));
            Assert.AreEqual(started, RunRecorder.VideoZeroUtc(started, 0.0), "no latency, no shift");
        }

        [Test]
        public void FinishingEarly_BeforeStart_WritesNothingAndMakesNoFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "downbeat-recorder-never-" + Guid.NewGuid().ToString("N"));
            var runnerObject = new GameObject("recorder-runner");
            try
            {
                var recorder = new RunRecorder(new FakeAudioEngine(), runnerObject.AddComponent<CoroutineRunner>(), LaunchArgs.Parse(new[] { "--record-dir", dir }));

                recorder.FinishEarly();

                Assert.IsFalse(recorder.IsFinished, "nothing was started, so nothing is finished");
                Assert.IsFalse(Directory.Exists(dir), "a recorder that was never started leaves no folder");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runnerObject);
            }
        }
    }
}
