using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The size floor of far particles: a seed stays at least a few pixels across at any distance and any render scale, and nothing near changes.</summary>
    public class ParticleSizeFloorTests
    {
        const float Seed = 0.032f;
        const float M11 = 2.1445f;      // 1 / tan(25 deg): the vertical field of view of the meadow camera is 50 degrees
        const float Height = 1080f;
        const float Floor = 3.5f;

        static float Pixels(float size, float depth, float height = Height) => size * M11 * 0.5f * height / depth;

        [Test]
        public void ASeedThatIsFarAway_IsBroughtUpToTheFloor_ExactlyThatMany_PixelsAcross()
        {
            var depth = 14f;
            Assert.Less(Pixels(Seed, depth), Floor, "without the floor it is under the floor");

            var scale = ParticleSizeFloor.Scale(Seed, depth, M11, Height, Floor);

            Assert.Greater(scale, 1f);
            Assert.AreEqual(Floor, Pixels(Seed * scale, depth), 1e-3f);
        }

        [Test]
        public void ASeedThatIsNear_IsNotChanged()
        {
            Assert.AreEqual(1f, ParticleSizeFloor.Scale(Seed, 3f, M11, Height, Floor));
            Assert.AreEqual(1f, ParticleSizeFloor.Scale(Seed, 6f, M11, Height, Floor));
        }

        [Test]
        public void TheFactor_IsContinuous_NeverBelowOne_AndGrowsWithTheDistance()
        {
            var edge = Seed * M11 * 0.5f * Height / Floor;   // the depth at which a seed is exactly at the floor
            var last = 1f;

            Assert.AreEqual(1f, ParticleSizeFloor.Scale(Seed, edge * 0.999f, M11, Height, Floor), 2e-3f, "no pop where the floor begins");
            for (var depth = 1f; depth <= 60f; depth += 0.25f)
            {
                var scale = ParticleSizeFloor.Scale(Seed, depth, M11, Height, Floor);
                Assert.GreaterOrEqual(scale, 1f);
                Assert.GreaterOrEqual(scale, last - 1e-6f, "farther, never smaller");
                last = scale;
            }
        }

        [Test]
        public void ALowerRenderScale_NeedsTheSameNumberOfPixels_SoItBringsTheSeedUpEarlier()
        {
            var depth = 9f;

            var full = ParticleSizeFloor.Scale(Seed, depth, M11, Height, Floor);
            var low = ParticleSizeFloor.Scale(Seed, depth, M11, Height * 0.7f, Floor);

            Assert.AreEqual(1f, full, "at 9 m on the full picture a seed is already 3.8 pixels");
            Assert.Greater(low, 1f, "on a picture 0.7 times as high it is 2.7");
        }

        [TestCase(0f, 14f, 2f, 1080f, 3.5f)]
        [TestCase(0.03f, 0f, 2f, 1080f, 3.5f)]
        [TestCase(0.03f, 14f, 0f, 1080f, 3.5f)]
        [TestCase(0.03f, 14f, 2f, 0f, 3.5f)]
        [TestCase(0.03f, 14f, 2f, 1080f, 0f)]
        public void WithoutAFloor_OrWithNonsense_NothingChanges(float size, float depth, float m11, float height, float floor)
        {
            Assert.AreEqual(1f, ParticleSizeFloor.Scale(size, depth, m11, height, floor));
        }
    }
}
