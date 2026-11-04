using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SkyArcTests
    {
        const float Tolerance = 1e-6f;

        [Test]
        public void SkyArc_AtZero_IsBlueHourOnly()
        {
            var (blueHour, mid, sunset) = SkyArc.Weights(0f);

            Assert.AreEqual(1f, blueHour, Tolerance);
            Assert.AreEqual(0f, mid, Tolerance);
            Assert.AreEqual(0f, sunset, Tolerance);
        }

        [Test]
        public void SkyArc_AtHalf_IsMidOnly()
        {
            var (blueHour, mid, sunset) = SkyArc.Weights(0.5f);

            Assert.AreEqual(0f, blueHour, Tolerance);
            Assert.AreEqual(1f, mid, Tolerance);
            Assert.AreEqual(0f, sunset, Tolerance);
        }

        [Test]
        public void SkyArc_AtOne_IsSunsetOnly()
        {
            var (blueHour, mid, sunset) = SkyArc.Weights(1f);

            Assert.AreEqual(0f, blueHour, Tolerance);
            Assert.AreEqual(0f, mid, Tolerance);
            Assert.AreEqual(1f, sunset, Tolerance);
        }

        [Test]
        public void SkyArc_BetweenTwoStops_MixesOnlyTheNeighbours()
        {
            var (a0, a1, a2) = SkyArc.Weights(0.25f);
            Assert.AreEqual(0.5f, a0, Tolerance);
            Assert.AreEqual(0.5f, a1, Tolerance);
            Assert.AreEqual(0f, a2, Tolerance, "the sunset plate is not part of the first half");

            var (b0, b1, b2) = SkyArc.Weights(0.9f);
            Assert.AreEqual(0f, b0, Tolerance, "the blue-hour plate is not part of the second half");
            Assert.AreEqual(0.2f, b1, Tolerance);
            Assert.AreEqual(0.8f, b2, Tolerance);
        }

        [Test]
        public void SkyArc_WeightsSumToOne()
        {
            for (var i = 0; i <= 20; i++)
            {
                var t = i * 0.05f;
                var (a, b, c) = SkyArc.Weights(t);

                Assert.AreEqual(1f, a + b + c, 1e-5f, $"t {t}");
                Assert.GreaterOrEqual(a, 0f);
                Assert.GreaterOrEqual(b, 0f);
                Assert.GreaterOrEqual(c, 0f);
            }
        }

        [Test]
        public void SkyArc_ClampsOutOfRange()
        {
            Assert.AreEqual(SkyArc.Weights(0f), SkyArc.Weights(-3f));
            Assert.AreEqual(SkyArc.Weights(1f), SkyArc.Weights(7f));
            Assert.AreEqual(SkyArc.Weights(0f), SkyArc.Weights(float.NaN), "NaN counts as the quiet start");
        }

        [Test]
        public void SkyArc_Mix_BlendsThreeValuesWithTheSameWeights()
        {
            Assert.AreEqual(10f, SkyArc.Mix(0f, 10f, 20f, 30f), Tolerance);
            Assert.AreEqual(20f, SkyArc.Mix(0.5f, 10f, 20f, 30f), Tolerance);
            Assert.AreEqual(30f, SkyArc.Mix(1f, 10f, 20f, 30f), Tolerance);
            Assert.AreEqual(15f, SkyArc.Mix(0.25f, 10f, 20f, 30f), Tolerance);
        }
    }
}
