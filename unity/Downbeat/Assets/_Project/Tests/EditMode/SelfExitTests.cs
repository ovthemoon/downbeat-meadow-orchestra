using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SelfExitTests
    {
        const int WaitMs = 5000;
        const int ShortStepWaitMs = 50;
        static readonly DateTime FixedUtc = new DateTime(2026, 11, 3, 3, 55, 40, 123, DateTimeKind.Utc);

        /// <summary>The four things a self-exit does, recorded in the order they happen; nothing is killed.</summary>
        sealed class Probe
        {
            public readonly List<string> Steps = new List<string>();
            public readonly List<string> Records = new List<string>();
            public readonly List<string> Logs = new List<string>();
            public readonly List<int> Terminated = new List<int>();
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public Action<string> OnAppend = _ => { };
            public Action<string> OnLog = _ => { };
            readonly object _gate = new object();

            public SelfExit Make(int stepWaitMs = ShortStepWaitMs) => new SelfExit(() => FixedUtc, () => 25012.34, Append, Log, Terminate, stepWaitMs);

            void Append(string line)
            {
                lock (_gate) { Steps.Add("record"); Records.Add(line); }
                OnAppend(line);
            }

            void Log(string line)
            {
                lock (_gate) { Steps.Add("log"); Logs.Add(line); }
                OnLog(line);
            }

            void Terminate(int code)
            {
                lock (_gate) { Steps.Add("terminate"); Terminated.Add(code); }
                Done.Set();
            }
        }

        // ---- the line --------------------------------------------------------------------------------------------------------------------

        [Test]
        public void ADeviceLost_IsRecorded_AndEndsWithTheDeviceLostCode()
        {
            var probe = new Probe();

            Assert.IsTrue(probe.Make().Request(SelfExitReason.GraphicsDeviceLost, "d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]"));
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            CollectionAssert.AreEqual(new[] { SelfExitReasons.GraphicsDeviceLostExitCode }, probe.Terminated);
            Assert.AreEqual("2026-11-03T03:55:40.123Z reason=graphics_device_lost exit_code=71 uptime_s=25012.3 detail=d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]", probe.Records[0]);
            Assert.AreEqual("[selfexit] reason=graphics_device_lost exit_code=71 uptime_s=25012.3 detail=d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]", probe.Logs[0]);
        }

        [Test]
        public void AStall_HasItsOwnWord_AndItsOwnCode()
        {
            var probe = new Probe();

            probe.Make().Request(SelfExitReason.MainThreadStalled, "the main loop has not advanced for 20.0 s (limit 20 s)");
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            CollectionAssert.AreEqual(new[] { SelfExitReasons.MainThreadStalledExitCode }, probe.Terminated);
            StringAssert.Contains("reason=main_thread_stalled exit_code=72", probe.Records[0]);
            Assert.AreNotEqual(SelfExitReasons.GraphicsDeviceLostExitCode, SelfExitReasons.MainThreadStalledExitCode, "the supervisor log shows the code: it must tell the two apart");
            Assert.AreNotEqual(0, SelfExitReasons.GraphicsDeviceLostExitCode, "0 would look like a normal quit");
            Assert.AreNotEqual(0, SelfExitReasons.MainThreadStalledExitCode);
        }

        [Test]
        public void TheDetail_IsOneLine_AndNotLongerThanTheLimit()
        {
            var probe = new Probe();
            var detail = "first\r\nsecond\tthird " + new string('x', 1000);

            probe.Make().Request(SelfExitReason.GraphicsDeviceLost, detail);
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            var record = probe.Records[0];
            StringAssert.DoesNotContain("\n", record);
            StringAssert.DoesNotContain("\r", record);
            StringAssert.DoesNotContain("\t", record);
            StringAssert.Contains("detail=first  second third xxx", record);
            Assert.LessOrEqual(record.Length, 100 + SelfExitReasons.MaxDetailChars + 3, "a line in a log, not a dump");
            StringAssert.EndsWith("...", record, "and it says it was cut");
        }

        // ---- what happens, in which order ------------------------------------------------------------------------------------------------

        [Test]
        public void TheRecordComesFirst_ThenTheLog_ThenTheEnd()
        {
            var probe = new Probe();

            probe.Make().Request(SelfExitReason.MainThreadStalled, "stalled");
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            CollectionAssert.AreEqual(new[] { "record", "log", "terminate" }, probe.Steps);
        }

        [Test]
        public void ASecondRequest_IsIgnored_TheProcessIsEndedOnce()
        {
            var probe = new Probe();
            var exit = probe.Make();

            var first = exit.Request(SelfExitReason.GraphicsDeviceLost, "first");
            var second = exit.Request(SelfExitReason.MainThreadStalled, "second");
            Assert.IsTrue(probe.Done.Wait(WaitMs));
            Thread.Sleep(100);   // a late second end would show up now

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.IsTrue(exit.IsRequested);
            Assert.AreEqual(1, probe.Terminated.Count);
            Assert.AreEqual(1, probe.Records.Count);
            StringAssert.Contains("first", probe.Records[0]);
        }

        [Test]
        public void TheLineItWritesItself_IsNotAReasonToEndAgain()
        {
            // the player log line contains the text of the device-lost log line; the hook that watches the log sees it too (PlayerGuardTests checks the hook): the guard against a second request is here
            var probe = new Probe();
            var exit = probe.Make();
            probe.OnLog = line => Assert.IsFalse(exit.Request(SelfExitReason.GraphicsDeviceLost, line));

            exit.Request(SelfExitReason.GraphicsDeviceLost, "d3d11 [0x887A0005]");
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            Assert.AreEqual(1, probe.Terminated.Count);
        }

        [Test]
        public void AFrozenLog_DoesNotKeepTheProcessAlive()
        {
            var probe = new Probe();
            var neverSet = new ManualResetEventSlim(false);
            probe.OnLog = _ => neverSet.Wait(WaitMs * 4);   // a log call that never returns, as if the frozen main thread held its lock
            try
            {
                probe.Make(ShortStepWaitMs).Request(SelfExitReason.MainThreadStalled, "stalled");

                Assert.IsTrue(probe.Done.Wait(WaitMs), "the process is ended although the log call is stuck");
                CollectionAssert.AreEqual(new[] { "record", "log", "terminate" }, probe.Steps);
            }
            finally
            {
                neverSet.Set();
            }
        }

        [Test]
        public void AFrozenRecordFile_DoesNotKeepTheProcessAlive_AndTheLogStillSaysWhy()
        {
            var probe = new Probe();
            var neverSet = new ManualResetEventSlim(false);
            probe.OnAppend = _ => neverSet.Wait(WaitMs * 4);
            try
            {
                probe.Make(ShortStepWaitMs).Request(SelfExitReason.GraphicsDeviceLost, "lost");

                Assert.IsTrue(probe.Done.Wait(WaitMs));
                CollectionAssert.AreEqual(new[] { "record", "log", "terminate" }, probe.Steps);
                StringAssert.Contains("reason=graphics_device_lost", probe.Logs[0]);
            }
            finally
            {
                neverSet.Set();
            }
        }

        [Test]
        public void AFailingRecordFile_IsReportedInTheLog_AndTheProcessStillEnds()
        {
            var probe = new Probe();
            probe.OnAppend = _ => throw new IOException("disk full");

            probe.Make().Request(SelfExitReason.GraphicsDeviceLost, "lost");
            Assert.IsTrue(probe.Done.Wait(WaitMs));

            StringAssert.Contains("could not write the record file: disk full", probe.Logs[0]);
            Assert.AreEqual(1, probe.Terminated.Count);
        }

        [Test]
        public void AFailingLog_StillEndsTheProcess()
        {
            var probe = new Probe();
            probe.OnLog = _ => throw new InvalidOperationException("log is gone");

            probe.Make().Request(SelfExitReason.MainThreadStalled, "stalled");

            Assert.IsTrue(probe.Done.Wait(WaitMs));
            Assert.AreEqual(1, probe.Terminated.Count);
        }

        [Test]
        public void RequestReturnsAtOnce_EvenWhenTheStepsAreSlow()
        {
            // the fast path runs inside the log call of the thread that logged (maybe the frozen main thread): it must only start the work
            var probe = new Probe();
            var release = new ManualResetEventSlim(false);
            probe.OnAppend = _ => release.Wait(WaitMs);
            try
            {
                var started = DateTime.UtcNow;
                probe.Make(WaitMs).Request(SelfExitReason.GraphicsDeviceLost, "lost");

                Assert.Less((DateTime.UtcNow - started).TotalMilliseconds, 1000.0);
            }
            finally
            {
                release.Set();
                probe.Done.Wait(WaitMs);
            }
        }

        // ---- the record file -------------------------------------------------------------------------------------------------------------

        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-selfexit-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void TheRecordFile_IsInTheHealthFolder_NextToTheHeartbeat_AndKeepsEveryExit()
        {
            var writer = new HealthFileWriter(Path.Combine(_dir, "logs"), Path.Combine(_dir, "fallback"), _ => { });
            writer.WriteHeartbeat(FixedUtc);

            writer.AppendExitRecord("2026-11-03T03:55:40.123Z reason=graphics_device_lost exit_code=71 uptime_s=25012.3 detail=a");
            writer.AppendExitRecord("2026-11-03T09:00:00.000Z reason=main_thread_stalled exit_code=72 uptime_s=120.0 detail=b");

            var path = Path.Combine(writer.Directory, HealthFileWriter.ExitRecordFile);
            Assert.IsTrue(File.Exists(Path.Combine(writer.Directory, "heartbeat.txt")));
            Assert.IsTrue(File.Exists(path), "the same folder as the heartbeat: the one the supervisor reads");
            var lines = File.ReadAllLines(path);
            Assert.AreEqual(2, lines.Length, "a restart loop leaves one line per exit, not only the last");
            StringAssert.Contains("graphics_device_lost", lines[0]);
            StringAssert.Contains("main_thread_stalled", lines[1]);
            Assert.AreEqual("exit-reasons.txt", HealthFileWriter.ExitRecordFile);
        }

        [Test]
        public void WithoutAFolder_TheRecordFileIsSkipped_WithoutAnError()
        {
            var blocker = Path.Combine(_dir, "a-file");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(blocker, "x");
            var writer = new HealthFileWriter(Path.Combine(blocker, "a"), Path.Combine(blocker, "b"), _ => { });

            Assert.DoesNotThrow(() => writer.AppendExitRecord("anything"));
        }

        [Test]
        public void TheWords_AreTheOnesTheRunbookNames()
        {
            Assert.AreEqual("graphics_device_lost", SelfExitReason.GraphicsDeviceLost.Word());
            Assert.AreEqual("main_thread_stalled", SelfExitReason.MainThreadStalled.Word());
            Assert.AreEqual(71, SelfExitReason.GraphicsDeviceLost.ExitCode());
            Assert.AreEqual(72, SelfExitReason.MainThreadStalled.ExitCode());
            Assert.AreEqual("reason=main_thread_stalled exit_code=72 uptime_s=3.5 detail=x", SelfExitReasons.Describe(SelfExitReason.MainThreadStalled, 3.5, "x"));
            Assert.AreEqual("reason=main_thread_stalled exit_code=72 uptime_s=3.5 detail=", SelfExitReasons.Describe(SelfExitReason.MainThreadStalled, 3.5, null));
        }
    }
}
