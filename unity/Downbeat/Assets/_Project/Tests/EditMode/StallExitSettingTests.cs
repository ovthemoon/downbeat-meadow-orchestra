using System;
using System.IO;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class StallExitSettingTests
    {
        static double Resolve(out string problem, params string[] args) => StallExitSetting.Resolve(LaunchArgs.Parse(args), out problem);

        [Test]
        public void WithoutTheFlag_TheDefaultApplies()
        {
            var seconds = Resolve(out var problem);

            Assert.AreEqual(StallExitSetting.DefaultSec, seconds);
            Assert.IsNull(problem);
        }

        [Test]
        public void ANumberOfSeconds_IsTaken_AsGiven()
        {
            Assert.AreEqual(45.0, Resolve(out var problem, "--stall-exit-sec", "45"));
            Assert.IsNull(problem);
            Assert.AreEqual(7.5, Resolve(out problem, "--stall-exit-sec=7.5"));
            Assert.IsNull(problem);
        }

        [Test]
        public void Zero_TurnsItOff()
        {
            Assert.AreEqual(0.0, Resolve(out var problem, "--stall-exit-sec", "0"));
            Assert.IsNull(problem, "0 is a choice, not a mistake");
            Assert.IsFalse(StallExitSetting.IsActive(isEditor: false, stallSec: 0.0));
        }

        [TestCase("abc")]
        [TestCase("NaN")]
        [TestCase("")]
        [TestCase("Infinity")]
        public void ANonsenseValue_FallsBackToTheDefault_AndSaysSo(string value)
        {
            string problem;
            var seconds = value.Length == 0 ? Resolve(out problem, "--stall-exit-sec") : Resolve(out problem, "--stall-exit-sec", value);   // the empty value is the bare flag

            Assert.AreEqual(StallExitSetting.DefaultSec, seconds);
            StringAssert.Contains("--stall-exit-sec", problem);
            StringAssert.Contains(StallExitSetting.DefaultSec.ToString("0"), problem);
        }

        [TestCase("0.5", 2.0)]
        [TestCase("1.99", 2.0)]
        [TestCase("-3", 2.0)]
        [TestCase("-1e300", 2.0)]
        [TestCase("3600.5", 3600.0)]
        [TestCase("1e300", 3600.0)]
        [TestCase("86400", 3600.0)]
        public void ANumberOutsideTheRange_IsHeldToTheNearestEnd_AndSaysSo(string value, double expected)
        {
            var seconds = Resolve(out var problem, "--stall-exit-sec", value);

            Assert.AreEqual(expected, seconds);
            StringAssert.Contains("--stall-exit-sec", problem);
            StringAssert.Contains("2..3600", problem);
            StringAssert.Contains("using " + expected.ToString("0"), problem);
        }

        [TestCase("2", 2.0)]
        [TestCase("20", 20.0)]
        [TestCase("3600", 3600.0)]
        public void TheEndsOfTheRange_AreInsideIt(string value, double expected)
        {
            Assert.AreEqual(expected, Resolve(out var problem, "--stall-exit-sec", value));
            Assert.IsNull(problem);
        }

        [Test]
        public void TheDefault_IsInsideTheRange()
        {
            Assert.GreaterOrEqual(StallExitSetting.DefaultSec, StallExitSetting.MinSec);
            Assert.LessOrEqual(StallExitSetting.DefaultSec, StallExitSetting.MaxSec);
        }

        [Test]
        public void TheEditor_IsNeverActive_WhateverTheLimit()
        {
            Assert.IsFalse(StallExitSetting.IsActive(isEditor: true, stallSec: StallExitSetting.DefaultSec));
            Assert.IsFalse(StallExitSetting.IsActive(isEditor: true, stallSec: 1.0));
            Assert.IsTrue(StallExitSetting.IsActive(isEditor: false, stallSec: StallExitSetting.DefaultSec));
        }

        // ---- why 20 s: the numbers it was chosen between -----------------------------------------------------------------------------------

        const double SoakWorstFrameSec = 0.87;          // docs/windows-soak-2026-11-02.md: the longest frame of 6 h 57 min
        const double HeartbeatIntervalSec = 5.0;        // DownbeatApp.HeartbeatIntervalSec: the supervisor sees a beat at most this old when the main thread freezes
        const double MinimumClearanceFactor = 10.0;

        [Test]
        public void TheDefault_IsFarAboveTheWorstMeasuredFrame()
        {
            Assert.Greater(StallExitSetting.DefaultSec, SoakWorstFrameSec * MinimumClearanceFactor);
        }

        [Test]
        public void TheDefault_FiresBeforeTheSupervisorWouldRestartTheFrozenPlayer_SoTheReasonIsTheAppsOwn()
        {
            var supervisorTimeoutSec = SupervisorHeartbeatTimeoutSec();
            // worst case: the main thread freezes right after the beat that was written, so the supervisor's clock (age of the last beat) started HeartbeatIntervalSec before the freeze.
            // The player is gone after the limit, the look of the watchdog thread and the two steps of the exit (the record and the log, each given SelfExit.DefaultStepWaitMs at most)
            var latestSelfExitSec = StallExitSetting.DefaultSec + StallWatchdog.CheckIntervalSec + 2 * SelfExit.DefaultStepWaitMs / 1000.0;
            var earliestSupervisorKillAfterFreezeSec = supervisorTimeoutSec - HeartbeatIntervalSec;

            Assert.Less(latestSelfExitSec, earliestSupervisorKillAfterFreezeSec,
                $"the player ends itself {latestSelfExitSec:0} s after the freeze at the latest; the supervisor (heartbeatTimeoutSec {supervisorTimeoutSec:0}) may kill it {earliestSupervisorKillAfterFreezeSec:0} s after");
        }

        [Serializable]
        sealed class SupervisorConfig
        {
            public double heartbeatTimeoutSec;   // the keys this test does not name (the tracker's own heartbeatTimeoutSec is inside processes[]) are skipped
        }

        /// <summary>The top-level <c>heartbeatTimeoutSec</c> of the supervisor's Windows configuration in the repository: the one that applies to every process without its own.</summary>
        static double SupervisorHeartbeatTimeoutSec()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "supervisor", "configs", "windows.json"));
            Assert.IsTrue(File.Exists(path), "the supervisor's Windows configuration is not where this test expects it: " + path);
            var config = JsonUtility.FromJson<SupervisorConfig>(File.ReadAllText(path));
            Assert.IsNotNull(config, "not JSON: " + path);
            Assert.Greater(config.heartbeatTimeoutSec, 0.0, "no top-level heartbeatTimeoutSec in " + path);
            return config.heartbeatTimeoutSec;
        }
    }
}
