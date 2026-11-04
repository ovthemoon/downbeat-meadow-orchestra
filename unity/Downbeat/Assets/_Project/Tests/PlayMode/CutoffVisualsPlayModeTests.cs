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
    /// <summary>The real meadow when the piece is ended: what the notes that are not heard yet had asked of it (flowers to open, rings of wind, petals, star dust) is taken back, and a note that is heard before the end is left alone.</summary>
    public class CutoffVisualsPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const double FakeHeardNow = 3000.0;
        const double Lead = 0.2;

        static IEnumerator LoadAndWaitUntilRunning(System.Action<MeadowStage> done)
        {
            using (LaunchArgs.OverrideForTests(new[] { "Downbeat", "--input", "mouse", "--mute", "--no-record" }))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
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
            done(Object.FindFirstObjectByType<MeadowStage>());
        }

        static ScheduledNote Note(Part part, float x01, float y01) => new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, part, 4, 0.9f, false, x01, y01);

        static (int blooms, int cues, int sky, int waves) Pending(MeadowStage stage) =>
            (stage.Flowers.BloomsNotYetHeard(0.0).Count, stage.Particles.PendingCount, stage.Particles.PendingSkyCount, stage.Wind.CountStartingAfter(MeadowStage.WindTime));

        [UnityTest]
        public IEnumerator CancellingTheNotesNotHeardYet_TakesBackTheirFlowersWindAndParticles_ButNotThoseHeardBefore()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(s => stage = s);
            var before = Pending(stage);

            stage.OnNote(Note(Part.Marimba, 0.5f, 0.75f), FakeHeardNow);   // the ground: flowers, a ring of wind, petals
            stage.OnNote(Note(Part.Harp, 0.2f, 0.25f), FakeHeardNow);      // the air: an effect where the hand is
            var asked = Pending(stage);
            Assert.Greater(asked.blooms, before.blooms, "a flower is to open");
            Assert.Greater(asked.cues, before.cues, "petals are to be thrown");
            Assert.Greater(asked.sky, before.sky, "an effect in the air is to be made");
            Assert.Greater(asked.waves, before.waves, "a ring of wind is to start");

            stage.CancelNotesHeardAfter(FakeHeardNow + Lead * 2.0, FakeHeardNow);   // the end comes after both notes are heard: nothing is touched
            Assert.AreEqual(asked, Pending(stage), "a note that is heard before the end stays");

            stage.CancelNotesHeardAfter(FakeHeardNow + Lead * 0.5, FakeHeardNow);   // the end comes before they are heard
            Assert.AreEqual(before, Pending(stage), "nothing opens, blows or flies after the end");
        }
    }
}
