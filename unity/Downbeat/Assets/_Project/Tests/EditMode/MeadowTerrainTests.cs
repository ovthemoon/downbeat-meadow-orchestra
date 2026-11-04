using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class MeadowTerrainTests
    {
        const float MaxHeight = 1.2f;
        const int Seed = 7;
        const float MaxSlopePerMeter = 0.25f;

        [Test]
        public void Terrain_IsDeterministic()
        {
            for (var i = 0; i < 50; i++)
            {
                var x = -30f + i * 1.37f;
                var z = 1f + i * 0.81f;

                Assert.AreEqual(MeadowTerrain.Height(x, z, Seed, MaxHeight), MeadowTerrain.Height(x, z, Seed, MaxHeight));
            }
        }

        [Test]
        public void Terrain_DiffersBetweenSeeds()
        {
            var different = 0;
            for (var i = 0; i < 40; i++)
            {
                if (MeadowTerrain.Height(i * 1.9f, 3f + i, 1, MaxHeight) != MeadowTerrain.Height(i * 1.9f, 3f + i, 2, MaxHeight)) different++;
            }

            Assert.Greater(different, 30);
        }

        [Test]
        public void Terrain_StaysInRange()
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            for (var x = -40f; x <= 40f; x += 0.7f)
            {
                for (var z = -2f; z <= 48f; z += 0.7f)
                {
                    var h = MeadowTerrain.Height(x, z, Seed, MaxHeight);
                    min = System.Math.Min(min, h);
                    max = System.Math.Max(max, h);
                }
            }

            Assert.GreaterOrEqual(min, 0f);
            Assert.LessOrEqual(max, MaxHeight);
            Assert.Greater(max - min, MaxHeight * 0.4f, "the ground is not flat");
        }

        [Test]
        public void Terrain_SlopeIsGentle()
        {
            var steepest = 0f;
            for (var x = -40f; x <= 40f; x += 0.5f)
            {
                for (var z = -2f; z <= 48f; z += 0.5f)
                {
                    var h = MeadowTerrain.Height(x, z, Seed, MaxHeight);
                    steepest = System.Math.Max(steepest, System.Math.Abs(MeadowTerrain.Height(x + 1f, z, Seed, MaxHeight) - h));
                    steepest = System.Math.Max(steepest, System.Math.Abs(MeadowTerrain.Height(x, z + 1f, Seed, MaxHeight) - h));
                }
            }

            Assert.LessOrEqual(steepest, MaxSlopePerMeter, "height change over one metre");
        }

        [Test]
        public void Terrain_ScalesWithMaxHeight()
        {
            Assert.AreEqual(MeadowTerrain.Height(5f, 9f, Seed, 1f) * 2f, MeadowTerrain.Height(5f, 9f, Seed, 2f), 1e-5f);
        }

        // ---- the near undulation (Part D, #3): hummocks you can see, the grass still planted, the hand-over to the plate as it was ----------------------

        static readonly TerrainRelief Hummocks = new TerrainRelief(0.1f, 2.6f, 3.4f, 5.2f);

        [Test]
        public void TheNearGround_UndulatesByAboutTheReliefHeight_AndNoMore()
        {
            var largest = 0f;
            for (var x = -6f; x <= 6f; x += 0.1f)
            {
                for (var z = 1f; z <= 3.4f; z += 0.1f) largest = System.Math.Max(largest, System.Math.Abs(MeadowTerrain.Relief(x, z, Seed, Hummocks)));
            }

            Assert.GreaterOrEqual(largest, 0.6f * Hummocks.Height, "real hummocks near the camera");
            Assert.LessOrEqual(largest, Hummocks.Height + 1e-5f, "and gentle");
        }

        [Test]
        public void TheRelief_IsGoneBeyondItsEnd_SoTheGroundWhereItHandsOverToThePlateIsAsItWas()
        {
            for (var x = -20f; x <= 20f; x += 0.9f)
            {
                for (var z = Hummocks.ToZ; z <= 30f; z += 0.9f)
                {
                    Assert.AreEqual(MeadowTerrain.Height(x, z, Seed, MaxHeight), MeadowTerrain.Height(x, z, Seed, MaxHeight, Hummocks), "no change at depth " + z);
                }
            }
        }

        [Test]
        public void TheRelief_FadesWithoutASeam()
        {
            for (var x = -8f; x <= 8f; x += 1.3f)
            {
                for (var z = 1f; z < 6f; z += 0.02f)
                {
                    var step = System.Math.Abs(MeadowTerrain.Relief(x, z + 0.02f, Seed, Hummocks) - MeadowTerrain.Relief(x, z, Seed, Hummocks));
                    Assert.Less(step, 0.01f, $"a smooth surface at ({x:0.0}, {z:0.00})");
                }
                Assert.AreEqual(0f, MeadowTerrain.Relief(x, Hummocks.ToZ, Seed, Hummocks), 1e-6f, "nothing where it ends");
            }
        }

        [Test]
        public void TheGround_StaysGentle_WithTheRelief()
        {
            var steepest = 0f;
            var steepestRelief = 0f;
            const float Step = 0.1f;
            for (var x = -8f; x <= 8f; x += 0.3f)
            {
                for (var z = -1f; z <= 8f; z += 0.3f)
                {
                    var dx = (MeadowTerrain.Height(x + Step, z, Seed, MaxHeight, Hummocks) - MeadowTerrain.Height(x - Step, z, Seed, MaxHeight, Hummocks)) / (2f * Step);
                    var dz = (MeadowTerrain.Height(x, z + Step, Seed, MaxHeight, Hummocks) - MeadowTerrain.Height(x, z - Step, Seed, MaxHeight, Hummocks)) / (2f * Step);
                    steepest = System.Math.Max(steepest, (float)System.Math.Sqrt(dx * dx + dz * dz));
                    var rx = (MeadowTerrain.Relief(x + Step, z, Seed, Hummocks) - MeadowTerrain.Relief(x - Step, z, Seed, Hummocks)) / (2f * Step);
                    var rz = (MeadowTerrain.Relief(x, z + Step, Seed, Hummocks) - MeadowTerrain.Relief(x, z - Step, Seed, Hummocks)) / (2f * Step);
                    steepestRelief = System.Math.Max(steepestRelief, (float)System.Math.Sqrt(rx * rx + rz * rz));
                }
            }

            Assert.Less(steepestRelief, 0.2f, "the hummocks alone rise at most 0.2 m over a metre");
            Assert.Less(steepest, 0.4f, "and with the rolling ground the slope is still gentle");
        }

        [Test]
        public void TheRelief_IsRepeatable_DiffersBetweenSeeds_AndNoneMeansTheOldGround()
        {
            var different = 0;
            for (var i = 0; i < 40; i++)
            {
                var x = -6f + i * 0.3f;
                Assert.AreEqual(MeadowTerrain.Relief(x, 2f, Seed, Hummocks), MeadowTerrain.Relief(x, 2f, Seed, Hummocks));
                if (MeadowTerrain.Relief(x, 2f, 1, Hummocks) != MeadowTerrain.Relief(x, 2f, 2, Hummocks)) different++;
                Assert.AreEqual(MeadowTerrain.Height(x, 2f, Seed, MaxHeight), MeadowTerrain.Height(x, 2f, Seed, MaxHeight, TerrainRelief.None));
            }
            Assert.Greater(different, 30);
        }
    }
}
