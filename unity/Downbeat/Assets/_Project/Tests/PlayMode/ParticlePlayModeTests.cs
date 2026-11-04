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
    /// <summary>Particles and hand ribbons in the real scene: notes throw seeds and petals when they are heard, fireflies follow their level, pollen is always there, a hand draws a ribbon that fades.</summary>
    public class ParticlePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 1920;
        const int Height = 1080;
        const double FakeHeardNow = 2000.0;
        const double Lead = 0.1;

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
            // these tests put the sky and the fireflies where they want them: the energy of the nature layers must not move them back
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false; // these tests look at the meadow alone: the record strip over its lower part is tested on its own
            done(Object.FindFirstObjectByType<MeadowStage>());
        }

        static void Save(Texture2D picture, string name) => CaptureOutput.SavePicture(picture, name);

        static ScheduledNote Note(Part part, int step, float velocity, float x01) =>
            new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, part, step, velocity, false, x01);

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

        [UnityTest]
        public IEnumerator AMarimbaNote_ThrowsPetals_WhenItIsHeard_NotBefore()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var particles = stage.Particles;
            var emittedBefore = particles.EmittedPetals;

            stage.OnNote(Note(Part.Marimba, 4, 1f, 0.5f), FakeHeardNow); // heard 0.1 s from now
            yield return null;
            Assert.AreEqual(emittedBefore, particles.EmittedPetals, "nothing is thrown before the note is heard");
            Assert.AreEqual(1, particles.PendingCount);

            yield return new WaitForSecondsRealtime(0.25f);

            Assert.AreEqual(emittedBefore + 9, particles.EmittedPetals, "3 + round(6 x 1.0) petals");
            Assert.AreEqual(0, particles.PendingCount);
            Assert.GreaterOrEqual(particles.AlivePetals, 5);
        }

        [UnityTest]
        public IEnumerator TheSkyRow_ThrowsNothingOnTheGround_ItMakesEffectsInTheAir()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var particles = stage.Particles;

            stage.OnNote(Note(Part.Recorder, 4, 0.5f, 0.5f), FakeHeardNow);
            stage.OnNote(Note(Part.Harp, 4, 1f, 0.15f), FakeHeardNow);
            stage.OnNote(Note(Part.Bell, 4, 1f, 0.85f), FakeHeardNow);
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.AreEqual(0, particles.EmittedPetals, "nothing is thrown on the ground by the harp, the recorder or the bell");
            Assert.Greater(particles.EmittedSky(SkyEffectKind.Seeds), 0, "the recorder lifts its seeds in the air");
            Assert.Greater(particles.EmittedSky(SkyEffectKind.LightRing), 0, "the harp makes its ring of light");
            Assert.Greater(particles.EmittedSky(SkyEffectKind.StarDust), 0, "and the bell its star dust");
        }

        [UnityTest]
        public IEnumerator Petals_DriftWithTheWindRing_OfTheirNote()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var particles = stage.Particles;
            var at = new FieldPoint(0f, 5f);
            stage.AddNoteRadial(at, 1f);   // a burst of petals is put in by hand, with the ring of a note
            particles.Enqueue(new ParticleCue(ParticleKind.Petals, 12, at, FakeHeardNow), MeadowStage.WindTime);
            yield return new WaitForSecondsRealtime(0.3f);
            var system = particles.transform.Find("Petals").GetComponent<ParticleSystem>();
            var live = new ParticleSystem.Particle[64];
            var n = system.GetParticles(live);
            Assert.Greater(n, 5);
            var start = new Vector3[n];
            for (var i = 0; i < n; i++) start[i] = live[i].position;

            yield return new WaitForSecondsRealtime(1.2f);
            n = system.GetParticles(live);

            var moved = 0f;
            for (var i = 0; i < Mathf.Min(n, start.Length); i++) moved += new Vector2(live[i].position.x - start[i].x, live[i].position.z - start[i].z).magnitude;
            Assert.Greater(moved / Mathf.Max(1, n), 0.05f, "the petals travelled sideways: the wind of the note's ring carries them");
        }

        [UnityTest]
        public IEnumerator PetalsAreBlended_TheEffectsInTheAirFirefliesAndPollenAreAdded()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            float Blend(string system) => stage.Particles.transform.Find(system).GetComponent<ParticleSystemRenderer>().sharedMaterial.GetFloat("_DstBlend");

            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, Blend("Petals"), "petals are things: alpha blended");
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, Blend("SkySeeds"), "seeds in the air are light: added");
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, Blend("Fireflies"));
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, Blend("Pollen"));
        }

        [UnityTest]
        public IEnumerator Fireflies_FollowTheLevel_AndPollenIsAlwaysThere()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var particles = stage.Particles;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(0, particles.AliveFireflies, "level 0: no fireflies");
            Assert.Greater(particles.AlivePollen, 0, "pollen floats in the air in every state");

            particles.SetFireflyLevel(1f);
            yield return new WaitForSecondsRealtime(4f);

            Assert.Greater(particles.AliveFireflies, 4, "level 1: fireflies appear");
            Assert.LessOrEqual(particles.AliveFireflies, stage.Settings.quality[0].fireflyMax + 8);
            stage.Backdrop.SetSunset(0.25f); // fireflies come with the first nature layer: early in the sky arc, when the light is low
            var fireflySystem = particles.transform.Find("Fireflies").GetComponent<ParticleSystem>();
            var live = new ParticleSystem.Particle[200];
            var count = fireflySystem.GetParticles(live);
            for (var i = 0; i < count; i++)
            {
                Assert.That(live[i].position.z, Is.InRange(stage.Layout.NearZ - 2f, stage.Layout.FarZ + 2f), $"fireflies stay over the field (particle {i} at {live[i].position})");
                Assert.GreaterOrEqual(live[i].position.y, stage.Terrain.HeightAt(live[i].position.x, live[i].position.z) + 0.2f, $"and above the ground (particle {i} at {live[i].position}, ground {stage.Terrain.HeightAt(live[i].position.x, live[i].position.z)}, count {count})");
            }
            var picture = MeadowCapture.Render(stage.Camera, Width, Height);
            Save(picture, "task8-fireflies.png");
            Object.Destroy(picture);
        }

        [UnityTest]
        public IEnumerator AHandDrawsARibbon_ThatFadesWhenTheHandIsGone()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var ribbon = stage.HandWind;
            Assert.AreEqual(0f, ribbon.AlphaOf(0), "no hand: no ribbon");
            stage.Backdrop.SetSunset(0.5f);

            // the hand moves across the field for a moment
            var started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 0.4f)
            {
                var u = (Time.realtimeSinceStartup - started) / 0.4f;
                stage.HandsOverride = new Hands((0.3f + 0.2f * u, 0.55f), (0.75f, 0.7f - 0.1f * u));
                yield return null;
            }
            Assert.Greater(ribbon.AlphaOf(0), 0.99f);
            Assert.Greater(ribbon.AlphaOf(1), 0.99f, "two hands, two ribbons");
            var withHands = MeadowCapture.Render(stage.Camera, Width, Height);
            Save(withHands, "task8-ribbon.png");

            stage.HandsOverride = new Hands();
            yield return new WaitForSecondsRealtime(0.2f);
            var halfway = ribbon.AlphaOf(0);
            yield return new WaitForSecondsRealtime(0.6f);

            Assert.Less(halfway, 0.85f, "it starts to fade at once");
            Assert.Greater(halfway, 0.01f, "and has not gone yet after 0.2 s");
            Assert.AreEqual(0f, ribbon.AlphaOf(0), 1e-4f, "after 0.8 s it is gone");
            var without = MeadowCapture.Render(stage.Camera, Width, Height);
            var difference = 0f;
            var a = withHands.GetPixels(0, Height / 4, Width, Height / 2);
            var b = without.GetPixels(0, Height / 4, Width, Height / 2);
            for (var i = 0; i < a.Length; i++) difference += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            Object.Destroy(withHands);
            Object.Destroy(without);
            Assert.Greater(difference / (3f * a.Length), 0.003f, "the ribbons are visible in the picture");
        }

        [UnityTest]
        public IEnumerator ScriptedPlay_ThrowsPetals_AndTheFieldLooksAlive()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Backdrop.SetSunset(0.6f);
            stage.Particles.SetFireflyLevel(0.6f);
            var specs = new[] { (Part.LowString, 3, 0.9f, 0.18f), (Part.LowString, 6, 1f, 0.22f), (Part.Marimba, 2, 1f, 0.48f), (Part.Marimba, 5, 0.9f, 0.55f), (Part.HandDrum, 4, 0.9f, 0.84f) };
            for (var round = 0; round < 3; round++)
            {
                var dsp = AudioSettings.dspTime; // the real audio clock: the app's own Tick moves the flowers by it
                foreach (var (part, step, velocity, x) in specs) stage.OnNote(new ScheduledNote(round, dsp + 0.1, dsp, part, step, velocity, false, x), dsp);
                yield return new WaitForSecondsRealtime(0.45f);
            }
            stage.HandsOverride = new Hands((0.5f, 0.6f));
            yield return new WaitForSecondsRealtime(0.5f);
            var moveStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - moveStart < 0.4f)
            {
                stage.HandsOverride = new Hands((0.45f + 0.15f * (Time.realtimeSinceStartup - moveStart) / 0.4f, 0.6f));
                yield return null;
            }

            var picture = MeadowCapture.Render(stage.Camera, Width, Height);
            Save(picture, "task8-play.png");
            Object.Destroy(picture);

            Assert.Greater(stage.Particles.EmittedPetals, 12, "three rounds of two marimba notes threw petals");
            Assert.Greater(stage.Particles.AlivePetals, 8, "some of them are still in the air");
            Assert.Greater(stage.Particles.AliveFireflies, 0, "fireflies at level 0.6");
            Assert.Greater(stage.HandWind.AlphaOf(0), 0.99f, "the hand's ribbon is there");
            var open = 0;
            for (var i = 0; i < stage.Flowers.Count; i++) { if (stage.Flowers.LevelOf(i) > 0.5f) open++; }
            Assert.Greater(open, 4, "and flowers of the played instruments are open");
            Assert.Greater(stage.Wind.Count, 3, "every note started a ring of wind");
        }
    }
}
