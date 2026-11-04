using System.Collections;
using Downbeat.App;
using Downbeat.Controls;
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
    /// A hand is seen where it is on the screen, in the real meadow with the real camera: its ribbon of light, the grass it blows, the flowers and the waves its notes make on the ground, the cell that lights and the
    /// notes behind it all line up on one screen point, and a hand that goes up goes up on the screen. The top row of the hand is the sky above the horizon of the meadow and the bottom row is the ground below it.
    /// </summary>
    public class HandOnScreenPlayModeTests
    {
        const int SceneBuildIndex = 0;
        const float BootTimeoutSec = 40f;
        const float Frame = 1f / 60f;
        const float ScreenTolerance = 0.012f;
        const double FakeHeardNow = 3000.0;
        const double Lead = 0.1;
        const int Width = 960;
        const int Height = 540;
        const float MinGrassDifference = 0.002f;
        const float LeftColumn = 0.17f, MiddleColumn = 0.5f, RightColumn = 0.83f;
        static readonly float[] Strips = { 0.28f, 0f };   // one screen with the record strip over the lower part, or two screens

        internal static IEnumerator LoadAndWaitUntilRunning(System.Action<MeadowStage> done)
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

        /// <summary>One hand whose place and capture time the test sets (a tracker picture at a time); a new epoch starts its ribbon over.</summary>
        internal sealed class TestHand : IHandMotionView
        {
            HandMotion _hand;
            int _epoch;

            public void Show(float x, float y, double captureMs, HandPose pose = HandPose.Open) =>
                _hand = new HandMotion(0, true, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, captureMs, _epoch, pose);

            public void NewHand() => _epoch++;

            public long FrameSeq => 1;
            public int Count => 1;
            public HandMotion Hand(int index) => _hand;

            public bool TryGetById(int id, out HandMotion motion)
            {
                motion = _hand;
                return id == 0;
            }
        }

        internal sealed class NoHands : IHandMotionView
        {
            public long FrameSeq => 1;
            public int Count => 0;
            public HandMotion Hand(int index) => default;
            public bool TryGetById(int id, out HandMotion motion) { motion = default; return false; }
        }

        /// <summary>The hand stays at (x, y) for <paramref name="frames"/> drawn frames, seen by a tracker 6 times a second.</summary>
        internal static double Settle(MeadowStage stage, TestHand hand, float x, float y, double captureMs, int frames, HandPose pose = HandPose.Open)
        {
            for (var i = 0; i < frames; i++)
            {
                if (i % 10 == 0) hand.Show(x, y, captureMs + i / 10 * 1000.0 / 6.0, pose);
                stage.SetHands(hand, Frame);
            }
            return captureMs + (frames + 9) / 10 * 1000.0 / 6.0;
        }

        internal static Vector3 HeadOf(MeadowStage stage, int slot, out bool inSky)
        {
            Assert.IsTrue(stage.HandWind.TryGetHead(slot, out var head, out inSky), "the ribbon of the hand is there");
            return head;
        }

        internal static Vector3 OnGround(MeadowStage stage, FieldPoint point) => new Vector3(point.X, stage.Terrain.HeightAt(point.X, point.Z), point.Z);

        internal static ScheduledNote Note(Part part, float velocity, float x01, float y01) =>
            new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, part, 4, velocity, false, x01, y01);

        // ---- the horizon ----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheHorizon_IsTheFarEdgeOfTheFieldAsTheCameraSeesIt()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);

            var horizon = stage.Overlay.HorizonViewport;
            var farEdge = stage.Camera.WorldToViewportPoint(new Vector3(0f, stage.Terrain.HeightAt(0f, stage.Layout.FarZ), stage.Layout.FarZ));

            Assert.AreEqual(farEdge.y, horizon, 0.01f, "the rows meet where the ground of the playing field ends");
            Assert.Greater(horizon, 0.3f, "well above the bottom of the picture");
            Assert.Less(horizon, 0.7f, "and under the horizon line of the camera, so that the ground row has ground in it");
            Assert.AreEqual(CellSettings.From(stage.Bundle.Dto.cells).RowSplit, stage.Overlay.RowSplit, 1e-6f);
            Assert.IsTrue(stage.Placement.TryGround(0.5f, horizon, out var far));
            Assert.AreEqual(stage.Layout.FarZ, far.Z, 0.6f, "a hand on the line between the rows touches the far end of the field");
            Assert.IsTrue(stage.Placement.TryGround(0.5f, 0.28f, out var nearest));
            Assert.Less(nearest.Z, far.Z, "and one over the strip touches ground nearer to the camera");
        }

        // ---- the ribbon -----------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Ribbon_ProjectsToTheHandsScreenPoint()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var hand = new TestHand();
            var captureMs = 1000.0;   // the pictures of a new hand come later than the last hand's, as the tracker's do: the row of the ribbon is the row of the cell the hand is picked into from them
            var places = new[]
            {
                (x: LeftColumn, y: 0.05f), (x: MiddleColumn, y: 0.25f), (x: RightColumn, y: 0.45f),      // the top row
                (x: LeftColumn, y: 0.6f), (x: MiddleColumn, y: 0.75f), (x: RightColumn, y: 0.95f), (x: 0.02f, y: 1f),   // the bottom row
            };

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                foreach (var place in places)
                {
                    hand.NewHand();
                    hand.Show(place.x, place.y, captureMs += 1000.0);
                    stage.SetHands(hand, Frame);

                    var head = HeadOf(stage, 0, out var inSky);
                    var (vx, vy) = stage.Overlay.ViewportOfHand(place.x, place.y);
                    var seen = stage.Camera.WorldToViewportPoint(head);
                    var name = $"hand ({place.x}, {place.y}) with strip {strip}";
                    Assert.Greater(seen.z, 0f, name + ": in front of the camera");
                    Assert.AreEqual(vx, seen.x, ScreenTolerance, name + ": the ribbon is seen across the screen where the hand is");
                    Assert.AreEqual(vy, seen.y, ScreenTolerance, name + ": and up the screen where the hand is");
                    Assert.AreEqual(place.y <= CellSettings.From(stage.Bundle.Dto.cells).RowSplit, inSky, name + ": in the air for the top row, over the grass for the bottom row");
                    if (!inSky) Assert.AreEqual(stage.Terrain.HeightAt(head.x, head.z) + stage.Settings.ribbon.height, head.y, 0.25f, name + ": the ribbon floats at its height over the grass");
                }
            }
        }

        [UnityTest]
        public IEnumerator Ribbon_RaisingTheHand_MovesItUpOnScreen_NotBackwards()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);

            var hand = new TestHand();
            var captureBase = 1000.0;
            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                hand.NewHand();   // the hand of the last round has gone: a new one comes in at the bottom
                captureBase += 10000.0;   // later than the last one's pictures
                var horizon = stage.Overlay.HorizonViewport;

                // the hand is raised from the bottom of the picture to the top in two seconds, seen by a tracker that delivers thirty pictures a second, drawn at sixty frames a second
                var heights = new System.Collections.Generic.List<float>();
                var crossedAt = -1;
                var biggestStep = 0f;
                var previous = float.NaN;
                for (var frame = 0; frame <= 120; frame++)
                {
                    var picture = Mathf.Floor(frame / 2f) / 60f;   // 0 .. 1 over 120 frames, a new tracker picture every second frame
                    hand.Show(0.4f, 1f - picture, captureBase + Mathf.Floor(frame / 2f) * 1000.0 / 30.0);
                    stage.SetHands(hand, Frame);
                    var seen = stage.Camera.WorldToViewportPoint(HeadOf(stage, 0, out _));
                    if (crossedAt < 0 && seen.y > horizon) crossedAt = frame;
                    if (!float.IsNaN(previous)) biggestStep = Mathf.Max(biggestStep, Mathf.Abs(seen.y - previous));
                    previous = seen.y;
                    heights.Add(seen.y);
                }

                for (var i = 1; i < heights.Count; i++) Assert.GreaterOrEqual(heights[i], heights[i - 1] - 0.002f, $"strip {strip}, frame {i}: a hand that is raised is never drawn lower than a frame ago");
                Assert.Less(heights[0], horizon, "it starts over the grass");
                Assert.Greater(heights[heights.Count - 1], 0.85f, "and ends near the top of the screen");
                Assert.Greater(crossedAt, 0, "it crosses the horizon on its way");
                Assert.Less(biggestStep, 0.03f, $"strip {strip}: and it never jumps on the screen, also where it passes from the ground row into the sky row (no streak)");
            }
        }

        [UnityTest]
        public IEnumerator Ribbon_FollowsTheHandSmoothly_AndStartsAtTheHandThatArrives()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Overlay.StripFraction = 0f;
            const float StepX01 = 0.006f;   // a hand going 1 m/s seen by a tracker that delivers 20 pictures a second
            var hand = new TestHand();
            hand.Show(0.3f, 0.7f, 1000.0);
            stage.SetHands(hand, Frame);
            var first = stage.Camera.WorldToViewportPoint(HeadOf(stage, 0, out _));

            hand.Show(0.3f + StepX01, 0.7f, 1050.0);
            stage.SetHands(hand, Frame);
            var next = stage.Camera.WorldToViewportPoint(HeadOf(stage, 0, out _));

            Assert.AreEqual(0.3f, first.x, ScreenTolerance, "the first place of a hand is where it is");
            Assert.Greater(next.x, first.x, "then it glides toward the new place");
            Assert.Less(next.x - first.x, 0.6f * StepX01, "and does not jump the whole step in one drawn frame");
        }

        [UnityTest]
        public IEnumerator Ribbon_OfAHandThatArrives_DrawsNoLineFromWhereItWasBefore()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Overlay.StripFraction = 0.28f;
            var hand = new TestHand();
            var captureMs = 1000.0;

            foreach (var y in new[] { 0.25f, 0.8f, 0.25f })   // the sky, the ground, and the sky again: the ribbon is the same one, the hand is a new one
            {
                stage.HandsOverride = new NoHands();
                yield return null;
                yield return null;
                hand.NewHand();
                captureMs += 10000.0;   // its pictures are later than the last hand's
                hand.Show(0.17f, y, captureMs);
                stage.HandsOverride = hand;
                for (var frame = 0; frame < 20; frame++)
                {
                    // the hand moves a little to the right, seen by a tracker 30 times a second
                    if (frame % 2 == 0) hand.Show(0.17f + 0.0025f * frame, y, captureMs += 1000.0 / 30.0);
                    yield return null;
                }

                var head = HeadOf(stage, 0, out _);
                var trail = stage.HandWind.GetComponentsInChildren<TrailRenderer>()[0];
                var points = new Vector3[trail.positionCount];
                trail.GetPositions(points);
                Assert.Greater(points.Length, 1, $"y {y}: a hand that moves leaves a trail");
                foreach (var point in points) Assert.Less(Vector3.Distance(point, head), 1.5f, $"y {y}: every point of the trail is along the way the hand moved, none is where the last hand was");
            }
        }

        // ---- the grass ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BottomRowHand_BlowsTheGrassUnderItsScreenPoint()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var hand = new TestHand();
            var clock = 1000.0;

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                foreach (var place in new[] { (x: LeftColumn, y: 0.6f), (x: MiddleColumn, y: 0.8f), (x: RightColumn, y: 1f) })
                {
                    hand.NewHand();
                    clock = Settle(stage, hand, place.x, place.y, clock, 30);

                    var wind = stage.WindHand(0);
                    Assert.IsTrue(wind.active, $"({place.x}, {place.y}) blows");
                    var seen = stage.Camera.WorldToViewportPoint(OnGround(stage, new FieldPoint(wind.x, wind.z)));
                    var (vx, vy) = stage.Overlay.ViewportOfHand(place.x, place.y);
                    Assert.AreEqual(vx, seen.x, ScreenTolerance, $"strip {strip}, ({place.x}, {place.y}): the grass that is blown is seen across the screen where the hand is");
                    Assert.AreEqual(vy, seen.y, ScreenTolerance, "and up the screen where the hand is");
                }
            }
        }

        [UnityTest]
        public IEnumerator TopRowHand_PushesNoGrass()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Overlay.StripFraction = 0f;
            stage.Backdrop.SetSunset(SkyArc.MidT);
            var hand = new TestHand();

            Settle(stage, hand, MiddleColumn, 0.2f, 1000.0, 30);
            var inTheSky = stage.WindHand(0);
            stage.UploadWind();
            stage.SetHands(new NoHands(), Frame);
            var empty = MeadowCapture.Render(stage.Camera, Width, Height);
            Settle(stage, hand, MiddleColumn, 0.2f, 2000.0, 30);
            stage.UploadWind();
            var withTheHandInTheSky = MeadowCapture.Render(stage.Camera, Width, Height);
            Settle(stage, hand, MiddleColumn, 0.75f, 3000.0, 30);
            var overTheGround = stage.WindHand(0);
            stage.UploadWind();
            var withTheHandOnTheGround = MeadowCapture.Render(stage.Camera, Width, Height);

            var skyRow = GrassDifference(empty, withTheHandInTheSky);
            var groundRow = GrassDifference(empty, withTheHandOnTheGround);
            Object.Destroy(empty);
            Object.Destroy(withTheHandInTheSky);
            Object.Destroy(withTheHandOnTheGround);
            TestContext.Out.WriteLine($"grass difference with the hand in the sky {skyRow:0.00000}, with the hand on the ground {groundRow:0.00000} (mean per channel, ground half of the picture)");
            Assert.IsFalse(inTheSky.active, "a hand of the top row blows no grass: the grass only sways in the breeze");
            Assert.IsTrue(overTheGround.active, "a hand of the bottom row does");
            Assert.Less(skyRow, MinGrassDifference * 0.25f, "and the picture of the grass is the same with a hand in the sky as with none");
            Assert.Greater(groundRow, MinGrassDifference, "while a hand over the ground bends it");
        }

        // The mean difference of two pictures in the ground half of the screen (under the horizon, where the grass is).
        static float GrassDifference(Texture2D a, Texture2D b)
        {
            var lower = a.height / 2 - a.height / 12;   // a little under the horizon, clear of the ribbon in the sky
            var pa = a.GetPixels(0, 0, a.width, lower);
            var pb = b.GetPixels(0, 0, b.width, lower);
            var sum = 0f;
            for (var i = 0; i < pa.Length; i++) sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (3f * pa.Length);
        }

        // ---- the notes on the ground ----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator GroundNote_EffectsStartUnderTheHand()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                foreach (var c in new[] { (part: Part.LowString, x: LeftColumn, y: 0.9f), (part: Part.Marimba, x: MiddleColumn, y: 0.65f), (part: Part.HandDrum, x: RightColumn, y: 0.8f) })
                {
                    stage.CancelNotesHeardAfter(FakeHeardNow - 1.0, FakeHeardNow);   // what the last round asked for is taken back
                    Assert.IsTrue(stage.Placement.TryGroundOfHand(c.x, c.y, out var under));

                    stage.OnNote(Note(c.part, 1f, c.x, c.y), FakeHeardNow);

                    // the wave of the note starts at the spot under the hand
                    var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
                    var count = stage.Wind.CopyActive(MeadowStage.WindTime + Lead, scratch);
                    var found = false;
                    for (var i = 0; i < count; i++)
                    {
                        if (!Mathf.Approximately(scratch[i].OriginX, under.X) || !Mathf.Approximately(scratch[i].OriginZ, under.Z)) continue;
                        found = true;
                        var seen = stage.Camera.WorldToViewportPoint(OnGround(stage, new FieldPoint(scratch[i].OriginX, scratch[i].OriginZ)));
                        var (vx, vy) = stage.Overlay.ViewportOfHand(c.x, c.y);
                        Assert.AreEqual(vx, seen.x, ScreenTolerance, $"{c.part} at strip {strip}: the wave starts where the hand is on the screen, across");
                        Assert.AreEqual(vy, seen.y, ScreenTolerance, $"{c.part} at strip {strip}: and up and down");
                    }
                    Assert.IsTrue(found, $"{c.part}: a wave starts at the ground under the hand ({under.X}, {under.Z})");

                    // the flowers of the column that open are around the same spot (not at the depth of the pitch step, which is far from here for a low hand)
                    var blooms = stage.Flowers.BloomsNotYetHeard(0.0);
                    Assert.Greater(blooms.Count, 0, $"{c.part}: flowers open");
                    float sumX = 0f, sumZ = 0f;
                    foreach (var (flower, _) in blooms)
                    {
                        sumX += stage.Flowers.Instances[flower].X;
                        sumZ += stage.Flowers.Instances[flower].Z;
                    }
                    Assert.AreEqual(under.X, sumX / blooms.Count, 1.6f, $"{c.part}: the flowers that open are around the hand, across the field");
                    Assert.AreEqual(under.Z, sumZ / blooms.Count, 1.6f, $"{c.part}: and in depth");
                }
            }
        }

        [UnityTest]
        public IEnumerator GroundNote_ThrowsItsPetalsFromUnderTheHand()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Overlay.StripFraction = 0.28f;
            Assert.IsTrue(stage.Placement.TryGroundOfHand(0.6f, 0.9f, out var under));

            stage.OnNote(Note(Part.Marimba, 1f, 0.6f, 0.9f), FakeHeardNow);

            var cues = stage.Particles.PendingCues();
            Assert.AreEqual(1, cues.Count, "the marimba throws petals");
            Assert.AreEqual(under.X, cues[0].cue.FieldPos.X, 1e-4f);
            Assert.AreEqual(under.Z, cues[0].cue.FieldPos.Z, 1e-4f, "from the ground under the hand");
        }

        [UnityTest]
        public IEnumerator GroundNote_WithNoHandPlace_IsWhereThePitchStepPutsIt()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var note = new ScheduledNote(1, FakeHeardNow + Lead, FakeHeardNow, Part.Marimba, 7, 1f, false);   // no x, no y: a key

            stage.OnNote(note, FakeHeardNow);

            var cues = stage.Particles.PendingCues();
            Assert.AreEqual(1, cues.Count);
            Assert.AreEqual(stage.Layout.DepthForPitchStep(7, stage.Bundle.Dto.song.pitchSteps), cues[0].cue.FieldPos.Z, 1e-3f, "the way it always was");
        }

        // ---- the lit cell and the trail -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CellGuide_LinesUpWithTheRibbon()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var hand = new TestHand();
            var names = new[] { Part.Harp, Part.Recorder, Part.Bell, Part.LowString, Part.Marimba, Part.HandDrum };
            var cells = new[]
            {
                (x: LeftColumn, y: 0.25f), (x: MiddleColumn, y: 0.25f), (x: RightColumn, y: 0.25f),
                (x: LeftColumn, y: 0.75f), (x: MiddleColumn, y: 0.75f), (x: RightColumn, y: 0.75f),
            };
            var clock = 1000.0;

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                for (var i = 0; i < cells.Length; i++)
                {
                    hand.NewHand();
                    clock = Settle(stage, hand, cells[i].x, cells[i].y, clock, 40);

                    var cell = stage.Trail.Cells.CellOf(0);
                    var rect = CellGuideLayout.Cell(cell, strip, stage.Overlay.HorizonViewport);
                    var ribbon = stage.Camera.WorldToViewportPoint(HeadOf(stage, 0, out _));
                    var trail = stage.Trail.Notes.PlaceOf(0);
                    var name = $"strip {strip}, cell {i}";
                    Assert.AreEqual((int)names[i], cell, name + ": the cell the hand plays");
                    Assert.Greater(stage.Trail.Guide.Glow(cell), 0.3f, name + ": is lit");
                    Assert.That(ribbon.x, Is.InRange(rect.XMin - ScreenTolerance, rect.XMax + ScreenTolerance), name + ": the ribbon is inside the lit cell across");
                    Assert.That(ribbon.y, Is.InRange(rect.YMin - ScreenTolerance, rect.YMax + ScreenTolerance), name + ": and up and down");
                    Assert.AreEqual(ribbon.x, trail.X, ScreenTolerance, name + ": the notes of the trail are born where the ribbon is");
                    Assert.AreEqual(ribbon.y, trail.Y, ScreenTolerance, name + ": (the same screen point)");
                }
            }
        }

        [UnityTest]
        public IEnumerator TheMouse_PicksTheCellUnderThePointer_ByTheHorizonOfTheMeadow()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            var scope = Object.FindFirstObjectByType<DownbeatLifetimeScope>();
            var pointer = scope.Container.Resolve<DebugPointerInput>();

            Assert.AreSame(stage, pointer.Screen, "the mouse is told where the horizon of the meadow is");
            var view = new RectF(0f, 0f, 1600f, 900f);
            var horizon = stage.Overlay.HorizonViewport;
            var above = PointerMapping.PartAt(view, 800f, (horizon + 0.03f) * 900f, horizon);
            var below = PointerMapping.PartAt(view, 800f, (horizon - 0.03f) * 900f, horizon);
            Assert.AreEqual(Part.Recorder, above, "above the horizon is the sky row");
            Assert.AreEqual(Part.Marimba, below, "below it the ground row");
        }

        [UnityTest]
        public IEnumerator ANoteFromTheMouse_ComesOutUnderThePointer()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            stage.Overlay.StripFraction = 0.28f;
            var view = new RectF(0f, 0f, 1600f, 900f);

            foreach (var (pixelX, pixelY) in new[] { (400f, 600f), (800f, 330f), (1300f, 450f), (300f, 300f) })
            {
                var x01 = PointerMapping.X01(view, pixelX);
                var y01 = PointerMapping.HandY01(view, pixelX, pixelY, stage.Overlay.StripFraction, stage.Overlay.RowSplit, stage.Overlay.HorizonViewport);
                var (vx, vy) = stage.Overlay.ViewportOfHand(x01, y01);
                Assert.AreEqual(pixelX / 1600f, vx, 1e-4f);
                Assert.AreEqual(Mathf.Max(pixelY / 900f, 0.28f), vy, 1e-3f, $"a click at ({pixelX}, {pixelY}) is drawn under the pointer (not under the record strip)");
            }
        }
    }
}
