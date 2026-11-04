using System.Diagnostics;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// The baked cloud tile of the weak quality tier: the clouds there are read from a picture of the very noise that the strong tier works out per pixel, so that they have the same
    /// shapes and move the same way, for one texture read a layer.
    /// </summary>
    public class CloudTileTests
    {
        const float OneLevel = 1f / 255f;
        static byte[] _tile;

        static byte[] Tile() => _tile ?? (_tile = CloudTile.Bake());

        static float Texel(byte[] tile, int x, int z, int channel) => tile[(z * CloudTile.Size + x) * CloudTile.Channels + channel] / 255f;

        [Test]
        public void Bake_HasOneTexelPerQuarterCell_ForTheWholePeriodOfTheNoise()
        {
            // Arrange / Act
            var tile = Tile();

            // Assert: it wraps exactly where the noise repeats, so the tile can be read with wrap mode "repeat" and never shows a seam
            Assert.AreEqual(CloudNoise.Period * CloudTile.TexelsPerCell, CloudTile.Size);
            Assert.AreEqual(CloudTile.Size * CloudTile.Size * CloudTile.Channels, tile.Length);
        }

        [Test]
        public void Bake_RedChannel_IsTheNoiseAtTheCentreOfEveryTexel()
        {
            var tile = Tile();
            var checkedTexels = 0;
            foreach (var (x, z) in new[] { (0, 0), (1, 0), (0, 1), (3, 7), (511, 300), (1023, 1023), (1000, 17), (64, 900), (777, 555) })
            {
                var cellX = (x + 0.5f) / CloudTile.TexelsPerCell;
                var cellZ = (z + 0.5f) / CloudTile.TexelsPerCell;
                Assert.AreEqual(CloudNoise.Fbm(cellX, cellZ), Texel(tile, x, z, 0), OneLevel, $"texel {x},{z}");
                checkedTexels++;
            }
            Assert.AreEqual(9, checkedTexels);
        }

        [Test]
        public void Bake_GreenChannel_IsTheSameNoiseALittleTowardsTheSun()
        {
            // the lit side of a cloud is where the noise a little towards the horizon (the shader looks at uv + (0, 0.3)) is less dense
            var tile = Tile();
            foreach (var (x, z) in new[] { (0, 0), (5, 9), (511, 300), (1023, 1023), (640, 123) })
            {
                var cellX = (x + 0.5f) / CloudTile.TexelsPerCell;
                var cellZ = (z + 0.5f) / CloudTile.TexelsPerCell + CloudTile.SunOffsetCells;
                Assert.AreEqual(CloudNoise.Fbm(cellX, cellZ), Texel(tile, x, z, 1), OneLevel, $"texel {x},{z}");
            }
        }

        [Test]
        public void Noise_RepeatsEveryPeriod_SoTheTileWrapsWithoutASeam()
        {
            foreach (var (x, z) in new[] { (0.3f, 0.7f), (12.5f, 99.1f), (200.2f, 3.9f) })
            {
                Assert.AreEqual(CloudNoise.Fbm(x, z), CloudNoise.Fbm(x + CloudNoise.Period, z), 1e-5f);
                Assert.AreEqual(CloudNoise.Fbm(x, z), CloudNoise.Fbm(x, z + CloudNoise.Period), 1e-5f);
            }
        }

        [Test]
        public void Bake_NeighbouringTexelsAcrossTheWrapEdgeDifferNoMoreThanElsewhere()
        {
            // a seam would be a jump between the last texel and the first one
            var tile = Tile();
            var worstInside = 0f;
            var worstAtEdge = 0f;
            for (var z = 0; z < CloudTile.Size; z += 7)
            {
                for (var x = 0; x < CloudTile.Size - 1; x += 5)
                    worstInside = System.Math.Max(worstInside, System.Math.Abs(Texel(tile, x, z, 0) - Texel(tile, x + 1, z, 0)));
                worstAtEdge = System.Math.Max(worstAtEdge, System.Math.Abs(Texel(tile, CloudTile.Size - 1, z, 0) - Texel(tile, 0, z, 0)));
            }
            Assert.LessOrEqual(worstAtEdge, worstInside + 2 * OneLevel);
        }

        [Test]
        public void Bake_UsesTheWholeRange_AndIsNotFlat()
        {
            var tile = Tile();
            byte low = 255;
            byte high = 0;
            for (var i = 0; i < tile.Length; i += 2)
            {
                if (tile[i] < low) low = tile[i];
                if (tile[i] > high) high = tile[i];
            }
            Assert.Less(low, 40, "there are clear patches");
            Assert.Greater(high, 215, "and dense ones");
        }

        [Test]
        public void Bake_IsReadyAtStart_InWellUnderASecond()
        {
            // Arrange / Act: the tile is made when the meadow loads, on the thread that loads it
            var clock = Stopwatch.StartNew();
            var tile = CloudTile.Bake();
            clock.Stop();

            // Assert (an editor under test is several times slower than a player: this is a generous limit)
            Assert.NotNull(tile);
            Assert.Less(clock.ElapsedMilliseconds, 900, $"took {clock.ElapsedMilliseconds} ms");
            TestContext.Out.WriteLine($"[CloudTile] baked {CloudTile.Size}x{CloudTile.Size} in {clock.ElapsedMilliseconds} ms");
        }
    }
}
