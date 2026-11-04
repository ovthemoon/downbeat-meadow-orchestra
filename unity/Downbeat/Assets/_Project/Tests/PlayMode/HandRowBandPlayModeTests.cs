using System.Collections;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Downbeat.Tests.HandOnScreenPlayModeTests;

namespace Downbeat.Tests
{
    /// <summary>
    /// The dead band of the rows in the real meadow (<c>cells.rowHysteresis</c>, 0.03 either side of the line at 0.5): a hand keeps its row a little past the line, and the ribbon, the grass it blows, the lit cell and the
    /// notes follow the row of the instrument that plays (the cell of <see cref="HandCellTracker"/>, which the conducting play agrees with, see HandRowBandTests), not the height of the drawn ribbon. The ribbon is
    /// drawn where the hand is on the screen all the while. Well past the band everything changes row together.
    /// </summary>
    public class HandRowBandPlayModeTests
    {
        const float ScreenTolerance = 0.012f;
        const int SettleFrames = 90;          // a second and a half: the ribbon's smoothing has caught up with the hand
        const float Split = 0.5f;
        const float Hysteresis = 0.03f;   // cells.rowHysteresis of content.json
        const float Middle = 0.5f;
        const double FakeHeardNow = 3000.0;
        const double Lead = 0.1;
        static readonly float[] Strips = { 0.28f, 0f };   // one screen with the record strip over the lower part, or two screens

        static void AssertTheRowsAreTheOnesTheTestsAreWrittenFor(MeadowStage stage)
        {
            Assert.AreEqual(Split, stage.Bundle.Dto.cells.rowSplit, 1e-6f, "cells.rowSplit");
            Assert.AreEqual(Hysteresis, stage.Bundle.Dto.cells.rowHysteresis, 1e-6f, "cells.rowHysteresis");
        }

        /// <summary>What the visitor sees and the guide lights for the hand in slot 0, with the part of the cell it is in.</summary>
        static void AssertHandIsInRow(MeadowStage stage, float y, bool sky, Part expectedPart, string name)
        {
            var cell = stage.Trail.Cells.CellOf(0);
            Assert.AreEqual((int)expectedPart, cell, name + ": the cell the hand plays");
            Assert.Greater(stage.Trail.Guide.Glow(cell), 0.3f, name + ": and it is lit");

            var head = HeadOf(stage, 0, out var inSky);
            Assert.AreEqual(sky, inSky, name + ": the ribbon is in the " + (sky ? "air" : "grass") + " row of that cell");
            var (vx, vy) = stage.Overlay.ViewportOfHand(Middle, y);
            var seen = stage.Camera.WorldToViewportPoint(head);
            Assert.Greater(seen.z, 0f, name + ": in front of the camera");
            Assert.AreEqual(vx, seen.x, ScreenTolerance, name + ": the ribbon is where the hand is on the screen, across");
            Assert.AreEqual(vy, seen.y, ScreenTolerance, name + ": and up and down (HandScreenMap.Viewport of the hand)");

            var wind = stage.WindHand(0);
            Assert.AreEqual(!sky, wind.active, name + (sky ? ": a hand of the sky row pushes no grass" : ": a hand of the ground row keeps blowing"));
            if (sky)
            {
                Assert.Greater(head.y, stage.Terrain.HeightAt(head.x, head.z) + 0.2f, name + ": and its ribbon is in the air");
                return;
            }
            // the grass it blows is under the screen point, held at the far edge of the field while the screen point is over the horizon
            var windSeen = stage.Camera.WorldToViewportPoint(OnGround(stage, new FieldPoint(wind.x, wind.z)));
            var (_, groundVy) = stage.Overlay.ViewportOfHand(Middle, Mathf.Max(y, Split));
            Assert.AreEqual(vx, windSeen.x, ScreenTolerance, name + ": the grass that is blown is across the screen where the hand is");
            Assert.AreEqual(groundVy, windSeen.y, ScreenTolerance, name + ": and at the ground the hand is over, or at the far edge of the field where it is a little over the horizon");
        }

        [UnityTest]
        public IEnumerator GroundRowHand_DriftingFrom052To048_KeepsItsWindAndStaysLitInTheGroundRow()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            AssertTheRowsAreTheOnesTheTestsAreWrittenFor(stage);
            var hand = new TestHand();
            var clock = 1000.0;

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                hand.NewHand();
                foreach (var y in new[] { 0.52f, 0.51f, 0.5f, 0.49f, 0.48f })   // 0.49 and 0.48 are over the horizon
                {
                    clock = Settle(stage, hand, Middle, y, clock, SettleFrames);

                    AssertHandIsInRow(stage, y, false, Part.Marimba, $"strip {strip}, y {y}");
                }
            }
        }

        [UnityTest]
        public IEnumerator SkyRowHand_DriftingFrom048To052_PushesNoGrassAndStaysInTheAir()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            AssertTheRowsAreTheOnesTheTestsAreWrittenFor(stage);
            var hand = new TestHand();
            var clock = 1000.0;

            foreach (var strip in Strips)
            {
                stage.Overlay.StripFraction = strip;
                hand.NewHand();
                foreach (var y in new[] { 0.48f, 0.49f, 0.5f, 0.51f, 0.52f })   // 0.51 and 0.52 are under the horizon
                {
                    clock = Settle(stage, hand, Middle, y, clock, SettleFrames);

                    AssertHandIsInRow(stage, y, true, Part.Recorder, $"strip {strip}, y {y}");
                }
            }
        }

        [UnityTest]
        public IEnumerator CrossingWellPastTheBand_SwitchesTheRibbonTheWindAndTheCellTogether_AndTheBandHoldsTheRowEitherWay()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            AssertTheRowsAreTheOnesTheTestsAreWrittenFor(stage);
            stage.Overlay.StripFraction = 0.28f;
            var hand = new TestHand();
            hand.NewHand();
            var clock = 1000.0;
            // up from the ground row: it keeps its row down to 0.47 (the line less the band), and is in the sky row above that; down again it keeps the sky row up to 0.53
            var path = new[]
            {
                (y: 0.6f, sky: false), (y: 0.5f, sky: false), (y: 0.48f, sky: false), (y: 0.46f, sky: true), (y: 0.4f, sky: true),
                (y: 0.5f, sky: true), (y: 0.52f, sky: true), (y: 0.54f, sky: false), (y: 0.6f, sky: false),
            };

            foreach (var step in path)
            {
                clock = Settle(stage, hand, Middle, step.y, clock, SettleFrames);

                AssertHandIsInRow(stage, step.y, step.sky, step.sky ? Part.Recorder : Part.Marimba, $"y {step.y}");
            }
        }

        [UnityTest]
        public IEnumerator NotesInTheBand_FollowTheirOwnPartRow_NotTheHeightOfTheHand()
        {
            MeadowStage stage = null;
            yield return LoadAndWaitUntilRunning(m => stage = m);
            AssertTheRowsAreTheOnesTheTestsAreWrittenFor(stage);
            stage.Overlay.StripFraction = 0.28f;
            Assert.IsTrue(stage.Placement.TryGroundOfHand(Middle, Split, out var farEdge));

            // a marimba note struck while the hand is a little over the horizon (the ground row, held by the band): flowers open and a wave starts at the far edge of the field, as the hand's wind does
            stage.OnNote(Note(Part.Marimba, 1f, Middle, 0.48f), FakeHeardNow);
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            var count = stage.Wind.CopyActive(MeadowStage.WindTime + Lead, scratch);
            var wavesAtTheFarEdge = 0;
            for (var i = 0; i < count; i++)
            {
                if (Mathf.Approximately(scratch[i].OriginX, farEdge.X) && Mathf.Approximately(scratch[i].OriginZ, farEdge.Z)) wavesAtTheFarEdge++;
            }
            var blooms = stage.Flowers.BloomsNotYetHeard(0.0).Count;
            Assert.AreEqual(1, wavesAtTheFarEdge, "the marimba's ring starts on the line between the rows: it does not go into the sky");
            Assert.Greater(blooms, 0, "and flowers open around it");

            // a harp note struck while the hand is a little under it (the sky row, held by the band): it is an effect in the air; the ground does nothing
            stage.CancelNotesHeardAfter(FakeHeardNow - 1.0, FakeHeardNow);
            var wavesBefore = stage.Wind.CopyActive(MeadowStage.WindTime + Lead, scratch);
            var skyBefore = stage.Particles.PendingSkyCount;
            stage.OnNote(Note(Part.Harp, 1f, Middle, 0.52f), FakeHeardNow);
            Assert.AreEqual(wavesBefore, stage.Wind.CopyActive(MeadowStage.WindTime + Lead, scratch), "the harp pushes no grass");
            Assert.AreEqual(0, stage.Flowers.BloomsNotYetHeard(0.0).Count, "and opens no flowers");
            Assert.AreEqual(skyBefore + 1, stage.Particles.PendingSkyCount, "it makes its effect in the air");
            var (vx, vy) = stage.Overlay.ViewportOfHand(Middle, 0.52f);
            var effect = stage.Camera.WorldToViewportPoint(stage.SkyPoint(vx, vy));
            Assert.AreEqual(vx, effect.x, ScreenTolerance, "where the hand is on the screen, across");
            Assert.AreEqual(vy, effect.y, ScreenTolerance, "and up and down, a little under the horizon");
        }
    }
}
