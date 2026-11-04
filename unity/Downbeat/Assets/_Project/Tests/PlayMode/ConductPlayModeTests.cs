using System.Collections;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>The real scene with the recorded demo hands as the camera (what <c>--soak</c> and a camera-less demo use): a raised hand conducts, and the old play by striking is still there behind <c>--play-mode strike</c>.</summary>
    public class ConductPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float PlaySec = 12f;

        static IEnumerator Start(string playMode, System.Action<DownbeatApp, ConductPlayer> ready)
        {
            var args = playMode == null
                ? new[] { "Downbeat", "--input", "replay", "--mute", "--no-record" }
                : new[] { "Downbeat", "--input", "replay", "--mute", "--no-record", "--play-mode", playMode };
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            ready(app, scope.Container.Resolve<ConductPlayer>());
        }

        [UnityTest]
        public IEnumerator TheDemoHands_ConductTheMeadow_ByDefault()
        {
            DownbeatApp app = null;
            ConductPlayer conduct = null;
            yield return Start(null, (a, c) => { app = a; conduct = c; });

            yield return new WaitForSecondsRealtime(PlaySec);

            Assert.IsTrue(conduct.IsConducting, "the shipped content conducts");
            var seconds = PlaySec - 0.5f;
            Assert.GreaterOrEqual(app.HandNotes, 12, $"{app.HandNotes} hand notes in {seconds:0} s of the demo hands: a hand that is raised keeps playing");
            Assert.LessOrEqual(app.HandNotes / seconds, 14f, "and it is music, not a machine gun: well under the 16 slots a second the grid has for three voices");
            Assert.GreaterOrEqual(app.Energy.Level, 1, "the meadow wakes up");
            TestContext.Out.WriteLine($"CONDUCT-PLAYMODE notes={app.HandNotes} in {seconds:0} s energy={app.Energy.Value:0.000} level={app.Energy.Level}");
        }

        [UnityTest]
        public IEnumerator PlayModeStrike_IsTheOldPlayByStrikingDown()
        {
            DownbeatApp app = null;
            ConductPlayer conduct = null;
            yield return Start("strike", (a, c) => { app = a; conduct = c; });

            yield return new WaitForSecondsRealtime(PlaySec);

            Assert.IsFalse(conduct.IsConducting);
            Assert.GreaterOrEqual(app.HandNotes, 1, "the strikes of the demo hands still make notes");
            TestContext.Out.WriteLine($"STRIKE-PLAYMODE notes={app.HandNotes} in {PlaySec - 0.5f:0} s");
        }
    }
}
