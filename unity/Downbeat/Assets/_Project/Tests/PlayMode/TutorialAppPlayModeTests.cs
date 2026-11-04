using System.Collections;
using System.Linq;
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
    /// The tutorial in the real scene, on the real clock: it is there under the overlay layer, hidden while hands play, it shows ten seconds after the start of a meadow that nobody plays, and an open hand that comes
    /// while it is showing gets its first note as fast as one that comes when it is not.
    /// </summary>
    public class TutorialAppPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;

        sealed class Probe
        {
            public DownbeatApp App;
            public IHandMotionView Motion;
            public MeadowStage Stage;
        }

        static IEnumerator Start(Probe probe, params string[] extraArgs) => Start(probe, false, extraArgs);

        static IEnumerator Start(Probe probe, bool withRecordStrip, params string[] extraArgs)
        {
            var args = (withRecordStrip ? new[] { "Downbeat", "--mute" } : new[] { "Downbeat", "--mute", "--no-record" }).Concat(extraArgs).ToArray();
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            probe.App = scope.Container.Resolve<DownbeatApp>();
            probe.Motion = scope.Container.Resolve<IHandMotionView>();
            probe.Stage = Object.FindFirstObjectByType<MeadowStage>();
            var waited = 0f;
            while (!probe.App.IsRunning && string.IsNullOrEmpty(probe.App.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!probe.App.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + probe.App.FailureMessage);
            Assert.IsTrue(probe.App.IsRunning, probe.App.FailureMessage);
        }

        [UnityTest]
        public IEnumerator TheView_IsUnderTheOverlayLayer_AndHiddenWhileHandsPlay()
        {
            var probe = new Probe();
            yield return Start(probe, "--input", "replay");

            yield return new WaitForSecondsRealtime(6f);

            Assert.IsNotNull(probe.App.Tutorial, "the tutorial is built with the meadow");
            Assert.IsTrue(probe.App.Tutorial.View.OpenHandIsPicture, "the pictures of the content are the hands of the tutorial");
            Assert.IsTrue(probe.App.Tutorial.View.FistIsPicture);
            Assert.IsNotNull(probe.Stage.Overlay);
            Assert.AreSame(probe.Stage.Overlay.Root, probe.App.Tutorial.View.transform.parent, "it hangs on the overlay layer in front of the meadow camera");
            Assert.Greater(probe.App.HandNotes, 0, "the demo hands are playing");
            Assert.IsFalse(probe.App.Tutorial.View.IsShown, "nothing of the tutorial while hands play");
            Assert.AreNotEqual(ExperienceState.Idle, probe.App.Experience.State);
        }

        [UnityTest]
        public IEnumerator WithNoTutorialFlag_NothingIsBuilt()
        {
            var probe = new Probe();
            yield return Start(probe, "--input", "mouse", "--no-tutorial");

            Assert.IsNull(probe.App.Tutorial);
            Assert.IsNull(probe.Stage.Overlay.GetComponentInChildren<TutorialView>(true));
        }

        [UnityTest]
        public IEnumerator Appears_TenSecondsAfterStart_AndGoesRoundItsSteps()
        {
            var probe = new Probe();
            yield return Start(probe, "--input", "mouse");
            var began = Time.realtimeSinceStartup;

            yield return new WaitForSecondsRealtime(8.5f);
            Assert.IsFalse(probe.App.Tutorial.View.IsShown, "not yet, eight seconds after the start");

            yield return new WaitForSecondsRealtime(2.5f);
            Assert.IsTrue(probe.App.Tutorial.View.IsShown, $"{Time.realtimeSinceStartup - began:0.0} s after the start");
            Assert.AreEqual(0, probe.App.Tutorial.Frame.Step);
            Assert.AreEqual("손을 펴서 들어 보세요", probe.App.Tutorial.View.CaptionText);
            Assert.AreEqual(ExperienceState.Idle, probe.App.Experience.State);

            yield return new WaitForSecondsRealtime(5f);
            Assert.AreEqual(1, probe.App.Tutorial.Frame.Step, "five seconds on, the second step");
        }

        /// <summary>
        /// One still per step on the real meadow with the record strip under it, 1920 x 1080: written to captures/visual, and (with DOWNBEAT_KEEP_CAPTURES=1) as JPEG to measurements/visual/six-cells-tutorial-N.jpg.
        /// The frames are given by hand and drawn at once, so a still takes no wait of ten seconds.
        /// </summary>
        [UnityTest]
        public IEnumerator Stills_OnePerStep_OnTheRealMeadow()
        {
            var probe = new Probe();
            yield return Start(probe, true, "--input", "mouse");
            yield return new WaitForSecondsRealtime(3f);
            var view = probe.App.Tutorial.View;
            var stripFraction = probe.Stage.Overlay.StripFraction;
            Assert.Greater(stripFraction, 0.1f, "the record strip is under the meadow");
            var clock = Time.realtimeSinceStartupAsDouble;

            var frames = new[]
            {
                new TutorialFrame(1f, 0, 0.45f, true, false, false),
                new TutorialFrame(1f, 1, 0.55f, true, true, false),
                new TutorialFrame(1f, 2, 0.90f, true, true, true),
            };
            var bare = MeadowCapture.Render(probe.Stage.Camera, 1920, 1080);
            try
            {
                for (var step = 0; step < frames.Length; step++)
                {
                    view.Show(frames[step], clock);
                    view.Show(frames[step], clock + 1.0);   // the ticks have popped up by then
                    var picture = MeadowCapture.Render(probe.Stage.Camera, 1920, 1080);
                    try
                    {
                        CaptureOutput.SavePicture(picture, $"six-cells-tutorial-{step + 1}.png");
                        Assert.Greater(MeanAbsoluteDifference(picture, bare, 0.28f), 6f, $"step {step + 1} changes the stage");
                        Assert.AreEqual(0f, MeanAbsoluteDifference(picture, bare, 0f, 0.27f), 1f, $"step {step + 1} leaves the strip alone");
                    }
                    finally
                    {
                        Object.Destroy(picture);
                    }
                }
            }
            finally
            {
                Object.Destroy(bare);
            }
        }

        /// <summary>The mean difference (0..255) of the pixels between the rows <paramref name="fromHeight"/> and <paramref name="toHeight"/> of the picture's height, counted from the bottom.</summary>
        static float MeanAbsoluteDifference(Texture2D a, Texture2D b, float fromHeight, float toHeight = 1f)
        {
            var width = a.width;
            var first = Mathf.RoundToInt(a.height * fromHeight);
            var last = Mathf.RoundToInt(a.height * toHeight);
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            double sum = 0;
            long n = 0;
            for (var y = first; y < last; y += 2)
            {
                for (var x = 0; x < width; x += 2)
                {
                    var i = y * width + x;
                    sum += (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b)) / 3f;
                    n++;
                }
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        /// <summary>What one run saw, on the real clock.</summary>
        struct Run
        {
            public float TutorialVisibleAt, HandsAt, NoteAt, GoneAt, AlphaWhenHandsCame;
            public float DelayToFirstNote => NoteAt - HandsAt;
        }

        static IEnumerator RunUntilFirstNote(Probe probe, Run[] result, params string[] extraArgs)
        {
            yield return Start(probe, extraArgs);
            var run = new Run { TutorialVisibleAt = float.NaN, HandsAt = float.NaN, NoteAt = float.NaN, GoneAt = float.NaN };
            var started = Time.realtimeSinceStartup;
            var end = started + 40f;
            var finishAfter = float.NaN;
            while (Time.realtimeSinceStartup < end && (float.IsNaN(finishAfter) || Time.realtimeSinceStartup < finishAfter))
            {
                var now = Time.realtimeSinceStartup;
                var tutorial = probe.App.Tutorial;
                if (float.IsNaN(run.TutorialVisibleAt) && tutorial != null && tutorial.View.IsShown) run.TutorialVisibleAt = now;
                if (float.IsNaN(run.HandsAt) && probe.Motion.Count > 0) run.HandsAt = now;
                if (float.IsNaN(run.HandsAt) && tutorial != null) run.AlphaWhenHandsCame = tutorial.Frame.Alpha;
                if (float.IsNaN(run.NoteAt) && probe.App.HandNotes > 0)
                {
                    run.NoteAt = now;
                    finishAfter = now + 1.5f;
                }
                if (!float.IsNaN(run.HandsAt) && float.IsNaN(run.GoneAt) && tutorial != null && !tutorial.View.IsShown && !float.IsNaN(run.TutorialVisibleAt)) run.GoneAt = now;
                yield return null;
            }
            result[0] = run;
        }

        [UnityTest]
        public IEnumerator OpenHand_FadesItOut_AndPlayingIsNotDelayed()
        {
            var showing = new Run[1];
            yield return RunUntilFirstNote(new Probe(), showing, "--input", "replay", "--replay-delay", "13");
            var quiet = new Run[1];
            yield return RunUntilFirstNote(new Probe(), quiet, "--input", "replay", "--replay-delay", "3");
            var a = showing[0];
            var b = quiet[0];
            TestContext.Out.WriteLine($"TUTORIAL-DELAY with the tutorial: visible at {a.TutorialVisibleAt:0.0} s, hands at {a.HandsAt:0.0} s, first note {a.DelayToFirstNote * 1000f:0} ms later, gone {(a.GoneAt - a.HandsAt) * 1000f:0} ms after the hands; without it: first note {b.DelayToFirstNote * 1000f:0} ms after the hands");

            Assert.IsFalse(float.IsNaN(a.TutorialVisibleAt), "the tutorial showed while nobody played");
            Assert.Less(a.TutorialVisibleAt, a.HandsAt - 1f, "and it had been there a while when the hands came");
            Assert.GreaterOrEqual(a.AlphaWhenHandsCame, 0.99f, "fully");
            Assert.IsFalse(float.IsNaN(a.GoneAt), "the hands made it go");
            Assert.That(a.GoneAt - a.HandsAt, Is.InRange(0.3f, 1.6f), "within about the fade-out time");
            Assert.IsTrue(float.IsNaN(b.TutorialVisibleAt) || b.TutorialVisibleAt > b.HandsAt, "in the other run the hands came before it was due");

            Assert.IsFalse(float.IsNaN(a.NoteAt) || float.IsNaN(b.NoteAt), "both runs played");
            Assert.Less(a.DelayToFirstNote, 0.8f, "the first note comes at once, not after the fade-out");
            Assert.LessOrEqual(a.DelayToFirstNote, b.DelayToFirstNote + 0.35f, "as fast as without the tutorial (the notes wait for the next slot of the grid, 0.16 s, which differs from run to run)");
        }
    }
}
