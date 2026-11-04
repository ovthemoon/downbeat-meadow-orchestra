using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Downbeat.App;
using Downbeat.Content;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>F5 in the real scene: content that is broken is refused and the picture stays; a changed meadow section is built again at the next bar.</summary>
    public class ContentReloadPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int CaptureWidth = 960;
        const int CaptureHeight = 540;
        const float MinPictureBrightness = 0.05f;

        sealed class FakeRepository : IContentRepository
        {
            public string Failure;
            public ContentBundle Bundle;
            public int Reads;
            public Exception Throws;

            public IEnumerator Load(Action<ContentBundle> onLoaded, Action<string> onFailed) => Reload(onLoaded, onFailed);

            public int Confirmed;

            public void ConfirmLoaded() => Confirmed++;

            public IEnumerator Reload(Action<ContentBundle> onLoaded, Action<string> onFailed)
            {
                Reads++;
                yield return null;
                if (Throws != null) throw Throws;
                if (Failure != null) onFailed(Failure);
                else onLoaded(Bundle);
            }
        }

        static IEnumerator LoadAndWaitUntilRunning(Action<DownbeatApp, MeadowStage> ready)
        {
            yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
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
            if (app.Record != null) app.Record.View.Visible = false;
            ready(app, UnityEngine.Object.FindFirstObjectByType<MeadowStage>());
        }

        static float Brightness(MeadowStage stage)
        {
            var picture = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            var pixels = picture.GetPixels();
            var sum = 0f;
            foreach (var pixel in pixels) sum += (pixel.r + pixel.g + pixel.b) / 3f;
            UnityEngine.Object.Destroy(picture);
            return sum / pixels.Length;
        }

        [UnityTest]
        public IEnumerator Reload_OfBrokenContent_IsRefused_AndTheMeadowStaysAsItIs()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var settingsBefore = stage.Settings;
            var amplitudeBefore = settingsBefore.wind.beatSweep.amplitude;
            var broken = new FakeRepository { Failure = "content.json is not valid JSON: Invalid value." };
            var runner = new GameObject("test runner").AddComponent<CoroutineRunner>();
            var reload = new ContentReload(broken, runner, stage, app.ContentReload.Current, new BeatGrid(0.0, 96.0, 4), 16, () => 1.0);

            LogAssert.Expect(LogType.Log, new Regex(@"\[content\] reload requested"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[content\] reload refused, the picture stays as it is: content.json is not valid JSON"));
            reload.Request();
            yield return null;
            yield return null;

            Assert.AreEqual(1, broken.Reads);
            Assert.AreEqual(1, reload.Refusals);
            Assert.AreEqual(0, reload.Reloads);
            Assert.IsFalse(reload.HasPending);
            Assert.AreSame(settingsBefore, stage.Settings, "the meadow was not built again");
            Assert.AreEqual(amplitudeBefore, stage.Settings.wind.beatSweep.amplitude);
            Assert.Greater(Brightness(stage), MinPictureBrightness, "and it still draws");
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        [UnityTest]
        public IEnumerator Reload_ThatThrows_DoesNotBlockTheNextF5()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var repository = new FakeRepository { Throws = new InvalidOperationException("the disk went away") };
            var runner = new GameObject("test runner").AddComponent<CoroutineRunner>();
            var reload = new ContentReload(repository, runner, stage, app.ContentReload.Current, new BeatGrid(0.0, 96.0, 4), 16, () => 1.0);

            LogAssert.Expect(LogType.Log, new Regex(@"\[content\] reload requested"));
            LogAssert.Expect(LogType.Warning, new Regex(@"reload refused, the picture stays as it is: reading the content threw InvalidOperationException: the disk went away"));
            reload.Request();
            yield return null;
            yield return null;
            Assert.AreEqual(1, repository.Reads);
            Assert.AreEqual(1, reload.Refusals, "a reload that threw ends as a refusal");

            repository.Throws = null;
            repository.Failure = "content.json is not valid JSON: Invalid value.";
            LogAssert.Expect(LogType.Log, new Regex(@"\[content\] reload requested"));
            LogAssert.Expect(LogType.Warning, new Regex(@"reload refused"));
            reload.Request();   // ignored for ever before: the flag of the first request was never given back
            yield return null;
            yield return null;

            Assert.AreEqual(2, repository.Reads, "the second F5 was read");
            Assert.AreEqual(2, reload.Refusals);
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        [UnityTest]
        public IEnumerator Reload_OfUnchangedContent_AppliesNothing()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var settingsBefore = stage.Settings;
            var same = new FakeRepository { Bundle = new ContentBundle(JsonUtility.FromJson<ContentDto>(JsonUtility.ToJson(app.ContentReload.Current.Dto)), "file:///x/", new Dictionary<string, Texture2D>()) };
            var runner = new GameObject("test runner").AddComponent<CoroutineRunner>();
            var reload = new ContentReload(same, runner, stage, app.ContentReload.Current, new BeatGrid(0.0, 96.0, 4), 16, () => 1.0);

            LogAssert.Expect(LogType.Log, new Regex(@"\[content\] reload requested"));
            LogAssert.Expect(LogType.Log, new Regex(@"the meadow section is the same"));
            reload.Request();
            yield return null;
            yield return null;

            Assert.AreEqual(0, reload.Refusals);
            Assert.AreEqual(0, reload.Reloads);
            Assert.IsFalse(reload.HasPending);
            Assert.AreSame(settingsBefore, stage.Settings);
            UnityEngine.Object.Destroy(runner.gameObject);
        }

        [UnityTest]
        public IEnumerator Reload_OfAChangedMeadow_IsBuiltAgainAtTheNextBar_AndStillDraws()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var amplitudeBefore = stage.Settings.wind.beatSweep.amplitude;
            ContentBundle changed = null;
            string failure = null;
            yield return new ContentRepository().Load(b => changed = b, e => failure = e);
            Assert.IsNotNull(changed, failure);
            changed.Dto.meadow.wind.beatSweep.amplitude = amplitudeBefore * 3f;
            var heardNow = AudioSettings.dspTime;

            app.ContentReload.Queue(changed, heardNow + 1000.0);   // its bar line is far away: nothing happens yet
            yield return null;
            yield return null;
            Assert.IsTrue(app.ContentReload.HasPending);
            Assert.AreEqual(amplitudeBefore, stage.Settings.wind.beatSweep.amplitude, "not before the bar line");

            app.ContentReload.Queue(changed, 0.0);   // the bar line has come
            yield return null;
            yield return null;

            Assert.IsFalse(app.ContentReload.HasPending);
            Assert.AreEqual(1, app.ContentReload.Reloads);
            Assert.AreEqual(amplitudeBefore * 3f, stage.Settings.wind.beatSweep.amplitude, 1e-5f, "the meadow now has the new number");
            Assert.AreSame(changed, app.ContentReload.Current);
            yield return null;
            Assert.Greater(Brightness(stage), MinPictureBrightness, "the rebuilt meadow draws");
        }
    }
}
