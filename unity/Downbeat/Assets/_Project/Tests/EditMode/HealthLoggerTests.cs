using System;
using System.IO;
using System.Linq;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HealthLoggerTests
    {
        sealed class FakeSource : IHealthSource
        {
            public DateTime UtcNow { get; set; }
            public AudioStats Audio { get; set; }
            public int Errors { get; set; }
            public string ExperienceState { get; set; } = "Playing";
            public int EnergyLevel { get; set; } = 2;
            public int Sessions { get; set; } = 1;
            public int NatureDropped { get; set; }
            public float RenderScale { get; set; } = 1f;
            public int QualityLevel { get; set; } = -1;
            public bool StarveIgnored { get; set; }
            public int QualityChangesInTheMinute { get; set; }
            public int TakeQualityChanges()
            {
                var count = QualityChangesInTheMinute;
                QualityChangesInTheMinute = 0;
                return count;
            }
            public bool AudioFailed { get; set; }
            public AppPhase App { get; set; } = AppPhase.Running;
            public long MonoBytes => 30L * 1024 * 1024;
            public long AllocatedBytes => 90L * 1024 * 1024;
            public long WorkingSetBytes => 200L * 1024 * 1024;
        }

        string _dir;
        static readonly DateTime BaseUtc = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-health-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        HealthFileWriter Writer(string primary = null, string fallback = null, Action<string> warn = null) =>
            new HealthFileWriter(primary ?? Path.Combine(_dir, "logs"), fallback ?? Path.Combine(_dir, "fallback"), warn ?? (_ => { }));

        static void Run(HealthLogger logger, FakeSource source, double fromSec, double toSec, float step = 0.01f)
        {
            for (var t = fromSec; t < toSec; t += step)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, step, t, true, false);
            }
        }

        static string[] Lines(string path) => File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();

        [Test]
        public void Logger_WritesOneRowEveryInterval_AndTheHeaderOnce()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            Run(logger, source, 0.0, 185.0);

            var lines = Lines(writer.RowFileFor(BaseUtc));
            Assert.AreEqual(1 + 3, lines.Length, "a header and the rows at 60, 120 and 180 s");
            Assert.AreEqual(HealthRow.CsvHeader, lines[0]);
            Assert.AreEqual(3, logger.Rows);
            var cells = lines[1].Split(',');
            Assert.AreEqual(HealthRow.CsvHeader.Split(',').Length, cells.Length);
            Assert.AreEqual(100.0, double.Parse(cells[2], System.Globalization.CultureInfo.InvariantCulture), 1.5, "fps_avg of 10 ms frames");
            Assert.AreEqual("Playing", cells[19]);
        }

        [Test]
        public void Logger_WritesAHeartbeatEveryFiveSeconds_AsAUtcTime()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            Run(logger, source, 0.0, 12.3);

            var text = File.ReadAllLines(Path.Combine(writer.Directory, "heartbeat.txt"))[0].Trim();
            var stamp = DateTime.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.AreEqual(DateTimeKind.Utc, stamp.Kind);
            Assert.AreEqual(10.0, (stamp - BaseUtc).TotalSeconds, 0.05, "the beat at 10 s was the last one before 12.3 s");
        }

        [Test]
        public void Heartbeat_SecondLine_SaysWhetherTheAudioClockIsMoving()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            string AudioLine() => File.ReadAllLines(Path.Combine(writer.Directory, "heartbeat.txt"))[1];

            Run(logger, source, 0.0, 6.0);                      // the audio clock moves with every frame
            Assert.AreEqual("audio=ok", AudioLine());

            for (var t = 6.0; t < 12.5; t += 0.01)               // the audio device is gone: the clock stands still while the frames go on
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.01f, 5.99, true, false);
            }
            Assert.AreEqual("audio=stalled", AudioLine(), "more than 3 s without a step of the audio clock");
        }

        [Test]
        public void Heartbeat_AudioIsOkAgain_WhenTheClockMovesAgain()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 1.0);
            for (var t = 1.0; t < 6.5; t += 0.01)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.01f, 0.99, true, false);
            }
            Run(logger, source, 6.5, 11.5);                     // moving again (audio time = real time here)

            Assert.AreEqual("audio=ok", File.ReadAllLines(Path.Combine(writer.Directory, "heartbeat.txt"))[1]);
        }

        // ---- the app is alive before its sound is (a missing sound device must not look like a dead app) --------------------------------------------

        static string[] HeartbeatLines(HealthFileWriter writer) => File.ReadAllLines(Path.Combine(writer.Directory, "heartbeat.txt"));

        static void RunWhileLoading(HealthLogger logger, FakeSource source, double fromSec, double toSec, float step = 0.05f, double audioNow = 0.0, bool countFrames = false)
        {
            if (source.App == AppPhase.Running) source.App = AppPhase.Loading;
            for (var t = fromSec; t < toSec; t += step)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, step, audioNow, false, false, countFrames);   // the audio clock stands still at 0: no sound device
            }
        }

        [Test]
        public void Heartbeat_IsWrittenFromTheFirstTick_AndSaysTheSoundIsStarting()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            source.App = AppPhase.Loading;
            source.UtcNow = BaseUtc;
            logger.Tick(0.0, 0.016f, 0.0, false, false, countFrames: false);

            CollectionAssert.AreEqual(new[] { "audio=starting", "app=loading" }, HeartbeatLines(writer).Skip(1).ToArray(), "the first line of news is that the app is there, loading, and the sound is not up yet");
        }

        [Test]
        public void Heartbeat_KeepsSayingStarting_AsLongAsTheAudioClockNeverMoves_AndKeepsBeating()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            RunWhileLoading(logger, source, 0.0, 41.0);

            Assert.AreEqual("audio=starting", HeartbeatLines(writer)[1], "40 s without a step of the clock is still 'starting', not 'stalled': it never ran");
            var stamp = DateTime.Parse(HeartbeatLines(writer)[0], null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.AreEqual(40.0, (stamp - BaseUtc).TotalSeconds, 0.1, "and the beat is as fresh as ever: the app is alive");
        }

        [Test]
        public void Heartbeat_SaysFailed_WhenTheAppHasGivenUpOnTheSound_AndAppFailedToo()
        {
            var writer = Writer();
            var source = new FakeSource { AudioFailed = true, App = AppPhase.Failed };
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            RunWhileLoading(logger, source, 0.0, 6.0);

            CollectionAssert.AreEqual(new[] { "audio=failed", "app=failed" }, HeartbeatLines(writer).Skip(1).ToArray());
        }

        [Test]
        public void Heartbeat_SaysAppFailed_WithoutBlamingTheSound_WhenTheFailureIsSomethingElse()
        {
            var writer = Writer();
            var source = new FakeSource { App = AppPhase.Failed };       // the content could not be loaded
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            Run(logger, source, 0.0, 6.0);                           // the audio clock moves

            CollectionAssert.AreEqual(new[] { "audio=ok", "app=failed" }, HeartbeatLines(writer).Skip(1).ToArray());
        }

        [Test]
        public void Heartbeat_GoesFromStartingToOk_WhenTheClockStartsToMove()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            RunWhileLoading(logger, source, 0.0, 6.0);
            Assert.AreEqual("audio=starting", HeartbeatLines(writer)[1]);

            Run(logger, source, 6.0, 12.0);                          // the clock moves (audio time = real time)

            Assert.AreEqual("audio=ok", HeartbeatLines(writer)[1]);
        }

        [Test]
        public void Heartbeat_SaysStalled_NotStarting_WhenTheClockStopsAfterItRan()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 2.0);
            for (var t = 2.0; t < 8.5; t += 0.01)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.01f, 1.99, true, false);
            }

            Assert.AreEqual("audio=stalled", HeartbeatLines(writer)[1]);
        }

        [Test]
        public void Heartbeat_SaysTheAppIsStillLoading_EvenWhenTheAudioClockRuns()
        {
            // A Boot that threw leaves the app in Loading for ever while the audio clock goes on: audio=ok must not be taken for "the app is up".
            var writer = Writer();
            var source = new FakeSource { App = AppPhase.Loading };
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            for (var t = 0.0; t < 12.0; t += 0.05)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.05f, t, false, false, countFrames: false);   // the clock moves with real time
            }

            CollectionAssert.AreEqual(new[] { "audio=ok", "app=loading" }, HeartbeatLines(writer).Skip(1).ToArray());
        }

        [Test]
        public void Heartbeat_SaysRunning_OnlyOnceTheAppReachedRunning_AndAtOnce()
        {
            var writer = Writer();
            var source = new FakeSource { App = AppPhase.Loading };
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 1.0);
            Assert.AreEqual("app=loading", HeartbeatLines(writer)[2]);

            source.App = AppPhase.Running;
            Run(logger, source, 1.0, 1.2);

            CollectionAssert.AreEqual(new[] { "audio=ok", "app=running" }, HeartbeatLines(writer).Skip(1).ToArray(), "written when it changed, not at the next beat");
        }

        [Test]
        public void Heartbeat_IsRewrittenAtOnce_WhenTheStateOfTheSoundChanges_NotOnlyEveryFiveSeconds()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            RunWhileLoading(logger, source, 0.0, 1.0);                       // the beat at 0 s says starting; the next one is due at 5 s
            Assert.AreEqual("audio=starting", HeartbeatLines(writer)[1]);

            Run(logger, source, 1.0, 1.5);                                   // the clock moves at 1 s
            Assert.AreEqual("audio=ok", HeartbeatLines(writer)[1], "written when it changed, 4 s before the next beat was due");

            source.AudioFailed = true;
            source.App = AppPhase.Failed;
            source.UtcNow = BaseUtc.AddSeconds(1.6);
            logger.Tick(1.6, 0.01f, 1.6, true, false);
            CollectionAssert.AreEqual(new[] { "audio=failed", "app=failed" }, HeartbeatLines(writer).Skip(1).ToArray(), "and again at the moment the app gives up");
        }

        [Test]
        public void Heartbeat_IsNotRewrittenOnEveryFrame_WhileNothingChanges()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 2.0);
            var first = File.GetLastWriteTimeUtc(Path.Combine(writer.Directory, "heartbeat.txt"));
            var text = File.ReadAllText(Path.Combine(writer.Directory, "heartbeat.txt"));

            Run(logger, source, 2.0, 4.9);

            Assert.AreEqual(text, File.ReadAllText(Path.Combine(writer.Directory, "heartbeat.txt")), "no beat is due before 5 s and nothing changed");
            Assert.AreEqual(first, File.GetLastWriteTimeUtc(Path.Combine(writer.Directory, "heartbeat.txt")));
        }

        [Test]
        public void Rows_AreWrittenEvenIfTheSoundNeverStarted_AndSayWhatTheStateWas()
        {
            var writer = Writer();
            var source = new FakeSource { ExperienceState = "Loading", EnergyLevel = 0, Sessions = 0 };
            var logger = new HealthLogger(writer, source, 60.0, 5.0);

            RunWhileLoading(logger, source, 0.0, 70.0);                       // nothing but loading for 70 s
            source.AudioFailed = true;
            source.App = AppPhase.Failed;
            source.ExperienceState = "Failed";
            RunWhileLoading(logger, source, 70.0, 130.0);

            var lines = Lines(writer.RowFileFor(BaseUtc));
            Assert.AreEqual(1 + 2, lines.Length, "a header and the rows at 60 and 120 s");
            var header = HealthRow.CsvHeader.Split(',');
            string Cell(int row, string name) => lines[row].Split(',')[Array.IndexOf(header, name)];
            Assert.AreEqual("starting", Cell(1, "audio_state"));
            Assert.AreEqual("failed", Cell(2, "audio_state"));
            Assert.AreEqual("Loading", Cell(1, "experience_state"));
            Assert.AreEqual("Failed", Cell(2, "experience_state"));
            Assert.AreEqual("0.0", Cell(1, "fps_avg"), "no frame of the loading counts: the row does not pretend the meadow ran");
            Assert.AreEqual("0.0", Cell(1, "frame_ms_max"));
        }

        [Test]
        public void LoadingFramesAreNotStalls_AndTheWarmupStartsWhenTheAppRuns()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 1.0, 5.0);

            source.UtcNow = BaseUtc;
            logger.Tick(0.0, 1.5f, 0.0, false, false, countFrames: false);   // a content load frame
            logger.Tick(1.5, 2.0f, 0.0, false, false, countFrames: false);
            logger.Tick(3.5, 2.0f, 0.0, false, false, countFrames: true);    // the first two frames of the running app carry its boot hitch ...
            logger.Tick(5.5, 2.0f, 0.0, false, false, countFrames: true);
            Run(logger, source, 5.6, 7.0);                                    // ... and then it runs

            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            Assert.Less(double.Parse(cells[5], System.Globalization.CultureInfo.InvariantCulture), 50.0, "frame_ms_max holds none of the long frames");
            Assert.IsFalse(File.Exists(Path.Combine(writer.Directory, "stalls-" + BaseUtc.ToLocalTime().ToString("yyyyMMdd") + ".csv")));
        }

        [Test]
        public void TrackerAge_IsCollectedFromTheAgesOfThePacketsThatArrived()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 10.0, 5.0);

            for (var t = 0.0; t < 10.5; t += 0.01)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.01f, t, true, trackerFrameArrived: true, countFrames: true, trackerAgeMs: 12.5);
            }

            var header = HealthRow.CsvHeader.Split(',');
            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            Assert.AreEqual("12.5", cells[Array.IndexOf(header, "tracker_age_ms_p95")]);
        }

        [Test]
        public void TrackerAge_IsEmpty_WhenThePacketsHaveNoAge()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 10.0, 5.0);

            for (var t = 0.0; t < 10.5; t += 0.01)
            {
                source.UtcNow = BaseUtc.AddSeconds(t);
                logger.Tick(t, 0.01f, t, true, trackerFrameArrived: true);   // a replay: frames, but nothing measured on a socket
            }

            var header = HealthRow.CsvHeader.Split(',');
            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            Assert.AreEqual(string.Empty, cells[Array.IndexOf(header, "tracker_age_ms_p95")]);
        }

        [Test]
        public void Rows_SayTheRenderScaleTheQualityStepAndWhatTheAutomaticQualityDidAtTheirEnd()
        {
            var writer = Writer();
            var source = new FakeSource { RenderScale = 0.7f, QualityLevel = 4, StarveIgnored = true, QualityChangesInTheMinute = 3 };
            var logger = new HealthLogger(writer, source, 10.0, 5.0);

            Run(logger, source, 0.0, 10.5);

            var header = HealthRow.CsvHeader.Split(',');
            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            Assert.AreEqual("0.70", cells[Array.IndexOf(header, "render_scale")]);
            Assert.AreEqual("4", cells[Array.IndexOf(header, "quality_level")]);
            Assert.AreEqual("1", cells[Array.IndexOf(header, "starve_ignored")], "a starved tracker that stepping down does not help is ignored: the row says so");
            Assert.AreEqual("3", cells[Array.IndexOf(header, "quality_changes")], "so that the frames of a rebuild can be told apart from a stall in an 8-hour analysis");
            Assert.AreEqual(header.Length - 4, Array.IndexOf(header, "render_scale"), "the new columns are the last ones: whatever reads the older columns by position still works");
            Assert.AreEqual(header.Length - 3, Array.IndexOf(header, "quality_level"));
            Assert.AreEqual(header.Length - 2, Array.IndexOf(header, "starve_ignored"));
            Assert.AreEqual(header.Length - 1, Array.IndexOf(header, "quality_changes"));
            Assert.AreEqual(0, source.QualityChangesInTheMinute, "the count starts again at every row");
        }

        [Test]
        public void Logger_KnowsTheFolderItWritesTo()
        {
            var writer = Writer();

            Assert.AreEqual(writer.Directory, new HealthLogger(writer, new FakeSource(), 60.0, 5.0).Directory);
        }

        [Test]
        public void Writer_RemembersThatItFellBack_SoTheAppCanSayLoudlyThatTheSupervisorLooksElsewhere()
        {
            Directory.CreateDirectory(_dir);
            var blocker = Path.Combine(_dir, "a-file");
            File.WriteAllText(blocker, "not a folder");
            var primary = Path.Combine(blocker, "logs");

            var fallback = Writer(primary: primary);
            var normal = Writer(primary: Path.Combine(_dir, "fine"));

            Assert.IsTrue(fallback.UsedFallback);
            Assert.AreEqual(primary, fallback.PrimaryDirectory);
            Assert.IsFalse(normal.UsedFallback);
        }

        [Test]
        public void Logger_LogsAFrameOver100Ms_AsAStallLine()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 2.0);

            source.UtcNow = BaseUtc.AddSeconds(2.5);
            logger.Tick(2.5, 0.5f, 2.5, true, false);   // the audio clock ran on by 0.5 s while the frame took 0.5 s
            Run(logger, source, 2.5, 3.0);

            var lines = Lines(Path.Combine(writer.Directory, "stalls-" + BaseUtc.ToLocalTime().ToString("yyyyMMdd") + ".csv"));
            Assert.AreEqual("timestamp_utc,uptime_s,frame_ms,audio_step_s", lines[0]);
            Assert.AreEqual(2, lines.Length, "one stall");
            var cells = lines[1].Split(',');
            Assert.AreEqual(500.0, double.Parse(cells[2], System.Globalization.CultureInfo.InvariantCulture), 1.0);
            Assert.AreEqual(0.5, double.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture), 0.02);
        }

        [Test]
        public void Logger_IgnoresTheFirstFrames_WhichCarryTheBootHitch()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 1.0, 5.0);

            source.UtcNow = BaseUtc;
            logger.Tick(0.0, 2.0f, 0.0, false, false);   // the first frame after loading is long
            Run(logger, source, 0.01, 1.5);

            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            Assert.Less(double.Parse(cells[5], System.Globalization.CultureInfo.InvariantCulture), 50.0, "frame_ms_max does not hold the 2 s boot frame");
            Assert.IsFalse(File.Exists(Path.Combine(writer.Directory, "stalls-" + BaseUtc.ToLocalTime().ToString("yyyyMMdd") + ".csv")), "and it is not a stall");
        }

        [Test]
        public void Flush_WritesTheLastPartialRow_OnlyWhenEnoughTimePassedSinceTheLastOne()
        {
            var writer = Writer();
            var source = new FakeSource();
            var logger = new HealthLogger(writer, source, 60.0, 5.0);
            Run(logger, source, 0.0, 70.0);   // one row at 60 s, 10 s since

            logger.Flush(70.0);
            Assert.AreEqual(1 + 2, Lines(writer.RowFileFor(BaseUtc)).Length, "10 s of data: a final row");

            logger.Flush(70.5);
            Assert.AreEqual(1 + 2, Lines(writer.RowFileFor(BaseUtc)).Length, "0.5 s later: nothing new to say");
        }

        [Test]
        public void Logger_PassesTheStateOfTheRunIntoTheRow_AndCountsErrorsPerRow()
        {
            var writer = Writer();
            var source = new FakeSource { ExperienceState = "Settling", EnergyLevel = 1, Sessions = 4, NatureDropped = 2, Errors = 3, Audio = new AudioStats(50, 1, 2, 0) };
            var logger = new HealthLogger(writer, source, 10.0, 5.0);

            Run(logger, source, 0.0, 10.5);

            var header = HealthRow.CsvHeader.Split(',');
            var cells = Lines(writer.RowFileFor(BaseUtc))[1].Split(',');
            string Cell(string name) => cells[Array.IndexOf(header, name)];
            Assert.AreEqual("Settling", Cell("experience_state"));
            Assert.AreEqual("1", Cell("energy_level"));
            Assert.AreEqual("4", Cell("sessions"));
            Assert.AreEqual("2", Cell("nature_dropped"));
            Assert.AreEqual("3", Cell("errors"));
            Assert.AreEqual("50", Cell("notes_scheduled"));
            Assert.AreEqual("200.0", Cell("working_set_mb"));
        }

        [Test]
        public void Writer_AppendsToAnExistingFile_WithoutASecondHeader()
        {
            var first = Writer();
            first.AppendRow(BaseUtc, "a,b");
            var second = Writer();   // the app was started again the same day

            second.AppendRow(BaseUtc.AddMinutes(1), "c,d");

            var lines = Lines(second.RowFileFor(BaseUtc));
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(1, lines.Count(l => l == HealthRow.CsvHeader));
            Assert.AreEqual("c,d", lines[2]);
        }

        [Test]
        public void Writer_StartsANewFile_WhenTheLocalDateChanges()
        {
            var writer = Writer();
            var beforeMidnight = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 29, 23, 59, 30, DateTimeKind.Unspecified), TimeZoneInfo.Local);

            writer.AppendRow(beforeMidnight, "x");
            writer.AppendRow(beforeMidnight.AddSeconds(60), "y");

            var a = writer.RowFileFor(beforeMidnight);
            var b = writer.RowFileFor(beforeMidnight.AddSeconds(60));
            Assert.AreNotEqual(a, b);
            StringAssert.EndsWith("health-20261029.csv", a);
            StringAssert.EndsWith("health-20261030.csv", b);
            Assert.AreEqual(2, Lines(a).Length);
            Assert.AreEqual(2, Lines(b).Length);
        }

        [Test]
        public void Writer_UsesTheFallbackFolder_WhenTheFirstCannotBeCreated()
        {
            Directory.CreateDirectory(_dir);
            var blocker = Path.Combine(_dir, "a-file");
            File.WriteAllText(blocker, "not a folder");
            var warnings = new System.Collections.Generic.List<string>();

            var writer = Writer(primary: Path.Combine(blocker, "logs"), warn: warnings.Add);
            writer.AppendRow(BaseUtc, "row");

            Assert.IsTrue(writer.IsActive);
            Assert.AreEqual(Path.Combine(_dir, "fallback"), writer.Directory);
            Assert.AreEqual(2, Lines(writer.RowFileFor(BaseUtc)).Length);
            Assert.IsTrue(warnings.Any(w => w.Contains("fallback")), "the fallback is announced");
        }

        [Test]
        public void Writer_DoesNotCrash_WhenNoFolderCanBeUsed()
        {
            Directory.CreateDirectory(_dir);
            var blocker = Path.Combine(_dir, "a-file");
            File.WriteAllText(blocker, "not a folder");
            var warnings = new System.Collections.Generic.List<string>();
            var writer = new HealthFileWriter(Path.Combine(blocker, "a"), Path.Combine(blocker, "b"), warnings.Add);

            Assert.DoesNotThrow(() => writer.AppendRow(BaseUtc, "row"));
            Assert.DoesNotThrow(() => writer.WriteHeartbeat(BaseUtc));
            Assert.DoesNotThrow(() => writer.AppendStall(BaseUtc, 1.0, 500.0, 0.5));
            Assert.IsFalse(writer.IsActive);
            Assert.AreEqual(1, warnings.Count(w => w.Contains("no folder")), "said once, not on every row");
        }

        [Test]
        public void Writer_SaysOncePerFileThatTheStallCapWasReached_SoTheNextDayIsNotSilent()
        {
            var warnings = new System.Collections.Generic.List<string>();
            var writer = Writer(warn: warnings.Add);
            var dayOne = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
            var dayTwo = dayOne.AddDays(2);

            for (var i = 0; i < HealthFileWriter.MaxStallLinesPerFile + 5; i++) writer.AppendStall(dayOne, i, 150.0, 0.1);
            for (var i = 0; i < HealthFileWriter.MaxStallLinesPerFile + 5; i++) writer.AppendStall(dayTwo, i, 150.0, 0.1);

            Assert.AreEqual(2, warnings.Count(w => w.Contains("stall lines")), "one warning for each of the two files");
        }

        [Test]
        public void Writer_StopsWritingStalls_AfterTheCap()
        {
            var writer = Writer();

            for (var i = 0; i < HealthFileWriter.MaxStallLinesPerFile + 50; i++) writer.AppendStall(BaseUtc, i, 150.0, 0.1);

            var path = Path.Combine(writer.Directory, "stalls-" + BaseUtc.ToLocalTime().ToString("yyyyMMdd") + ".csv");
            Assert.AreEqual(1 + HealthFileWriter.MaxStallLinesPerFile, Lines(path).Length);
        }
    }
}
