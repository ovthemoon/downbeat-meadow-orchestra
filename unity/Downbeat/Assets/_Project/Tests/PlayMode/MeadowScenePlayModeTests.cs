using System.Collections;
using System.IO;
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
    /// <summary>The real scene against the repository content: the meadow is drawn (backdrop plate and ground), and the sound engine owns the only AudioListener.</summary>
    public class MeadowScenePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float SettleSec = 1f;
        const int CaptureWidth = 1920;
        const int CaptureHeight = 1080;
        const float SkyBandBottom = 0.18f;
        const float MinSkyBrightness = 0.1f;
        const float MaxLayoutRowError = 0.02f; // fraction of the picture height
        const float MinGroundBrightness = 0.005f;
        const float MinWaveDifference = 0.004f; // mean absolute difference per channel (0..1) over the grass
        static readonly float[] SkyStops = { 0f, 0.5f, 1f };
        const float GroundBandTop = 0.6f;

        static IEnumerator LoadAndWaitUntilRunning()
        {
            yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
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
            // these tests put the sky and the fireflies where they want them: the energy of the nature layers must not move them back
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false; // these tests look at the meadow alone: the record strip over its lower part is tested on its own
        }

        static float MeanBrightness(Texture2D picture, float fromTop, float toTop)
        {
            var yStart = Mathf.RoundToInt((1f - toTop) * picture.height);
            var yEnd = Mathf.RoundToInt((1f - fromTop) * picture.height);
            var pixels = picture.GetPixels(0, yStart, picture.width, yEnd - yStart);
            var sum = 0f;
            foreach (var pixel in pixels) sum += (pixel.r + pixel.g + pixel.b) / 3f;
            return sum / pixels.Length;
        }

        static void Save(Texture2D picture, string name) => CaptureOutput.SavePicture(picture, name);

        [UnityTest]
        public IEnumerator MeadowScene_RendersBackdropAndTerrain()
        {
            yield return LoadAndWaitUntilRunning();
            yield return new WaitForSecondsRealtime(SettleSec);

            var stage = Object.FindFirstObjectByType<MeadowStage>();
            Assert.IsTrue(stage.IsConfigured);
            Assert.Greater(stage.Terrain.VertexCount, 1000, "the ground mesh exists");
            stage.Backdrop.SetSunset(SkyArc.MidT); // the playing dusk: the picture to compare with meadow_front_b.png (the measurements/visual/task5* pictures are the commit before the grass)
            var picture = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            Save(picture, "task8.png");

            var sky = MeanBrightness(picture, 0f, SkyBandBottom);
            var ground = MeanBrightness(picture, GroundBandTop, 1f);
            Object.Destroy(picture);
            Assert.Greater(sky, MinSkyBrightness, "the backdrop plate is drawn in the top of the picture");
            Assert.Greater(ground, MinGroundBrightness, "the ground is drawn in the lower part (not just the clear colour)");
            Assert.Less(ground, sky, "the meadow is darker than the sky of the playing dusk");
        }

        [UnityTest]
        public IEnumerator MeadowScene_SkyArc_GetsBrighterFromBlueHourToSunset()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var skyBrightness = new float[SkyStops.Length];

            for (var i = 0; i < SkyStops.Length; i++)
            {
                stage.Backdrop.SetSunset(SkyStops[i]);
                var picture = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
                Save(picture, $"task8-sky-{SkyStops[i]:0.0}.png");
                skyBrightness[i] = MeanBrightness(picture, 0f, SkyBandBottom);
                Object.Destroy(picture);
            }

            Assert.Less(skyBrightness[0], skyBrightness[1], "the playing dusk is brighter than the blue hour");
            Assert.Less(skyBrightness[1], skyBrightness[2], "the climax sunset is brighter than the playing dusk");
            Assert.Greater(skyBrightness[0], 0.01f, "the blue hour is dark but not black");
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

        [UnityTest]
        public IEnumerator Wind_RingWave_ChangesTheGrassAroundItsRing_AndLeavesTheFarSideAlone()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            const float RingAge = 0.4f;
            var before = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);

            // a ring that started 0.4 s ago in the middle of the field: it has grown to radius speed x 0.4 and bends the grass on that circle
            var radial = stage.Settings.wind.noteRadial;
            stage.Wind.Add(new WindWave(0f, 6f, MeadowStage.WindTime - RingAge, 0.6f, radial.speed, 10f, WindWaveKind.Radial, 0f));
            stage.UploadWind();
            var after = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            Save(after, "task8-ring.png");

            var middle = MeanDifference(before, after, 0.45f, 0.8f);
            var skyOnly = MeanDifference(before, after, 0f, 0.2f);
            Object.Destroy(before);
            Object.Destroy(after);
            Assert.Greater(middle, MinWaveDifference, "grass on the ring bends and brightens, so the picture changes");
            Assert.AreEqual(0f, skyOnly, 1e-4f, "the sky is not wind");
        }

        [UnityTest]
        public IEnumerator Wind_SweepWave_ChangesThePictureWhileItCrossesTheField()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var before = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);

            var sweep = stage.Settings.wind.beatSweep;
            // started 1 s ago, so that its front is in the middle of the field now (x = 0), whatever the speed of the sweep is
            stage.Wind.Add(new WindWave(-sweep.speed, 0f, MeadowStage.WindTime - 1.0, 0.6f, sweep.speed, 10f, WindWaveKind.Sweep, 0f));
            stage.UploadWind();
            var after = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            Save(after, "task8-sweep.png");

            var middle = MeanDifference(before, after, 0.45f, 0.9f);
            Object.Destroy(before);
            Object.Destroy(after);
            Assert.Greater(middle, MinWaveDifference);
        }

        sealed class OneHand : IHandMotionView
        {
            readonly HandMotion _hand;

            public OneHand(float x, float y) => _hand = new HandMotion(0, true, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, HandPose.Open);

            public long FrameSeq => 1;
            public int Count => 1;
            public HandMotion Hand(int index) => _hand;

            public bool TryGetById(int id, out HandMotion motion)
            {
                motion = _hand;
                return id == 0;
            }
        }

        [UnityTest]
        public IEnumerator Hands_PushTheGrassAwayAroundThem()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            stage.Backdrop.SetSunset(SkyArc.MidT);
            var before = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);

            stage.SetHands(new OneHand(0.5f, 0.6f)); // the middle of the picture, a little below the centre of the playing area
            stage.UploadWind();
            var after = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            Save(after, "task8-hand.png");

            var underHand = MeanDifferenceColumns(before, after, 0.3f, 0.7f, 0.6f, 0.95f);
            var farAway = MeanDifferenceColumns(before, after, 0f, 0.2f, 0.45f, 0.55f); // far field at the left: well outside the hand's radius
            Object.Destroy(before);
            Object.Destroy(after);
            Assert.Greater(underHand, MinWaveDifference, "a hand bends the grass around it");
            Assert.Less(farAway, underHand * 0.2f, "and only around it");
        }

        /// <summary>A hand whose place and capture time the test sets (a tracker frame at a time).</summary>
        sealed class MovingHand : IHandMotionView
        {
            HandMotion _hand;

            public MovingHand(float x, float y, double captureMs) => Move(x, y, captureMs);

            public void Move(float x, float y, double captureMs) => _hand = new HandMotion(0, true, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, captureMs, 0, HandPose.Open);

            public long FrameSeq => 1;
            public int Count => 1;
            public HandMotion Hand(int index) => _hand;

            public bool TryGetById(int id, out HandMotion motion)
            {
                motion = _hand;
                return id == 0;
            }
        }

        [UnityTest]
        public IEnumerator TheWindOfAHand_GlidesBetweenTrackerFrames_InsteadOfJumping()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            const float Frame = 1f / 60f;
            const float StepX01 = 0.006f;   // about 5 cm on the ground: a hand going 1 m/s seen by a tracker that delivers 20 pictures a second
            const float HandY = 0.7f;       // the bottom row: the hand is over the ground
            var hand = new MovingHand(0.4f, HandY, 1000.0);
            stage.SetHands(hand, Frame);   // all in one frame: the app's own frames are not mixed in
            var start = stage.WindHand(0);

            hand.Move(0.4f + StepX01, HandY, 1050.0);
            stage.SetHands(hand, Frame);
            var firstFrame = stage.WindHand(0);
            stage.SetHands(hand, Frame);
            stage.SetHands(hand, Frame);
            var later = stage.WindHand(0);

            Assert.IsTrue(stage.Placement.TryGroundOfHand(0.4f, HandY, out var before));
            Assert.IsTrue(stage.Placement.TryGroundOfHand(0.4f + StepX01, HandY, out var after));
            var jump = after.X - before.X;
            Assert.IsTrue(start.active && firstFrame.active, "the hand blows");
            Assert.AreEqual(before.X, start.x, 1e-3f, "the first place of a hand is the ground under where it is on the screen");
            Assert.AreEqual(before.Z, start.z, 1e-3f);
            Assert.Greater(jump, 0.03f);
            // 0.8, where it was 0.6 while the wind was read from the field plane: since the wind is read at the ground under the hand's screen point (and a little ahead along the motion, wind.handLookAheadSec)
            // the first drawn frame after a new picture carries it about 0.64 of the step (logged here). An unsmoothed wind carries it the whole step (1.0) in that frame, so 0.8 still tells the two apart.
            TestContext.Out.WriteLine($"the first drawn frame moves the wind {(firstFrame.x - start.x) / jump:0.000} of the step");
            Assert.Less(firstFrame.x - start.x, 0.8f * jump, "the wind of the hand does not jump to the new place in one drawn frame (an unsmoothed one would)");
            Assert.Greater(later.x, firstFrame.x, "it keeps gliding toward the hand while the tracker is silent");
            Assert.Greater(firstFrame.vx, 0f, "and it knows the hand is going to the right");
        }

        static float MeanDifferenceColumns(Texture2D a, Texture2D b, float fromLeft, float toLeft, float fromTop, float toTop)
        {
            var x = Mathf.RoundToInt(fromLeft * a.width);
            var w = Mathf.RoundToInt((toLeft - fromLeft) * a.width);
            var yStart = Mathf.RoundToInt((1f - toTop) * a.height);
            var h = Mathf.RoundToInt((toTop - fromTop) * a.height);
            var pa = a.GetPixels(x, yStart, w, h);
            var pb = b.GetPixels(x, yStart, w, h);
            var sum = 0f;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }

        [UnityTest]
        public IEnumerator Layout_PutsGroundAtTheScreenRowTheCameraDrawsItAt()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var groundLevel = stage.Terrain.HeightAt(0f, 0f); // the layout assumes flat ground at the viewer's feet

            for (var z = stage.Layout.NearZ + 0.1f; z <= stage.Layout.FarZ; z += 0.7f)
            {
                var row = 1f - stage.Camera.WorldToViewportPoint(new Vector3(0f, groundLevel, z)).y;

                Assert.AreEqual(stage.Layout.ScreenYForDepth(z), row, MaxLayoutRowError, $"ground at depth {z:0.0} m: a hand there must be where the flowers are drawn");
            }
        }

        [UnityTest]
        public IEnumerator ConfiguringTheMeadowAgain_LeaksNothing_AndSwitchingItOffAndOnBuildsItAgain()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var flowerCount = stage.Flowers.Count;
            Assert.AreEqual(1, WindFieldGpu.LiveInstances, "one wind buffer in a running meadow");

            stage.Configure(stage.Bundle, stage.QualityName);
            stage.Configure(stage.Bundle, stage.QualityName);

            Assert.AreEqual(1, WindFieldGpu.LiveInstances, "built three times, still one wind buffer");
            stage.OnNote(new ScheduledNote(1, AudioSettings.dspTime + 0.1, AudioSettings.dspTime, Part.Marimba, 3, 1f, false, 0.5f), AudioSettings.dspTime);
            Assert.AreEqual(1, stage.Particles.PendingCount, "and one burst per note: the router is not subscribed three times");

            stage.gameObject.SetActive(false);
            Assert.AreEqual(0, WindFieldGpu.LiveInstances, "switched off: the buffer is given back");
            stage.gameObject.SetActive(true);
            yield return null;

            Assert.AreEqual(1, WindFieldGpu.LiveInstances, "switched on: built again");
            Assert.AreEqual(flowerCount, stage.Flowers.Count);
            var picture = MeadowCapture.Render(stage.Camera, CaptureWidth, CaptureHeight);
            Assert.Greater(MeanBrightness(picture, GroundBandTop, 1f), MinGroundBrightness, "and it draws");
            Object.Destroy(picture);
        }

        [UnityTest]
        public IEnumerator TheOverlayLayer_IsAChildOfTheMeadowCamera_AndPutsAPointOfThePictureOnThePlaneInFrontOfIt()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var overlay = stage.Overlay;

            Assert.IsNotNull(overlay, "the stage makes the layer when it is built");
            Assert.AreSame(stage.Camera.transform, overlay.Root.parent, "a child of the meadow camera");
            Assert.AreEqual(100, overlay.SortingOrderBase);
            var centre = overlay.LocalPoint(0.5f, 0.5f);
            Assert.AreEqual(0f, centre.x, 1e-4f);
            Assert.AreEqual(0f, centre.y, 1e-4f);
            Assert.AreEqual(1f, centre.z, 1e-4f, "one unit in front of the camera");
            var halfHeight = Mathf.Tan(stage.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Assert.AreEqual(halfHeight, overlay.LocalPoint(0.5f, 1f).y, 1e-4f, "the top of the picture");
            Assert.AreEqual(-halfHeight, overlay.LocalPoint(0.5f, 0f).y, 1e-4f, "the bottom");
            Assert.Less(overlay.LocalPoint(0f, 0.5f).x, 0f);
            Assert.Greater(overlay.LocalPoint(1f, 0.5f).x, 0f);
            Assert.AreEqual(-overlay.LocalPoint(0f, 0.5f).x, overlay.LocalPoint(1f, 0.5f).x, 1e-4f, "left and right are mirror images");
            var world = stage.Camera.WorldToViewportPoint(overlay.Root.TransformPoint(overlay.LocalPoint(0.25f, 0.75f)));
            Assert.AreEqual(0.25f, world.x, 1e-3f, "the point is where it says on the camera's own viewport");
            Assert.AreEqual(0.75f, world.y, 1e-3f);
        }

        [UnityTest]
        public IEnumerator TheOverlayLayer_KeepsClearOfTheRecordStrip_WhenThereIsOne_AndSurvivesTheMeadowBeingBuiltAgain()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var overlay = stage.Overlay;
            overlay.StripFraction = 0.28f;

            var bottomOfTheHand = overlay.LocalPointOfHand(0.5f, 1f);
            var viewport = stage.Camera.WorldToViewportPoint(overlay.Root.TransformPoint(bottomOfTheHand));
            stage.Configure(stage.Bundle, stage.QualityName);

            Assert.AreEqual(0.28f, viewport.y, 1e-3f, "a hand at the bottom of the tracker's picture is drawn just over the strip");
            Assert.AreSame(overlay, stage.Overlay, "one layer, not one more every time the meadow is built");
            Assert.AreEqual(1, stage.Camera.GetComponentsInChildren<MeadowOverlayLayer>().Length);
        }

        [UnityTest]
        public IEnumerator MeadowScene_HasExactlyOneAudioListener_OwnedByTheSoundEngine()
        {
            yield return LoadAndWaitUntilRunning();

            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.AreEqual(1, listeners.Length, "one pair of ears for the whole app");
            Assert.AreEqual("NativeAudioEngine", listeners[0].gameObject.name, "the sound engine owns it, no scene camera does");
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Assert.IsNull(camera.GetComponent<AudioListener>(), $"camera {camera.name} must not carry a listener");
            }
        }
    }
}
