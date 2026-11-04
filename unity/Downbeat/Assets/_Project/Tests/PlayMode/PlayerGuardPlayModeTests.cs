using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
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
    /// The self-exit in the real scene. The editor never ends itself, so every test here turns the guard on with <see cref="PlayerGuard.OverrideTerminateForTests"/>, which also puts a recording stub in place of the
    /// process kill: nothing is ever killed. A frozen main thread is a <see cref="Thread.Sleep(int)"/> of the test, which stops the frames exactly as a hung graphics call does.
    /// </summary>
    public class PlayerGuardPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int FrozenMs = 4500;         // longer than --stall-exit-sec 2 plus the one-second look of the watchdog thread
        const string StallFlagSec = "2";
        const string SoakLine = "d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]";

        string _dir;
        ConcurrentQueue<int> _terminated;
        IDisposable _override;
        DownbeatApp _app;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "downbeat-guard-" + Guid.NewGuid().ToString("N"));
            _terminated = new ConcurrentQueue<int>();   // one fixture object serves every test: what an earlier test ended must not show here
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            _app?.Guard?.Stop();
            _override?.Dispose();
            _override = null;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        /// <summary>A content repository whose load never ends: the app stays Loading.</summary>
        sealed class HangingRepository : Downbeat.Content.IContentRepository
        {
            public IEnumerator Load(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed)
            {
                while (true) yield return null;
            }

            public IEnumerator Reload(Action<Downbeat.Content.ContentBundle> onLoaded, Action<string> onFailed) => Load(onLoaded, onFailed);

            public void ConfirmLoaded() { }
        }

        IEnumerator LoadScene(bool guardOn, Downbeat.Content.IContentRepository repository = null)
        {
            LogAssert.ignoreFailingMessages = true;   // the exit writes its reason to the log as an error (set here, in the test's own scope: a SetUp's setting does not reach the test)
            if (guardOn) _override = PlayerGuard.OverrideTerminateForTests(_terminated.Enqueue);
            var args = new[] { "Downbeat", "--input", "mouse", "--mute", "--no-tutorial", "--no-record", "--health-dir", _dir, "--stall-exit-sec", StallFlagSec };
            IDisposable repositoryScope = repository == null ? null : (IDisposable)LifetimeScope.Enqueue(builder => builder.RegisterInstance<Downbeat.Content.IContentRepository>(repository));
            using (LaunchArgs.OverrideForTests(args))
            using (repositoryScope)
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            _app = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>().Container.Resolve<DownbeatApp>();
        }

        IEnumerator WaitUntilRunning()
        {
            var waited = 0f;
            while (!_app.IsRunning && string.IsNullOrEmpty(_app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!_app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + _app.FailureMessage);
            Assert.IsTrue(_app.IsRunning, _app.FailureMessage);
        }

        string ExitRecord()
        {
            var path = Path.Combine(_dir, HealthFileWriter.ExitRecordFile);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        [UnityTest]
        public IEnumerator InTheEditor_TheAppHasNoGuard_EvenWithTheFlag_AndAFrozenMainThreadIsLeftAlone()
        {
            yield return LoadScene(guardOn: false);
            yield return WaitUntilRunning();

            Thread.Sleep(FrozenMs);
            yield return null;

            Assert.IsNull(_app.Guard, "Application.isEditor: the editor is never ended by the game it runs");
            Assert.IsTrue(_terminated.IsEmpty);
        }

        [UnityTest]
        public IEnumerator ARunningApp_WhoseMainThreadFreezes_EndsItself_AndWritesWhy()
        {
            yield return LoadScene(guardOn: true);
            yield return WaitUntilRunning();
            Assert.IsNotNull(_app.Guard, "the flag and a player (here: the test override) make the guard");

            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsTrue(_terminated.IsEmpty, "frames are coming: no stall");

            Thread.Sleep(FrozenMs);   // the main thread freezes: no frame, no beat

            Assert.IsTrue(SpinWait.SpinUntil(() => !_terminated.IsEmpty, 3000), "the watchdog thread ended the player");
            CollectionAssert.AreEqual(new[] { SelfExitReasons.MainThreadStalledExitCode }, _terminated);
            var record = ExitRecord();
            StringAssert.Contains("reason=main_thread_stalled exit_code=72", record);
            StringAssert.Contains("(limit 2 s)", record);
        }

        [UnityTest]
        public IEnumerator AnAppThatIsStillLoading_IsNotEnded_HoweverLongTheMainThreadStands()
        {
            yield return LoadScene(guardOn: true, repository: new HangingRepository());
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsFalse(_app.IsRunning);
            Assert.IsNotNull(_app.Guard);

            Thread.Sleep(FrozenMs);
            yield return null;

            Assert.IsTrue(_terminated.IsEmpty, "the watchdog is armed only when the app runs: a slow boot is the supervisor's grace period");
        }

        [UnityTest]
        public IEnumerator ADeviceLostLineInTheLog_EndsTheApp_AtOnce_AndWritesWhy()
        {
            yield return LoadScene(guardOn: true);
            yield return WaitUntilRunning();

            Debug.LogError(SoakLine);

            Assert.IsTrue(SpinWait.SpinUntil(() => !_terminated.IsEmpty, 3000));
            CollectionAssert.AreEqual(new[] { SelfExitReasons.GraphicsDeviceLostExitCode }, _terminated);
            var record = ExitRecord();
            StringAssert.Contains("reason=graphics_device_lost exit_code=71", record);
            StringAssert.Contains("[0x887A0005]", record);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(1, _terminated.Count, "its own log line, which has the same text, does not end it twice");
        }
    }
}
