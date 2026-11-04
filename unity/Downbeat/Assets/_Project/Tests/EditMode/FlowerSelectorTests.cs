using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class FlowerSelectorTests
    {
        static FlowerInstance[] Garden() => new[]
        {
            new FlowerInstance(FlowerSpecies.Bellflower, -4f, 5f, 1f),   // 0
            new FlowerInstance(FlowerSpecies.Dandelion, 0.1f, 5.1f, 1f), // 1  closest to the target but the wrong species
            new FlowerInstance(FlowerSpecies.Bellflower, -3f, 5f, 1f),   // 2  the nearest bellflower
            new FlowerInstance(FlowerSpecies.Bellflower, -1f, 6f, 1f),   // 3
            new FlowerInstance(FlowerSpecies.Bellflower, 3f, 3f, 1f),    // 4
            new FlowerInstance(FlowerSpecies.Poppy, -3f, 5f, 1f),        // 5
        };

        [Test]
        public void Selector_ReturnsNearestOfSpecies_NearestFirst()
        {
            var output = new List<int>();

            FlowerSelector.Select(Garden(), FlowerSpecies.Bellflower, new FieldPoint(-3.2f, 5f), 3, output);

            CollectionAssert.AreEqual(new[] { 2, 0, 3 }, output);
        }

        [Test]
        public void Selector_NeverReturnsAFlowerOfAnotherSpecies()
        {
            var output = new List<int>();

            FlowerSelector.Select(Garden(), FlowerSpecies.Poppy, new FieldPoint(0f, 5f), 4, output);

            CollectionAssert.AreEqual(new[] { 5 }, output, "only one poppy exists: the others are not poppies");
        }

        [Test]
        public void Selector_GivesWhatThereIs_WhenFewerFlowersExistThanAsked()
        {
            var output = new List<int>();

            FlowerSelector.Select(Garden(), FlowerSpecies.Dandelion, new FieldPoint(0f, 5f), 6, output);

            CollectionAssert.AreEqual(new[] { 1 }, output);
        }

        [Test]
        public void Selector_ClearsTheOutputFirst_AndAcceptsZero()
        {
            var output = new List<int> { 99, 98 };

            FlowerSelector.Select(Garden(), FlowerSpecies.Bellflower, new FieldPoint(0f, 0f), 0, output);

            Assert.IsEmpty(output);
        }

        [Test]
        public void Selector_CountGrowsWithVelocity()
        {
            Assert.AreEqual(1, FlowerSelector.CountFor(0f, 6));
            Assert.AreEqual(4, FlowerSelector.CountFor(0.5f, 6));
            Assert.AreEqual(7, FlowerSelector.CountFor(1f, 6));
            Assert.Less(FlowerSelector.CountFor(0.2f, 6), FlowerSelector.CountFor(0.9f, 6));
            Assert.AreEqual(1, FlowerSelector.CountFor(-3f, 6), "a velocity below 0 counts as 0");
            Assert.AreEqual(7, FlowerSelector.CountFor(5f, 6), "and above 1 as 1");
        }

        [Test]
        public void Selector_OnAWholeGarden_PicksTheNearestOfTheRightKind()
        {
            var layout = new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);
            var flowers = FlowerPlacement.Generate(7, 1800, layout);
            var target = new FieldPoint(-3f, 5f);
            var output = new List<int>();

            FlowerSelector.Select(flowers, FlowerSpecies.Bellflower, target, 6, output);

            Assert.AreEqual(6, output.Count);
            var previous = -1f;
            foreach (var index in output)
            {
                Assert.AreEqual(FlowerSpecies.Bellflower, flowers[index].Species);
                var d = (flowers[index].X - target.X) * (flowers[index].X - target.X) + (flowers[index].Z - target.Z) * (flowers[index].Z - target.Z);
                Assert.GreaterOrEqual(d, previous - 1e-6f, "nearest first");
                previous = d;
            }
            var farthest = previous;
            for (var i = 0; i < flowers.Length; i++)
            {
                if (flowers[i].Species != FlowerSpecies.Bellflower || output.Contains(i)) continue;
                var d = (flowers[i].X - target.X) * (flowers[i].X - target.X) + (flowers[i].Z - target.Z) * (flowers[i].Z - target.Z);
                Assert.GreaterOrEqual(d, farthest - 1e-6f, "no unchosen bellflower is closer than a chosen one");
            }
        }
    }
}
