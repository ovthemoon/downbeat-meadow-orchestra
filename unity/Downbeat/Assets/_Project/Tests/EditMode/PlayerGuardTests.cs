using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Downbeat.Tests
{
    public class PlayerGuardTests
    {
        const int WaitMs = 5000;
        const int QuietMs = 300;
        const int FastCheckMs = 5;
        const int StepRealMs = 12;
        const double StallSec = 20.0;
        const string SoakLine = "d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]";

        sealed class Rig
        {
            public readonly long[] Clock = { 0L };
            public readonly ConcurrentQueue<int> Terminated = new ConcurrentQueue<int>();
            public readonly ConcurrentQueue<string> Records = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();         // the lines of the exit itself
            public readonly ConcurrentQueue<string> PlayerLog = new ConcurrentQueue<string>();    // the lines of the guard (the watch says when it ends)
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public readonly StallWatchdog Watchdog;
            public readonly PlayerGuard Guard;
            int _clockThrows;

            public Rig(double stallSec = StallSec)
            {
                Watchdog = new StallWatchdog(stallSec, ReadClock);
                var exit = new SelfExit(() => DateTime.UtcNow, () => 1.0, Records.Enqueue, Logs.Enqueue, code => { Terminated.Enqueue(code); Done.Set(); }, 50);
                Guard = new PlayerGuard(Watchdog, exit, FastCheckMs, PlayerLog.Enqueue);
            }

            /// <summary>From now on the clock throws: the watchdog thread fails.</summary>
            public void BreakTheClock() => Volatile.Write(ref _clockThrows, 1);

            long ReadClock()
            {
                if (Volatile.Read(ref _clockThrows) != 0) throw new InvalidOperationException("clock broke");
                return Interlocked.Read(ref Clock[0]);
            }

            public bool PlayerLogSays(string text) => SpinWait.SpinUntil(() => Contains(PlayerLog, text), WaitMs);

            static bool Contains(ConcurrentQueue<string> lines, string text)
            {
                foreach (var line in lines)
                {
                    if (line.Contains(text)) return true;
                }
                return false;
            }

            public void AdvanceSec(double seconds) => Interlocked.Add(ref Clock[0], (long)(seconds * 1000.0));

            /// <summary>Lets the (manual) clock run one second at a time, with a real pause after each so that the watchdog thread looks in between, as it does once a second in the player.</summary>
            public void PassSeconds(int seconds)
            {
                for (var i = 0; i < seconds; i++)
                {
                    AdvanceSec(1.0);
                    Thread.Sleep(StepRealMs);
                }
            }

            /// <summary>The same, until the player was ended; false when it was not within <paramref name="maxSeconds"/> of that time.</summary>
            public bool PassUntilEnded(int maxSeconds)
            {
                for (var i = 0; i < maxSeconds && !Done.IsSet; i++) PassSeconds(1);
                return Done.Wait(WaitMs);
            }
        }

        Rig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Guard.Stop();
            _rig = null;
        }

        // ---- the fast path: the log ------------------------------------------------------------------------------------------------------

        [Test]
        public void ADeviceLostLine_EndsThePlayer_WithTheLineAsTheDetail()
        {
            _rig = new Rig();

            _rig.Guard.OnLogMessage(SoakLine, string.Empty, LogType.Error);

            Assert.IsTrue(_rig.Done.Wait(WaitMs));
            CollectionAssert.AreEqual(new[] { SelfExitReasons.GraphicsDeviceLostExitCode }, _rig.Terminated);
            StringAssert.Contains("detail=" + SoakLine, _rig.Records.TryPeek(out var record) ? record : null);
        }

        [TestCase(LogType.Error)]
        [TestCase(LogType.Exception)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        [TestCase(LogType.Assert)]
        public void ADeviceLostLine_IsRecognised_AtAnyLogLevel(LogType type)
        {
            _rig = new Rig();

            _rig.Guard.OnLogMessage("Present failed: DXGI_ERROR_DEVICE_REMOVED", string.Empty, type);

            Assert.IsTrue(_rig.Done.Wait(WaitMs));
        }

        [Test]
        public void OrdinaryLines_DoNothing()
        {
            _rig = new Rig();

            _rig.Guard.OnLogMessage("[Downbeat] ready", string.Empty, LogType.Log);
            _rig.Guard.OnLogMessage("d3d11: failed to create buffer [0x80070057]", string.Empty, LogType.Error);
            _rig.Guard.OnLogMessage(null, null, LogType.Error);

            Assert.IsFalse(_rig.Done.Wait(QuietMs));
            Assert.IsTrue(_rig.Terminated.IsEmpty);
        }

        [Test]
        public void TheLogLineItWritesItself_DoesNotEndItAgain()
        {
            _rig = new Rig();
            _rig.Guard.OnLogMessage(SoakLine, string.Empty, LogType.Error);
            Assert.IsTrue(_rig.Done.Wait(WaitMs));

            // the player log line of the exit contains the device-lost text, and the hook sees every line
            Assert.IsTrue(_rig.Logs.TryPeek(out var ownLine));
            _rig.Guard.OnLogMessage(ownLine, string.Empty, LogType.Error);
            _rig.Guard.OnLogMessage(SoakLine, string.Empty, LogType.Error);
            Thread.Sleep(QuietMs);

            Assert.AreEqual(1, _rig.Terminated.Count);
        }

        // ---- the backstop: the watchdog thread -------------------------------------------------------------------------------------------

        [Test]
        public void AFrozenMainThread_AfterTheAppIsRunning_EndsThePlayer_WithTheStallCode()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            var ended = _rig.PassUntilEnded(3 * (int)StallSec);   // the main thread beats no more

            Assert.IsTrue(ended);
            CollectionAssert.AreEqual(new[] { SelfExitReasons.MainThreadStalledExitCode }, _rig.Terminated);
            Assert.IsTrue(_rig.Records.TryPeek(out var record));
            StringAssert.Contains("reason=main_thread_stalled", record);
            StringAssert.Contains("the main loop has not advanced for ", record);
            StringAssert.Contains("(limit 20 s)", record);
            Assert.GreaterOrEqual(_rig.Clock[0], (long)(StallSec * 1000.0), "not a moment before the limit");
        }

        [Test]
        public void AfterAWakeUp_AMainThreadThatNeverRunsAFrame_IsEnded_AtTwiceTheLimit_AndTheRecordSaysWhyItWaitedLonger()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            _rig.AdvanceSec(3600.0);   // the PC slept: the clock jumped, and the main thread never ran a frame again
            var ended = _rig.PassUntilEnded(4 * (int)StallSec);

            Assert.IsTrue(ended);
            Assert.IsTrue(_rig.Records.TryPeek(out var record));
            StringAssert.Contains("counted from the wake-up of a suspended process", record);
            StringAssert.Contains("has not advanced for 4", record);   // 40 s, the longer window
        }

        [Test]
        public void WhileTheAppIsStillLoading_AHugeSilence_IsNoStall()
        {
            _rig = new Rig();
            _rig.Guard.Start();   // started, not marked as running

            _rig.PassSeconds(3 * (int)StallSec);

            Assert.IsFalse(_rig.Done.Wait(QuietMs), "a slow boot is the supervisor's grace period, not ours");
        }

        [Test]
        public void ABeatingMainThread_IsNeverEnded()
        {
            _rig = new Rig(stallSec: 4.0);
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            for (var frame = 0; frame < 30; frame++)
            {
                _rig.PassSeconds(3);   // a frame of three seconds, a long one but a frame, and it ends with a beat
                _rig.Guard.Beat();
            }

            Assert.IsTrue(_rig.Terminated.IsEmpty);
        }

        [Test]
        public void AQuitThatDoesNotFinish_IsEnded_EvenIfTheAppNeverGotToRunning()
        {
            _rig = new Rig();
            _rig.Guard.Start();   // still loading

            _rig.Guard.NoteQuitting();
            var ended = _rig.PassUntilEnded(3 * (int)StallSec);   // the shutdown hangs (a dead graphics driver): no frame, no exit

            Assert.IsTrue(ended);
            Assert.IsTrue(_rig.Records.TryPeek(out var record));
            StringAssert.Contains("while quitting", record);
        }

        [Test]
        public void AQuitThatFinishesInTime_IsNotDisturbed()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            _rig.Guard.NoteQuitting();
            _rig.PassSeconds((int)StallSec - 5);
            Thread.Sleep(QuietMs);

            Assert.IsTrue(_rig.Terminated.IsEmpty);
        }

        // ---- the watch says when it ends -------------------------------------------------------------------------------------------------

        [Test]
        public void WhenTheWatchIsStopped_ThePlayerLogSaysSo()
        {
            _rig = new Rig();
            _rig.Guard.Start();

            _rig.Guard.Stop();

            Assert.IsTrue(_rig.PlayerLogSays("the watchdog thread ended (stopped)"));
        }

        [Test]
        public void WhenTheWatchRequestsTheEnd_ThePlayerLogSaysSo()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            Assert.IsTrue(_rig.PassUntilEnded(3 * (int)StallSec));

            Assert.IsTrue(_rig.PlayerLogSays("the watchdog thread ended (the main loop stalled, the end of the player is requested)"));
        }

        [Test]
        public void AWatchThatFails_SaysSo_AndDoesNotTakeThePlayerDown()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();

            _rig.BreakTheClock();

            Assert.IsTrue(_rig.PlayerLogSays("the watchdog thread ended (it failed, nothing watches the main loop any more: InvalidOperationException: clock broke)"));
            Assert.IsTrue(_rig.Terminated.IsEmpty, "a broken watch is reported, it does not end the player");
        }

        [Test]
        public void NoteQuitting_SaysWhatIsStillWatching()
        {
            _rig = new Rig();
            _rig.Guard.Start();

            _rig.Guard.NoteQuitting();

            Assert.IsTrue(_rig.PlayerLogSays("quitting: the watchdog thread goes on watching until the runtime stops it"));
        }

        [Test]
        public void Stop_EndsTheWatch_ForGood()
        {
            _rig = new Rig();
            _rig.Guard.Start();
            _rig.Guard.MarkRunning();
            _rig.Guard.Stop();

            _rig.PassSeconds(2 * (int)StallSec);

            Assert.IsFalse(_rig.Done.Wait(QuietMs));
        }

        // ---- when there is no guard at all -----------------------------------------------------------------------------------------------

        [Test]
        public void InTheEditor_NoGuardIsMade_WhateverTheCommandLineSays()
        {
            var guard = PlayerGuard.Begin(LaunchArgs.Parse(new[] { "--stall-exit-sec", "1" }), isEditor: true, appendRecord: null, warn: null);

            Assert.IsNull(guard, "the editor must never be ended by its own game");
        }

        [Test]
        public void WithZeroSeconds_NoGuardIsMade_InAPlayer()
        {
            var guard = PlayerGuard.Begin(LaunchArgs.Parse(new[] { "--stall-exit-sec", "0" }), isEditor: false, appendRecord: null, warn: null);

            Assert.IsNull(guard, "0 turns the whole self-exit off, the device-lost exit too");
        }

        [Test]
        public void AMadeUpValue_IsReported_AndTheDefaultUsed()
        {
            var warnings = new ConcurrentQueue<string>();
            using (PlayerGuard.OverrideTerminateForTests(_ => { }))
            {
                var guard = PlayerGuard.Begin(LaunchArgs.Parse(new[] { "--stall-exit-sec", "soon" }), isEditor: false, appendRecord: null, warn: warnings.Enqueue);
                try
                {
                    Assert.IsNotNull(guard);
                    Assert.AreEqual(StallExitSetting.DefaultSec, guard.Watchdog.StallSec);
                    Assert.AreEqual(1, warnings.Count);
                }
                finally
                {
                    guard?.Stop();
                }
            }
        }

        [Test]
        public void TheTestOverride_MakesAGuardInTheEditor_ThatNeverKillsAnything()
        {
            var ended = new ConcurrentQueue<int>();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[selfexit\] reason=graphics_device_lost exit_code=71 "));   // the real log step of the exit; only the kill is a stub
            using (PlayerGuard.OverrideTerminateForTests(ended.Enqueue))
            {
                var guard = PlayerGuard.Begin(LaunchArgs.Parse(new[] { "--stall-exit-sec", "30" }), isEditor: true, appendRecord: null, warn: null);
                try
                {
                    Assert.IsNotNull(guard);
                    guard.OnLogMessage(SoakLine, string.Empty, LogType.Error);
                    SpinWait.SpinUntil(() => !ended.IsEmpty, WaitMs);
                    CollectionAssert.AreEqual(new[] { SelfExitReasons.GraphicsDeviceLostExitCode }, ended);
                }
                finally
                {
                    guard?.Stop();
                }
            }
        }
    }
}
