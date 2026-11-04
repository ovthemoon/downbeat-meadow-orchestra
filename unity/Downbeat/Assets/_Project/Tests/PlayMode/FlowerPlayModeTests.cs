using System.Collections;
using System.Collections.Generic;
using System.IO;
using Downbeat.App;
using Downbeat.Controls;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>Flowers in the real scene: buds in the quiet state, the right species open where a note was played, and a note played through the app's own path opens flowers.</summary>
    public class FlowerPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 1920;
        const int Height = 1080;
        const double FakeHeardNow = 1000.0;
        const double Lead = 0.1;
        const float PlayWaitSec = 2.0f;

        static IEnumerator LoadAndWaitUntilRunning(System.Action<DownbeatLifetimeScope, MeadowStage> done)
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
            // these tests put the sky and the fireflies where they want them: the energy of the nature layers must not move them back
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false; // these tests look at the meadow alone: the record strip over its lower part is tested on its own
            done(scope, Object.FindFirstObjectByType<MeadowStage>());
        }

        static void Save(Texture2D picture, string name) => CaptureOutput.SavePicture(picture, name);

        static ScheduledNote Note(Part part, int step, float velocity, float x01) =>
            new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, part, step, velocity, false, x01);

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
        public IEnumerator TheFlowersExist_AreOfTheThreeSpecies_AndStartAsBuds()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);

            var flowers = stage.Flowers;
            var counts = new int[3];
            foreach (var flower in flowers.Instances) counts[(int)flower.Species]++;

            Assert.AreEqual(stage.Settings.quality[0].flowerCount, flowers.Count);
            Assert.Greater(counts[0], 40);
            Assert.Greater(counts[1], 40);
            Assert.Greater(counts[2], 40);
            for (var i = 0; i < flowers.Count; i++) Assert.AreEqual(0f, flowers.LevelOf(i), "a quiet meadow: every flower is a closed bud");
        }

        [UnityTest]
        public IEnumerator IdleMeadow_ShowsBuds_InTheField_AndNoneInTheSky_AtTheQuietAndThePlayingSky()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);

            foreach (var t in new[] { 0f, 0.5f })
            {
                stage.Backdrop.SetSunset(t);
                var withFlowers = MeadowCapture.Render(stage.Camera, Width, Height);
                stage.Flowers.Visible = false; // the flower field stops drawing: the same picture without the flowers
                var without = MeadowCapture.Render(stage.Camera, Width, Height);
                stage.Flowers.Visible = true;

                var inField = MeanDifference(withFlowers, without, 0.55f, 1f);
                var inSky = MeanDifference(withFlowers, without, 0f, 0.4f);
                var budPixels = CountDifferingPixels(withFlowers, without, 0.55f, 1f);
                Save(withFlowers, t < 0.1f ? "task7b-idle-bluehour.png" : "task7b-idle.png");
                Object.Destroy(withFlowers);
                Object.Destroy(without);

                Assert.Greater(budPixels, 150, $"sky t {t}: buds are drawn in the field (pixels that change when the flowers are switched off): {budPixels}, mean {inField}");
                Assert.Greater(inField, 0.0003f, $"sky t {t}: the buds show in the field");
                Assert.AreEqual(0f, inSky, 1e-6f, $"sky t {t}: and there is no flower in the sky above the horizon");
            }
        }

        static int CountDifferingPixels(Texture2D a, Texture2D b, float fromTop, float toTop)
        {
            var yStart = Mathf.RoundToInt((1f - toTop) * a.height);
            var yEnd = Mathf.RoundToInt((1f - fromTop) * a.height);
            var pa = a.GetPixels(0, yStart, a.width, yEnd - yStart);
            var pb = b.GetPixels(0, yStart, b.width, yEnd - yStart);
            var count = 0;
            for (var i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 0.012f) count++;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator NearerFlowersAreLargerOnTheScreen_ThanFartherOnesOfTheSameKind()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            var look = stage.Settings.flowers.dandelion;
            FlowerInstance near = default, far = default;
            var haveNear = false;
            var haveFar = false;
            foreach (var flower in stage.Flowers.Instances)
            {
                if (flower.Species != FlowerSpecies.Dandelion) continue;
                if (!haveNear || flower.Z < near.Z) { near = flower; haveNear = true; }
                if (!haveFar || flower.Z > far.Z) { far = flower; haveFar = true; }
            }
            Assert.IsTrue(haveNear && haveFar);
            Assert.Greater(far.Z, near.Z + 2f, "the garden has flowers near and far");

            float ScreenHeight(FlowerInstance f)
            {
                var foot = new Vector3(f.X, stage.Terrain.HeightAt(f.X, f.Z), f.Z);
                var head = foot + Vector3.up * (look.stemHeight * f.Height * f.Scale);
                return (stage.Camera.WorldToViewportPoint(head).y - stage.Camera.WorldToViewportPoint(foot).y) * Height;
            }

            var nearPixels = Mathf.Abs(ScreenHeight(near));
            var farPixels = Mathf.Abs(ScreenHeight(far));

            Assert.Greater(nearPixels, farPixels * 1.8f, $"a flower at {near.Z:0.0} m is {nearPixels:0} px tall, one at {far.Z:0.0} m is {farPixels:0} px: perspective makes the near one bigger");
        }

        [UnityTest]
        public IEnumerator AFlowerStruckAgainWhileClosing_KeepsMovingBeforeTheNewBloomStarts()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            var settings = stage.Settings.flowers.bloom;
            var flowers = new List<int> { 0 };
            var first = FakeHeardNow + 1.0;
            stage.Flowers.Bloom(flowers, first);
            var closing = first - settings.leadSec + settings.openSec + settings.holdSec + 0.5 * settings.closeSec;
            stage.Flowers.Tick(closing);
            var level = stage.Flowers.LevelOf(0);
            Assert.Less(level, 1f);

            var newAudible = closing + 0.1;
            Assert.Greater(newAudible - settings.leadSec, closing + 0.017, "the test reads the flower before the new bloom starts");
            stage.Flowers.Bloom(flowers, newAudible);
            stage.Flowers.Tick(closing + 0.008); // the new bloom starts at newAudible - leadSec = closing + 0.025 with the shipped lead
            var a = stage.Flowers.LevelOf(0);
            stage.Flowers.Tick(closing + 0.016);
            var b = stage.Flowers.LevelOf(0);

            Assert.Less(a, level, "the old close goes on while the new bloom has not started");
            Assert.Less(b, a, "and on again: the flower does not stand still for up to 80 ms");
        }

        [UnityTest]
        public IEnumerator Notes_OpenTheFlowersOfTheirOwnSpecies_WhereTheyWerePlayed()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            stage.Backdrop.SetSunset(0.5f);
            var before = MeadowCapture.Render(stage.Camera, Width, Height);

            var played = new[]
            {
                Note(Part.LowString, 2, 0.8f, 0.12f), Note(Part.LowString, 5, 0.9f, 0.2f), Note(Part.LowString, 7, 0.7f, 0.28f),
                Note(Part.Marimba, 3, 0.8f, 0.42f), Note(Part.Marimba, 6, 0.9f, 0.5f), Note(Part.Marimba, 4, 0.7f, 0.58f),
                Note(Part.HandDrum, 2, 0.8f, 0.72f), Note(Part.HandDrum, 5, 0.9f, 0.82f), Note(Part.HandDrum, 8, 0.7f, 0.9f),
            };
            foreach (var note in played) stage.OnNote(note, FakeHeardNow);
            stage.Tick(FakeHeardNow + Lead - stage.Settings.flowers.bloom.leadSec - 0.01); // before the bloom starts: nothing has opened yet
            var closedLevels = 0f;
            for (var i = 0; i < stage.Flowers.Count; i++) closedLevels += stage.Flowers.LevelOf(i);
            stage.Tick(FakeHeardNow + Lead + 0.6);  // 0.6 s after they are heard: open

            var open = new int[3];
            for (var i = 0; i < stage.Flowers.Count; i++)
            {
                if (stage.Flowers.LevelOf(i) > 0.99f) open[(int)stage.Flowers.Instances[i].Species]++;
            }
            var after = MeadowCapture.Render(stage.Camera, Width, Height);
            Save(after, "task7b-bloom.png");
            Save(MeadowCapture.Render(stage.Camera, Width * 2, Height * 2), "task7b-bloom-4k.png");

            Assert.Less(closedLevels, 0.01f, "the flowers do not open before their bloom starts, a few tens of ms before their sound is heard");
            Assert.Greater(open[0], 2, "the low string opened bellflowers");
            Assert.Greater(open[1], 2, "the marimba opened dandelions");
            Assert.Greater(open[2], 2, "the hand drum opened poppies");
            Assert.Greater(MeanDifference(before, after, 0.45f, 1f), 0.002f, "the picture changes");
            Object.Destroy(before);
            Object.Destroy(after);
        }

        [UnityTest]
        public IEnumerator ABloomClosesAgain_AndSettlesAsABudAfterAboutEightSeconds()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            stage.OnNote(Note(Part.HandDrum, 4, 1f, 0.8f), FakeHeardNow);
            var opened = new List<int>();
            stage.Tick(FakeHeardNow + Lead + 1.0);
            for (var i = 0; i < stage.Flowers.Count; i++) { if (stage.Flowers.LevelOf(i) > 0.99f) opened.Add(i); }
            Assert.IsNotEmpty(opened);

            stage.Tick(FakeHeardNow + Lead + 12.0);

            var rest = stage.Settings.flowers.bloom.restLevel;
            foreach (var i in opened) Assert.AreEqual(rest, stage.Flowers.LevelOf(i), 1e-5f, "a half-closed bud again");
            Assert.AreEqual(0, stage.Flowers.ActiveBlooms, "nothing keeps running once the blooms are over");
        }

        [UnityTest]
        public IEnumerator ANotePlayedThroughTheApp_OpensFlowersOfItsSpecies()
        {
            DownbeatLifetimeScope scope = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => { scope = s; stage = m; });
            var input = scope.Container.Resolve<DebugPointerInput>();
            stage.Settings.wake.maxFlowers = 0;   // the first note of a visit also wakes the meadow (its own tests are below); this test is about the note alone

            input.Inject(new NoteIntent(Part.LowString, 4, 0.9f, x01: 0.15f, y01: 0.8f));
            var until = Time.realtimeSinceStartup + PlayWaitSec;
            var sawActive = false;
            while (Time.realtimeSinceStartup < until)
            {
                sawActive |= stage.Flowers.ActiveBlooms > 0;
                yield return null;
            }

            Assert.IsTrue(sawActive, "the scheduled note reached the flower field");
            var bellflowersOpened = 0;
            var othersOpened = 0;
            for (var i = 0; i < stage.Flowers.Count; i++)
            {
                if (stage.Flowers.LevelOf(i) < 0.5f) continue;
                if (stage.Flowers.Instances[i].Species == FlowerSpecies.Bellflower) bellflowersOpened++; else othersOpened++;
            }
            Assert.Greater(bellflowersOpened, 0);
            Assert.AreEqual(0, othersOpened, "a low string note opens no dandelion and no poppy");
        }

        [UnityTest]
        public IEnumerator ASkyNotePlayedThroughTheApp_OpensNoFlower()
        {
            DownbeatLifetimeScope scope = null;
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => { scope = s; stage = m; });
            var input = scope.Container.Resolve<DebugPointerInput>();
            stage.Settings.wake.maxFlowers = 0;   // the first note of a visit also wakes the meadow; this test is about the note alone

            input.Inject(new NoteIntent(Part.Bell, 4, 0.9f, x01: 0.85f, y01: 0.2f));
            var until = Time.realtimeSinceStartup + PlayWaitSec;
            var sawActive = false;
            while (Time.realtimeSinceStartup < until)
            {
                sawActive |= stage.Flowers.ActiveBlooms > 0;
                yield return null;
            }
            input.Inject(new NoteIntent(Part.Harp, 4, 0.9f, x01: 0.15f, y01: 0.2f));
            until = Time.realtimeSinceStartup + PlayWaitSec;
            while (Time.realtimeSinceStartup < until)
            {
                sawActive |= stage.Flowers.ActiveBlooms > 0;
                yield return null;
            }

            Assert.IsFalse(sawActive, "a note of the sky row (the bell, the harp) is an effect in the air: it opens no flower (that it sends no ring through the grass is tested on the router)");
        }

        [UnityTest]
        public IEnumerator Wake_GlintsTheBudsAroundThePlace_InAWaveThatSpreads_AndStartsASoftGust()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            var origin = WakeOrigin.Of(null, stage.Layout);
            var wake = stage.Settings.wake;
            var wavesBefore = stage.Wind.Count;
            var blooming = stage.Flowers.ActiveBlooms;

            var count = stage.Wake(origin, FakeHeardNow);

            Assert.Greater(count, 0, "there are buds near the middle of the field");
            Assert.LessOrEqual(count, wake.maxFlowers);
            Assert.AreEqual(blooming + count, stage.Flowers.ActiveBlooms, "one bloom per planned flower");
            Assert.AreEqual(wavesBefore + 1, stage.Wind.Count, "and one ring of wind");

            // the front: the flowers nearest the place open first, the ones at the rim only at the end
            var plan = new List<WakeBloom>();
            WakePlanner.Plan(stage.Flowers.Instances, origin, WakeSettings.From(wake), plan);
            var nearest = plan[0];
            var rim = plan[plan.Count - 1];
            stage.Tick(FakeHeardNow + 0.12);
            Assert.Greater(stage.Flowers.LevelOf(nearest.Flower), 0.3f, "the nearest bud has started to open");
            Assert.AreEqual(0f, stage.Flowers.LevelOf(rim.Flower), 1e-4f, "the wave has not reached the rim yet");
            stage.Tick(FakeHeardNow + wake.radius / wake.speed + wake.jitterSec + stage.Settings.flowers.bloom.openSec + 0.1);
            foreach (var bloom in plan) Assert.Greater(stage.Flowers.LevelOf(bloom.Flower), 0.99f, "every planned bud is open once the wave has passed");
        }

        [UnityTest]
        public IEnumerator Wake_IsCalm_TheFlowersItOpensAreTheSameOnAnotherWake()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((s, m) => stage = m);
            var origin = WakeOrigin.Of(null, stage.Layout);

            var first = stage.Wake(origin, FakeHeardNow);
            stage.Tick(FakeHeardNow + 30.0);   // all of it has run its course and is back to a half-closed bud
            var second = stage.Wake(origin, FakeHeardNow + 30.0);

            Assert.AreEqual(first, second, "the same place wakes the same flowers: nothing random between visits");
            Assert.AreEqual(second, stage.Flowers.ActiveBlooms);
        }
    }
}
