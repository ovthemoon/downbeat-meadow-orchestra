using System.Collections;
using Downbeat.App;
using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>
    /// Play mode uses the same NativeAudioEngine (Unity audio, PlayScheduled) as the players.
    /// Boots the real scene against the repository's content folder and plays one note.
    /// </summary>
    public class EditorPlayModeSmokeTest
    {
        const int MainSceneBuildIndex = 0; // the only scene in the build settings
        const float BootTimeoutSec = 40f;
        const float PlayWaitSec = 1.5f;

        [UnityTest]
        public IEnumerator Boots_with_the_repository_content_and_schedules_a_note()
        {
            yield return SceneManager.LoadSceneAsync(MainSceneBuildIndex, LoadSceneMode.Single);
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            Assert.IsNotNull(scope, "the scene needs a DownbeatLifetimeScope");
            var app = scope.Container.Resolve<DownbeatApp>();

            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);

            Assert.IsNotNull(Object.FindFirstObjectByType<Downbeat.Meadow.MeadowStage>(), "the meadow is part of the scene");
            Assert.IsTrue(Object.FindFirstObjectByType<Downbeat.Meadow.MeadowStage>().IsConfigured, "the meadow is built from content.json");

            var input = scope.Container.Resolve<DebugPointerInput>();
            input.Inject(new NoteIntent(Part.Harp, 3, 0.8f));
            yield return new WaitForSecondsRealtime(PlayWaitSec);
            Assert.AreEqual(1, scope.Container.Resolve<IAudioEngine>().Stats.Scheduled, "the injected note was scheduled on the audio engine");
        }
    }
}
