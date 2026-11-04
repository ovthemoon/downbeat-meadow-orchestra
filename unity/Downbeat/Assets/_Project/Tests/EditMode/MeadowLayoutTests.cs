using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class MeadowLayoutTests
    {
        const float FieldWidth = 40f;
        const float NearZ = 2.5f;
        const float FarZ = 24f;
        const float HorizonY = 0.3f;
        const float Tolerance = 1e-4f;

        static MeadowLayout Layout() => new MeadowLayout(FieldWidth, NearZ, FarZ, HorizonY);

        [Test]
        public void HandToField_BottomIsNear_TopIsFar()
        {
            var layout = Layout();

            var bottom = layout.HandToField(0.5f, 1f);
            var middle = layout.HandToField(0.5f, 0.6f);
            var top = layout.HandToField(0.5f, 0f);

            Assert.AreEqual(NearZ, bottom.Z, Tolerance, "the bottom of the image is the nearest ground");
            Assert.AreEqual(FarZ, top.Z, Tolerance, "the top of the image is the farthest ground");
            Assert.Less(bottom.Z, middle.Z);
            Assert.Less(middle.Z, top.Z);
        }

        [Test]
        public void HandToField_DepthIncreasesMonotonically_AsTheHandMovesUp()
        {
            var layout = Layout();
            var previous = float.NegativeInfinity;

            for (var y = 1f; y >= 0f; y -= 0.02f)
            {
                var z = layout.HandToField(0.5f, y).Z;
                Assert.GreaterOrEqual(z, previous - Tolerance, $"handY {y}");
                previous = z;
            }
        }

        [Test]
        public void HandToField_XSpansFieldWidth()
        {
            var layout = Layout();

            var left = layout.HandToField(0f, 0f);
            var right = layout.HandToField(1f, 0f);
            var center = layout.HandToField(0.5f, 0f);

            Assert.AreEqual(-FieldWidth / 2f, left.X, Tolerance, "the left edge of the image at the far end of the field");
            Assert.AreEqual(FieldWidth / 2f, right.X, Tolerance);
            Assert.AreEqual(0f, center.X, Tolerance);
        }

        [Test]
        public void HandToField_FieldNarrowsTowardTheCamera_SoTheThirdsStayOnScreen()
        {
            var layout = Layout();

            var nearLeft = layout.HandToField(0f, 1f);
            var farLeft = layout.HandToField(0f, 0f);

            Assert.AreEqual(-FieldWidth / 2f * NearZ / FarZ, nearLeft.X, Tolerance);
            Assert.Less(farLeft.X, nearLeft.X);
            Assert.AreEqual(layout.LateralFraction(nearLeft.X, nearLeft.Z), layout.LateralFraction(farLeft.X, farLeft.Z), Tolerance, "the same share of the screen width at every depth");
        }

        [Test]
        public void HandToField_ClampsOutOfRangeInput()
        {
            var layout = Layout();

            var corner = layout.HandToField(-3f, 7f);

            Assert.AreEqual(NearZ, corner.Z, Tolerance);
            Assert.AreEqual(-FieldWidth / 2f * NearZ / FarZ, corner.X, Tolerance);
        }

        [Test]
        public void HandYForDepth_IsTheInverseOfTheDepthMapping()
        {
            var layout = Layout();

            for (var z = NearZ; z <= FarZ; z += 1.5f)
            {
                var y = layout.HandYForDepth(z);
                Assert.AreEqual(z, layout.HandToField(0.5f, y).Z, 1e-3f, $"z {z}");
            }
            Assert.AreEqual(1f, layout.HandYForDepth(NearZ), Tolerance);
            Assert.AreEqual(0f, layout.HandYForDepth(FarZ), Tolerance);
        }

        [Test]
        public void ScreenYForDepth_NearIsTheBottomEdge_AndTheGroundStaysBelowTheHorizon()
        {
            var layout = Layout();

            Assert.AreEqual(1f, layout.ScreenYForDepth(NearZ), Tolerance);
            Assert.Greater(layout.ScreenYForDepth(FarZ), HorizonY);
            Assert.AreEqual(layout.FarScreenY, layout.ScreenYForDepth(FarZ), Tolerance);
            Assert.Less(layout.ScreenYForDepth(10f), layout.ScreenYForDepth(5f), "farther ground is higher on the screen");
        }

        [Test]
        public void HandToField_SpreadsTheHandRangeEvenlyOverTheScreenRows()
        {
            var layout = Layout();

            var rows = new System.Collections.Generic.List<float>();
            for (var i = 0; i <= 4; i++) rows.Add(layout.ScreenYForDepth(layout.HandToField(0.5f, i / 4f).Z));

            var step = rows[1] - rows[0];
            for (var i = 2; i < rows.Count; i++) Assert.AreEqual(step, rows[i] - rows[i - 1], 1e-3f, "equal hand steps are equal screen steps");
        }

        [Test]
        public void DepthForPitchStep_HigherIsFarther()
        {
            var layout = Layout();
            const int steps = 10;

            var previous = float.NegativeInfinity;
            for (var step = 0; step < steps; step++)
            {
                var z = layout.DepthForPitchStep(step, steps);
                Assert.Greater(z, previous, $"step {step}");
                Assert.GreaterOrEqual(z, NearZ);
                Assert.LessOrEqual(z, FarZ);
                previous = z;
            }
        }

        [Test]
        public void DepthForPitchStep_MatchesTheHandHeightThatPlaysThatStep()
        {
            var layout = Layout();
            const int steps = 10;

            for (var step = 0; step < steps; step++)
            {
                var z = layout.DepthForPitchStep(step, steps);
                var handY = 1f - (PitchHeightMap.VerticalMargin + (step + 0.5f) / steps * PitchHeightMap.UsableHeight);
                Assert.AreEqual(layout.HandToField(0.5f, handY).Z, z, Tolerance, $"step {step}");
                Assert.AreEqual(step, PitchHeightMap.StepForHeight(1f - handY, steps), $"the hand height of step {step} plays step {step}");
            }
        }

        [Test]
        public void LateralFraction_IsHalfOnTheCenterLine()
        {
            Assert.AreEqual(0.5f, Layout().LateralFraction(0f, 12f), Tolerance);
        }

        [TestCase(0f, 2f, 38f, 0.28f)]
        [TestCase(40f, 0f, 38f, 0.28f)]
        [TestCase(40f, 5f, 5f, 0.28f)]
        [TestCase(40f, 2f, 38f, 0f)]
        [TestCase(40f, 2f, 38f, 1f)]
        public void Constructor_RejectsNonsense(float width, float near, float far, float horizon)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MeadowLayout(width, near, far, horizon));
        }
    }
}
