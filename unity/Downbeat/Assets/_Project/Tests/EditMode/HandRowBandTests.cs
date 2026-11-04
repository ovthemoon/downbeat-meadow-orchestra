using System;
using System.Collections.Generic;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using static Downbeat.Tests.HandTestData;
using Pose = Downbeat.Tests.HandTestData.Pose;   // not UnityEngine.Pose

namespace Downbeat.Tests
{
    /// <summary>
    /// The dead band of the rows (<c>cells.rowHysteresis</c>): a hand keeps its row a little past the line between the rows, and everything drawn for it follows the row of the instrument that plays. A hand of the ground
    /// row whose screen point is a little over the horizon keeps its wind at the far edge of the field and its ribbon on its line of sight; a hand of the sky row a little under it stays in the air.
    /// </summary>
    public class HandRowBandTests
    {
        const float Split = 0.5f;
        const float Hysteresis = 0.03f;
        const float CameraHeight = 2f;
        const float RibbonHeight = 0.4f;
        const float ScreenTolerance = 1e-3f;
        static readonly float[] Columns = { 0.17f, 0.5f, 0.83f };

        sealed class Rig : IDisposable
        {
            readonly GameObject _cameraObject;

            public Camera Camera { get; }
            public MeadowOverlayLayer Overlay { get; }
            public HandPlacement Placement { get; }

            public Rig(float strip)
            {
                _cameraObject = new GameObject("HandRowBandTests camera");
                _cameraObject.transform.SetPositionAndRotation(new Vector3(0f, CameraHeight, 0f), Quaternion.Euler(11.6f, 0f, 0f));
                Camera = _cameraObject.AddComponent<Camera>();
                Camera.fieldOfView = 50f;
                Camera.aspect = 16f / 9f;
                Overlay = MeadowOverlayLayer.Create(Camera);
                Overlay.RowSplit = Split;
                Overlay.StripFraction = strip;
                Placement = new HandPlacement(Camera, Rolling, Overlay, new MeadowLayout(13.2f, 2.3f, 8f, 0.3f), 0.6f);
                Overlay.HorizonViewport = Placement.FarEdgeViewport();
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(_cameraObject);
        }

        // gently rolling ground between 0.4 and 0.8 m
        static float Rolling(float x, float z) => 0.6f + 0.2f * Mathf.Sin(z * 0.4f) * Mathf.Cos(x * 0.3f);

        // ---- what is drawn: the ground row ---------------------------------------------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(0.28f)]
        public void AGroundRowHandHeldAboveTheHorizonByTheDeadBand_HasItsRibbonOnItsLineOfSight_AtTheDepthOfTheFarEdge(float strip)
        {
            // Arrange
            using var rig = new Rig(strip);
            foreach (var x in Columns)
            {
                Assert.IsTrue(rig.Placement.TryGroundRowHead(x, Split, RibbonHeight, out var onTheLine), "on the line between the rows");
                var farEdgeDepth = rig.Camera.WorldToViewportPoint(onTheLine).z;

                for (var y = Split + Hysteresis; y >= Split - Hysteresis - 1e-4f; y -= 0.005f)
                {
                    // Act
                    var found = rig.Placement.TryGroundRowHead(x, y, RibbonHeight, out var head);

                    // Assert: seen where the hand is drawn, whichever side of the horizon it is
                    var (vx, vy) = rig.Overlay.ViewportOfHand(x, y);
                    var seen = rig.Camera.WorldToViewportPoint(head);
                    var name = $"strip {strip}, hand ({x}, {y:0.000})";
                    Assert.IsTrue(found, name);
                    Assert.Greater(seen.z, 0f, name + ": in front of the camera");
                    Assert.AreEqual(vx, seen.x, ScreenTolerance, name + ": across the screen where the hand is");
                    Assert.AreEqual(vy, seen.y, ScreenTolerance, name + ": and up it");
                    if (y < Split) Assert.AreEqual(farEdgeDepth, seen.z, 0.05f, name + ": above the horizon it stays as far away as the far edge of the field (no line into the sky)");
                }
            }
        }

        [Test]
        public void AGroundRowHandDriftingThroughTheDeadBand_HasARibbonThatNeverJumps()
        {
            using var rig = new Rig(0.28f);
            var previous = Vector3.zero;
            var biggestStep = 0f;

            for (var y = Split + Hysteresis; y >= Split - Hysteresis - 1e-4f; y -= 0.0025f)
            {
                Assert.IsTrue(rig.Placement.TryGroundRowHead(0.5f, y, RibbonHeight, out var head));
                if (y < Split + Hysteresis - 1e-4f) biggestStep = Mathf.Max(biggestStep, Vector3.Distance(previous, head));
                previous = head;
            }

            Assert.Less(biggestStep, 0.3f, "a step of a quarter of a hundredth of the picture moves the ribbon a few decimetres at the most, also where the hand passes the horizon");
        }

        [Test]
        public void AGroundRowHandHeldAboveTheHorizonByTheDeadBand_BlowsTheGrassAtTheFarEdgeOfTheField()
        {
            using var rig = new Rig(0.28f);
            foreach (var x in Columns)
            {
                Assert.IsTrue(rig.Placement.TryGroundOfHand(x, Split, out var farEdge));
                for (var y = Split - 0.005f; y >= Split - Hysteresis - 1e-4f; y -= 0.005f)
                {
                    Assert.IsTrue(rig.Placement.TryGroundOfHand(x, y, out var ground), $"hand ({x}, {y:0.000})");
                    Assert.AreEqual(farEdge, ground, $"hand ({x}, {y:0.000}): the ground is held at the far edge, it does not go on into the sky");
                }
            }
            Assert.IsTrue(rig.Placement.TryGroundOfHand(0.5f, Split, out var middle));
            Assert.AreEqual(8f, middle.Z, 0.8f, "which is the far end of the playing field");
        }

        // ---- what is drawn: the sky row ------------------------------------------------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(0.28f)]
        public void ASkyRowHandHeldBelowTheHorizonByTheDeadBand_StaysInTheAirOnItsLineOfSight(float strip)
        {
            using var rig = new Rig(strip);
            foreach (var x in Columns)
            {
                for (var y = Split; y <= Split + Hysteresis + 1e-4f; y += 0.005f)
                {
                    // Act
                    var (vx, vy) = rig.Overlay.ViewportOfHand(x, y);
                    var point = rig.Placement.SkyPoint(vx, vy);

                    // Assert
                    var seen = rig.Camera.WorldToViewportPoint(point);
                    var name = $"strip {strip}, hand ({x}, {y:0.000})";
                    Assert.Greater(seen.z, 0f, name + ": in front of the camera");
                    Assert.AreEqual(vx, seen.x, ScreenTolerance, name + ": across the screen where the hand is");
                    Assert.AreEqual(vy, seen.y, ScreenTolerance, name + ": and up it");
                    Assert.Greater(point.y, Rolling(point.x, point.z) + 0.25f, name + ": over the grass, not in it");
                }
            }
        }

        // ---- the row of a hand that is in no cell ---------------------------------------------------------------------------------------------

        [Test]
        public void TheRowOfAHandInNoCell_IsTheRowCellPickerGivesAHandItHasNotSeen_AndTheLineIsTheGroundRow()
        {
            using var rig = new Rig(0.28f);
            var cells = CellSettings.Default;

            for (var y = 0f; y <= 1f; y += 0.0125f)
            {
                var row = Parts.IsSky(CellPicker.Pick(0.5f, y, Part.Harp, false, 0.09f, cells));
                Assert.AreEqual(row, rig.Placement.IsSkyRow(y), $"y {y}");
            }
            Assert.IsFalse(rig.Placement.IsSkyRow(Split), "on the line is the ground row");
            Assert.IsTrue(rig.Placement.IsSkyRow(Split - 1e-3f));
        }

        // ---- the row the instrument plays in -------------------------------------------------------------------------------------------------

        [Test]
        public void TheRowOfAHand_IsTheRowOfItsCell_HeldThroughTheDeadBandInBothDirections()
        {
            // Arrange
            var run = new CellRun();
            run.Arrive(Open(0, 0.5f, 0.56f));
            var row = new List<string>();

            // Act: down the picture from the ground row to the line and past the band, then up again
            void Visit(float y)
            {
                run.Picture(Open(0, 0.5f, y));
                row.Add(run.Cells.TryGetRow(0, out var isSky) ? (isSky ? "sky" : "ground") : "none");
            }
            foreach (var y in new[] { 0.52f, 0.5f, 0.48f, 0.471f, 0.469f, 0.45f, 0.4f }) Visit(y);
            var goingUp = row.ToArray();
            row.Clear();
            foreach (var y in new[] { 0.45f, 0.5f, 0.52f, 0.529f, 0.531f, 0.6f }) Visit(y);

            // Assert
            CollectionAssert.AreEqual(new[] { "ground", "ground", "ground", "ground", "sky", "sky", "sky" }, goingUp, "up from 0.56: the ground row until it is 0.03 above the line");
            CollectionAssert.AreEqual(new[] { "sky", "sky", "sky", "sky", "ground", "ground" }, row, "and down again: the sky row until it is 0.03 under the line");
        }

        [Test]
        public void AHandInNoCell_HasNoRow()
        {
            var run = new CellRun();

            for (var i = 0; i < 4; i++) run.Picture(PoseHand(0, 0.5f, 0.52f, PalmSize, HandPose.Fist));

            Assert.IsFalse(run.Cells.TryGetRow(0, out var isSky), "a fist plays nothing");
            Assert.IsFalse(isSky);
            Assert.IsFalse(run.Cells.TryGetRow(1, out _), "a hand that is not there");
            Assert.IsFalse(run.Cells.TryGetRow(-1, out _), "and an id that does not exist");
        }

        [Test]
        public void TheSound_AndTheRowTheVisualsFollow_ChangeRowTogether_ForAHandThatDriftsThroughTheDeadBandAndOutOfIt()
        {
            // Arrange: ground row 0.56, drifting up through the dead band to 0.48 (still the ground row), out of it to 0.40 (the sky row), back down to 0.52 (still the sky row), and out of it to 0.60
            var rig = new ConductRig();
            var tracker = HandCellTracker.From(TestContent.LoadShipped());
            var keyframes = new[] { (t: 0.0, y: 0.56f), (t: 3.0, y: 0.56f), (t: 9.0, y: 0.48f), (t: 12.0, y: 0.48f), (t: 16.0, y: 0.40f), (t: 19.0, y: 0.40f), (t: 24.0, y: 0.52f), (t: 27.0, y: 0.52f), (t: 31.0, y: 0.60f), (t: 34.0, y: 0.60f) };
            Func<double, Pose[]> path = t =>
                new[] { new Pose(0, 0.5f + 0.01f * (float)Math.Sin(t * 7.0), HeightAt(keyframes, t) + 0.004f * (float)Math.Cos(t * 5.0)) };
            var rowAtEnd = new Dictionary<double, bool>();
            var noteRows = new List<(double t, bool sky, bool trackerSky, bool fading)>();
            var lastChangeAt = double.NegativeInfinity;
            var rowBeforeChange = false;
            var previousSky = false;

            // Act
            while (rig.AudioNow < ConductRig.GridOrigin - 1.0 + 34.0)
            {
                var before = rig.Notes.Count;
                rig.Run(ConductRig.AudioFrameSec, path, 1.0 / 30.0);
                tracker.Update(rig.AudioNow, rig.Motion);
                var t = rig.AudioNow - (ConductRig.GridOrigin - 1.0);
                var hasRow = tracker.TryGetRow(0, out var sky);
                if (hasRow && previousSky != sky) { lastChangeAt = t; rowBeforeChange = previousSky; }
                if (hasRow) previousSky = sky;
                foreach (var end in new[] { 3.0, 12.0, 19.0, 27.0, 34.0 })
                {
                    if (t >= end - 0.05 && t <= end && hasRow) rowAtEnd[end] = sky;
                }
                for (var i = before; i < rig.Notes.Count; i++)
                {
                    // a part of the row the hand has just left goes on for a moment while it fades out (the crossfade)
                    var fading = t - lastChangeAt < 0.6 && Parts.IsSky(rig.Notes[i].Part) == rowBeforeChange;
                    noteRows.Add((t, Parts.IsSky(rig.Notes[i].Part), sky, fading));
                }
            }

            // Assert: every note is of the row the visuals are put in, and the row changes where the dead band says
            Assert.Greater(noteRows.Count, 40, "the engine played");
            foreach (var note in noteRows) Assert.IsTrue(note.sky == note.trackerSky || note.fading, $"at {note.t:0.00} s a {(note.sky ? "sky" : "ground")} instrument plays while the visuals are in the {(note.trackerSky ? "sky" : "ground")} row");
            Assert.IsFalse(rowAtEnd[3.0], "ground at 0.56");
            Assert.IsFalse(rowAtEnd[12.0], "ground at 0.48: the sound and the visuals keep the ground row inside the dead band");
            Assert.IsTrue(rowAtEnd[19.0], "sky at 0.40");
            Assert.IsTrue(rowAtEnd[27.0], "sky at 0.52: and keep the sky row inside the dead band");
            Assert.IsFalse(rowAtEnd[34.0], "ground again at 0.60");
            Assert.IsTrue(noteRows.Exists(n => n.t > 9.0 && n.t < 12.0 && !n.sky), "the ground instrument was playing while the hand was at 0.48");
            Assert.IsTrue(noteRows.Exists(n => n.t > 24.0 && n.t < 27.0 && n.sky), "and the sky instrument while it was at 0.52");
        }

        static float HeightAt((double t, float y)[] keyframes, double t)
        {
            for (var i = 1; i < keyframes.Length; i++)
            {
                if (t > keyframes[i].t) continue;
                var along = (float)((t - keyframes[i - 1].t) / (keyframes[i].t - keyframes[i - 1].t));
                return Mathf.Lerp(keyframes[i - 1].y, keyframes[i].y, along);
            }
            return keyframes[keyframes.Length - 1].y;
        }

        static PosedHand Open(int id, float x, float y) => PoseHand(id, x, y, PalmSize, HandPose.Open);

        /// <summary>The cell tracker over the motion tracker, with pictures that come one drawn frame apart.</summary>
        sealed class CellRun
        {
            const double RenderDt = 1.0 / 60.0;
            readonly HandSettingsHolder _settings = new HandSettingsHolder();
            long _seq = 1;
            double _now;

            public CellRun()
            {
                _settings.Set(new HandSettingsDto(), null, null);
                Motion = new HandMotionTracker(_settings);
                Cells = new HandCellTracker(null, 0.03f, 0.8);
            }

            public HandMotionTracker Motion { get; }
            public HandCellTracker Cells { get; }

            public void Picture(params PosedHand[] hands)
            {
                Motion.Update(PosedFrame(_seq++, _now * 1000.0, hands));
                Cells.Update(_now, Motion);
                _now += RenderDt;
            }

            public void Arrive(params PosedHand[] hands)
            {
                for (var i = 0; i < PoseSettings.DefaultConfirmFrames; i++) Picture(hands);
            }
        }
    }
}
