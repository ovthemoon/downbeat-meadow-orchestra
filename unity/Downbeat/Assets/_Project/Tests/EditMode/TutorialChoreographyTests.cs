using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>What the three scenes of the tutorial do over the five seconds of their step (the hand glyph breathes, a hand visits the six cells, two fists spread apart).</summary>
    public class TutorialChoreographyTests
    {
        const float Tolerance = 1e-3f;

        [Test]
        public void TheCells_AreNumberedLikeTheParts_RowTimesThreePlusColumn()
        {
            var (u0, v0) = TutorialChoreography.CellCenter(0);
            var (u2, v2) = TutorialChoreography.CellCenter(2);
            var (u3, v3) = TutorialChoreography.CellCenter(3);
            var (u4, v4) = TutorialChoreography.CellCenter(4);

            Assert.AreEqual(1f / 6f, u0, Tolerance);
            Assert.AreEqual(0.25f, v0, Tolerance, "the top row is the upper half");
            Assert.AreEqual(5f / 6f, u2, Tolerance);
            Assert.AreEqual(0.25f, v2, Tolerance);
            Assert.AreEqual(1f / 6f, u3, Tolerance);
            Assert.AreEqual(0.75f, v3, Tolerance, "cell 3 is the first of the bottom row");
            Assert.AreEqual(0.5f, u4, Tolerance);
            Assert.AreEqual(0.75f, v4, Tolerance);
        }

        [Test]
        public void TheRoamingHand_VisitsTheSixCells_TopRowLeftToRight_ThenBottomRowRightToLeft()
        {
            // the order of the mock-up: it is a walk round the grid
            var order = new[] { 0, 1, 2, 5, 4, 3 };
            for (var i = 0; i < order.Length; i++)
            {
                var (cu, cv) = TutorialChoreography.CellCenter(order[i]);
                var (u, v) = TutorialChoreography.RoamPosition((i + 0.3f) / 6f);   // while it rests in the cell
                Assert.AreEqual(cu, u, Tolerance, "cell " + order[i]);
                Assert.AreEqual(cv, v, Tolerance, "cell " + order[i]);
            }
        }

        [Test]
        public void TheRoamingHand_MovesSmoothly_AndEndsInTheLastCell()
        {
            var (lastU, lastV) = TutorialChoreography.CellCenter(3);
            var (endU, endV) = TutorialChoreography.RoamPosition(1f);
            Assert.AreEqual(lastU, endU, Tolerance);
            Assert.AreEqual(lastV, endV, Tolerance);

            var (prevU, prevV) = TutorialChoreography.RoamPosition(0f);
            for (var p = 0.001f; p <= 1f; p += 0.001f)
            {
                var (u, v) = TutorialChoreography.RoamPosition(p);
                Assert.That(System.Math.Abs(u - prevU) + System.Math.Abs(v - prevV), Is.LessThan(0.02f), "no jump at " + p);
                prevU = u;
                prevV = v;
            }
        }

        [Test]
        public void TheCellUnderTheHand_IsLit_TheOthersAreNot()
        {
            var order = new[] { 0, 1, 2, 5, 4, 3 };
            for (var i = 0; i < order.Length; i++)
            {
                var p = (i + 0.3f) / 6f;
                for (var cell = 0; cell < TutorialChoreography.CellCount; cell++)
                {
                    var light = TutorialChoreography.CellLight(cell, p);
                    if (cell == order[i]) Assert.AreEqual(1f, light, Tolerance, $"cell {cell} while the hand rests in it");
                    else Assert.AreEqual(0f, light, Tolerance, $"cell {cell} while the hand rests in {order[i]}");
                }
            }
        }

        [Test]
        public void TwoCellsAreNeverBothFullyLit_WhileTheHandIsOnItsWay()
        {
            for (var p = 0f; p <= 1f; p += 0.002f)
            {
                var sum = 0f;
                for (var cell = 0; cell < TutorialChoreography.CellCount; cell++) sum += TutorialChoreography.CellLight(cell, p);
                Assert.That(sum, Is.LessThanOrEqualTo(1.05f), "at " + p);
            }
        }

        [Test]
        public void TheBreath_RunsBetweenTheSizeAndTwelvePercentMore()
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var t = 0.0; t < 3.2; t += 0.01)
            {
                var s = TutorialChoreography.Breath(t);
                min = System.Math.Min(min, s);
                max = System.Math.Max(max, s);
            }

            Assert.AreEqual(1f, min, 0.002f);
            Assert.AreEqual(1.12f, max, 0.002f);
            Assert.AreEqual(TutorialChoreography.Breath(0.3), TutorialChoreography.Breath(0.3 + TutorialChoreography.BreathPeriodSec), Tolerance, "it repeats");
        }

        [Test]
        public void TheRing_GrowsAndFadesOnce_ThenStartsAgain()
        {
            Assert.AreEqual(0f, TutorialChoreography.RingPhase(0.0), Tolerance);
            Assert.That(TutorialChoreography.RingPhase(0.8), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(TutorialChoreography.RingPhase(1.6 + 0.4), Is.EqualTo(0.25f).Within(Tolerance));
        }

        [Test]
        public void TheFists_StartTogether_SpreadApart_AndStay()
        {
            Assert.AreEqual(0f, TutorialChoreography.FistSpread(0f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.FistSpread(0.2f), Tolerance, "they wait a moment first");
            Assert.AreEqual(1f, TutorialChoreography.FistSpread(0.6f), Tolerance);
            Assert.AreEqual(1f, TutorialChoreography.FistSpread(1f), Tolerance);
            var previous = 0f;
            for (var p = 0f; p <= 1f; p += 0.01f)
            {
                var s = TutorialChoreography.FistSpread(p);
                Assert.That(s, Is.GreaterThanOrEqualTo(previous - 1e-6f), "they only move apart, at " + p);
                previous = s;
            }
        }

        [Test]
        public void TheFistCentres_AreMirrorImages_AndNeverOverlapAtTheStart()
        {
            var (left0, right0) = TutorialChoreography.FistCentres(0f);
            var (left1, right1) = TutorialChoreography.FistCentres(1f);

            Assert.AreEqual(1f, left0 + right0, Tolerance, "mirror images round the middle");
            Assert.AreEqual(1f, left1 + right1, Tolerance);
            Assert.That(right0 - left0, Is.GreaterThan(0.15f), "side by side, not on top of each other");
            Assert.That(left1, Is.LessThan(left0));
            Assert.That(left1, Is.GreaterThan(0.05f), "and the spread stays on the screen");
            Assert.That(right1, Is.LessThan(0.95f));
        }

        [Test]
        public void TheArrows_ShowWhileTheFistsMove_AndTheEndWordComesAfter()
        {
            Assert.AreEqual(0f, TutorialChoreography.ArrowAlpha(0f), Tolerance);
            Assert.AreEqual(1f, TutorialChoreography.ArrowAlpha(0.45f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.ArrowAlpha(0.9f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.EndWordAlpha(0.3f), Tolerance);
            Assert.AreEqual(1f, TutorialChoreography.EndWordAlpha(0.8f), Tolerance);
            Assert.AreEqual(1f, TutorialChoreography.EndWordAlpha(1f), Tolerance);
        }

        [Test]
        public void TheSoundBars_DieAwayAsTheFistsSpread()
        {
            Assert.AreEqual(1f, TutorialChoreography.SoundLevel(0f, 0), Tolerance, "the music is playing");
            Assert.That(TutorialChoreography.SoundLevel(0.9f, 0), Is.LessThan(0.05f), "and it has stopped");
            Assert.That(TutorialChoreography.SoundLevel(0.4f, 4), Is.GreaterThan(TutorialChoreography.SoundLevel(0.4f, 0)), "the bars do not all die at the same moment");
        }

        [Test]
        public void TheSceneAlpha_ShutsAtTheEdgesOfAStep_AndIsOpenInTheMiddle()
        {
            Assert.AreEqual(0f, TutorialChoreography.SceneAlpha(0f), Tolerance);
            Assert.AreEqual(1f, TutorialChoreography.SceneAlpha(0.5f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.SceneAlpha(1f), Tolerance);
            Assert.AreEqual(TutorialChoreography.SceneAlpha(0.03f), TutorialChoreography.SceneAlpha(0.97f), Tolerance, "the same shape in and out");
        }

        [Test]
        public void TheGrid_IsFaintInTheFirstStep_StrongWithNamesInTheSecond_AndGoneInTheThird()
        {
            Assert.That(TutorialChoreography.GridStrength(0, 0.5f), Is.InRange(0.3f, 0.7f), "faint");
            Assert.AreEqual(1f, TutorialChoreography.GridStrength(1, 0.5f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.GridStrength(2, 0.5f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.LabelStrength(0, 0.5f), Tolerance, "no names yet");
            Assert.AreEqual(1f, TutorialChoreography.LabelStrength(1, 0.5f), Tolerance);
            Assert.AreEqual(0f, TutorialChoreography.LabelStrength(2, 0.5f), Tolerance);
        }

        [Test]
        public void TheGrid_DoesNotFlicker_BetweenTheFirstAndTheSecondStep()
        {
            var endOfFirst = TutorialChoreography.GridStrength(0, 1f);
            var startOfSecond = TutorialChoreography.GridStrength(1, 0f);

            Assert.AreEqual(endOfFirst, startOfSecond, Tolerance, "it stays as faint as it was");
            Assert.AreEqual(0f, TutorialChoreography.GridStrength(1, 1f), Tolerance, "and fades before the fists");
            Assert.AreEqual(0f, TutorialChoreography.GridStrength(2, 1f), Tolerance);
            Assert.AreEqual(TutorialChoreography.GridStrength(0, 0f), 0f, Tolerance, "and comes back from nothing after the fists");
        }
    }
}
