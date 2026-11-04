using Downbeat.Controls;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class PointerMappingTests
    {
        static readonly RectF Stage = new RectF(0f, 0f, 900f, 450f);

        [Test]
        public void Horizontal_thirds_pick_the_column_in_both_rows()
        {
            foreach (var (y, row) in new[] { (340f, 0), (110f, 1) })
            {
                Assert.IsTrue(PointerMapping.TryMap(Stage, 100f, y, 10, out var left, out _));
                Assert.IsTrue(PointerMapping.TryMap(Stage, 450f, y, 10, out var middle, out _));
                Assert.IsTrue(PointerMapping.TryMap(Stage, 800f, y, 10, out var right, out _));
                Assert.AreEqual(Parts.At(row, 0), left);
                Assert.AreEqual(Parts.At(row, 1), middle);
                Assert.AreEqual(Parts.At(row, 2), right);
            }
        }

        [Test]
        public void Pointer_TopHalfIsSkyRow()
        {
            Assert.IsTrue(PointerMapping.TryMap(Stage, 100f, 440f, 10, out var top, out _));
            Assert.IsTrue(PointerMapping.TryMap(Stage, 100f, 226f, 10, out var justAbove, out _));
            Assert.IsTrue(PointerMapping.TryMap(Stage, 100f, 224f, 10, out var justBelow, out _));
            Assert.IsTrue(PointerMapping.TryMap(Stage, 100f, 10f, 10, out var bottom, out _));
            Assert.AreEqual(Part.Harp, top, "the top half is the sky row: harp, recorder, bell");
            Assert.AreEqual(Part.Harp, justAbove);
            Assert.AreEqual(Part.LowString, justBelow, "the bottom half is the ground row: low string, marimba, hand drum");
            Assert.AreEqual(Part.LowString, bottom);
            Assert.IsTrue(Parts.IsSky(top) && !Parts.IsSky(bottom));
        }

        [Test]
        public void TheRowDoesNotMoveThePitch_HeightStillPicksIt()
        {
            PointerMapping.TryMap(Stage, 450f, 449f, 10, out var topPart, out var topStep);
            PointerMapping.TryMap(Stage, 450f, 1f, 10, out var bottomPart, out var bottomStep);

            Assert.AreEqual(Part.Recorder, topPart);
            Assert.AreEqual(Part.Marimba, bottomPart);
            Assert.AreEqual(9, topStep);
            Assert.AreEqual(0, bottomStep);
        }

        [Test]
        public void TheNoteKnowsWhereUpAndDownTheViewItWasPlayed()
        {
            Assert.AreEqual(0f, PointerMapping.Y01(Stage, 450f), 1e-6f, "the top of the view is 0 (the way a hand's y is counted)");
            Assert.AreEqual(1f, PointerMapping.Y01(Stage, 0f), 1e-6f);
            Assert.AreEqual(0.5f, PointerMapping.Y01(Stage, 225f), 1e-6f);
            Assert.AreEqual(0.25f, PointerMapping.X01(Stage, 225f), 1e-6f);
            Assert.IsTrue(float.IsNaN(PointerMapping.Y01(new RectF(0, 0, 0, 0), 10f)));
        }

        [Test]
        public void TheRowLineIsTheHorizonOfTheMeadow_NotTheMiddleOfTheView()
        {
            const float Horizon = 0.4f;   // 40% up the view

            Assert.IsTrue(PointerMapping.TryMap(Stage, 450f, 0.41f * 450f, 10, Horizon, out var justAbove, out _));
            Assert.IsTrue(PointerMapping.TryMap(Stage, 450f, 0.39f * 450f, 10, Horizon, out var justBelow, out _));

            Assert.AreEqual(Part.Recorder, justAbove, "above the horizon is the sky row, though it is below the middle of the view");
            Assert.AreEqual(Part.Marimba, justBelow);
            Assert.AreEqual(Part.Marimba, PointerMapping.PartAt(Stage, 450f, 0.45f * 450f), "without a horizon the middle of the view is the line");
        }

        [Test]
        public void ThePointerIsMappedBackToTheHandThatWouldBeDrawnUnderIt()
        {
            const float Strip = 0.28f;
            const float Split = 0.5f;
            const float Horizon = 0.5f;

            foreach (var pixelY in new[] { 450f, 400f, 260f, 226f, 224f, 180f, 130f })
            {
                var x01 = PointerMapping.X01(Stage, 300f);
                var y01 = PointerMapping.HandY01(Stage, 300f, pixelY, Strip, Split, Horizon);
                var drawn = HandScreenMap.Viewport(x01, y01, Strip, Split, Horizon);

                Assert.AreEqual(300f / 900f, drawn.vx, 1e-5f);
                Assert.AreEqual(pixelY / 450f, drawn.vy, 1e-4f, $"a hand with the y of a click at {pixelY} is drawn at that click");
            }
        }

        [Test]
        public void AClickOverTheRecordStrip_IsTheBottomOfTheHandsRange()
        {
            Assert.AreEqual(1f, PointerMapping.HandY01(Stage, 100f, 20f, 0.28f, 0.5f, 0.5f), 1e-6f);
            Assert.IsTrue(float.IsNaN(PointerMapping.HandY01(new RectF(0, 0, 0, 0), 0f, 0f, 0.28f, 0.5f, 0.5f)));
        }

        [Test]
        public void Height_picks_the_pitch_with_the_top_being_the_highest_and_margins_clamped()
        {
            PointerMapping.TryMap(Stage, 450f, 449f, 10, out _, out var top);
            PointerMapping.TryMap(Stage, 450f, 1f, 10, out _, out var bottom);
            PointerMapping.TryMap(Stage, 450f, 225f, 10, out _, out var middle);
            Assert.AreEqual(9, top);
            Assert.AreEqual(0, bottom);
            Assert.AreEqual(5, middle);
        }

        [Test]
        public void Pitch_never_decreases_as_the_pointer_moves_up()
        {
            var previous = -1;
            for (var y = 0f; y <= 450f; y += 3f)
            {
                PointerMapping.TryMap(Stage, 450f, y, 10, out _, out var step);
                Assert.GreaterOrEqual(step, previous);
                previous = step;
            }
        }

        [Test]
        public void Clicks_outside_the_stage_view_are_ignored()
        {
            Assert.IsFalse(PointerMapping.TryMap(Stage, 901f, 100f, 10, out _, out _));
            Assert.IsFalse(PointerMapping.TryMap(Stage, 100f, -1f, 10, out _, out _));
            Assert.IsFalse(PointerMapping.TryMap(new RectF(0, 0, 0, 0), 0f, 0f, 10, out _, out _));
        }

        [Test]
        public void A_stage_that_does_not_start_at_the_origin_is_handled()
        {
            var offset = new RectF(100f, 50f, 600f, 300f);
            Assert.IsTrue(PointerMapping.TryMap(offset, 650f, 200f, 10, out var part, out _));
            Assert.AreEqual(Part.Bell, part, "the right third, and 200 is over the middle of a stage that begins at 50 and is 300 tall: the upper row");
            Assert.IsFalse(PointerMapping.TryMap(offset, 50f, 200f, 10, out _, out _));
        }

        [Test]
        public void PartAt_clamps_outside_positions()
        {
            Assert.AreEqual(Part.Harp, PointerMapping.PartAt(Stage, -50f, 2000f));
            Assert.AreEqual(Part.HandDrum, PointerMapping.PartAt(Stage, 2000f, -50f));
        }
    }
}
