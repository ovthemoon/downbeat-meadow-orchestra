using System;
using System.Globalization;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HealthAccumulatorTests
    {
        static readonly DateTime T0 = new DateTime(2026, 10, 29, 22, 0, 0, DateTimeKind.Utc);

        static HealthRow Snap(HealthAccumulator a, AudioStats audio = default, int errors = 0, double uptime = 60.0) =>
            a.Snapshot(T0, uptime, 40L * 1024 * 1024, 120L * 1024 * 1024, audio, errors);

        static void Frames(HealthAccumulator a, int count, float deltaSec)
        {
            for (var i = 0; i < count; i++) a.AddFrame(deltaSec);
        }

        [Test]
        public void Snapshot_ComputesFpsAndP95()
        {
            var a = new HealthAccumulator();
            Frames(a, 90, 0.010f);   // 90 frames of 10 ms
            Frames(a, 10, 0.050f);   // 10 slow frames of 50 ms: 1.4 s in all

            var row = Snap(a);

            Assert.AreEqual(100 / 1.4, row.FpsAverage, 0.01);
            Assert.AreEqual(50.0, row.FrameMsP95, 0.5, "the 95th of 100 sorted frames is a slow one");
            Assert.AreEqual(50.0, row.FrameMsMax, 0.5);
            Assert.AreEqual(0, row.FramesOver100Ms);
        }

        [Test]
        public void Snapshot_ResetsAfterRow()
        {
            var a = new HealthAccumulator();
            Frames(a, 100, 0.010f);
            a.AddFrame(0.5f);
            a.AddTrackerPacket(12.0);
            a.SetHandsPresent(true);
            Snap(a);

            var second = Snap(a);

            Assert.AreEqual(0.0, second.FpsAverage);
            Assert.AreEqual(0.0, second.FrameMsMax);
            Assert.AreEqual(0, second.FramesOver100Ms);
            Assert.AreEqual(0.0, second.TrackerFps);
            Assert.IsTrue(double.IsNaN(second.TrackerAgeMsP95), "no packets in this interval: no age");
            Assert.AreEqual(0.0, second.HandsPresentRatio);
        }

        [Test]
        public void Snapshot_CountsFramesOver100Ms_AndKeepsTheLongestFrame()
        {
            var a = new HealthAccumulator();
            Frames(a, 50, 0.016f);
            a.AddFrame(0.983f);
            a.AddFrame(0.101f);
            a.AddFrame(0.100f);   // exactly 100 ms is not over

            var row = Snap(a);

            Assert.AreEqual(2, row.FramesOver100Ms);
            Assert.AreEqual(983.0, row.FrameMsMax, 0.5);
        }

        [Test]
        public void Snapshot_FpsMin1s_IsTheSlowestCompleteSecond()
        {
            var a = new HealthAccumulator();
            Frames(a, 100, 0.010f);   // second 1: 100 fps
            Frames(a, 20, 0.050f);    // second 2: 20 fps
            Frames(a, 100, 0.010f);   // second 3: 100 fps
            Frames(a, 30, 0.010f);    // 0.3 s left over: not a complete second

            var row = Snap(a);

            Assert.AreEqual(20.0, row.FpsMin1s, 0.5);
        }

        [Test]
        public void Snapshot_FpsMin1s_FallsBackToTheAverageWhenNoSecondIsComplete()
        {
            var a = new HealthAccumulator();
            Frames(a, 10, 0.010f);

            var row = Snap(a);

            Assert.AreEqual(row.FpsAverage, row.FpsMin1s, 1e-9);
        }

        [Test]
        public void Snapshot_DspStall_IsTheLargestStepOfTheAudioClockBetweenTwoFrames()
        {
            var a = new HealthAccumulator();
            a.ObserveAudioClock(10.000);
            a.ObserveAudioClock(10.021);      // a normal frame: the clock moves by a block or two
            a.ObserveAudioClock(10.021);      // the clock only moves per block: no step
            a.ObserveAudioClock(10.981);      // 0.96 s in one frame: the main thread stalled while the audio clock ran on
            a.ObserveAudioClock(11.002);

            var row = Snap(a);
            var next = Snap(a);

            Assert.AreEqual(0.96, row.MaxDspStallSec, 0.001);
            Assert.AreEqual(0.0, next.MaxDspStallSec, "the maximum is per row");
        }

        [Test]
        public void Snapshot_DspStall_IgnoresAClockThatJumpsBack()
        {
            var a = new HealthAccumulator();
            a.ObserveAudioClock(50.0);
            a.ObserveAudioClock(3.0);         // a device reset
            a.ObserveAudioClock(3.02);

            Assert.AreEqual(0.02, Snap(a).MaxDspStallSec, 0.001);
        }

        [Test]
        public void Snapshot_HandsPresentRatio_IsTheShareOfFramesWithHands()
        {
            var a = new HealthAccumulator();
            for (var i = 0; i < 100; i++)
            {
                a.AddFrame(0.01f);
                a.SetHandsPresent(i < 25);
            }

            Assert.AreEqual(0.25, Snap(a).HandsPresentRatio, 1e-9);
        }

        [Test]
        public void Snapshot_TrackerFpsAndAgeP95()
        {
            var a = new HealthAccumulator();
            Frames(a, 200, 0.010f);   // 2 s
            for (var i = 1; i <= 60; i++) a.AddTrackerPacket(i);   // 60 packets, ages 1..60 ms

            var row = Snap(a);

            Assert.AreEqual(30.0, row.TrackerFps, 0.01);
            Assert.AreEqual(57.0, row.TrackerAgeMsP95, 1.0);
        }

        [Test]
        public void Snapshot_AudioCountsAreWhatHappenedSinceTheLastRow()
        {
            var a = new HealthAccumulator();

            var first = Snap(a, new AudioStats(scheduled: 120, late: 2, stolen: 5, missingSample: 0));
            var second = Snap(a, new AudioStats(scheduled: 150, late: 2, stolen: 9, missingSample: 0));

            Assert.AreEqual(120, first.NotesScheduled);
            Assert.AreEqual(2, first.NotesLate);
            Assert.AreEqual(5, first.VoicesStolen);
            Assert.AreEqual(30, second.NotesScheduled);
            Assert.AreEqual(0, second.NotesLate);
            Assert.AreEqual(4, second.VoicesStolen);
        }

        [Test]
        public void Snapshot_ErrorsAreWhatHappenedSinceTheLastRow()
        {
            var a = new HealthAccumulator();

            Assert.AreEqual(3, Snap(a, errors: 3).Errors);
            Assert.AreEqual(0, Snap(a, errors: 3).Errors);
            Assert.AreEqual(2, Snap(a, errors: 5).Errors);
        }

        [Test]
        public void Snapshot_MemoryIsInMegabytes()
        {
            var row = Snap(new HealthAccumulator());

            Assert.AreEqual(40.0, row.MonoMb, 1e-9);
            Assert.AreEqual(120.0, row.AllocMb, 1e-9);
        }

        [Test]
        public void Snapshot_IgnoresNonsenseFrames()
        {
            var a = new HealthAccumulator();
            a.AddFrame(float.NaN);
            a.AddFrame(-1f);
            a.AddFrame(0f);
            a.AddFrame(0.01f);

            var row = Snap(a);

            Assert.AreEqual(100.0, row.FpsAverage, 0.01, "only the one real frame counts");
        }

        [Test]
        public void Csv_HeaderMatchesColumns()
        {
            var a = new HealthAccumulator();
            Frames(a, 100, 0.01f);
            var row = Snap(a).WithRun("Playing", 2, 3, 1, 150L * 1024 * 1024, "ok");

            var header = HealthRow.CsvHeader.Split(',');
            var cells = row.ToCsv().Split(',');

            Assert.AreEqual(header.Length, cells.Length);
            CollectionAssert.AreEqual(
                new[]
                {
                    "timestamp_utc", "uptime_s", "fps_avg", "fps_min_1s", "frame_ms_p95", "frame_ms_max", "frames_over_100ms", "max_dsp_stall_s", "mono_mb", "alloc_mb", "working_set_mb",
                    "tracker_fps", "tracker_age_ms_p95", "hands_present_ratio", "notes_scheduled", "notes_late", "voices_stolen", "nature_dropped", "errors", "experience_state", "energy_level", "sessions", "audio_state", "render_scale", "quality_level", "starve_ignored", "quality_changes",
                },
                header);
            Assert.AreEqual("2026-10-29T22:00:00Z", cells[0]);
            Assert.AreEqual("Playing", cells[Array.IndexOf(header, "experience_state")]);
            Assert.AreEqual("2", cells[Array.IndexOf(header, "energy_level")]);
            Assert.AreEqual("3", cells[Array.IndexOf(header, "sessions")]);
            Assert.AreEqual("1", cells[Array.IndexOf(header, "nature_dropped")]);
            Assert.AreEqual("150.0", cells[Array.IndexOf(header, "working_set_mb")]);
            Assert.AreEqual("ok", cells[Array.IndexOf(header, "audio_state")]);
            Assert.AreEqual(string.Empty, cells[Array.IndexOf(header, "tracker_age_ms_p95")], "no tracker packets: an empty cell, not a made-up number");
        }

        [Test]
        public void Csv_UsesInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // decimal comma
                var a = new HealthAccumulator();
                Frames(a, 100, 0.0123f);
                var csv = Snap(a).ToCsv();

                Assert.IsFalse(csv.Split(',').Any(c => c.Contains(";")));
                Assert.AreEqual(HealthRow.CsvHeader.Split(',').Length, csv.Split(',').Length, "a decimal comma would add columns");
                Assert.IsTrue(csv.Contains("81.3"), csv);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Csv_ExperienceStateNeverContainsACommaOrNewline()
        {
            var row = Snap(new HealthAccumulator()).WithRun("Pla,ying\nx", 0, 0, 0, 0);

            Assert.AreEqual(1, row.ToCsv().Split('\n').Length);
            Assert.AreEqual(HealthRow.CsvHeader.Split(',').Length, row.ToCsv().Split(',').Length);
        }
    }
}
