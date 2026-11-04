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
    /// <summary>The nature layers in the real scene: the energy decides what is scheduled, how the sky and the fireflies look, and the drum lights the far hills' haze.</summary>
    public class NaturePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float WaitForLeadSec = 4.2f;       // the nature score is planned a bar (2.5 s) ahead; the first note after that is on the clock
        const float WaitForSkySec = 7f;          // the sky glides towards the energy over about 2.5 s
        const float HillRowFrom = 0.34f;
        const float HillRowTo = 0.44f;

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

        static IEnumerator WaitRealSeconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        static float BandLuminance(Texture2D picture)
        {
            // a texture's row 0 is the bottom of the picture; the rows of the haze are counted from the top
            var from = (int)((1f - HillRowTo) * picture.height);
            var to = (int)((1f - HillRowFrom) * picture.height);
            var sum = 0.0;
            var count = 0;
            var pixels = picture.GetPixels32();
            for (var y = from; y < to; y++)
            {
                for (var x = 0; x < picture.width; x++)
                {
                    var c = pixels[y * picture.width + x];
                    sum += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
                    count++;
                }
            }
            return (float)(sum / count);
        }

        [UnityTest]
        public IEnumerator ANewMeadow_IsIdleAndQuiet_NoEnergy_NoNatureNotes()
        {
            DownbeatApp app = null;
            yield return LoadAndWaitUntilRunning((a, _) => app = a);

            yield return WaitRealSeconds(WaitForLeadSec);

            Assert.AreEqual(ExperienceState.Idle, app.Experience.State);
            Assert.AreEqual(0, app.Energy.Level);
            Assert.AreEqual(0, app.NatureBridge.Scheduled, "level 0 plays nothing");
        }

        [UnityTest]
        public IEnumerator HoldingTheEnergyHigh_StartsTheNatureScore_WithoutDroppingNotes_AndMovesTheSkyAndFireflies()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var skyBefore = stage.Backdrop.SunsetT;

            app.HoldEnergy(0.85f);
            yield return WaitRealSeconds(WaitForSkySec);

            Assert.AreEqual(3, app.Energy.Level);
            Assert.Greater(app.NatureBridge.Scheduled, 2, "fireflies, a chord and drums are on the clock");
            Assert.AreEqual(0, app.NatureBridge.Dropped, "nothing was late");
            Assert.AreEqual(0, app.NatureBridge.Unmapped);
            Assert.Greater(stage.Backdrop.SunsetT, skyBefore + 0.5f, "the sky has moved towards the sunset");
            Assert.Greater(stage.Particles.FireflyLevel, 0.5f, "the fireflies came with the energy");
            app.ReleaseEnergy();
        }

        [UnityTest]
        public IEnumerator ADrumBeat_LightsTheHillHaze_AndItFadesBackToTheQuietPicture()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((a, s) => { app = a; stage = s; });
            var visuals = app.NatureVisuals;
            visuals.ControlsSky = false; // the picture is compared at one sky

            yield return WaitRealSeconds(0.5f);
            var quiet = MeadowCapture.Render(stage.Camera, 960, 540);
            visuals.Model.OnHillBeat(); // the drum is heard
            yield return null;
            var lit = MeadowCapture.Render(stage.Camera, 960, 540);
            yield return WaitRealSeconds(1.2f); // the pulse lasts hillPulseSec
            var after = MeadowCapture.Render(stage.Camera, 960, 540);

            var quietBand = BandLuminance(quiet);
            Assert.Greater(BandLuminance(lit), quietBand + 3f, "the band of the far hills is brighter while the pulse is on");
            Assert.AreEqual(quietBand, BandLuminance(after), 2.0f, "and back to the quiet picture when it is off");
            Object.Destroy(quiet);
            Object.Destroy(lit);
            Object.Destroy(after);
        }
    }
}
