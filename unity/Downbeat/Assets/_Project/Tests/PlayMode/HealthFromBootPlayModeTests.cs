using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Downbeat.Tests
{
    /// <summary>
    /// The real scene with a sound device that never comes up (an HDMI monitor that is the only sound device and is switched off). The app must be seen to be alive: the heartbeat is written from
    /// the first frame and says <c>audio=starting</c>, then <c>audio=failed</c> and <c>app=failed</c> once the app gives up; the health CSV gets its rows although the app never ran. Without that the
    /// supervisor would see no heartbeat at all, kill the app every 90 s and leave an all-night log with no rows.
    /// The other half: a heartbeat must never say the app is up when it is not. The audio clock can run while Boot hangs or throws, so the heartbeat has its own <c>app=loading|running|failed</c>.
    /// </summary>
    public class HealthFromBootPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float FirstBeatWaitSec = 10f;
        const float GiveUpWaitSec = 25f;
        const float AudioTimeoutSec = 3f;
        const float HealthIntervalSec = 1f;
        const float HeartbeatIntervalSec = 0.5f;

        /// <summary>An audio engine whose clock never starts: what the app sees when there is no sound device.</summary>
        sealed class StuckAudioEngine : IAudioEngine
        {
            public bool IsRunning => false;
            public bool SamplesReady => false;
            public int SamplesFailed => 0;
            public double Now => 0.0;
            public double HeardNow => 0.0;
            public double VisualNow => 0.0;
            public AudioStats Stats => new AudioStats(0, 0, 0, 0);

            public void Configure(float masterGainLinear, int maxVoices) { }
            public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples) { }
            public void Tick() { }
            public double Schedule(int sampleIndex, double when, double playbackRate, double gain) => when;
            public void FadeOutAll(double seconds) { }
            public void LogFlash(double when, double heardNow) { }
        }

        /// <summary>An audio clock that runs (real time) although the app is not up: the device is fine, the boot is not.</summary>
        class MovingAudioEngine : IAudioEngine
        {
            public bool IsRunning => false;
            public bool SamplesReady => false;
            public int SamplesFailed => 0;
            public double Now => Time.realtimeSinceStartupAsDouble;
            public double HeardNow => Now;
            public double VisualNow => Now;
            public AudioStats Stats => new AudioStats(0, 0, 0, 0);

            public virtual void Configure(float masterGainLinear, int maxVoices) { }
            public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples) { }
            public void Tick() { }
            public double Schedule(int sampleIndex, double when, double playbackRate, double gain) => when;
            public void FadeOutAll(double seconds) { }
            public void LogFlash(double when, double heardNow) { }
        }

        /// <summary>The audio setup throws, as a display or card-folder problem in Boot would.</summary>
        sealed class ThrowingAudioEngine : MovingAudioEngine
        {
            public override void Configure(float masterGainLinear, int maxVoices) => throw new InvalidOperationException("boot probe: audio configure exploded");
        }

        /// <summary>A content repository whose load never ends.</summary>
        sealed class HangingRepository : Downbeat.Content.IContentRepository
        {
            public IEnumerator Load(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed)
            {
                while (true) yield return null;
            }

            public IEnumerator Reload(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed) => Load(onLoaded, onFailed);

            public void ConfirmLoaded() { }
        }

        /// <summary>A content repository whose load throws inside the nested routine that Boot waits for.</summary>
        sealed class ThrowingRepository : Downbeat.Content.IContentRepository
        {
            public IEnumerator Load(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed)
            {
                yield return null;
                throw new InvalidOperationException("boot probe: content load exploded");
            }

            public IEnumerator Reload(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed) => Load(onLoaded, onFailed);

            public void ConfirmLoaded() { }
        }

        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "downbeat-boot-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        string[] HeartbeatLines()
        {
            var path = Path.Combine(_dir, "heartbeat.txt");
            try { return File.Exists(path) ? File.ReadAllLines(path) : new string[0]; }
            catch (IOException) { return new string[0]; }   // read at the moment the app rewrites it
        }

        string[] HealthRows()
        {
            if (!Directory.Exists(_dir)) return new string[0];
            var file = Directory.GetFiles(_dir, "health-*.csv").FirstOrDefault();
            if (file == null) return new string[0];
            try { return File.ReadAllLines(file); }
            catch (IOException) { return new string[0]; }
        }

        static DateTime StampOf(string line) => DateTime.Parse(line, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        [UnityTest]
        public IEnumerator ASoundDeviceThatNeverComesUp_StillGivesAHeartbeatAndHealthRows_AndSaysItFailed()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[Downbeat\] audio did not start"));
            var args = new[]
            {
                "Downbeat", "--health-dir", _dir, "--health-interval", HealthIntervalSec.ToString(CultureInfo.InvariantCulture), "--heartbeat-interval", HeartbeatIntervalSec.ToString(CultureInfo.InvariantCulture),
                "--audio-timeout", AudioTimeoutSec.ToString(CultureInfo.InvariantCulture), "--no-record",
            };
            using (LaunchArgs.OverrideForTests(args))
            using (LifetimeScope.Enqueue(builder => builder.RegisterInstance<IAudioEngine>(new StuckAudioEngine())))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();

            // 1. a heartbeat from the start, before the app has given up and although the sound is not there
            var waited = 0f;
            while (HeartbeatLines().Length == 0 && waited < FirstBeatWaitSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            var first = HeartbeatLines();
            Assert.GreaterOrEqual(first.Length, 2, "heartbeat.txt exists although the sound never started");
            Assert.IsFalse(app.IsRunning);
            Assert.AreEqual(string.Empty, app.FailureMessage, "it was written before the app gave up: it is the first sign of life");
            Assert.AreEqual("audio=starting", first[1]);
            Assert.AreEqual("app=loading", first[2], "and the app says it is still booting");
            Assert.Less((DateTime.UtcNow - StampOf(first[0])).TotalSeconds, 3.0, "a fresh beat");

            // 2. the app gives up on the sound after --audio-timeout and says so
            waited = 0f;
            while (string.IsNullOrEmpty(app.FailureMessage) && waited < GiveUpWaitSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            StringAssert.Contains("audio", app.FailureMessage);
            yield return new WaitForSecondsRealtime(HeartbeatIntervalSec * 2f + 0.3f);
            var failed = HeartbeatLines();
            CollectionAssert.AreEqual(new[] { "audio=failed", "app=failed" }, failed.Skip(1).ToArray());
            Assert.Greater((StampOf(failed[0]) - StampOf(first[0])).TotalSeconds, AudioTimeoutSec - 1.0, "and it goes on beating after it failed: the app is alive, it is only deaf");

            // 3. the health CSV got its rows although the app never ran
            yield return new WaitForSecondsRealtime(HealthIntervalSec + 0.5f);
            var rows = HealthRows();
            Assert.GreaterOrEqual(rows.Length, 3, "a header and at least two rows");
            Assert.AreEqual(HealthRow.CsvHeader, rows[0]);
            var header = HealthRow.CsvHeader.Split(',');
            string Cell(string row, string name) => row.Split(',')[Array.IndexOf(header, name)];
            Assert.AreEqual("0.0", Cell(rows[1], "fps_avg"), "the loading frames are not counted as if the meadow ran");
            Assert.That(rows.Skip(1).Select(r => Cell(r, "audio_state")), Has.Member("failed").Or.Member("starting"));
            Assert.AreEqual("failed", Cell(rows.Last(), "audio_state"));
            Assert.AreEqual("Failed", Cell(rows.Last(), "experience_state"));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "content-source.txt")), "which content was tried is written also when the app did not get to run");
        }

        DownbeatApp _app;

        IEnumerator LoadSceneWith(IAudioEngine engine, Downbeat.Content.IContentRepository repository)
        {
            var args = new[]
            {
                "Downbeat", "--health-dir", _dir, "--health-interval", HealthIntervalSec.ToString(CultureInfo.InvariantCulture), "--heartbeat-interval", HeartbeatIntervalSec.ToString(CultureInfo.InvariantCulture),
                "--audio-timeout", "60", "--no-record",
            };
            using (LaunchArgs.OverrideForTests(args))
            using (LifetimeScope.Enqueue(builder =>
            {
                builder.RegisterInstance<IAudioEngine>(engine);
                if (repository != null) builder.RegisterInstance<Downbeat.Content.IContentRepository>(repository);
            }))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            _app = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>().Container.Resolve<DownbeatApp>();
        }

        /// <summary>Looks at the heartbeat every frame for this long and remembers every distinct content it had.</summary>
        IEnumerator WatchHeartbeat(float seconds, List<string> seen, Func<bool> stopWhen = null)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && (stopWhen == null || !stopWhen()))
            {
                var lines = HeartbeatLines();
                if (lines.Length >= 3) seen.Add(lines[1] + " " + lines[2]);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator AnAppThatNeverFinishesLoading_NeverSaysRunning_EvenThoughTheAudioClockRuns()
        {
            yield return LoadSceneWith(new MovingAudioEngine(), new HangingRepository());
            var seen = new List<string>();

            yield return WatchHeartbeat(4f, seen);

            Assert.IsFalse(_app.IsRunning);
            Assert.AreEqual(string.Empty, _app.FailureMessage, "nothing failed: it is just never ready");
            Assert.IsNotEmpty(seen, "a heartbeat is there");
            Assert.That(seen, Has.None.Contains("app=running"), "the supervisor would take the app for healthy and leave a black screen all night");
            Assert.AreEqual("audio=ok app=loading", seen.Last(), "the clock runs, the app does not: the heartbeat says both");
        }

        [UnityTest]
        public IEnumerator ABootThatThrows_EndsInAppFailed_NotInSilentLoading()
        {
            LogAssert.ignoreFailingMessages = true;   // the exception is logged on purpose
            yield return LoadSceneWith(new ThrowingAudioEngine(), null);
            var seen = new List<string>();

            yield return WatchHeartbeat(20f, seen, () => !string.IsNullOrEmpty(_app.FailureMessage));
            yield return WatchHeartbeat(HeartbeatIntervalSec * 2f + 0.3f, seen);
            LogAssert.ignoreFailingMessages = false;

            StringAssert.StartsWith("boot:", _app.FailureMessage, "the reason is logged, not lost");
            StringAssert.Contains("audio configure exploded", _app.FailureMessage);
            Assert.IsFalse(_app.IsRunning);
            Assert.That(seen, Has.None.Contains("app=running"));
            Assert.AreEqual("audio=ok app=failed", seen.Last(), "audio=ok is true and means nothing: the app has given up");
        }

        [UnityTest]
        public IEnumerator AThrowInANestedPartOfBoot_IsCaughtToo()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadSceneWith(new MovingAudioEngine(), new ThrowingRepository());
            var seen = new List<string>();

            yield return WatchHeartbeat(20f, seen, () => !string.IsNullOrEmpty(_app.FailureMessage));
            yield return WatchHeartbeat(HeartbeatIntervalSec * 2f + 0.3f, seen);
            LogAssert.ignoreFailingMessages = false;

            StringAssert.StartsWith("boot:", _app.FailureMessage);
            StringAssert.Contains("content load exploded", _app.FailureMessage);
            Assert.That(seen, Has.None.Contains("app=running"));
            Assert.AreEqual("audio=ok app=failed", seen.Last());
        }
    }
}
