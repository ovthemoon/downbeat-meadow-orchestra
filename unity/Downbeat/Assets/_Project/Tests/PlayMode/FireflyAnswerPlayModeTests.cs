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
    /// <summary>The fireflies in the real scene answer a bell of the nature score: the nearest glow for a moment, and a bell reaches them when it is heard, not when it was scheduled.</summary>
    public class FireflyAnswerPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Bell = 6;

        static IEnumerator Load(System.Action<DownbeatApp, MeadowStage> done)
        {
            var args = new List<string> { "Downbeat", "--input", "mouse", "--mute", "--no-record", "--quality", "High", "--fireflies", "1" };
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
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment: " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            done(app, Object.FindFirstObjectByType<MeadowStage>());
        }

        static IEnumerator UntilFirefliesHaveCome(MeadowStage stage)
        {
            var until = Time.realtimeSinceStartup + 20f;
            while (stage.Particles.AliveFireflies < 60 && Time.realtimeSinceStartup < until) yield return null;
            Assert.GreaterOrEqual(stage.Particles.AliveFireflies, 60, "the fireflies have come");
        }

        [UnityTest]
        public IEnumerator ABell_IsAnsweredByTheNearestFireflies_ForAMoment_ThenEverythingIsDark()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Load((a, s) => { app = a; stage = s; });
            yield return UntilFirefliesHaveCome(stage);
            var settings = FireflyAnswerSettings.From(stage.Settings.particles);

            var count = stage.AnswerBell(Bell, 40);

            Assert.That(count, Is.InRange(1, settings.Count), "a bell is answered by the nearest few, not by all");
            Assert.AreEqual(count, stage.Particles.AnsweringFireflies);
            yield return null;
            var glow = new List<Vector4>();
            stage.Particles.GetAnswerGlow(glow);
            var lit = 0;
            foreach (var value in glow)
            {
                if (value.x > 0f) lit++;
                Assert.That(value.x, Is.InRange(0f, 1f));
            }
            Assert.AreEqual(count, lit, "exactly those fireflies glow; the others are as they were");

            yield return new WaitForSecondsRealtime(settings.AttackSec + settings.DecaySec + 0.4f);
            stage.Particles.GetAnswerGlow(glow);
            Assert.AreEqual(0, stage.Particles.AnsweringFireflies);
            foreach (var value in glow) Assert.AreEqual(0f, value.x, 1e-6f, "after the glow nothing stays lit");
        }

        [UnityTest]
        public IEnumerator ABellThatCannotBeAnswered_ChangesNothing()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Load((a, s) => { app = a; stage = s; });
            stage.Settings.particles.fireflyAnswerCount = 0;
            stage.ApplyQuality(stage.QualityName);   // the director reads its settings when it is built
            yield return UntilFirefliesHaveCome(stage);

            Assert.AreEqual(0, stage.AnswerBell(Bell, 40));
            Assert.AreEqual(0, stage.Particles.AnsweringFireflies);
        }

        [UnityTest]
        public IEnumerator ABellOfTheNatureScore_ReachesTheFireflies_WhenItIsHeard()
        {
            DownbeatApp app = null;
            MeadowStage stage = null;
            yield return Load((a, s) => { app = a; stage = s; });
            yield return UntilFirefliesHaveCome(stage);
            var visuals = app.NatureVisuals;
            var bell = new NatureEvent(new NatureNote(NatureLayer.Fireflies, 40, Bell, 1f), 101.0);
            var other = new NatureEvent(new NatureNote(NatureLayer.Sky, 40, 3, 1f), 101.0);
            var events = new List<NatureEvent> { bell, other };
            var before = visuals.BellsAnswered;

            visuals.Tick(100.0, 0.016f, 0.5f, 1, true, events);   // scheduled, a second ahead: it is not heard yet
            Assert.AreEqual(before, visuals.BellsAnswered, "a bell that has only been scheduled is not answered yet");
            visuals.Tick(100.5, 0.016f, 0.5f, 1, true, new List<NatureEvent>());
            Assert.AreEqual(before, visuals.BellsAnswered);
            visuals.Tick(101.0, 0.016f, 0.5f, 1, true, new List<NatureEvent>());

            Assert.AreEqual(before + 1, visuals.BellsAnswered, "it is answered when it is heard, and the pad of the sky is not a bell");
            Assert.Greater(stage.Particles.AnsweringFireflies, 0);
        }
    }
}
