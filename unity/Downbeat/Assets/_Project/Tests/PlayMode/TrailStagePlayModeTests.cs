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
    /// <summary>The note trail and the cell names in the real scene: the stage makes them under its overlay, and the hands it is given (a tracker that sees a hand 6 times a second, a fist, a hand that moves into another cell) show on them.</summary>
    public class TrailStagePlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float Frame = 1f / 60f;

        static ContentDto ShippedContent() =>
            JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));

        static IEnumerator LoadAndWaitUntilRunning()
        {
            yield return SceneManager.LoadSceneAsync(SceneBuildIndex, LoadSceneMode.Single);
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
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
        }

        /// <summary>One hand whose place, pose and capture time the test sets (a tracker picture at a time).</summary>
        sealed class TestHand : IHandMotionView
        {
            HandMotion _hand;
            readonly int _id;

            public TestHand(int id = 0) => _id = id;

            public void Show(float x, float y, double captureMs, HandPose pose = HandPose.Open) =>
                _hand = new HandMotion(_id, true, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, captureMs, 0, pose);

            public long FrameSeq => 1;
            public int Count => 1;
            public HandMotion Hand(int index) => _hand;

            public bool TryGetById(int id, out HandMotion motion)
            {
                motion = _hand;
                return id == _id;
            }
        }

        /// <summary>The hand stays at (x, y) for <paramref name="frames"/> drawn frames, seen by a tracker 6 times a second (a new picture every 10th frame, from <paramref name="captureMs"/> on).</summary>
        static double Settle(MeadowStage stage, TestHand hand, float x, float y, double captureMs, int frames, HandPose pose = HandPose.Open)
        {
            for (var i = 0; i < frames; i++)
            {
                if (i % 10 == 0) hand.Show(x, y, captureMs + i / 10 * 1000.0 / 6.0, pose);
                stage.SetHands(hand, Frame);
            }
            return captureMs + (frames + 9) / 10 * 1000.0 / 6.0;
        }

        [UnityTest]
        public IEnumerator TheStage_MakesTheTrailAndTheGuide_UnderItsOverlay_AboveTheOtherThingsOnIt()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();

            var trail = stage.Trail;

            Assert.IsNotNull(trail, "the stage makes them when it is built");
            Assert.AreSame(stage.Overlay.Root, trail.Notes.transform.parent, "the notes are a child of the overlay layer");
            Assert.AreSame(stage.Overlay.Root, trail.Guide.transform.parent);
            Assert.AreEqual(stage.Overlay.SortingOrderBase + 20, trail.Notes.GetComponent<MeshRenderer>().sortingOrder, "the notes sort at +20");
            Assert.AreEqual(stage.Overlay.SortingOrderBase + 10, trail.Guide.GetComponent<MeshRenderer>().sortingOrder, "the cells at +10");
            Assert.AreEqual(stage.Overlay.gameObject.layer, trail.Notes.gameObject.layer);
            Assert.IsTrue(trail.Notes.IsConfigured && trail.Guide.IsConfigured);
        }

        [UnityTest]
        public IEnumerator TheTrailAndTheGuide_FollowTheStripOfTheRecordScreen_AndTheTier()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var hand = new TestHand();
            var shipped = ShippedContent().trail;

            stage.Overlay.StripFraction = 0.28f;
            Settle(stage, hand, 0.5f, 0.5f, 1000.0, 1);
            var withStrip = (stage.Trail.Notes.OverlayFraction, stage.Trail.Guide.OverlayFraction);
            stage.Overlay.StripFraction = 0f;
            Settle(stage, hand, 0.5f, 0.5f, 1050.0, 1);
            var withoutStrip = (stage.Trail.Notes.OverlayFraction, stage.Trail.Guide.OverlayFraction);
            stage.ApplyQuality("Low");
            var low = stage.Trail.Notes.CountScale;
            stage.ApplyQuality("High");
            var high = stage.Trail.Notes.CountScale;

            Assert.AreEqual((0.28f, 0.28f), withStrip, "both read the strip from the overlay at every frame");
            Assert.AreEqual((0f, 0f), withoutStrip, "Tab hides the strip: the cells and the hands' place use the whole picture");
            Assert.AreEqual(shipped.lowCountScale, low, 1e-6f, "the Low tier draws trail.lowCountScale of the notes");
            Assert.AreEqual(1f, high, 1e-6f);
        }

        [UnityTest]
        public IEnumerator Trail_FollowsTheHand_SmoothlyAt6Fps_ThroughTheStage()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            stage.Overlay.StripFraction = 0f;   // so that the place of the hand on the picture is its x
            var hand = new TestHand();
            const float Speed = 0.4f;           // picture widths a second
            var places = new List<float>();
            var born = 0;

            // act: two seconds of a hand that a tracker sees six times a second, drawn at 60 frames a second, through the stage
            for (var frame = 0; frame < 120; frame++)
            {
                var picture = Mathf.Floor(frame / 10f) / 6f;
                hand.Show(0.1f + Speed * picture, 0.5f, 1000.0 + picture * 1000.0);
                stage.SetHands(hand, Frame);
                places.Add(stage.Trail.Notes.PlaceOf(0).X);
                born = System.Math.Max(born, stage.Trail.Notes.AliveCount);
            }

            // assert: the place of the trail glides: no drawn frame moves it by more than a quarter of what a tracker picture moves the hand, and none stands still
            var jump = Speed / 6f;
            var biggest = 0f;
            var smallest = float.MaxValue;
            for (var i = 61; i < places.Count; i++)
            {
                var step = places[i] - places[i - 1];
                biggest = Mathf.Max(biggest, step);
                smallest = Mathf.Min(smallest, step);
            }
            Assert.Less(biggest, 0.25f * jump, "biggest drawn step " + biggest + " against a tracker jump of " + jump);
            Assert.Greater(smallest, 0.2f * Speed * Frame, "smallest drawn step " + smallest);
            Assert.Greater(born, 0, "and the hand leaves notes");
        }

        [UnityTest]
        public IEnumerator TheTrailAndTheNameOfTheCell_AreDrawnOverTheMeadow()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            if (stage.Overlay.Viewer == null) Assert.Inconclusive("no camera");
            stage.Overlay.StripFraction = 0f;
            var before = MeadowCapture.Render(stage.Camera, 1280, 720);
            var hand = new TestHand();

            // a hand goes across the top row of the picture, from the harp's cell to the recorder's
            for (var frame = 0; frame < 75; frame++)
            {
                hand.Show(0.15f + 0.5f * Mathf.Floor(frame / 10f) / 6f / 1.2f, 0.25f, 1000.0 + Mathf.Floor(frame / 10f) * 1000.0 / 6.0);
                stage.SetHands(hand, Frame);
            }
            var after = MeadowCapture.Render(stage.Camera, 1280, 720);
            CaptureOutput.SavePicture(after, "trail-over-meadow.png");

            var changed = 0;
            var pixelsBefore = before.GetPixels32();
            var pixelsAfter = after.GetPixels32();
            for (var i = 0; i < pixelsAfter.Length; i++)
            {
                var d = Mathf.Abs(pixelsAfter[i].r - pixelsBefore[i].r) + Mathf.Abs(pixelsAfter[i].g - pixelsBefore[i].g) + Mathf.Abs(pixelsAfter[i].b - pixelsBefore[i].b);
                if (d > 90) changed++;
            }
            Object.Destroy(before);
            Object.Destroy(after);

            Assert.Greater(changed, 300, "the notes, the glowing border and the name of the cell are in the picture of the meadow camera");
        }

        [UnityTest]
        public IEnumerator AFist_LeavesNoTrail_AndLightsNoCell()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            var hand = new TestHand();
            var mostNotes = 0;
            var mostGlow = 0f;

            for (var frame = 0; frame < 90; frame++)
            {
                var picture = Mathf.Floor(frame / 10f) / 6f;
                hand.Show(0.1f + 0.8f * picture / 1.5f, 0.5f, 1000.0 + picture * 1000.0, HandPose.Fist);
                stage.SetHands(hand, Frame);
                mostNotes = System.Math.Max(mostNotes, stage.Trail.Notes.AliveCount);
                for (var cell = 0; cell < CellGuideView.Cells; cell++) mostGlow = Mathf.Max(mostGlow, stage.Trail.Guide.Glow(cell));
            }

            Assert.AreEqual(0, mostNotes, "a fist leaves no trail");
            Assert.AreEqual(0f, mostGlow, "and its cell is not lit: it plays nothing");
        }

        [UnityTest]
        public IEnumerator TheNameOfACell_ShowsWhenAHandMovesIntoIt_AndTheCellIsTheOneThatPlays()
        {
            yield return LoadAndWaitUntilRunning();
            var stage = Object.FindFirstObjectByType<MeadowStage>();
            stage.Overlay.StripFraction = 0f;
            var hand = new TestHand();
            var labels = ShippedContent().cells.labels;

            var captured = Settle(stage, hand, 0.17f, 0.25f, 1000.0, 30);          // the harp's cell
            var first = stage.Trail.Guide.LabelAlpha((int)Part.Harp);
            captured = Settle(stage, hand, 0.5f, 0.75f, captured, 30);              // the marimba's cell
            var second = stage.Trail.Guide.LabelAlpha((int)Part.Marimba);
            var litBefore = stage.Trail.Guide.Glow((int)Part.Marimba);
            Settle(stage, hand, 0.5f, 0.75f, captured, 180);                        // it stays there for 3 s
            var staying = stage.Trail.Guide.LabelAlpha((int)Part.Marimba);

            Assert.Greater(first, 0.9f, "the harp's name shows when the hand comes in");
            Assert.Greater(second, 0.9f, "and the marimba's when it moves there");
            Assert.AreEqual((int)Part.Marimba, stage.Trail.Cells.CellOf(0), "the lit cell is the one the conducting play picks");
            Assert.Greater(litBefore, 0.3f, "its border has begun to glow");
            Assert.AreEqual(0f, staying, 1e-6f, "the name does not come back while the hand stays");
            Assert.Greater(stage.Trail.Guide.Glow((int)Part.Marimba), 0.95f, "but the border goes on glowing");
            Assert.AreEqual(labels.Length, CellGuideView.Cells);
        }
    }
}
