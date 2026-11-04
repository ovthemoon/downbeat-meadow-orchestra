using System.Collections.Generic;
using Downbeat.Core;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    public class FlowerPlacementTests
    {
        static MeadowLayout Layout() => new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);

        [Test]
        public void Placement_IsDeterministic()
        {
            var a = FlowerPlacement.Generate(7, 500, Layout());
            var b = FlowerPlacement.Generate(7, 500, Layout());

            Assert.AreEqual(a.Length, b.Length);
            for (var i = 0; i < a.Length; i++) Assert.AreEqual(a[i], b[i], $"flower {i}");
        }

        [Test]
        public void Placement_DiffersBetweenSeeds()
        {
            var a = FlowerPlacement.Generate(1, 200, Layout());
            var b = FlowerPlacement.Generate(2, 200, Layout());

            var same = 0;
            for (var i = 0; i < a.Length; i++) same += a[i].Equals(b[i]) ? 1 : 0;
            Assert.Less(same, 5);
        }

        [Test]
        public void Placement_MakesTheRequestedNumber_InsideTheField()
        {
            var layout = Layout();
            var flowers = FlowerPlacement.Generate(7, 800, layout);

            Assert.AreEqual(800, flowers.Length);
            foreach (var flower in flowers)
            {
                Assert.GreaterOrEqual(flower.Z, layout.NearZ - 1e-3f);
                Assert.LessOrEqual(flower.Z, layout.FarZ + 1e-3f);
                Assert.LessOrEqual(System.Math.Abs(flower.X), layout.HalfWidthAt(flower.Z) + 1e-3f, "inside the wedge that follows the view");
                Assert.That(flower.Scale, Is.InRange(0.5f, 1.6f));
            }
        }

        [Test]
        public void Placement_LeftThirdIsMostlyBellflower()
        {
            var layout = Layout();
            var flowers = FlowerPlacement.Generate(7, 3000, layout);

            var inThird = 0;
            var bellflowers = 0;
            foreach (var flower in flowers)
            {
                if (layout.LateralFraction(flower.X, flower.Z) >= 1f / 3f) continue;
                inThird++;
                if (flower.Species == FlowerSpecies.Bellflower) bellflowers++;
            }

            Assert.Greater(inThird, 500);
            Assert.GreaterOrEqual(bellflowers / (float)inThird, 0.8f);
        }

        [Test]
        public void Placement_MiddleThirdIsMostlyDandelion_RightThirdMostlyPoppy()
        {
            var layout = Layout();
            var flowers = FlowerPlacement.Generate(7, 3000, layout);
            int middle = 0, dandelions = 0, right = 0, poppies = 0;
            foreach (var flower in flowers)
            {
                var u = layout.LateralFraction(flower.X, flower.Z);
                if (u >= 1f / 3f && u < 2f / 3f) { middle++; if (flower.Species == FlowerSpecies.Dandelion) dandelions++; }
                if (u >= 2f / 3f) { right++; if (flower.Species == FlowerSpecies.Poppy) poppies++; }
            }

            Assert.GreaterOrEqual(dandelions / (float)middle, 0.8f);
            Assert.GreaterOrEqual(poppies / (float)right, 0.8f);
        }

        [Test]
        public void Placement_SpreadsOverTheDepthOfTheField_NotOnlyTheNearEdge()
        {
            var layout = Layout();
            var flowers = FlowerPlacement.Generate(7, 1000, layout);
            var near = 0;
            var far = 0;
            foreach (var flower in flowers)
            {
                var row = layout.ScreenYForDepth(flower.Z);
                if (row > 0.8f) near++;
                if (row < 0.65f) far++;
            }

            Assert.Greater(near, 150, "flowers near the viewer");
            Assert.Greater(far, 150, "and in the distance: the picture is evenly sown");
        }

        [Test]
        public void Placement_ClumpsInDrifts_InsteadOfSpreadingEvenly()
        {
            var layout = Layout();
            var flowers = FlowerPlacement.Generate(7, 1500, layout);
            const int columns = 10;
            const int rows = 6;
            var cells = new int[columns * rows];
            foreach (var flower in flowers)
            {
                var u = Mathf.Clamp01(layout.LateralFraction(flower.X, flower.Z));
                var v = Mathf.Clamp01((layout.ScreenYForDepth(flower.Z) - layout.FarScreenY) / (1f - layout.FarScreenY));
                cells[Mathf.Min(rows - 1, (int)(v * rows)) * columns + Mathf.Min(columns - 1, (int)(u * columns))]++;
            }

            var mean = cells.Average();
            var variance = cells.Select(c => (c - mean) * (c - mean)).Average();

            Assert.Greater(variance / mean, 2.5, "index of dispersion: an even (Poisson) sowing gives about 1, drifts give much more");
        }

        [Test]
        public void Placement_HeightsVary_AndManyFlowersSitLow()
        {
            var flowers = FlowerPlacement.Generate(7, 1000, Layout());

            var low = flowers.Count(f => f.Height < 0.75f);
            Assert.That(flowers.All(f => f.Height >= FlowerPlacement.MinHeight - 1e-4f && f.Height <= FlowerPlacement.MaxHeight + 1e-4f));
            Assert.Greater(low, 200, "many stand low, partly hidden in the grass");
            Assert.Greater(flowers.Count(f => f.Height > 0.95f), 100, "and some stand tall");
        }

        [Test]
        public void Placement_ZeroCountGivesNothing()
        {
            Assert.AreEqual(0, FlowerPlacement.Generate(7, 0, Layout()).Length);
        }
    }
}
