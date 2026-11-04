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
    /// <summary>
    /// How fast the meadow draws on this Mac at 1920x1080, per quality tier: the camera is drawn into a 1920x1080 texture every frame for 10 s (the editor's own
    /// window size is not the measurement). The numbers go to measurements/perf/grass-mac.json whether the test passes or not. The editor adds its own overhead; the
    /// player numbers come from tools/run_grass_perf.sh. The exhibition PC's graphics card is not known yet.
    /// </summary>
    public class GrassPerformanceTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 1920;
        const int Height = 1080;
        const float WarmupSec = 2f;
        const float MeasureSec = 10f;
        const double TargetFps = 60.0;

        static IEnumerator Measure(string tier, System.Action<PerfRun> done, bool withLife = false)
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

            var stage = Object.FindFirstObjectByType<MeadowStage>();
            stage.ApplyQuality(tier);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            stage.Camera.targetTexture = target;
            stage.Camera.enabled = false; // the editor does not draw cameras by itself in batchmode: this test draws the meadow once per frame, itself
            stage.DemoWaves = false;
            if (withLife) stage.Particles.SetFireflyLevel(1f);

            var frames = new List<float>(4096);
            var started = Time.realtimeSinceStartup;
            var lastNote = 0f;
            while (Time.realtimeSinceStartup - started < WarmupSec + MeasureSec)
            {
                if (withLife)
                {
                    // a busy performance: two hands moving about and a note of each instrument every quarter second
                    var t = Time.realtimeSinceStartup - started;
                    stage.HandsOverride = new LifeHands(0.3f + 0.25f * Mathf.Sin(t * 1.3f), 0.55f + 0.1f * Mathf.Sin(t * 2.1f), 0.7f + 0.2f * Mathf.Sin(t * 0.9f), 0.6f + 0.15f * Mathf.Cos(t * 1.7f));
                    if (t - lastNote > 0.25f)
                    {
                        lastNote = t;
                        var part = (Part)((int)(t * 4f) % Parts.Count);
                        stage.OnNote(new ScheduledNote(1, AudioSettings.dspTime + 0.1, AudioSettings.dspTime, part, (int)(t * 7f) % 10, 0.9f, false, 0.2f + 0.3f * ((int)(t * 4f) % 3)), AudioSettings.dspTime);
                    }
                }
                yield return null;
                stage.Camera.Render();
                AsyncGPUReadback.Request(target, 0, 0, 1, 0, 1, 0, 1, null).WaitForCompletion(); // wait until the GPU has drawn the frame: without a screen nothing else makes the loop wait
                if (Time.realtimeSinceStartup - started > WarmupSec) frames.Add(Time.unscaledDeltaTime);
            }
            stage.Camera.targetTexture = null;
            stage.Camera.enabled = true;
            target.Release();
            Object.Destroy(target);

            done(PerfRecord.Summarize("playmode-editor", tier, Width, Height, frames, stage.Grass.BladeCount,
                "Unity editor PlayMode (batchmode), meadow camera drawn by the test once per frame into a 1920x1080 texture and waited for (a one-pixel readback), vSync off, grass tier " + tier
                + (withLife ? ", with two hand ribbons, a note every 0.25 s (seeds, petals, flower blooms) and fireflies at level 1" : "")));
        }

        sealed class LifeHands : IHandMotionView
        {
            readonly HandMotion[] _hands;
            public LifeHands(float x0, float y0, float x1, float y1)
            {
                _hands = new[]
                {
                    new HandMotion(0, true, 1f, x0, y0, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, HandPose.Open),
                    new HandMotion(1, true, 1f, x1, y1, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, 0, HandPose.Open),
                };
            }
            public long FrameSeq => 1;
            public int Count => 2;
            public HandMotion Hand(int index) => _hands[index];
            public bool TryGetById(int id, out HandMotion motion) { motion = _hands[id]; return true; }
        }

        [UnityTest]
        public IEnumerator High_WithParticlesAndHands_Holds60FpsOnThisMac()
        {
            PerfRun run = null;
            yield return Measure("High", r => run = r, withLife: true);

            PerfRecord.Save(run, "particles-mac.json");
            Debug.Log($"[perf] High with life: {run.fpsAverage} fps average, frame p95 {run.frameMsP95} ms");
            Assert.GreaterOrEqual(run.fpsAverage, TargetFps, $"High tier with particles: {run.fpsAverage} fps average (p95 {run.frameMsP95} ms) on this Mac");
        }

        [UnityTest]
        public IEnumerator Low_WithParticlesAndHands_Holds60FpsOnThisMac()
        {
            PerfRun run = null;
            yield return Measure("Low", r => run = r, withLife: true);

            PerfRecord.Save(run, "particles-mac.json");
            Debug.Log($"[perf] Low with life: {run.fpsAverage} fps average, frame p95 {run.frameMsP95} ms");
            Assert.GreaterOrEqual(run.fpsAverage, TargetFps, $"Low tier with particles: {run.fpsAverage} fps average (p95 {run.frameMsP95} ms) on this Mac");
        }

        [UnityTest]
        public IEnumerator High_TierHolds60FpsOnThisMac()
        {
            PerfRun run = null;
            yield return Measure("High", r => run = r);

            PerfRecord.Save(run);
            Debug.Log($"[perf] High: {run.fpsAverage} fps average, frame p95 {run.frameMsP95} ms, {run.grassBlades} blades");
            Assert.GreaterOrEqual(run.fpsAverage, TargetFps, $"High tier: {run.fpsAverage} fps average (p95 {run.frameMsP95} ms) on this Mac");
        }

        [UnityTest]
        public IEnumerator Low_TierIsCheaperThanHigh_AndHolds60Fps()
        {
            PerfRun run = null;
            yield return Measure("Low", r => run = r);

            PerfRecord.Save(run);
            Debug.Log($"[perf] Low: {run.fpsAverage} fps average, frame p95 {run.frameMsP95} ms, {run.grassBlades} blades");
            Assert.GreaterOrEqual(run.fpsAverage, TargetFps, $"Low tier: {run.fpsAverage} fps average (p95 {run.frameMsP95} ms) on this Mac");
        }
    }
}
