using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class MeadowFieldsTests
    {
        const int Seed = 7;
        const float Scale = 6f;

        [Test]
        public void Hummock_IsDeterministic()
        {
            for (var i = 0; i < 40; i++)
            {
                Assert.AreEqual(MeadowFields.Hummock(i * 1.3f, 2f + i * 0.7f, Seed, Scale), MeadowFields.Hummock(i * 1.3f, 2f + i * 0.7f, Seed, Scale));
            }
        }

        [Test]
        public void Hummock_StaysInZeroToOne()
        {
            for (var x = -20f; x <= 20f; x += 0.5f)
            {
                for (var z = 0f; z <= 20f; z += 0.5f)
                {
                    var h = MeadowFields.Hummock(x, z, Seed, Scale);
                    Assert.GreaterOrEqual(h, 0f);
                    Assert.LessOrEqual(h, 1f);
                }
            }
        }

        [Test]
        public void Hummock_FormsRisesAndHollows_NotAFlatField()
        {
            float sum = 0f, sumSquares = 0f;
            var n = 0;
            for (var x = -20f; x <= 20f; x += 0.5f)
            {
                for (var z = 0f; z <= 20f; z += 0.5f)
                {
                    var h = MeadowFields.Hummock(x, z, Seed, Scale);
                    sum += h;
                    sumSquares += h * h;
                    n++;
                }
            }

            var mean = sum / n;
            var std = (float)System.Math.Sqrt(sumSquares / n - mean * mean);
            Assert.Greater(std, 0.18f, "hollows and rises differ clearly");
            Assert.That(mean, Is.InRange(0.3f, 0.7f), "neither all hollow nor all rise");
        }

        [Test]
        public void Hummock_ChangesSmoothlyOverAFewCentimetres_AndOverSeveralMetres()
        {
            var largest = 0f;
            for (var x = -15f; x <= 15f; x += 0.25f)
            {
                for (var z = 0f; z <= 15f; z += 0.25f)
                {
                    largest = System.Math.Max(largest, System.Math.Abs(MeadowFields.Hummock(x + 0.1f, z, Seed, Scale) - MeadowFields.Hummock(x, z, Seed, Scale)));
                }
            }
            Assert.Less(largest, 0.2f, "no jumps between neighbouring blades");

            var far = 0f;
            for (var i = 0; i < 100; i++) far += System.Math.Abs(MeadowFields.Hummock(i * 0.37f, 3f, Seed, Scale) - MeadowFields.Hummock(i * 0.37f + 9f, 3f, Seed, Scale));
            Assert.Greater(far / 100f, 0.1f, "ground 9 m apart is not the same hummock");
        }

        [Test]
        public void Hummock_ScaleStretchesTheFeatures()
        {
            static float MeanStep(float scale)
            {
                var sum = 0f;
                for (var i = 0; i < 400; i++) sum += System.Math.Abs(MeadowFields.Hummock(i * 0.2f + 0.1f, 4f, Seed, scale) - MeadowFields.Hummock(i * 0.2f, 4f, Seed, scale));
                return sum / 400f;
            }

            Assert.Less(MeanStep(12f), MeanStep(3f), "bigger hummocks change more slowly");
        }

        [Test]
        public void FadeWeight_IsOneBeforeTheBandAndZeroAfter_AndFallsSmoothly()
        {
            Assert.AreEqual(1f, MeadowFields.FadeWeight(3f, 6.6f, 9.6f), 1e-6f);
            Assert.AreEqual(1f, MeadowFields.FadeWeight(6.6f, 6.6f, 9.6f), 1e-6f);
            Assert.AreEqual(0f, MeadowFields.FadeWeight(9.6f, 6.6f, 9.6f), 1e-6f);
            Assert.AreEqual(0f, MeadowFields.FadeWeight(14f, 6.6f, 9.6f), 1e-6f);
            Assert.AreEqual(0.5f, MeadowFields.FadeWeight(8.1f, 6.6f, 9.6f), 1e-6f);
            var previous = 1f;
            for (var z = 6.6f; z <= 9.6f; z += 0.1f)
            {
                var w = MeadowFields.FadeWeight(z, 6.6f, 9.6f);
                Assert.LessOrEqual(w, previous + 1e-6f);
                previous = w;
            }
        }
    }
}
