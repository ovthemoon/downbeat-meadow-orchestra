using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// The two-fists ending in the real scene, with a recording of hands as the camera: an open hand plays, both hands become fists and are pulled apart, the piece ends at once (the visit is over, the card is
    /// written, the word "끝" is on the screen), fists held up start nothing, an open hand within the restart lock starts nothing and one after it starts a new visit. No error is logged.
    /// </summary>
    public class CutoffPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const string ReplayFolder = "replay";
        const string ReplayName = "zz-cutoff-playmode-test.json";
        const int Fps = 30;
        const float Palm = 0.15f;
        const float Row = 0.75f;

        // the recording, in seconds from its start
        const double PlayUntil = 4.0;       // an open hand plays
        const double SpreadAt = 5.0;        // two fists a palm and a third apart are pulled apart ...
        const double SpreadDoneAt = 5.4;    // ... to six palms
        const double OpenAgainAt = 6.3;     // open hands come back, inside the restart lock of the end (about 5.6 + 1.5 s)
        const double RecordingEnd = 16.0;

        string _replayPath;
        string _cardsDir;

        [SetUp]
        public void SetUp()
        {
            _replayPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", ReplayFolder, ReplayName));
            _cardsDir = Path.Combine(Path.GetTempPath(), "downbeat-cutoff-cards-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_replayPath)) File.Delete(_replayPath);
            if (Directory.Exists(_cardsDir)) Directory.Delete(_cardsDir, true);
        }

        // ---- the recording ------------------------------------------------------------------------------------------------------------

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>The 21 points of an upright hand (aspect 1): the fingertips of the four fingers are <paramref name="ratio"/> palm lengths from the wrist, the knuckles on the line to them.</summary>
        static float[] Landmarks(float x, float y, float ratio)
        {
            var p = new float[ReplayFormat.CoordinatesPerHand];
            void Put(int landmark, float dx, float dy)
            {
                p[landmark * 2] = x + dx * Palm;
                p[landmark * 2 + 1] = y + dy * Palm;
            }

            const float WristDy = 0.8f;
            var knuckles = new[] { (HandBufferLayout.IndexBase, -0.32f), (HandBufferLayout.MiddleBase, 0f), (HandBufferLayout.RingBase, 0.16f), (HandBufferLayout.PinkyBase, 0.16f) };
            Put(HandBufferLayout.Wrist, 0f, WristDy);
            foreach (var (knuckle, dx) in knuckles)
            {
                var length = Mathf.Sqrt(dx * dx + 1f);
                for (var step = 0; step <= 3; step++)
                {
                    var along = length + (ratio - length) * step / 3f;
                    Put(knuckle + step, dx / length * along, WristDy - 1f / length * along);
                }
            }
            Put(HandBufferLayout.Wrist + 1, -0.15f, WristDy - 0.1f);
            Put(HandBufferLayout.Wrist + 2, -0.35f, WristDy - 0.35f);
            Put(HandBufferLayout.Wrist + 3, -0.5f, WristDy - 0.55f);
            Put(HandBufferLayout.Wrist + 4, -0.6f, WristDy - 0.75f);
            return p;
        }

        const float OpenRatio = 1.8f;
        const float FistRatio = 0.9f;

        static ReplayHandDto Hand(int id, double x, float ratio) => new ReplayHandDto { id = id, s = 0.95f, p = Landmarks((float)x, Row, ratio) };

        static ReplayHandDto[] HandsAt(double t)
        {
            if (t < PlayUntil) return new[] { Hand(0, 0.5, OpenRatio) };
            if (t >= OpenAgainAt) return new[] { Hand(0, 0.5, OpenRatio), Hand(1, 0.65, OpenRatio) };
            var apart = Ramp(t, 1.3, 6.0, SpreadAt, SpreadDoneAt) * Palm;
            return new[] { Hand(0, 0.5 - apart / 2.0, FistRatio), Hand(1, 0.5 + apart / 2.0, FistRatio) };
        }

        void WriteRecording()
        {
            var frames = new List<ReplayFrameDto>();
            for (var i = 0; i <= (int)(RecordingEnd * Fps); i++) frames.Add(new ReplayFrameDto { t = i * 1000.0 / Fps, h = HandsAt((double)i / Fps) });
            var dto = new ReplayDto { format = ReplayFormat.Name, version = ReplayFormat.SupportedVersion, aspect = 1f, mirrored = true, frames = frames.ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(_replayPath));
            File.WriteAllText(_replayPath, JsonUtility.ToJson(dto));
        }

        // ---- the test -----------------------------------------------------------------------------------------------------------------

        /// <summary>The meadow camera drawn with and without the word: the middle of the picture differs, so the word is really on the screen (the picture is kept in the temp folder to look at).</summary>
        static void AssertTheWordIsDrawn(MeadowOverlayLayer overlay, EndWordView word)
        {
            const int Width = 640;
            const int Height = 360;
            var with = MeadowCapture.Render(overlay.Viewer, Width, Height);
            word.gameObject.SetActive(false);
            var without = MeadowCapture.Render(overlay.Viewer, Width, Height);
            word.gameObject.SetActive(true);
            try
            {
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "downbeat-cutoff-word.png"), with.EncodeToPNG());
                var a = with.GetPixels32();
                var b = without.GetPixels32();
                var sum = 0.0;
                var count = 0;
                for (var y = (int)(0.5f * Height); y < (int)(0.78f * Height); y++)
                {
                    for (var x = (int)(0.4f * Width); x < (int)(0.6f * Width); x++)
                    {
                        var i = y * Width + x;
                        sum += Mathf.Abs(Luminance(a[i]) - Luminance(b[i]));
                        count++;
                    }
                }
                Assert.Greater(sum / count, 3.0, "the middle of the picture is not the same with the word and without it: the word is drawn");
            }
            finally
            {
                UnityEngine.Object.Destroy(with);
                UnityEngine.Object.Destroy(without);
            }
        }

        static float Luminance(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(condition(), "timed out waiting for " + what);
        }

        [UnityTest]
        public IEnumerator TwoFistsSpread_EndTheVisit_ShowTheWord_AndWriteTheCardAtOnce()
        {
            WriteRecording();
            var errors = new List<string>();
            Application.LogCallback collect = (message, trace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + message);
            };
            Application.logMessageReceived += collect;
            try
            {
                yield return Run(errors);
            }
            finally
            {
                Application.logMessageReceived -= collect;
            }
        }

        IEnumerator Run(List<string> errors)
        {
            var args = new[] { "Downbeat", "--input", "replay", "--file", ReplayFolder + "/" + ReplayName, "--loop", "0", "--mute", "--cards-dir", _cardsDir };
            using (LaunchArgs.OverrideForTests(args))
            {
                yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            }
            var scope = UnityEngine.Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var app = scope.Container.Resolve<DownbeatApp>();
            var waited = 0f;
            while (!app.IsRunning && string.IsNullOrEmpty(app.FailureMessage) && waited < BootTimeoutSec)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!app.IsRunning && AudioSettings.dspTime < 0.2) Assert.Ignore("no audio device in this environment (the DSP clock does not run): " + app.FailureMessage);
            Assert.IsTrue(app.IsRunning, app.FailureMessage);

            var madeAtBuild = app.EndWord;
            Assert.IsNotNull(madeAtBuild, "the word was made when the app was built, not in the frame of the cutoff");
            Assert.IsFalse(madeAtBuild.IsShowing);
            Assert.IsFalse(madeAtBuild.gameObject.activeSelf, "and it is hidden until the piece is ended");

            var started = new List<float>();
            var ended = new List<float>();
            var cuts = new List<float>();
            var atTheCut = default((ExperienceState state, int cards, bool wordShowing, string word, int notes, int cutCount, double stateSinceMinusNow));
            var notesAtStart = new List<int>();
            app.Experience.SessionStarted += _ =>
            {
                started.Add(Time.realtimeSinceStartup);
                notesAtStart.Add(app.HandNotes);
            };
            app.Experience.SessionEnded += _ => ended.Add(Time.realtimeSinceStartup);
            app.PerformanceCutoff += cutNow =>
            {
                cuts.Add(Time.realtimeSinceStartup);
                // what a listener sees in the frame the piece is ended: the visit is over, the card is on disk, the word is up
                atTheCut = (app.Experience.State, app.Record.CardsWritten, app.EndWord != null && app.EndWord.IsShowing, app.EndWord?.Text, app.HandNotes, app.CutoffCount, app.Experience.StateSince - cutNow);
            };

            yield return WaitFor(() => started.Count == 1, 15f, "the open hand to start a visit");
            yield return WaitFor(() => cuts.Count == 1, 15f, "the fists to end the piece");

            Assert.AreEqual(1, ended.Count, "the visit ended in the frame of the cut");
            Assert.AreEqual(ExperienceState.Idle, atTheCut.state);
            Assert.AreEqual(1, atTheCut.cards, "the card was written at once, not 9 seconds after the hands left");
            Assert.IsTrue(atTheCut.wordShowing, "the word is on the screen");
            Assert.AreEqual("끝", atTheCut.word);
            Assert.AreEqual(1, atTheCut.cutCount);
            Assert.AreEqual(0.0, atTheCut.stateSinceMinusNow, 1e-9, "the visit's state machine and the cutoff read the same clock: Idle since the very time the event carries (the tutorial compares its own time with it)");
            Assert.GreaterOrEqual(atTheCut.notes, 3, "the open hand played before");
            Assert.AreEqual(0f, app.Energy.Value, 1e-3f, "the energy is down (the nature layers fade out)");

            var word = app.EndWord;
            Assert.AreSame(madeAtBuild, word, "the same word");
            var overlay = UnityEngine.Object.FindFirstObjectByType<MeadowStage>().Overlay;
            Assert.AreEqual(overlay.SortingOrderBase + EndWordView.SortingOrderAboveLayerBase + 1, word.SortingOrder, "above the guide and the trail on the overlay");
            Assert.AreSame(overlay.Root, word.transform.parent, "a child of the meadow's overlay layer");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.Greater(word.Alpha, 0f, "still there a moment later");
            AssertTheWordIsDrawn(overlay, word);
            Assert.AreEqual(ExperienceState.Idle, app.Experience.State, "fists held up start nothing");

            // an open hand comes back at 4.8 s, inside the lock (1.5 s after the end at about 4.1 s): the new visit starts only after the lock
            yield return WaitFor(() => started.Count == 2, 12f, "the open hands to start a new visit after the lock");
            var lockWas = CutoffSettings.Default.RestartLockSec;
            Assert.GreaterOrEqual(started[1] - cuts[0], lockWas - 0.1, "not inside the restart lock");
            Assert.LessOrEqual(started[1] - cuts[0], lockWas + 1.0, "and soon after it");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(word.IsShowing, "the word has faded out by then");
            Assert.AreEqual(atTheCut.notes, notesAtStart[1], "the open hands that came back inside the lock played nothing: not a note between the end and the next visit");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(app.HandNotes, notesAtStart[1], "and once the visit has started they play");

            Assert.AreEqual(1, cuts.Count, "the open hands did not end anything");
            Assert.AreEqual(1, app.CutoffCount);
            Assert.IsEmpty(errors, "no error was logged: " + string.Join(" | ", errors));
        }
    }
}
