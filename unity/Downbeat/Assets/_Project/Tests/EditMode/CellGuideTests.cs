using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The cell guide: the name of a cell shows once when a hand comes into it, and the border of the cell glows while a hand is in it.</summary>
    public class CellGuideTests
    {
        const double FrameDt = 1.0 / 60.0;
        const float LabelSec = 1.2f;
        const int None = -1;

        /// <summary>Feeds <paramref name="timer"/> with a hand in cell <paramref name="cell0"/> (and <paramref name="cell1"/> for the second hand) from <paramref name="from"/> for <paramref name="seconds"/>; returns the time after.</summary>
        static double Hold(CellLabelTimer timer, double from, double seconds, int cell0, int cell1 = None)
        {
            var now = from;
            for (var frame = 0; frame < (int)System.Math.Round(seconds / FrameDt); frame++)
            {
                timer.Update(now, cell0, cell1);
                now += FrameDt;
            }
            return now;
        }

        [Test]
        public void CellLabel_ShowsOnceOnEntering_NotWhileStaying()
        {
            var timer = new CellLabelTimer(LabelSec);
            var seen = new System.Collections.Generic.List<float>();

            // a hand comes into cell 2 and stays there for 6 seconds
            var now = 0.0;
            for (var frame = 0; frame < 6 * 60; frame++)
            {
                timer.Update(now, 2, None);
                seen.Add(timer.LabelAlpha(2, now));
                now += FrameDt;
            }

            // it shows up at once ...
            Assert.Greater(seen[(int)(0.3 / FrameDt)], 0.99f, "0.3 s after the entry the name is there");
            // ... and is gone after labelSec plus the fade, for good: the hand staying in the cell shows nothing again
            for (var frame = (int)((LabelSec + CellLabelTimer.FadeOutSec + 0.05) / FrameDt); frame < seen.Count; frame++) Assert.AreEqual(0f, seen[frame], 1e-6f, "frame " + frame);
            Assert.AreEqual(0f, timer.LabelAlpha(2, now), 1e-6f);
        }

        [Test]
        public void CellLabel_EnteringAnotherCell_ShowsThatCellsName_AndNotTheOthers()
        {
            var timer = new CellLabelTimer(LabelSec);
            var now = Hold(timer, 0.0, 3.0, 0);

            now = Hold(timer, now, 0.3, 4);

            Assert.Greater(timer.LabelAlpha(4, now), 0.9f);
            Assert.AreEqual(0f, timer.LabelAlpha(0, now), 1e-6f, "the first cell's name was shown long ago");
            for (var other = 0; other < CellLabelTimer.CellCount; other++)
            {
                if (other != 4 && other != 0) Assert.AreEqual(0f, timer.LabelAlpha(other, now), 1e-6f, "cell " + other);
            }
        }

        [Test]
        public void CellLabel_FadesAfterLabelSec()
        {
            var timer = new CellLabelTimer(LabelSec);
            var now = Hold(timer, 0.0, 0.0 + LabelSec - 0.02, 1);

            var justBefore = timer.LabelAlpha(1, now);
            now = Hold(timer, now, CellLabelTimer.FadeOutSec / 2.0 + 0.02, 1);
            var halfway = timer.LabelAlpha(1, now);
            now = Hold(timer, now, CellLabelTimer.FadeOutSec, 1);
            var after = timer.LabelAlpha(1, now);

            Assert.AreEqual(1f, justBefore, 1e-4f, "still fully there just before labelSec");
            Assert.That(halfway, Is.GreaterThan(0.1f).And.LessThan(0.9f), "half way through the fade");
            Assert.AreEqual(0f, after, 1e-6f, "gone");
        }

        [Test]
        public void CellLabel_ComesInWithAShortFade_NotAPop()
        {
            var timer = new CellLabelTimer(LabelSec);

            timer.Update(0.0, 3, None);
            var atEntry = timer.LabelAlpha(3, 0.0);
            timer.Update(CellLabelTimer.FadeInSec / 2.0, 3, None);
            var middle = timer.LabelAlpha(3, CellLabelTimer.FadeInSec / 2.0);

            Assert.AreEqual(0f, atEntry, 1e-6f);
            Assert.That(middle, Is.GreaterThan(0.1f).And.LessThan(0.9f));
        }

        [Test]
        public void CellLabel_AHandThatFlickersOutForAMoment_DoesNotShowTheNameAgain()
        {
            // a tracker that loses the hand for a few pictures and sees it again in the same cell
            var timer = new CellLabelTimer(LabelSec);
            var now = Hold(timer, 0.0, 3.0, 5);

            now = Hold(timer, now, 0.2, None);
            now = Hold(timer, now, 0.2, 5);

            Assert.AreEqual(0f, timer.LabelAlpha(5, now), 1e-6f);
        }

        [Test]
        public void CellLabel_AHandThatCameBackAfterALongTime_ShowsTheNameAgain()
        {
            var timer = new CellLabelTimer(LabelSec);
            var now = Hold(timer, 0.0, 3.0, 5);
            now = Hold(timer, now, 2.0, None);

            now = Hold(timer, now, 0.3, 5);

            Assert.Greater(timer.LabelAlpha(5, now), 0.9f);
        }

        [Test]
        public void CellLabel_LeavingAndComingBackWhileTheNameIsStillThere_DoesNotBlink()
        {
            var timer = new CellLabelTimer(LabelSec);
            var now = Hold(timer, 0.0, 0.5, 2);
            now = Hold(timer, now, 0.3, 3);
            var before = timer.LabelAlpha(2, now);

            timer.Update(now, 2, None);
            var after = timer.LabelAlpha(2, now);

            Assert.Greater(before, 0.5f);
            Assert.GreaterOrEqual(after, before - 0.05f, "the name goes on from where it was instead of starting from nothing");
        }

        [Test]
        public void CellLabel_NoLabelSec_ShowsNoName()
        {
            var timer = new CellLabelTimer(0f);

            var now = Hold(timer, 0.0, 1.0, 2);

            Assert.AreEqual(0f, timer.LabelAlpha(2, now), 1e-6f);
            Assert.Greater(timer.Glow(2), 0.5f, "the border still glows: labelSec is only for the name");
        }

        [Test]
        public void CellGlow_RisesWhileAHandIsInTheCell_AndFallsWhenItLeaves()
        {
            var timer = new CellLabelTimer(LabelSec);

            var now = Hold(timer, 0.0, 1.0, 1);
            var glowing = timer.Glow(1);
            var other = timer.Glow(0);
            now = Hold(timer, now, 2.0, None);
            var after = timer.Glow(1);

            Assert.Greater(glowing, 0.95f, "it stays lit all the time the hand is in the cell");
            Assert.AreEqual(0f, other, 1e-6f);
            Assert.Less(after, 0.02f);
        }

        [Test]
        public void CellGlow_TwoHandsInTwoCells_LightBoth()
        {
            var timer = new CellLabelTimer(LabelSec);

            Hold(timer, 0.0, 1.0, 0, 5);

            Assert.Greater(timer.Glow(0), 0.95f);
            Assert.Greater(timer.Glow(5), 0.95f);
            Assert.AreEqual(0f, timer.Glow(3), 1e-6f);
            Assert.IsTrue(timer.IsOccupied(0));
            Assert.IsFalse(timer.IsOccupied(3));
        }

        [Test]
        public void CellGlow_ComesInSoftly()
        {
            var timer = new CellLabelTimer(LabelSec);

            timer.Update(0.0, 2, None);
            timer.Update(FrameDt, 2, None);

            Assert.That(timer.Glow(2), Is.GreaterThan(0f).And.LessThan(0.5f), "one picture after the entry the border is only beginning to glow");
        }

        [Test]
        public void ACellNumberOutsideTheGrid_IsTakenAsNoHand()
        {
            var timer = new CellLabelTimer(LabelSec);

            Hold(timer, 0.0, 0.5, 9, -4);

            for (var cell = 0; cell < CellLabelTimer.CellCount; cell++)
            {
                Assert.AreEqual(0f, timer.LabelAlpha(cell, 0.5), 1e-6f);
                Assert.IsFalse(timer.IsOccupied(cell));
            }
            Assert.AreEqual(0f, timer.LabelAlpha(-1, 0.5), "a cell that does not exist has no name either");
            Assert.AreEqual(0f, timer.Glow(6));
        }

        [Test]
        public void Layout_TheSixCellsFillTheViewportAboveTheStrip_TopRowFirst()
        {
            const float strip = 0.28f;
            const float horizon = 0.5f;

            var topLeft = CellGuideLayout.Cell(0, strip, horizon);
            var topRight = CellGuideLayout.Cell(2, strip, horizon);
            var bottomLeft = CellGuideLayout.Cell(3, strip, horizon);
            var bottomRight = CellGuideLayout.Cell(5, strip, horizon);

            Assert.AreEqual(0f, topLeft.XMin, 1e-6f);
            Assert.AreEqual(1f / 3f, topLeft.XMax, 1e-6f);
            Assert.AreEqual(2f / 3f, topRight.XMin, 1e-6f);
            Assert.AreEqual(1f, topRight.XMax, 1e-6f);
            Assert.AreEqual(1f, topLeft.YMax, 1e-6f, "the top row reaches the top of the picture (y is up)");
            Assert.AreEqual(strip, bottomLeft.YMin, 1e-6f, "and the bottom row stops where the strip begins");
            Assert.AreEqual(bottomLeft.YMax, topLeft.YMin, 1e-6f, "the rows meet");
            Assert.AreEqual(horizon, topLeft.YMin, 1e-6f, "at the horizon");
            Assert.AreEqual(bottomRight.YMax, bottomLeft.YMax);
        }

        [Test]
        public void Layout_FollowsTheHorizon_AndASingleScreenWithoutAStrip()
        {
            var high = CellGuideLayout.Cell(0, 0f, 0.6f);
            var low = CellGuideLayout.Cell(0, 0f, 0.4f);

            Assert.AreEqual(0.6f, high.YMin, 1e-6f, "a horizon 60% up puts the line there");
            Assert.AreEqual(0.4f, low.YMin, 1e-6f);
            Assert.AreEqual(0f, CellGuideLayout.Cell(4, 0f, 0.4f).YMin, 1e-6f, "two screens: the grid goes all the way down");
        }

        [Test]
        public void Layout_AHandIsDrawnInsideTheCellThatItPlays()
        {
            const float strip = 0.28f;
            const float horizon = 0.5f;
            const float split = 0.5f;

            foreach (var y01 in new[] { 0f, 0.2f, 0.5f, 0.51f, 0.8f, 1f })
            {
                foreach (var x01 in new[] { 0.05f, 0.5f, 0.95f })
                {
                    var cell = (int)CellPicker.Pick(x01, y01, Part.Harp, false, 0f, CellSettings.From(new CellsDto { rowSplit = split, rowHysteresis = 0f }));
                    var rect = CellGuideLayout.Cell(cell, strip, horizon);
                    var (vx, vy) = HandScreenMap.Viewport(x01, y01, strip, split, horizon);

                    Assert.That(vx, Is.InRange(rect.XMin - 1e-5f, rect.XMax + 1e-5f), $"({x01}, {y01}) is in cell {cell} across");
                    Assert.That(vy, Is.InRange(rect.YMin - 1e-5f, rect.YMax + 1e-5f), $"({x01}, {y01}) is in cell {cell} up and down");
                }
            }
        }

        [Test]
        public void Layout_RejectsACellThatIsNotOnTheGrid()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => CellGuideLayout.Cell(6, 0.28f, 0.5f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => CellGuideLayout.Cell(-1, 0.28f, 0.5f));
        }

        [Test]
        public void TheCellFrame_IsBrightestAtTheEdge_AndFaintInside()
        {
            Assert.Greater(CellFrameShape.Alpha(0f), CellFrameShape.Alpha(0.3f));
            Assert.Greater(CellFrameShape.Alpha(0.3f), CellFrameShape.Alpha(1f));
            Assert.Greater(CellFrameShape.Alpha(1f), 0f, "a faint fill tints the whole cell");
            Assert.Less(CellFrameShape.Alpha(1f), 0.3f);
            Assert.LessOrEqual(CellFrameShape.Alpha(0f), 1f);
        }
    }
}
