using System.Collections;
using System.Collections.Generic;
using Downbeat.App;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Downbeat.Tests
{
    /// <summary>
    /// The glow of the meadow at its brightest (the sunset, all fireflies out): sparkles and fireflies are points with small halos, not blobs that cover the field. The picture is
    /// drawn the way the window is drawn (<see cref="MeadowCapture"/> matches the window's own back buffer to 0.3 of 255, checked by <c>--capture-compare</c>).
    /// </summary>
    public class GlowPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const int Width = 1920;
        const int Height = 1080;
        const float FieldFromRow = 0.56f;            // fraction of the height from the top: the field under the haze
        const float BrightLuminance = 235f;
        const float ClippedChannel = 250f;
        const float MaxGlowWidthShare = 0.025f;      // no glow wider than this share of the picture width (the fix brought it from about 14 % to under 2 %)
        const float MaxClippedShare = 0.02f;         // white-hot pixels in the field (3.2 % before)
        const float FireflySeconds = 5f;

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
            if (app.NatureVisuals != null) { app.NatureVisuals.ControlsSky = false; app.NatureVisuals.ControlsFireflies = false; }
            if (app.Record != null) app.Record.View.Visible = false;
            done(app, Object.FindFirstObjectByType<MeadowStage>());
        }

        /// <summary>Widest connected bright region of the field, as a share of the picture width, and the share of white-hot pixels.</summary>
        static (float widestShare, float clippedShare) Measure(Texture2D picture)
        {
            var pixels = picture.GetPixels32();
            var fieldRows = (int)((1f - FieldFromRow) * picture.height); // a texture's row 0 is the bottom: the field is its lower rows
            var width = picture.width;
            var bright = new bool[width * fieldRows];
            var clipped = 0;
            for (var y = 0; y < fieldRows; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = pixels[y * width + x];
                    var luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    bright[y * width + x] = luminance >= BrightLuminance;
                    if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) >= ClippedChannel) clipped++;
                }
            }

            var seen = new bool[bright.Length];
            var stack = new Stack<int>();
            var widest = 0;
            for (var start = 0; start < bright.Length; start++)
            {
                if (!bright[start] || seen[start]) continue;
                var minX = width;
                var maxX = 0;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    var x = i % width;
                    var y = i / width;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= fieldRows) continue;
                            var j = ny * width + nx;
                            if (!bright[j] || seen[j]) continue;
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
                }
                widest = Mathf.Max(widest, maxX - minX + 1);
            }
            return ((float)widest / width, (float)clipped / bright.Length);
        }

        [UnityTest]
        public IEnumerator AtTheSunsetWithEveryFireflyOut_NoGlowIsABlob_AndTheFieldIsNotBlownOut()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((_, s) => stage = s);
            stage.Backdrop.SetSunset(0.85f);
            stage.Particles.SetFireflyLevel(1f);
            yield return new WaitForSecondsRealtime(FireflySeconds);
            Assert.Greater(stage.Particles.AliveFireflies, 40, "the fireflies are out");

            var picture = MeadowCapture.Render(stage.Camera, Width, Height);
            var (widest, clipped) = Measure(picture);
            Object.Destroy(picture);

            Assert.Less(widest, MaxGlowWidthShare, "no bright region is a blob across the field");
            Assert.Less(clipped, MaxClippedShare, "the field is not white-hot");
        }

        [UnityTest]
        public IEnumerator TheBloomIsClamped_SoOneVeryBrightPointCannotMakeABlob()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning((_, s) => stage = s);
            var volume = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();

            Assert.IsTrue(volume.profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom));
            Assert.LessOrEqual(bloom.clamp.value, 8f, "each pixel feeds the bloom with at most this much");
            Assert.GreaterOrEqual(bloom.threshold.value, 1.1f);
            Assert.LessOrEqual(bloom.scatter.value, 0.5f, "the halos stay tight");
        }
    }
}
