using System.Collections;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>Automatic quality in the real scene: it starts from the graphics device, a forced change of level rebuilds the meadow at the other tier and sets the render scale, and an explicit tier turns it off.</summary>
    public class AutoQualityPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;

        static IEnumerator Start(string[] extraArgs, System.Action<DownbeatApp, MeadowStage> ready)
        {
            var args = new System.Collections.Generic.List<string> { "Downbeat", "--input", "mouse", "--mute", "--no-record" };
            args.AddRange(extraArgs);
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var app = Object.FindFirstObjectByType<DownbeatLifetimeScope>().Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            ready(app, Object.FindFirstObjectByType<MeadowStage>());
        }

        [UnityTest]
        public IEnumerator ByDefaultItIsAutomatic_AndAForcedLevelRebuildsTheMeadowAtTheOtherTier()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new string[0], (a, s) => { app = a; stage = s; });
            Assert.IsTrue(app.Quality.IsAuto, "no --quality: automatic");
            Assert.AreEqual(0, app.Quality.Level, "this machine's graphics do not look weak: it starts at the top");
            Assert.AreEqual("High", stage.QualityName);

            app.Quality.ForceLevel(3, Time.realtimeSinceStartupAsDouble, idle: true);   // Low at 0.85
            yield return null;
            yield return null;

            Assert.AreEqual(3, app.Quality.Level);
            Assert.AreEqual("Low", stage.QualityName, "the grass and the flowers are those of the Low tier");
            Assert.AreEqual("Low", QualitySettings.names[QualitySettings.GetQualityLevel()]);
            Assert.AreEqual(0.85f, RenderLevers.RenderScale, 0.02f);
            Assert.AreEqual(30, RenderLevers.FrameCap, "Low is held to 30 frames a second");
        }

        [UnityTest]
        public IEnumerator AnExplicitTier_LocksTheQuality_AndNoLevelIsForced()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new[] { "--quality", "Low" }, (a, s) => { app = a; stage = s; });

            Assert.IsFalse(app.Quality.IsAuto);
            Assert.AreEqual(-1, app.Quality.Level, "locked: no step of the ladder");
            Assert.AreEqual("Low", stage.QualityName);

            app.Quality.ForceLevel(0, Time.realtimeSinceStartupAsDouble, idle: true);
            yield return null;

            Assert.AreEqual("Low", stage.QualityName, "nothing changes by itself or by a forced level");
            Assert.AreEqual(0.7f, RenderLevers.RenderScale, 0.02f, "the Low tier's own render scale");
        }

        [UnityTest]
        public IEnumerator TheRenderScaleOverride_KeepsTheTierAndFixesTheNumber()
        {
            DownbeatApp app = null;
            yield return Start(new[] { "--quality", "High", "--render-scale", "0.5", "--max-fps", "45" }, (a, s) => app = a);

            Assert.IsFalse(app.Quality.IsAuto);
            Assert.AreEqual(0.5f, RenderLevers.RenderScale, 0.01f);
            Assert.AreEqual(45, RenderLevers.FrameCap);
        }

        [UnityTest]
        public IEnumerator AFrameCapAlone_PinsOnlyTheCap_TheQualityStaysAutomaticAndTheTierIsStillPicked()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new[] { "--max-fps", "30" }, (a, s) => { app = a; stage = s; });

            Assert.IsTrue(app.Quality.IsAuto, "-MaxFps alone must not lock the tier (to High, the first one)");
            Assert.AreEqual(30, RenderLevers.FrameCap);
            Assert.AreEqual(0, app.Quality.Level, "this machine's graphics do not look weak: High, from the start estimate");

            app.Quality.ForceLevel(2, Time.realtimeSinceStartupAsDouble, idle: true);   // Low@1.0
            yield return null;
            yield return null;

            Assert.AreEqual("Low", stage.QualityName, "the controller still moves the tier");
            Assert.AreEqual(30, RenderLevers.FrameCap, "the cap that was fixed by hand stays, whatever the tier says");
        }

        [UnityTest]
        public IEnumerator ARenderScaleAlone_PinsOnlyTheScale_TheQualityStaysAutomaticAndTheTierIsStillPicked()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new[] { "--render-scale", "0.5" }, (a, s) => { app = a; stage = s; });

            Assert.IsTrue(app.Quality.IsAuto);
            Assert.AreEqual(0.5f, RenderLevers.RenderScale, 0.01f);
            Assert.AreEqual("High", stage.QualityName);

            app.Quality.ForceLevel(1, Time.realtimeSinceStartupAsDouble, idle: true);   // with a pinned scale the ladder is High, Low
            yield return null;
            yield return null;

            Assert.AreEqual("Low", stage.QualityName);
            Assert.AreEqual(0.5f, RenderLevers.RenderScale, 0.01f, "the scale that was fixed by hand stays, whatever the tier or the ladder say");
        }

        [UnityTest]
        public IEnumerator WhenTheMeadowIsBuiltAgain_TheScaleOfTheControllerIsStillThere()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new string[0], (a, s) => { app = a; stage = s; });
            app.Quality.ForceLevel(3, Time.realtimeSinceStartupAsDouble, idle: true);   // Low@0.85
            yield return null;
            yield return null;
            Assert.AreEqual(0.85f, RenderLevers.RenderScale, 0.02f);

            stage.Configure(stage.Bundle, stage.QualityName);   // what F5 (a content reload) and switching the meadow off and on do
            yield return null;

            Assert.AreEqual("Low", stage.QualityName);
            Assert.AreEqual(0.85f, RenderLevers.RenderScale, 0.02f, "the Low tier's own 0.7 must not replace the scale the controller is at");
        }

        [UnityTest]
        public IEnumerator OnlyTheMeadowIsScaled_ACaptureIsDrawnAtFullScale_AndKeepsItsSize()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new[] { "--quality", "High" }, (a, s) => { app = a; stage = s; });
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.IsTrue(RenderLevers.ScalesCamera(stage.Camera), "the camera of the meadow is scaled");
            var target = new RenderTexture(64, 64, 0);
            var previousTarget = stage.Camera.targetTexture;
            stage.Camera.targetTexture = target;
            Assert.IsFalse(RenderLevers.ScalesCamera(stage.Camera), "the same camera drawing into a texture (a capture) is not");
            stage.Camera.targetTexture = previousTarget;
            target.Release();
            Object.Destroy(target);

            // two captures in the same frame: the meadow's scale is 0.5 for one and 1 for the other; a capture must not care
            RenderLevers.SetRenderScale(0.5f);
            var scaled = MeadowCapture.Render(stage.Camera, 480, 270);
            RenderLevers.SetRenderScale(1f);
            var full = MeadowCapture.Render(stage.Camera, 480, 270);
            RenderLevers.SetRenderScale(0.5f);
            var again = MeadowCapture.Render(stage.Camera, 480, 270);

            var difference = MeanAbsoluteDifference(scaled, full);
            var repeat = MeanAbsoluteDifference(scaled, again);
            Assert.AreEqual(480, scaled.width, "a capture keeps the size that was asked for, whatever the scale");
            Assert.AreEqual(270, scaled.height);
            Object.Destroy(scaled);
            Object.Destroy(full);
            Object.Destroy(again);
            Assert.Less(difference, 0.003f, "a capture at the meadow's scale 0.5 is the capture at scale 1: it is not drawn small and stretched");
            Assert.Less(repeat, 0.003f);
            Assert.AreEqual(0.5f, RenderLevers.RenderScale, 1e-4f, "and the meadow's scale is back after a capture");
        }

        [UnityTest]
        public IEnumerator TheRecordScreenIsDrawnAtFullScale_WhicheverDisplayTheMeadowIsOn()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Start(new[] { "--quality", "High" }, (a, s) => { app = a; stage = s; });
            yield return new WaitForSecondsRealtime(0.3f);
            RenderLevers.SetRenderScale(0.6f);
            var record = new GameObject("record screen camera").AddComponent<Camera>();   // what the record view builds: an orthographic camera on a display of its own
            record.orthographic = true;
            var meadowDisplay = stage.Camera.targetDisplay;

            // the layout of the default: the meadow on display 1 (index 0), the record on display 2
            stage.Camera.targetDisplay = 0;
            record.targetDisplay = 1;
            Assert.AreEqual(0.6f, RenderLevers.ScaleFor(stage.Camera), 1e-4f, "the meadow is scaled");
            Assert.AreEqual(1f, RenderLevers.ScaleFor(record), "the record screen is not");

            // the other layout, which the runbook tells people to try: meadowIndex 1, recordIndex 0
            stage.Camera.targetDisplay = 1;
            record.targetDisplay = 0;
            Assert.AreEqual(0.6f, RenderLevers.ScaleFor(stage.Camera), 1e-4f, "the meadow is still the scaled one, on the second display");
            Assert.AreEqual(1f, RenderLevers.ScaleFor(record), "and the record screen is not, on the first");

            stage.Camera.targetDisplay = meadowDisplay;
            Object.Destroy(record.gameObject);
        }

        static float MeanAbsoluteDifference(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels();
            var pb = b.GetPixels();
            var sum = 0f;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }
    }
}
