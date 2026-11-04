using System;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// A blade or a flower stem that is pushed bends along a circular arc of the length it has: it never gets longer (the old formula dragged the tip sideways without a limit, so a blade near a fast
    /// hand stretched into a long streak). This is the C# mirror of the HLSL helper in MeadowWind.hlsl (BladeBendGpuParityPlayModeTests holds the two together).
    /// </summary>
    public class BladeBendTests
    {
        static readonly float MaxBend = BladeBend.MaxBendRadians(BladeBend.DefaultMaxBendDeg);
        static readonly float Max120 = BladeBend.MaxBendRadians(120f);
        static readonly float[] Strengths = { 0f, 0.1f, 0.3f, 0.6f, 1f, 2f, 4f, 8f };

        /// <summary>The tip of the blade as the old shader drew it: the angle from the vertical of the line from the foot to the tip, for a bend of s blade heights.</summary>
        static double OldTipAngleDeg(double s) => Math.Atan2(s, 1.0 - 0.45 * Math.Min(s, 0.9) - 0.12 * s) * 180.0 / Math.PI;

        static double TipAngleDeg(float s, float maxBend, float height = 1f)
        {
            var tip = BladeBend.At(s, 0f, 1f, height, maxBend);
            return Math.Atan2(Math.Sqrt((double)tip.X * tip.X + (double)tip.Z * tip.Z), tip.Y) * 180.0 / Math.PI;
        }

        /// <summary>How far the tip of a blade moves (in blade heights) when a gust of <paramref name="gust"/> comes on top of a push <paramref name="lean"/> in the same direction.</summary>
        static double TipMovedByAGust(float lean, float gust, float maxBend)
        {
            var before = BladeBend.At(lean, 0f, 1f, 1f, maxBend);
            var after = BladeBend.At(lean + gust, 0f, 1f, 1f, maxBend);
            return Math.Sqrt(Math.Pow(after.X - before.X, 2) + Math.Pow(after.Y - before.Y, 2) + Math.Pow(after.Z - before.Z, 2));
        }

        static double ArcLength(float bendX, float bendZ, float height, float maxBend, int steps = 4000)
        {
            var previous = BladeBend.At(bendX, bendZ, 0f, height, maxBend);
            var length = 0.0;
            for (var i = 1; i <= steps; i++)
            {
                var point = BladeBend.At(bendX, bendZ, i / (float)steps, height, maxBend);
                var dx = (double)point.X - previous.X;
                var dy = (double)point.Y - previous.Y;
                var dz = (double)point.Z - previous.Z;
                length += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                previous = point;
            }
            return length;
        }

        [Test]
        public void ABentBlade_IsExactlyAsLongAsAStraightOne_HoweverHardItIsPushed()
        {
            foreach (var height in new[] { 1f, 0.35f })
            {
                foreach (var maxDeg in new[] { 40f, 75f, 120f, 150f })
                {
                    foreach (var s in Strengths)
                    {
                        var length = ArcLength(s * 0.6f, s * 0.8f, height, BladeBend.MaxBendRadians(maxDeg));
                        Assert.AreEqual(height, length, height * 0.01, $"strength {s}, height {height}, max bend {maxDeg}: the length of the blade");
                    }
                }
            }
        }

        [Test]
        public void TheTotalBend_NeverPassesTheMaximum_AndNeverGoesBackwards()
        {
            foreach (var s in new[] { 0f, 1e-9f, 0.5f, 1f, 3f, 10f, 100f, 1e4f, 1e9f, float.PositiveInfinity })
            {
                var angle = BladeBend.Angle(s, MaxBend);
                Assert.That(angle, Is.InRange(0f, MaxBend), $"strength {s}");
            }
            Assert.AreEqual(0f, BladeBend.Angle(-1f, MaxBend), 1e-6f, "a negative strength is no bend");
        }

        [Test]
        public void TheBlade_LiesDownTowardsTheMaximum_AsTheWindGetsHard_ButIsNotThereYetAtABendOfOne()
        {
            Assert.Less(BladeBend.Angle(1f, MaxBend), MaxBend * 0.95f, "a bend of 1 is not yet pinned at the maximum: there is room to bend further");
            Assert.Greater(BladeBend.Angle(8f, MaxBend), MaxBend * 0.99f, "a hard wind lies it down almost to the maximum");
            Assert.AreEqual(MaxBend, BladeBend.Angle(1e6f, MaxBend), MaxBend * 1e-4f, "and a gale does not stand it up again (no overflow)");
        }

        [Test]
        public void AHarderHand_BendsTheBladeFurther_AtBendsOf0Point6_1_1Point5_2_And3()
        {
            var degrees = new[] { 0.6f, 1f, 1.5f, 2f, 3f }.Select(s => BladeBend.Angle(s, MaxBend) * 180.0 / Math.PI).ToArray();
            TestContext.WriteLine("[blade-bend] total bend at s 0.6 / 1 / 1.5 / 2 / 3 with the shipped maximum: " + string.Join(" / ", degrees.Select(d => d.ToString("0.0"))));

            for (var i = 1; i < degrees.Length; i++) Assert.Greater(degrees[i], degrees[i - 1] + 0.5, "each step of a harder hand lays the grass down a little further");
            Assert.GreaterOrEqual(degrees[3] - degrees[1], 5.0, "theta(2) - theta(1) is at least 5 degrees");
        }

        [Test]
        public void AGust_StillMovesTheTipOfABladeLeaning0Point6AlongTheWind_ByAtLeast0Point15Heights()
        {
            Assert.GreaterOrEqual(TipMovedByAGust(0.6f, 0.35f, MaxBend), 0.15, "lean 0.6 along the wind, a gust of 0.35");
        }

        [Test]
        public void AGust_EvenMovesTheMostLeaningBlade_Lean0Point9_AlongTheWind_ThoughLittle()
        {
            // the worst case: the lean is straight along the wind, so the gust can only push the blade down the arc. The arc is already 100 degrees round there,
            // and the tip moves 0.09 of a blade height (the shipped maximum of 120 and the slope of 2.4 cannot make it 0.15: that takes a maximum of 150)
            var moved = TipMovedByAGust(0.9f, 0.35f, MaxBend);
            TestContext.WriteLine($"[blade-bend] lean 0.9 along the wind plus a gust of 0.35 moves the tip {moved:0.000} heights (shipped maximum)");
            Assert.GreaterOrEqual(moved, 0.05);
        }

        [Test]
        public void OverTheLeansOfTheGrass_AGustMovesTheTipOfEveryBlade_AndOfMostBladesMuch()
        {
            // the placement's lean (a blade's own 0.15..0.65 and its clump's 0.25..1.0, three parts of four the clump's) in any direction, the breeze of 0.154 and a gust of 0.352 along the wind,
            // blades 0.8 to 1.2 times as stiff
            var rng = new Random(3);
            const int blades = 20000;
            var moved = new double[blades];
            for (var i = 0; i < blades; i++)
            {
                var lean = 0.25 * (0.15 + 0.5 * rng.NextDouble()) + 0.75 * (0.25 + 0.75 * rng.NextDouble());
                var angle = rng.NextDouble() * 2.0 * Math.PI;
                var stiffness = 0.8 + 0.4 * rng.NextDouble();
                var bx = (float)(lean * Math.Cos(angle) + 0.154 * stiffness);
                var bz = (float)(lean * Math.Sin(angle));
                var before = BladeBend.At(bx, bz, 1f, 1f, MaxBend);
                var after = BladeBend.At(bx + (float)(0.352 * stiffness), bz, 1f, 1f, MaxBend);
                moved[i] = Math.Sqrt(Math.Pow(after.X - before.X, 2) + Math.Pow(after.Y - before.Y, 2) + Math.Pow(after.Z - before.Z, 2));
            }
            Array.Sort(moved);
            var mean = moved.Average();
            var quarter = moved[blades / 4];
            var frozen = moved.Count(m => m < 0.03) / (double)blades;
            TestContext.WriteLine($"[blade-bend] tip movement per gust over {blades} blades (heights): mean {mean:0.000}, lowest quarter {quarter:0.000}, frozen {frozen * 100:0.0} %");

            Assert.GreaterOrEqual(mean, 0.25, "on average a gust moves the tip a quarter of a blade height or more");
            Assert.GreaterOrEqual(quarter, 0.15, "three blades in four move at least 0.15");
            Assert.LessOrEqual(frozen, 0.001, "no blade stands frozen under a gust (the old formula had none)");
        }

        [Test]
        public void WithTheMaximumAt120_NoTipGoesBelowItsFoot_AndTheLengthIsStillExact()
        {
            for (var s = 0f; s <= 40f; s += 0.25f)
            {
                var tip = BladeBend.At(s * 0.8f, s * 0.6f, 1f, 1f, Max120);
                Assert.Greater(tip.Y, 0.3f, $"strength {s}: the tip stays above its foot (a total bend of 120 degrees is still under 180)");
            }
            foreach (var s in new[] { 0.6f, 1f, 2f, 8f, 40f })
                Assert.AreEqual(1.0, ArcLength(s * 0.6f, s * 0.8f, 1f, Max120), 0.01, $"strength {s}");
        }

        [Test]
        public void TheTipLean_NeverDecreasesWithTheStrength_AndGrowsWhereTheBladeIsStillBending()
        {
            var previous = -1.0;
            for (var s = 0f; s <= 30f; s += 0.005f)
            {
                var lean = TipAngleDeg(s, MaxBend);
                Assert.GreaterOrEqual(lean, previous - 1e-4, $"strength {s}");
                if (s > 0f && s <= 0.45f) Assert.Greater(lean, previous, $"strength {s}: still bending, so still leaning more");
                previous = lean;
            }
            Assert.AreEqual(MaxBend * 0.5 * 180.0 / Math.PI, TipAngleDeg(30f, MaxBend), 0.1, "the chord of an arc leans half the total bend");
        }

        [Test]
        public void TheBend_KeepsTheDirectionOfThePush_ForTheOffsetAndForTheTangent()
        {
            foreach (var s in new[] { 0.05f, 0.5f, 3f, 25f })
            {
                for (var degrees = 0; degrees < 360; degrees += 37)
                {
                    var bx = s * (float)Math.Cos(degrees * Math.PI / 180.0);
                    var bz = s * (float)Math.Sin(degrees * Math.PI / 180.0);
                    var p = BladeBend.At(bx, bz, 0.8f, 1f, MaxBend);
                    var offset = Math.Sqrt((double)p.X * p.X + (double)p.Z * p.Z);
                    var tangent = Math.Sqrt((double)p.TangentX * p.TangentX + (double)p.TangentZ * p.TangentZ);
                    Assert.Greater(offset, 0.0, $"s {s}, {degrees} deg");
                    Assert.AreEqual(bx / s, p.X / offset, 1e-4, $"s {s}, {degrees} deg: x of the direction");
                    Assert.AreEqual(bz / s, p.Z / offset, 1e-4, $"s {s}, {degrees} deg: z of the direction");
                    Assert.AreEqual(bx / s, p.TangentX / tangent, 1e-4, $"s {s}, {degrees} deg: the tangent leans the same way");
                    Assert.AreEqual(bz / s, p.TangentZ / tangent, 1e-4);
                }
            }
        }

        [Test]
        public void WithoutAnyPush_TheBladeStandsStraightUp()
        {
            foreach (var t in new[] { 0f, 0.3f, 1f })
            {
                var p = BladeBend.At(0f, 0f, t, 2f, MaxBend);
                Assert.AreEqual(0f, p.X, 0f);
                Assert.AreEqual(0f, p.Z, 0f);
                Assert.AreEqual(2f * t, p.Y, 1e-6f);
                Assert.AreEqual(0f, p.TangentX, 0f);
                Assert.AreEqual(1f, p.TangentY, 1e-6f);
                Assert.AreEqual(0f, p.TangentZ, 0f);
            }
        }

        [Test]
        public void NothingIsNotANumber_AtZero_AtTinyPushes_OrAtAbsurdOnes()
        {
            foreach (var s in new[] { 0f, 1e-30f, 1e-12f, 1e-6f, 1e-3f, 1e6f, 1e18f, 3e38f })
            {
                foreach (var t in new[] { 0f, 1e-6f, 0.5f, 1f })
                {
                    var p = BladeBend.At(s, -s * 0.5f, t, 1f, MaxBend);
                    foreach (var value in new[] { p.X, p.Y, p.Z, p.TangentX, p.TangentY, p.TangentZ })
                        Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), $"s {s}, t {t}");
                    var length = Math.Sqrt((double)p.TangentX * p.TangentX + (double)p.TangentY * p.TangentY + (double)p.TangentZ * p.TangentZ);
                    Assert.AreEqual(1.0, length, 1e-4, $"s {s}, t {t}: the tangent is a unit vector");
                }
            }
        }

        [Test]
        public void TheFoot_StaysPut_AndStandsUpright()
        {
            foreach (var s in Strengths)
            {
                var p = BladeBend.At(s, 0f, 0f, 1f, MaxBend);
                Assert.AreEqual(0f, p.X, 1e-7f);
                Assert.AreEqual(0f, p.Y, 1e-7f);
                Assert.AreEqual(0f, p.Z, 1e-7f);
                Assert.AreEqual(1f, p.TangentY, 1e-6f, "a blade leaves the ground straight up whatever blows");
            }
        }

        [Test]
        public void ARestingMeadow_LooksAsItDid_TheTipLeansWithin5DegreesOfTheOldFormula_UpToABendOf0Point6_WithTheShippedMaximum()
        {
            var worst = 0.0;
            for (var s = 0f; s <= 0.6001f; s += 0.005f)
            {
                var difference = Math.Abs(TipAngleDeg(s, MaxBend) - OldTipAngleDeg(s));
                worst = Math.Max(worst, difference);
                Assert.LessOrEqual(difference, 5.0, $"s {s}: new {TipAngleDeg(s, MaxBend):0.0} deg, old {OldTipAngleDeg(s):0.0} deg");
            }
            TestContext.WriteLine($"[blade-bend] worst tip direction difference from the old formula up to s 0.6: {worst:0.00} deg");
        }

        [Test]
        public void ARestingMeadow_LooksTheSame_WhateverTheMaximumIsSetTo_AsLongAsItIsAtLeast100()
        {
            // a lower maximum lies the resting blades down sooner (at 75 the worst is 9 degrees): the rule is for the shipped 120 and for any maximum from 100 up
            foreach (var maxDeg in new[] { 100f, 120f, 135f, 150f })
            {
                for (var s = 0f; s <= 0.6001f; s += 0.01f)
                    Assert.LessOrEqual(Math.Abs(TipAngleDeg(s, BladeBend.MaxBendRadians(maxDeg)) - OldTipAngleDeg(s)), 5.0, $"max {maxDeg}, s {s}");
            }
        }

        [Test]
        public void ThePoints_AgreeWithTheExactArc_AlsoWhereTheSeriesFormTakesOver()
        {
            // the point of the arc in double precision: horizontal = H (1 - cos a) / theta, up = H sin a / theta, with a = theta t
            foreach (var s in new[] { 1e-8f, 1e-5f, 3e-4f, 1e-3f, 3e-3f, 0.02f, 0.2f })
            {
                var theta = (double)BladeBend.Angle(s, MaxBend);
                foreach (var t in new[] { 0.1f, 0.5f, 1f })
                {
                    var p = BladeBend.At(s, 0f, t, 1f, MaxBend);
                    var a = theta * t;
                    var horizontal = theta > 1e-12 ? (1.0 - Math.Cos(a)) / theta : 0.0;
                    var up = theta > 1e-12 ? Math.Sin(a) / theta : t;
                    Assert.AreEqual(horizontal, p.X, 2e-6, $"s {s}, t {t}: sideways");
                    Assert.AreEqual(up, p.Y, 2e-6, $"s {s}, t {t}: up");
                }
            }
        }

        [Test]
        public void TheCurve_ScalesWithTheHeight()
        {
            var one = BladeBend.At(1.5f, 0.5f, 0.7f, 1f, MaxBend);
            var three = BladeBend.At(1.5f, 0.5f, 0.7f, 3f, MaxBend);

            Assert.AreEqual(one.X * 3f, three.X, 1e-5f);
            Assert.AreEqual(one.Y * 3f, three.Y, 1e-5f);
            Assert.AreEqual(one.Z * 3f, three.Z, 1e-5f);
            Assert.AreEqual(one.TangentY, three.TangentY, 1e-6f, "the direction does not depend on the size");
        }

        [Test]
        public void TheMaximumIsGivenInDegrees_AndMakesTheBladeLieFurtherDown()
        {
            Assert.AreEqual(1.3089969f, BladeBend.MaxBendRadians(75f), 1e-6f);
            Assert.Less(TipAngleDeg(5f, BladeBend.MaxBendRadians(50f)), TipAngleDeg(5f, BladeBend.MaxBendRadians(80f)));
        }
    }
}
