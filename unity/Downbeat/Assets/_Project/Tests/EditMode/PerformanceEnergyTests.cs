using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class PerformanceEnergyTests
    {
        static EnergySettings Settings() => new EnergySettings(0.06f, 0.06f, 6f, 0.2f, 0.45f, 0.7f, 0.05f);

        [Test]
        public void Energy_RisesWithNotes_DecaysWithHalfLife()
        {
            var energy = new PerformanceEnergy(Settings());

            energy.AddNote(0f);
            Assert.AreEqual(0.06f, energy.Value, 1e-6f, "a note of velocity 0 adds 0.06");
            energy.AddNote(1f);
            Assert.AreEqual(0.06f + 0.12f, energy.Value, 1e-6f, "a note of velocity 1 adds 0.12");

            var before = energy.Value;
            energy.Advance(6.0);
            Assert.AreEqual(before / 2f, energy.Value, 1e-5f, "half after the half-life of 6 s");
            energy.Advance(6.0);
            Assert.AreEqual(before / 4f, energy.Value, 1e-5f);
        }

        [Test]
        public void Energy_IsClampedToOne_AndNeverNegative()
        {
            var energy = new PerformanceEnergy(Settings());
            for (var i = 0; i < 40; i++) energy.AddNote(1f);
            Assert.AreEqual(1f, energy.Value, 1e-6f);

            energy.Advance(1000.0);
            Assert.GreaterOrEqual(energy.Value, 0f);
            Assert.Less(energy.Value, 1e-6f);
        }

        [Test]
        public void Advance_ByZeroOrNegativeTime_ChangesNothing()
        {
            var energy = new PerformanceEnergy(Settings());
            energy.AddNote(0.5f);
            var value = energy.Value;

            energy.Advance(0.0);
            energy.Advance(-3.0);

            Assert.AreEqual(value, energy.Value, 1e-7f);
        }

        [Test]
        public void Level_FollowsThePlannedThresholds()
        {
            var energy = new PerformanceEnergy(Settings());
            Assert.AreEqual(0, energy.Level);

            Raise(energy, 0.21f);
            Assert.AreEqual(1, energy.Level, "0.2 starts level 1");
            Raise(energy, 0.46f);
            Assert.AreEqual(2, energy.Level, "0.45 starts level 2");
            Raise(energy, 0.71f);
            Assert.AreEqual(3, energy.Level, "0.7 starts level 3");
        }

        static void Raise(PerformanceEnergy energy, float target)
        {
            while (energy.Value < target) energy.AddNote(1f);
        }

        [Test]
        public void Energy_LevelUsesHysteresis()
        {
            var energy = new PerformanceEnergy(Settings());
            Raise(energy, 0.46f);
            Assert.AreEqual(2, energy.Level);

            // just under the level-2 threshold of 0.45 but within the 0.05 band: still level 2
            energy.Set(0.43f);
            Assert.AreEqual(2, energy.Level, "0.43 is inside the hysteresis band of level 2 (0.40..0.45)");
            energy.Set(0.39f);
            Assert.AreEqual(1, energy.Level, "below 0.45 - 0.05 it falls to level 1");

            // and level 1 holds down to 0.15
            energy.Set(0.17f);
            Assert.AreEqual(1, energy.Level);
            energy.Set(0.14f);
            Assert.AreEqual(0, energy.Level);
        }

        [Test]
        public void Level_FallsOneLevelAtATime_InReverseOrder()
        {
            var energy = new PerformanceEnergy(Settings());
            Raise(energy, 0.9f);
            Assert.AreEqual(3, energy.Level);
            var seen = new System.Collections.Generic.List<int> { energy.Level };

            for (var t = 0.0; t < 60.0; t += 0.25)
            {
                energy.Advance(0.25);
                if (energy.Level != seen[seen.Count - 1]) seen.Add(energy.Level);
            }

            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, seen, "hills leave first, then the sky, then the fireflies");
        }

        [Test]
        public void Settings_RejectNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnergySettings(0.06f, 0.06f, 0f, 0.2f, 0.45f, 0.7f, 0.05f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnergySettings(0.06f, 0.06f, 6f, 0.5f, 0.45f, 0.7f, 0.05f), "thresholds must ascend");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnergySettings(0.06f, 0.06f, 6f, 0.2f, 0.45f, 0.7f, 0.3f), "the hysteresis must be smaller than the gap between levels");
        }
    }
}
