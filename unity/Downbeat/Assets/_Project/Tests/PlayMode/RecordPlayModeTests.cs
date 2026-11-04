using System.Collections;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Meadow;
using Downbeat.Record;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>The record screen in the real scene (one screen: a strip over the lower part of the meadow).</summary>
    public class RecordPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 960;
        const int Height = 540;

        static IEnumerator LoadAndWaitUntilRunning(System.Action<DownbeatApp, MeadowStage> done)
        {
            yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            done(app, Object.FindFirstObjectByType<MeadowStage>());
        }

        /// <summary>Mean absolute difference of the luminance between two pictures, over the rows from <paramref name="fromRow"/> to <paramref name="toRow"/> (0 = bottom of the picture).</summary>
        static float MeanDifference(Texture2D a, Texture2D b, float fromRow, float toRow)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            var from = (int)(fromRow * a.height);
            var to = (int)(toRow * a.height);
            var sum = 0.0;
            var count = 0;
            for (var y = from; y < to; y++)
            {
                for (var x = 0; x < a.width; x++)
                {
                    var i = y * a.width + x;
                    var la = 0.299f * pa[i].r + 0.587f * pa[i].g + 0.114f * pa[i].b;
                    var lb = 0.299f * pb[i].r + 0.587f * pb[i].g + 0.114f * pb[i].b;
                    sum += Mathf.Abs(la - lb);
                    count++;
                }
            }
            return (float)(sum / count);
        }

        [UnityTest]
        public IEnumerator OnOneScreen_TheRecordIsAStripOverTheLowerPartOfTheMeadow_AndHidingItGivesTheMeadowBack()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var view = app.Record.View;
            Assert.AreEqual(RecordMount.Strip, view.Mount, "the editor has one screen");
            Assert.AreEqual(DisplayMode.SingleOverlay, app.Display.Mode);

            view.Visible = false;
            yield return null;
            var without = MeadowCapture.Render(stage.Camera, Width, Height);
            view.Visible = true;
            yield return null;
            yield return null;
            var with = MeadowCapture.Render(stage.Camera, Width, Height);

            var strip = stage.Bundle.Dto.display.overlayFraction;
            var lower = MeanDifference(with, without, 0.02f, strip - 0.02f);
            var upper = MeanDifference(with, without, strip + 0.1f, 0.98f);
            Assert.Greater(lower, 12f, "the strip is drawn over the lower 28 % of the meadow");
            Assert.Less(upper, lower / 3f, "and nothing above it changes (the meadow itself only moves a little between two pictures)");
            Object.Destroy(with);
            Object.Destroy(without);
        }

        [UnityTest]
        public IEnumerator ANoteThatIsHeard_IsOnTheLiveScore_AndTheNatureLinesFollowTheLevel()
        {
            DownbeatApp app = null;
            yield return LoadAndWaitUntilRunning((a, _) => app = a);
            var view = app.Record.View;
            app.Record.View.Visible = true;
            yield return null;
            var before = view.FlowerQuadCount;

            var heardOneSecondAgo = AudioSettings.dspTime - 1.0;
            app.Record.OnNote(new ScheduledNote(1, heardOneSecondAgo, heardOneSecondAgo - 0.1, Part.Marimba, 4, 0.9f, false));
            app.HoldEnergy(0.85f);
            yield return new WaitForSecondsRealtime(0.5f);

            Assert.Greater(view.FlowerQuadCount, before, "the note is a flower on the score");
            Assert.AreEqual(3, app.Record.Score.ActiveLayerCount, "level 3: all three nature lines are lit");
            app.ReleaseEnergy();
        }
    }
}
