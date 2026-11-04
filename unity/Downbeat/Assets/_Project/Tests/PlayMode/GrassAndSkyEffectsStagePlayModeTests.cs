using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// The helpers of the real meadow for the two rows (not wired to any part yet): a point in the sky for a screen point, the effects of the top row made there, and the two waves of the bottom row.
    /// </summary>
    public class GrassAndSkyEffectsStagePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 1280;
        const int Height = 720;
        const float ScreenTolerance = 0.01f;

        static IEnumerator LoadAndWaitUntilRunning(System.Action<MeadowStage> done)
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
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false;
            done(Object.FindFirstObjectByType<MeadowStage>());
        }

        static float MeanDifference(Texture2D a, Texture2D b, float fromTop, float toTop)
        {
            var yStart = Mathf.RoundToInt((1f - toTop) * a.height);
            var yEnd = Mathf.RoundToInt((1f - fromTop) * a.height);
            var pa = a.GetPixels(0, yStart, a.width, yEnd - yStart);
            var pb = b.GetPixels(0, yStart, b.width, yEnd - yStart);
            var sum = 0f;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }

        // ---- a point in the sky -----------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SkyPoint_IsWhereTheScreenPointLooks_AtTheContentDistance()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var camera = stage.Camera;
            var distance = SkyEffectSettings.From(stage.Settings.skyEffects).Distance;

            foreach (var (vx, vy) in new[] { (0.5f, 0.5f), (0.1f, 0.9f), (0.9f, 0.9f), (0.5f, 0.95f), (0.2f, 0.65f) })
            {
                var point = stage.SkyPoint(vx, vy);
                var inCamera = camera.transform.InverseTransformPoint(point);
                Assert.AreEqual(distance, inCamera.z, 1e-3f, $"({vx}, {vy}) is {distance} m in front of the camera, along its line of sight");
                var back = camera.WorldToViewportPoint(point);
                Assert.AreEqual(vx, back.x, ScreenTolerance, $"({vx}, {vy}) looks at x");
                Assert.AreEqual(vy, back.y, ScreenTolerance, $"({vx}, {vy}) looks at y (up is more)");
            }
            Assert.Less(stage.SkyPoint(0.1f, 0.8f).x, stage.SkyPoint(0.9f, 0.8f).x, "left of the screen is left in the world");
            Assert.Greater(stage.SkyPoint(0.5f, 0.95f).y, stage.SkyPoint(0.5f, 0.7f).y, "the top of the screen is higher");
        }

        [UnityTest]
        public IEnumerator SkyPoint_OfTheTopRow_IsAboveTheGrass_EvenAtTheHorizon()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);

            // the lowest point of the top row is the horizon, where it meets the ground row: a point there is in the air in front of the far edge of the field, never under a hill
            var horizon = stage.Overlay.HorizonViewport;
            foreach (var vx in new[] { 0.02f, 0.1f, 0.5f, 0.9f, 0.98f })
            {
                var point = stage.SkyPoint(vx, horizon);
                // 0.25 m here, where the test asked for 0.8 m while the lowest point of the top row was 0.64 of the way up the picture: the lowest point is now the horizon itself, where the line of sight only
                // just clears the far edge of the field, so a point at the sky distance on it is lifted no further than HandPlacement's least clearance over the hills (0.3 m), with a little to spare for the float
                Assert.Greater(point.y, stage.Terrain.HeightAt(point.x, point.z) + 0.25f, $"x {vx}: above the ground and the lower blades");
                var back = stage.Camera.WorldToViewportPoint(point);
                Assert.AreEqual(vx, back.x, ScreenTolerance, $"x {vx}: and still where the screen point looks");
                Assert.AreEqual(horizon, back.y, ScreenTolerance);
            }
        }

        // ---- the effects made there -------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SkyEffectNow_IsMadeAtTheScreenPoint()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var positions = new List<Vector3>();
            var seen = new Dictionary<SkyEffectKind, Vector2>
            {
                { SkyEffectKind.LightRing, new Vector2(0.2f, 0.9f) }, { SkyEffectKind.Seeds, new Vector2(0.5f, 0.8f) }, { SkyEffectKind.StarDust, new Vector2(0.8f, 0.9f) },
            };

            foreach (var pair in seen) stage.EmitSkyNow(pair.Key, pair.Value.x, pair.Value.y, Color.white, 1f);

            foreach (var pair in seen)
            {
                stage.Particles.GetSkyPositions(pair.Key, positions);
                Assert.Greater(positions.Count, 0, pair.Key.ToString());
                foreach (var p in positions)
                {
                    var onScreen = stage.Camera.WorldToViewportPoint(p);
                    Assert.AreEqual(pair.Value.x, onScreen.x, 0.08f, $"{pair.Key} is where the hand is on the screen (x)");
                    Assert.AreEqual(pair.Value.y, onScreen.y, 0.08f, $"{pair.Key} (y)");
                }
            }
        }

        [UnityTest]
        public IEnumerator TheEffectsInTheAir_AreSeenInTheSkyOfThePicture()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Backdrop.SetSunset(0.5f);
            yield return new WaitForSecondsRealtime(0.5f);
            var before = MeadowCapture.Render(stage.Camera, Width, Height);

            var colour = new Color(1f, 0.85f, 0.5f, 1f);
            stage.EmitSkyNow(SkyEffectKind.LightRing, 0.2f, 0.85f, colour, 1f);
            stage.EmitSkyNow(SkyEffectKind.Seeds, 0.5f, 0.8f, new Color(1f, 1f, 0.9f, 1f), 1f);
            stage.EmitSkyNow(SkyEffectKind.StarDust, 0.8f, 0.9f, new Color(0.75f, 0.89f, 1f, 1f), 1f);
            yield return new WaitForSecondsRealtime(0.5f);
            var during = MeadowCapture.Render(stage.Camera, Width, Height);
            CaptureOutput.SavePicture(during, "six-cells-sky-effects.png");

            var inTheSky = MeanDifference(before, during, 0f, 0.35f);   // rows 0..0.35 from the top: above the hills, where the old haze would have hidden every particle
            Object.Destroy(before);
            Object.Destroy(during);
            Assert.Greater(inTheSky, 0.0003f, "the ring, the seeds and the dust are drawn in the sky of the picture");
        }

        [UnityTest]
        public IEnumerator TheLowTier_MakesHalfTheSkyParticles_InTheRealMeadow()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.ApplyQuality("High");
            stage.EmitSkyNow(SkyEffectKind.StarDust, 0.5f, 0.9f, Color.white, 1f);
            var high = stage.Particles.EmittedSky(SkyEffectKind.StarDust);

            stage.ApplyQuality("Low");
            stage.EmitSkyNow(SkyEffectKind.StarDust, 0.5f, 0.9f, Color.white, 1f);
            var low = stage.Particles.EmittedSky(SkyEffectKind.StarDust);

            Assert.IsTrue(stage.Particles.SkyIsLowTier);
            Assert.AreEqual(Mathf.Round(high * stage.Settings.skyEffects.lowCountScale), low, $"{high} on High, {low} on Low");
        }

        [UnityTest]
        public IEnumerator AChangeOfTier_KeepsAnEffectThatIsNotDueYet()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Particles.EmitSky(SkyEffectKind.Seeds, stage.SkyPoint(0.5f, 0.85f), Color.white, 1f, MeadowStage.WindTime + 30.0);
            Assert.AreEqual(1, stage.Particles.PendingSkyCount);

            stage.ApplyQuality("Low");

            Assert.AreEqual(1, stage.Particles.PendingSkyCount, "the effect that waits for its note to be heard is not lost by the rebuild");
        }

        // ---- the two waves of the bottom row ----------------------------------------------------------------------------

        static WindWaveGpu LiveWaveAt(MeadowStage stage, FieldPoint at)
        {
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            var count = stage.Wind.CopyActive(MeadowStage.WindTime, scratch);
            for (var i = 0; i < count; i++)
            {
                if (Mathf.Approximately(scratch[i].OriginX, at.X) && Mathf.Approximately(scratch[i].OriginZ, at.Z)) return scratch[i];
            }
            Assert.Fail($"no live wave starts at ({at.X}, {at.Z}); {count} are alive");
            return default;
        }

        [UnityTest]
        public IEnumerator LowString_LaysTheGrassInALongWave_TowardsTheHandsSide()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var settings = stage.Settings.wind.longWave;
            var left = stage.Layout.HandToField(0.17f, 0.3f);    // the left cell, far up the field
            var right = stage.Layout.HandToField(0.83f, 0.3f);

            stage.AddLongWave(left, 1f);
            stage.AddLongWave(right, 0f);

            var fromTheLeft = LiveWaveAt(stage, left);
            var fromTheRight = LiveWaveAt(stage, right);
            Assert.AreEqual((float)WindWaveKind.Sweep, fromTheLeft.Kind, "a sweep: the same at every depth");
            Assert.Less(fromTheLeft.Speed, 0f, "a hand on the left lays the grass towards the left");
            Assert.AreEqual(settings.speed, -fromTheLeft.Speed, 1e-4f);
            Assert.Greater(fromTheRight.Speed, 0f, "a hand on the right, towards the right");
            Assert.AreEqual(settings.widthScale, fromTheLeft.WidthScale, 1e-4f, "wider than the other waves");
            Assert.Greater(settings.widthScale, 1f);
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), fromTheLeft.Amplitude, 1e-4f, "as hard as the hand played");
            Assert.AreEqual(settings.amplitude, fromTheRight.Amplitude, 1e-4f);
            Assert.AreEqual(settings.lifeSec, fromTheLeft.LifeSec, 1e-4f);

            var push = stage.Wind.SampleVector(left.X, left.Z, MeadowStage.WindTime);
            Assert.Less(push.X, -0.1f, "and the grass at the hand is laid that way");
        }

        [UnityTest]
        public IEnumerator HandDrum_PushesAWideRing_StrongerThanANote()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var radial = stage.Settings.wind.noteRadial;
            var drum = stage.Settings.wind.drumRing;
            var at = stage.Layout.HandToField(0.83f, 0.8f);

            stage.AddDrumRing(at, 1f);

            var ring = LiveWaveAt(stage, at);
            Assert.AreEqual((float)WindWaveKind.Radial, ring.Kind, "a ring");
            Assert.AreEqual(drum.amplitude * (1f + drum.velocityGain), ring.Amplitude, 1e-4f);
            Assert.Greater(ring.Amplitude, 1.5f * radial.amplitude * (1f + radial.velocityGain), "stronger than the ring of a note");
            Assert.AreEqual(drum.widthScale, ring.WidthScale, 1e-4f, "and wider");
            Assert.AreEqual(drum.speed, ring.Speed, 1e-4f);
            Assert.AreEqual(drum.lifeSec, ring.LifeSec, 1e-4f);
        }
    }
}
