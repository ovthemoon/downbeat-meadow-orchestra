using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The six cells: the parts and their rows and columns, the legacy names, which cell a hand is in and how steadily.</summary>
    public class CellPickerTests
    {
        const float Margin = 0.09f;   // hand.partHysteresis 0.03 of the width, in columns
        static readonly CellSettings Cells = CellSettings.Default;

        // ---- Parts ---------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Parts_RowAndColumn_RoundTrip()
        {
            Assert.AreEqual(6, Parts.Count);
            for (var row = 0; row < Parts.Rows; row++)
            {
                for (var column = 0; column < Parts.Columns; column++)
                {
                    var part = Parts.At(row, column);
                    Assert.AreEqual(row * 3 + column, (int)part, "the value is row * 3 + column");
                    Assert.AreEqual(row, Parts.Row(part));
                    Assert.AreEqual(column, Parts.Column(part));
                    Assert.AreEqual(row == 0, Parts.IsSky(part), "the upper row is the sky");
                }
            }
            Assert.AreEqual(Part.Harp, Parts.At(0, 0));
            Assert.AreEqual(Part.HandDrum, Parts.At(1, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Parts.At(2, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Parts.At(0, -1));
        }

        [Test]
        public void Parts_NamesRoundTrip()
        {
            foreach (Part part in System.Enum.GetValues(typeof(Part)))
            {
                Assert.IsTrue(Parts.TryParse(Parts.Name(part), out var back), Parts.Name(part));
                Assert.AreEqual(part, back);
            }
            CollectionAssert.AreEqual(new[] { "harp", "recorder", "bell", "lowstring", "marimba", "handdrum" }, new[] { Part.Harp, Part.Recorder, Part.Bell, Part.LowString, Part.Marimba, Part.HandDrum }.Select(Parts.Name));
            Assert.IsFalse(Parts.TryParse("brass", out _));
            Assert.IsFalse(Parts.TryParse(null, out _));
        }

        [Test]
        public void Parts_TryParse_ReadsLegacyNames()
        {
            Assert.IsTrue(Parts.TryParse("strings", out var strings));
            Assert.IsTrue(Parts.TryParse("winds", out var winds));
            Assert.IsTrue(Parts.TryParse("perc", out var perc));
            Assert.AreEqual(Part.Harp, strings);
            Assert.AreEqual(Part.Recorder, winds);
            Assert.AreEqual(Part.Marimba, perc);
        }

        // ---- the cells -----------------------------------------------------------------------------------------------------------------------

        [TestCase(0.17f, 0.25f, Part.Harp)]
        [TestCase(0.50f, 0.25f, Part.Recorder)]
        [TestCase(0.83f, 0.25f, Part.Bell)]
        [TestCase(0.17f, 0.75f, Part.LowString)]
        [TestCase(0.50f, 0.75f, Part.Marimba)]
        [TestCase(0.83f, 0.75f, Part.HandDrum)]
        public void CellPicker_PicksSixCells_AtCellCentres(float x, float y, Part expected)
        {
            Assert.AreEqual(expected, CellPicker.Pick(x, y, Part.Harp, false, Margin, Cells));
            Assert.AreEqual(expected, CellPicker.Pick(x, y, expected, true, Margin, Cells), "and a hand already there stays");
        }

        [TestCase(0f, 0f, Part.Harp)]
        [TestCase(1f, 0f, Part.Bell)]
        [TestCase(0f, 1f, Part.LowString)]
        [TestCase(1f, 1f, Part.HandDrum)]
        [TestCase(-3f, -3f, Part.Harp)]
        [TestCase(9f, 9f, Part.HandDrum)]
        [TestCase(float.NaN, float.NaN, Part.Harp)]
        public void CellPicker_TheCornersAndWhatIsOutsideThePicture_AreTheNearestCell(float x, float y, Part expected)
        {
            Assert.AreEqual(expected, CellPicker.Pick(x, y, Part.Harp, false, Margin, Cells));
        }

        [TestCase(1f / 3f, 0.25f)]
        [TestCase(2f / 3f, 0.25f)]
        [TestCase(1f / 3f, 0.75f)]
        [TestCase(0.17f, 0.5f)]
        [TestCase(0.83f, 0.5f)]
        [TestCase(1f / 3f, 0.5f)]
        public void CellPicker_HandTremblingOnABoundary_KeepsItsCell(float x, float y)
        {
            foreach (var startShift in new[] { -0.05f, 0.05f })
            {
                var cell = Part.Harp;
                var has = false;
                // arrive from one side or the other, then tremble +-0.01 across the line (both axes at once where both are on a line)
                var arrivalX = IsOnAColumnEdge(x) ? x + startShift * 0.2f : x;
                var arrivalY = IsOnTheRowLine(y) ? y + startShift : y;
                cell = CellPicker.Pick(arrivalX, arrivalY, cell, has, Margin, Cells);
                has = true;
                var first = cell;
                for (var i = 0; i < 200; i++)
                {
                    var dx = IsOnAColumnEdge(x) ? 0.01f * (i % 2 == 0 ? 1f : -1f) : 0f;
                    var dy = IsOnTheRowLine(y) ? 0.01f * (i % 3 == 0 ? 1f : -1f) : 0f;
                    cell = CellPicker.Pick(x + dx, y + dy, cell, has, Margin, Cells);
                    Assert.AreEqual(first, cell, $"step {i}: a tremble of 0.01 across a line does not change the instrument (arrived from {startShift:+0.00;-0.00})");
                }
            }
        }

        static bool IsOnAColumnEdge(float x) => System.Math.Abs(x - 1f / 3f) < 1e-4f || System.Math.Abs(x - 2f / 3f) < 1e-4f;

        static bool IsOnTheRowLine(float y) => System.Math.Abs(y - CellSettings.DefaultRowSplit) < 1e-4f;

        [Test]
        public void CellPicker_AHandThatMovesPastTheDeadBand_ChangesCell_ThenStaysInTheNewOne()
        {
            var inSky = CellPicker.Pick(0.17f, 0.49f, Part.Harp, false, Margin, Cells);
            var stillSky = CellPicker.Pick(0.17f, 0.52f, inSky, true, Margin, Cells);
            var ground = CellPicker.Pick(0.17f, 0.54f, stillSky, true, Margin, Cells);
            var stillGround = CellPicker.Pick(0.17f, 0.48f, ground, true, Margin, Cells);
            var backInSky = CellPicker.Pick(0.17f, 0.46f, stillGround, true, Margin, Cells);

            Assert.AreEqual(Part.Harp, inSky);
            Assert.AreEqual(Part.Harp, stillSky, "0.52 is past the line but within the dead band (0.03)");
            Assert.AreEqual(Part.LowString, ground, "0.54 is past it");
            Assert.AreEqual(Part.LowString, stillGround, "and back to 0.48 is within the band again");
            Assert.AreEqual(Part.Harp, backInSky, "0.46 is past it the other way");
        }

        [Test]
        public void CellPicker_TheRowSplitAndTheDeadBandComeFromTheCells()
        {
            var low = CellSettings.From(new CellsDto { rowSplit = 0.4f, rowHysteresis = 0f });

            Assert.AreEqual(Part.Recorder, CellPicker.Pick(0.5f, 0.39f, Part.Recorder, false, Margin, low));
            Assert.AreEqual(Part.Marimba, CellPicker.Pick(0.5f, 0.41f, Part.Recorder, true, Margin, low), "no dead band: the line is the line");
        }

        [Test]
        public void CellPicker_TheColumnMargin_IsInColumns_AndComesFromThePartHysteresis()
        {
            Assert.AreEqual(0.09f, CellPicker.ColumnMarginFor(0.03f), 1e-6f);
            var inSecond = CellPicker.Pick(0.4f, 0.25f, Part.Recorder, true, CellPicker.ColumnMarginFor(0.03f), Cells);
            var pastTheEdge = CellPicker.Pick(0.31f, 0.25f, Part.Recorder, true, CellPicker.ColumnMarginFor(0.03f), Cells);
            var farPast = CellPicker.Pick(0.2f, 0.25f, Part.Recorder, true, CellPicker.ColumnMarginFor(0.03f), Cells);

            Assert.AreEqual(Part.Recorder, inSecond);
            Assert.AreEqual(Part.Recorder, pastTheEdge, "0.31 is 0.023 of the width past the edge at 0.333: within 0.03");
            Assert.AreEqual(Part.Harp, farPast);
        }

        [Test]
        public void CellSettings_AListThatIsNotSixLong_IsReplacedByTheDefaultOne()
        {
            var cells = CellSettings.From(new CellsDto { labels = new[] { "a" }, colors = null });

            CollectionAssert.AreEqual(CellsDto.DefaultLabels, cells.Labels);
            CollectionAssert.AreEqual(CellsDto.DefaultColors, cells.Colors);
            Assert.AreNotSame(CellsDto.DefaultLabels, cells.Labels, "a copy: nobody changes the defaults by changing a setting");
        }

        // ---- the hand on the screen ----------------------------------------------------------------------------------------------------------

        const float Horizon = 0.5f;   // about where the far edge of the field is on the real camera
        const float Split = 0.5f;

        [Test]
        public void HandScreenMap_SingleScreen_KeepsTheStripFree()
        {
            const float Strip = 0.28f;

            var top = HandScreenMap.Viewport(0.25f, 0f, Strip, Split, Horizon);
            var bottom = HandScreenMap.Viewport(0.25f, 1f, Strip, Split, Horizon);

            Assert.AreEqual(0.25f, top.vx, 1e-6f);
            Assert.AreEqual(1f, top.vy, 1e-6f, "a hand at the top of the tracker's picture is at the top of the screen");
            Assert.AreEqual(Strip, bottom.vy, 1e-6f, "and at the bottom it is just over the record strip, never under it");
            for (var y = 0f; y <= 1f; y += 0.05f) Assert.GreaterOrEqual(HandScreenMap.Viewport(0.5f, y, Strip, Split, Horizon).vy, Strip - 1e-6f);
        }

        [Test]
        public void HandScreenMap_TwoScreens_UsesTheWholePicture()
        {
            var bottom = HandScreenMap.Viewport(0.9f, 1f, 0f, Split, Horizon);

            Assert.AreEqual(0.9f, bottom.vx, 1e-6f);
            Assert.AreEqual(0f, bottom.vy, 1e-6f);
            Assert.AreEqual(Horizon + 0.5f * Horizon, HandScreenMap.Viewport(0.9f, 0.25f, 0f, Split, Horizon).vy, 1e-6f, "half way up the top row is half way between the horizon and the top");
        }

        [Test]
        public void HandScreenMap_WhatIsOutsideTheTrackersPicture_IsPutOnTheEdge()
        {
            var corner = HandScreenMap.Viewport(-2f, 5f, 0.28f, Split, Horizon);

            Assert.AreEqual(0f, corner.vx, 1e-6f);
            Assert.AreEqual(0.28f, corner.vy, 1e-6f);
        }

        [Test]
        public void HandScreenMap_RowsMeetAtTheHorizon()
        {
            foreach (var strip in new[] { 0f, 0.28f })
            {
                foreach (var (rowSplit, horizon) in new[] { (0.5f, 0.499f), (0.4f, 0.55f), (0.62f, 0.45f) })
                {
                    var onTheLine = HandScreenMap.Viewport(0.3f, rowSplit, strip, rowSplit, horizon).vy;
                    var justAbove = HandScreenMap.Viewport(0.3f, rowSplit - 1e-4f, strip, rowSplit, horizon).vy;
                    var justBelow = HandScreenMap.Viewport(0.3f, rowSplit + 1e-4f, strip, rowSplit, horizon).vy;

                    Assert.AreEqual(horizon, onTheLine, 1e-6f, $"strip {strip}, split {rowSplit}: the line between the rows is the horizon");
                    Assert.AreEqual(horizon, justAbove, 1e-3f, "the top row ends at the horizon");
                    Assert.AreEqual(horizon, justBelow, 1e-3f, "and the bottom row begins at it: no gap and no overlap");
                    Assert.AreEqual(1f, HandScreenMap.Viewport(0.3f, 0f, strip, rowSplit, horizon).vy, 1e-6f, "the top row reaches the top of the screen");
                    Assert.AreEqual(strip, HandScreenMap.Viewport(0.3f, 1f, strip, rowSplit, horizon).vy, 1e-6f, "the bottom row reaches the strip");
                }
            }
        }

        [Test]
        public void HandScreenMap_IsMonotonic_HandUpMeansHigherOnScreen()
        {
            foreach (var strip in new[] { 0f, 0.28f })
            {
                var previous = float.PositiveInfinity;
                for (var step = 0; step <= 200; step++)
                {
                    var vy = HandScreenMap.Viewport(0.5f, step / 200f, strip, 0.45f, 0.5f).vy;
                    Assert.Less(vy, previous + 1e-6f, $"strip {strip}, hand y {step / 200f}: a hand that goes down never goes up on the screen");
                    previous = vy;
                }
            }
        }

        [Test]
        public void HandScreenMap_WithoutAHorizon_IsOneStraightLine()
        {
            const float Strip = 0.28f;

            var middle = HandScreenMap.Viewport(0.5f, 0.5f, Strip, 0.5f, float.NaN);
            var quarter = HandScreenMap.Viewport(0.5f, 0.25f, Strip, 0.5f, float.NaN);

            Assert.AreEqual(1f - 0.5f * (1f - Strip), middle.vy, 1e-6f, "the horizon is half way between the strip and the top");
            Assert.AreEqual(1f - 0.25f * (1f - Strip), quarter.vy, 1e-6f);
        }

        [Test]
        public void HandScreenMap_HandOf_IsTheInverse()
        {
            foreach (var strip in new[] { 0f, 0.28f })
            {
                for (var x = 0f; x <= 1f; x += 0.25f)
                {
                    for (var y = 0f; y <= 1f; y += 0.05f)
                    {
                        var (vx, vy) = HandScreenMap.Viewport(x, y, strip, 0.45f, 0.52f);
                        var back = HandScreenMap.HandOf(vx, vy, strip, 0.45f, 0.52f);
                        Assert.AreEqual(x, back.x01, 1e-5f);
                        Assert.AreEqual(y, back.y01, 1e-4f, $"strip {strip}: the hand at ({x}, {y}) is found again from where it is drawn");
                    }
                }
            }
            Assert.AreEqual(1f, HandScreenMap.HandOf(0.5f, 0.05f, 0.28f, Split, Horizon).y01, 1e-6f, "a point over the record strip is the bottom of the hand's range");
        }

        [Test]
        public void HandScreenMap_AHorizonThatCannotBeUsed_IsKeptInsideThePicture()
        {
            var lowest = HandScreenMap.HorizonOf(-3f, 0.28f);
            var highest = HandScreenMap.HorizonOf(7f, 0.28f);

            Assert.Greater(lowest, 0.28f, "over the strip");
            Assert.Less(highest, 1f, "and under the top: both rows keep a height");
            Assert.AreEqual(0.64f, HandScreenMap.HorizonOf(float.NaN, 0.28f), 1e-6f);
        }

        [TestCase(0.98f)]
        [TestCase(0.99f)]
        [TestCase(1f)]
        [TestCase(4f)]
        public void HandScreenMap_AStripThatCoversNearlyAllThePicture_StillKeepsBothBandsAHeight(float strip)
        {
            // Arrange: the lowest horizon (strip + 0.01) must stay under the highest (0.99), else the way back from the screen to the hand divides by about zero
            foreach (var horizon in new[] { float.NaN, 0f, 0.5f, 1f })
            {
                // Act
                var lowest = HandScreenMap.Viewport(0.5f, 1f, strip, Split, horizon).vy;
                var top = HandScreenMap.Viewport(0.5f, 0f, strip, Split, horizon).vy;
                var usedHorizon = HandScreenMap.HorizonOf(horizon, strip);

                // Assert
                Assert.That(usedHorizon, Is.GreaterThan(lowest), $"strip {strip}, horizon {horizon}: the horizon is over the bottom of the hand's range");
                Assert.That(usedHorizon, Is.LessThan(top), "and under the top of it");
                Assert.That(top - usedHorizon, Is.GreaterThanOrEqualTo(0.0099f), "the sky band keeps its height");
                Assert.That(usedHorizon - lowest, Is.GreaterThanOrEqualTo(0.0099f), "and so does the ground band");
                foreach (var y in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f })
                {
                    var (vx, vy) = HandScreenMap.Viewport(0.3f, y, strip, Split, horizon);
                    var back = HandScreenMap.HandOf(vx, vy, strip, Split, horizon);
                    Assert.That(float.IsNaN(back.y01) || float.IsInfinity(back.y01), Is.False, $"strip {strip}, y {y}: the way back is a number");
                    Assert.AreEqual(y, back.y01, 2e-3f, $"strip {strip}, horizon {horizon}: the hand at {y} is found again from where it is drawn");
                }
            }
        }
    }
}
