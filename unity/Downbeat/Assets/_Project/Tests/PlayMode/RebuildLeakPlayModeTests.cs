using System;
using System.Collections;
using System.Collections.Generic;
using Downbeat.App;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>F5 builds the meadow again (MeadowStage.Configure): nothing it made before may be left behind, or an exhibition that reloads its content now and then grows for ever.</summary>
    public class RebuildLeakPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Rebuilds = 4;

        static IEnumerator LoadAndWaitUntilRunning(Action<MeadowStage> ready)
        {
            yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
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
            ready(UnityEngine.Object.FindFirstObjectByType<MeadowStage>());
        }

        /// <summary>How many objects of each kind and name there are: "Mesh MeadowGround" -> 2.</summary>
        static Dictionary<string, int> Counts()
        {
            var counts = new Dictionary<string, int>();
            void Add<T>(string kind) where T : UnityEngine.Object
            {
                foreach (var item in Resources.FindObjectsOfTypeAll<T>())
                {
                    var key = $"{kind} {item.name}";
                    counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            Add<Mesh>("Mesh");
            Add<Material>("Material");
            Add<Texture2D>("Texture2D");
            Add<RenderTexture>("RenderTexture");
            Add<GameObject>("GameObject");
            return counts;
        }

        [UnityTest]
        public IEnumerator Rebuilding_TheMeadowAgain_LeavesNoMeshMaterialOrTextureBehind()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(s => stage = s);
            // one rebuild first: the first one may add what only exists once something was built twice (a cached object)
            stage.Configure(stage.Bundle, stage.QualityName);
            yield return null;
            yield return null;
            var before = Counts();

            for (var i = 0; i < Rebuilds; i++)
            {
                stage.Configure(stage.Bundle, stage.QualityName);
                yield return null;
                yield return null;
            }
            var after = Counts();

            var grown = new List<string>();
            foreach (var entry in after)
            {
                before.TryGetValue(entry.Key, out var was);
                if (entry.Value > was) grown.Add($"{entry.Key}: {was} -> {entry.Value}");
            }
            Assert.IsEmpty(grown, "after " + Rebuilds + " rebuilds these grew: " + string.Join(", ", grown));
        }
        [UnityTest]
        public IEnumerator Rebuilding_KeepsTheBloomsAndParticlesOfNotesThatWereScheduledButNotYetHeard()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(s => stage = s);
            var dsp = AudioSettings.dspTime;
            // a note that will be heard a minute from now: its flowers are to open and its seeds to fly then, whatever happens to the meadow in between
            stage.OnNote(new ScheduledNote(1, dsp + 60.0, dsp + 59.9, Part.Marimba, 3, 1f, false, 0.5f), dsp);
            yield return null;
            var bloomsBefore = stage.Flowers.ActiveBlooms;
            var cuesBefore = stage.Particles.PendingCount;
            Assert.Greater(bloomsBefore, 0, "the note opens flowers");
            Assert.Greater(cuesBefore, 0, "and throws something");

            stage.Configure(stage.Bundle, stage.QualityName);
            yield return null;

            Assert.AreEqual(bloomsBefore, stage.Flowers.ActiveBlooms, "the flowers still wait for their sound");
            Assert.AreEqual(cuesBefore, stage.Particles.PendingCount, "and so do the particles");
        }
    }
}
