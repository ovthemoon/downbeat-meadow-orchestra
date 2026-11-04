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
    /// The effects of the six cells in the real meadow, from a played note to the picture: a note of the sky row (harp, recorder, bell) makes its effect in the air where the hand is on the screen, in the colour
    /// of its cell, when it is heard, and opens no flower; the low string lays the grass in a long wave, the hand drum thumps a wide ring; the Low tier makes half the particles. Three stills of it (<c>six-cells-effects-*</c>).
    /// </summary>
    public class SixCellsEffectsPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const double FakeHeardNow = 3000.0;
        const double Lead = 0.1;
        const int Width = 1280;
        const int Height = 720;
        const float ScreenTolerance = 0.03f;
        const int PinnedFps = 60;                // the game clock steps 1 / 60 s per frame in the test that pins it, whatever the machine is doing
        const int MaxFramesToWait = 240;         // 4 s of that clock: the effect is due after Lead (0.1 s, 6 frames); a bound that only a broken emitter reaches
        const double MaxSecondsToWait = 120.0;   // the same bound in real time, for a machine that makes a frame last seconds

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

        static ScheduledNote Note(Part part, float velocity, float x01, float y01) =>
            new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, part, 4, velocity, false, x01, y01);

        sealed class Hands : IHandMotionView
        {
            readonly HandMotion[] _hands;
            public Hands(params (float x, float y)[] positions)
            {
                _hands = new HandMotion[positions.Length];
                for (var i = 0; i < positions.Length; i++) _hands[i] = new HandMotion(i, true, 1f, positions[i].x, positions[i].y, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, HandPose.Open);
            }
            public long FrameSeq => 1;
            public int Count => _hands.Length;
            public HandMotion Hand(int index) => _hands[index];
            public bool TryGetById(int id, out HandMotion motion) { motion = id < _hands.Length ? _hands[id] : default; return id < _hands.Length; }
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

        static Vector3 Centre(List<Vector3> points)
        {
            var sum = Vector3.zero;
            foreach (var p in points) sum += p;
            return sum / Mathf.Max(1, points.Count);
        }

        static WindWaveGpu? LiveWave(MeadowStage stage, System.Func<WindWaveGpu, bool> match)
        {
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            var count = stage.Wind.CopyActive(MeadowStage.WindTime, scratch);
            for (var i = 0; i < count; i++)
            {
                if (match(scratch[i])) return scratch[i];
            }
            return null;
        }

        // ---- the sky row -----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SkyNote_SpawnsItsEffect_AtTheHandsScreenPoint()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            // The effect is due 0.1 s of the game clock after the note. On a machine under load the frames last as long as they like, so this test sets the clock to a fixed step per frame and
            // counts frames: the frame in which the effect is made (and where the hand's point is read) is then the same on every run. The clock is let go at the end, however the test ends.
            Time.captureFramerate = PinnedFps;
            try
            {
                yield return CheckSkyEffectsAtTheHandsPoint(stage);
            }
            finally
            {
                Time.captureFramerate = 0;
            }
        }

        static IEnumerator CheckSkyEffectsAtTheHandsPoint(MeadowStage stage)
        {
            var cases = new[]
            {
                (part: Part.Harp, kind: SkyEffectKind.LightRing, x: 0.17f, y: 0.2f),
                (part: Part.Recorder, kind: SkyEffectKind.Seeds, x: 0.55f, y: 0.35f),
                (part: Part.Bell, kind: SkyEffectKind.StarDust, x: 0.8f, y: 0.1f),
            };

            foreach (var strip in new[] { 0.28f, 0f })   // one screen with the record strip over the lower part, or two screens
            {
                stage.Overlay.StripFraction = strip;
                foreach (var c in cases)
                {
                    var emittedBefore = stage.Particles.EmittedSky(c.kind);
                    stage.OnNote(Note(c.part, 1f, c.x, c.y), FakeHeardNow);
                    Assert.AreEqual(emittedBefore, stage.Particles.EmittedSky(c.kind), $"{c.part}: nothing is made before the note is heard");
                    Assert.AreEqual(1, stage.Particles.PendingSkyCount, $"{c.part}: it waits for its time");

                    // Frame by frame, up to a generous bound, until the frame in which it is made (the seeds have not climbed away from the hand yet): the wait counts frames of the pinned clock,
                    // never real seconds, so a slow machine only takes longer
                    var framesWaited = 0;
                    var startedAt = Time.realtimeSinceStartupAsDouble;
                    while (stage.Particles.EmittedSky(c.kind) == emittedBefore && framesWaited < MaxFramesToWait && Time.realtimeSinceStartupAsDouble - startedAt < MaxSecondsToWait)
                    {
                        framesWaited++;
                        yield return null;
                    }

                    Assert.Greater(stage.Particles.EmittedSky(c.kind), emittedBefore, $"{c.part}: made when the note is heard (waited {framesWaited} frames)");
                    Assert.LessOrEqual(framesWaited, Mathf.CeilToInt((float)Lead * PinnedFps) + 2, $"{c.part}: made in the frame in which the pinned clock reaches its time, not before and not much later");
                    Assert.AreEqual(0, stage.Particles.PendingSkyCount);
                    var positions = new List<Vector3>();
                    stage.Particles.GetSkyPositions(c.kind, positions);
                    Assert.Greater(positions.Count, 0, c.part.ToString());
                    var (vx, vy) = stage.Overlay.ViewportOfHand(c.x, c.y);
                    var onScreen = stage.Camera.WorldToViewportPoint(Centre(positions));
                    Assert.AreEqual(vx, onScreen.x, ScreenTolerance, $"{c.part} at strip {strip}: across the screen where the hand is");
                    Assert.AreEqual(vy, onScreen.y, ScreenTolerance, $"{c.part} at strip {strip}: up the screen where the hand is");
                }
                stage.ApplyQuality(stage.QualityName);   // a rebuild clears the particles of this round: the next round sees only its own
            }
        }

        [UnityTest]
        public IEnumerator SkyNote_IsInTheColourOfItsCell()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var colors = stage.Bundle.Dto.cells.colors;

            stage.OnNote(Note(Part.Harp, 1f, 0.17f, 0.25f), FakeHeardNow);
            stage.OnNote(Note(Part.Bell, 1f, 0.83f, 0.25f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.3f);

            foreach (var (part, system) in new[] { (Part.Harp, "SkyRings"), (Part.Bell, "SkyStars") })
            {
                Assert.IsTrue(ColorHex.TryParse(colors[(int)part], out var rgb), colors[(int)part]);
                var live = new ParticleSystem.Particle[64];
                var n = stage.Particles.transform.Find(system).GetComponent<ParticleSystem>().GetParticles(live);
                Assert.Greater(n, 0, system);
                for (var i = 0; i < n; i++)
                {
                    Assert.AreEqual(rgb.R * 255f, live[i].startColor.r, 2f, $"{part}: red of the cell colour {colors[(int)part]}");
                    Assert.AreEqual(rgb.G * 255f, live[i].startColor.g, 2f, $"{part}: green");
                    Assert.AreEqual(rgb.B * 255f, live[i].startColor.b, 2f, $"{part}: blue");
                }
            }
        }

        [UnityTest]
        public IEnumerator SkyNote_NeverBlooms_AndPushesNoGrass_ButAGroundNoteDoes()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var wavesBefore = stage.Wind.Count;
            var bloomsBefore = stage.Flowers.BloomsNotYetHeard(0.0).Count;

            stage.OnNote(Note(Part.Harp, 1f, 0.17f, 0.2f), FakeHeardNow);
            stage.OnNote(Note(Part.Recorder, 1f, 0.5f, 0.2f), FakeHeardNow);
            stage.OnNote(Note(Part.Bell, 1f, 0.83f, 0.2f), FakeHeardNow);

            Assert.AreEqual(bloomsBefore, stage.Flowers.BloomsNotYetHeard(0.0).Count, "no flower is asked to open");
            Assert.AreEqual(wavesBefore, stage.Wind.Count, "and no wave is added to the grass");

            stage.OnNote(Note(Part.Marimba, 1f, 0.5f, 0.75f), FakeHeardNow);

            Assert.Greater(stage.Flowers.BloomsNotYetHeard(0.0).Count, bloomsBefore, "a note of the ground does open flowers (the probe works)");
            Assert.AreEqual(wavesBefore + 1, stage.Wind.Count);
        }

        // ---- the ground row -----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator LowString_LaysTheGrassInALongWave()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var settings = stage.Settings.wind.longWave;

            stage.OnNote(Note(Part.LowString, 1f, 0.17f, 0.75f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.2f);

            var wave = LiveWave(stage, w => w.Kind == (float)WindWaveKind.Sweep && w.WidthScale > 1f);
            Assert.IsTrue(wave.HasValue, "a wave that is a sweep and wider than the others is alive");
            Assert.Less(wave.Value.Speed, 0f, "from the left cell towards the left");
            Assert.AreEqual(settings.widthScale, wave.Value.WidthScale, 1e-4f);
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), wave.Value.Amplitude, 1e-4f);
        }

        [UnityTest]
        public IEnumerator HandDrum_PushesAWideRing()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var settings = stage.Settings.wind.drumRing;

            stage.OnNote(Note(Part.HandDrum, 1f, 0.83f, 0.75f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.2f);

            var wave = LiveWave(stage, w => w.Kind == (float)WindWaveKind.Radial && w.WidthScale > 1f);
            Assert.IsTrue(wave.HasValue, "a ring that is wider than the others is alive");
            Assert.AreEqual(settings.widthScale, wave.Value.WidthScale, 1e-4f);
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), wave.Value.Amplitude, 1e-4f);
            Assert.AreEqual(settings.speed, wave.Value.Speed, 1e-4f);
        }

        // ---- the Low tier -------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator LowQuality_HalvesTheSkyParticles()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var kinds = new[] { (part: Part.Harp, kind: SkyEffectKind.LightRing), (part: Part.Recorder, kind: SkyEffectKind.Seeds), (part: Part.Bell, kind: SkyEffectKind.StarDust) };
            var high = new Dictionary<SkyEffectKind, int>();

            stage.ApplyQuality("High");
            foreach (var c in kinds) stage.OnNote(Note(c.part, 1f, 0.5f, 0.25f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(stage.Particles.SkyIsLowTier);
            foreach (var c in kinds) high[c.kind] = stage.Particles.EmittedSky(c.kind);

            stage.ApplyQuality("Low");
            foreach (var c in kinds) stage.OnNote(Note(c.part, 1f, 0.5f, 0.25f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(stage.Particles.SkyIsLowTier);

            foreach (var c in kinds)
            {
                var low = stage.Particles.EmittedSky(c.kind);
                Assert.Greater(high[c.kind], 0, c.kind.ToString());
                Assert.AreEqual(Mathf.Round(high[c.kind] * stage.Settings.skyEffects.lowCountScale), low, $"{c.kind}: {high[c.kind]} on High, {low} on Low");
            }
        }

        // ---- stills -------------------------------------------------------------------------------------------------------

        static IEnumerator Still(MeadowStage stage, string name, Hands hands, System.Action play, float seconds, float fromTop, float toTop, System.Action<float> check)
        {
            stage.Backdrop.SetSunset(0.5f);
            stage.HandsOverride = new Hands();
            yield return new WaitForSecondsRealtime(0.6f);
            var before = MeadowCapture.Render(stage.Camera, Width, Height);

            stage.HandsOverride = hands;
            play();
            yield return new WaitForSecondsRealtime(seconds);
            var during = MeadowCapture.Render(stage.Camera, Width, Height);
            CaptureOutput.SavePicture(during, name);

            var difference = MeanDifference(before, during, fromTop, toTop);
            Object.Destroy(before);
            Object.Destroy(during);
            stage.HandsOverride = new Hands();
            TestContext.Out.WriteLine($"{name}: the picture changed by {difference:0.00000} on average in rows {fromTop}..{toTop} from the top");
            check(difference);
        }

        [UnityTest]
        public IEnumerator Still_TheSkyRow_RingSeedsAndStarDustWhereTheHandsAre()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            yield return Still(stage, "six-cells-effects-sky.png", new Hands((0.17f, 0.25f), (0.83f, 0.2f)), () =>
            {
                stage.OnNote(Note(Part.Harp, 1f, 0.17f, 0.25f), FakeHeardNow);
                stage.OnNote(Note(Part.Recorder, 1f, 0.5f, 0.3f), FakeHeardNow);
                stage.OnNote(Note(Part.Bell, 1f, 0.83f, 0.2f), FakeHeardNow);
            }, 0.55f, 0f, 0.4f, difference => Assert.Greater(difference, 0.0003f, "the three effects are drawn in the sky of the picture"));
        }

        [UnityTest]
        public IEnumerator Still_TheLowString_LaysTheGrassInALongWave()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            yield return Still(stage, "six-cells-effects-lowstring.png", new Hands((0.17f, 0.75f)), () => stage.OnNote(Note(Part.LowString, 1f, 0.17f, 0.75f), FakeHeardNow),
                0.3f, 0.5f, 1f, difference => Assert.Greater(difference, 0.002f, "the grass lies in the wave"));
        }

        [UnityTest]
        public IEnumerator Still_TheHandDrum_ThumpsAWideRing()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            yield return Still(stage, "six-cells-effects-handdrum.png", new Hands((0.83f, 0.75f)), () => stage.OnNote(Note(Part.HandDrum, 1f, 0.83f, 0.75f), FakeHeardNow),
                0.3f, 0.5f, 1f, difference => Assert.Greater(difference, 0.002f, "the grass is thrown out in a ring"));
        }
    }
}
