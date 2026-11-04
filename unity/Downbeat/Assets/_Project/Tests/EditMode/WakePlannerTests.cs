using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>The waking moment (a hand comes into an idle meadow): which buds glint, in what order, and where the wave starts from.</summary>
    public class WakePlannerTests
    {
        const float Radius = 3.5f;
        const float Speed = 3f;
        const int MaxFlowers = 20;

        static WakeSettings Settings(float jitter = 0f, int max = MaxFlowers) => new WakeSettings(Radius, Speed, max, jitter);

        /// <summary>Flowers on a regular grid, one per <paramref name="step"/> metres, over x in [-8, 8] and z in [2, 8].</summary>
        static FlowerInstance[] Grid(float step)
        {
            var list = new List<FlowerInstance>();
            for (var x = -8f; x <= 8f; x += step)
            {
                for (var z = 2f; z <= 8f; z += step) list.Add(new FlowerInstance(FlowerSpecies.Poppy, x, z, 1f));
            }
            return list.ToArray();
        }

        static float Distance(FlowerInstance flower, FieldPoint at)
        {
            var dx = flower.X - at.X;
            var dz = flower.Z - at.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public void Plans_nothing_when_no_flower_is_near()
        {
            var flowers = new[] { new FlowerInstance(FlowerSpecies.Poppy, 7f, 7f, 1f) };
            var output = new List<WakeBloom>();

            WakePlanner.Plan(flowers, new FieldPoint(-6f, 2.5f), Settings(), output);

            Assert.IsEmpty(output);
        }

        [Test]
        public void Plans_nothing_for_an_empty_field()
        {
            var output = new List<WakeBloom> { new WakeBloom(99, 9f) };

            WakePlanner.Plan(new FlowerInstance[0], new FieldPoint(0f, 4f), Settings(), output);

            Assert.IsEmpty(output, "the output is cleared first");
        }

        [Test]
        public void A_flower_outside_the_radius_is_left_asleep()
        {
            var origin = new FieldPoint(0f, 4f);
            var flowers = Grid(0.5f);
            var output = new List<WakeBloom>();

            WakePlanner.Plan(flowers, origin, Settings(), output);

            Assert.IsNotEmpty(output);
            foreach (var bloom in output) Assert.LessOrEqual(Distance(flowers[bloom.Flower], origin), Radius + 1e-4f);
        }

        [Test]
        public void The_wave_spreads_outward_at_the_set_speed()
        {
            var origin = new FieldPoint(0f, 4f);
            var flowers = new[]
            {
                new FlowerInstance(FlowerSpecies.Poppy, 0f, 4.5f, 1f),   // 0.5 m
                new FlowerInstance(FlowerSpecies.Dandelion, 1.5f, 4f, 1f), // 1.5 m
                new FlowerInstance(FlowerSpecies.Bellflower, -3f, 4f, 1f), // 3 m
            };
            var output = new List<WakeBloom>();

            WakePlanner.Plan(flowers, origin, Settings(), output);

            Assert.AreEqual(3, output.Count);
            Assert.AreEqual(0, output[0].Flower);
            Assert.AreEqual(0.5f / Speed, output[0].DelaySec, 1e-4f);
            Assert.AreEqual(1.5f / Speed, output[1].DelaySec, 1e-4f);
            Assert.AreEqual(3f / Speed, output[2].DelaySec, 1e-4f);
        }

        [Test]
        public void Every_species_glints_not_only_one()
        {
            var flowers = new[]
            {
                new FlowerInstance(FlowerSpecies.Poppy, 0.2f, 4f, 1f),
                new FlowerInstance(FlowerSpecies.Dandelion, -0.4f, 4f, 1f),
                new FlowerInstance(FlowerSpecies.Bellflower, 0.9f, 4f, 1f),
            };
            var output = new List<WakeBloom>();

            WakePlanner.Plan(flowers, new FieldPoint(0f, 4f), Settings(), output);

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, output.Select(b => b.Flower).ToArray());
        }

        [Test]
        public void A_thick_meadow_is_thinned_evenly_to_the_cap()
        {
            var origin = new FieldPoint(0f, 4f);
            var flowers = Grid(0.25f);   // about 150 flowers inside the radius
            var output = new List<WakeBloom>();

            WakePlanner.Plan(flowers, origin, Settings(), output);

            Assert.LessOrEqual(output.Count, MaxFlowers);
            Assert.GreaterOrEqual(output.Count, MaxFlowers / 2, "thinning keeps most of the cap, it does not starve the wave");
            var farthest = output.Max(b => Distance(flowers[b.Flower], origin));
            Assert.Greater(farthest, Radius * 0.7f, "the wave reaches out to the rim, not only the few nearest flowers");
        }

        [Test]
        public void A_thin_meadow_and_a_thick_one_wake_over_the_same_time()
        {
            var origin = new FieldPoint(0f, 4f);
            var thin = new List<WakeBloom>();
            var thick = new List<WakeBloom>();

            WakePlanner.Plan(Grid(0.6f), origin, Settings(), thin);
            WakePlanner.Plan(Grid(0.2f), origin, Settings(), thick);

            Assert.AreEqual(thick.Max(b => b.DelaySec), thin.Max(b => b.DelaySec), 0.35f, "same duration on Low (few flowers) and High (many)");
        }

        [Test]
        public void The_whole_wave_is_over_within_the_radius_over_the_speed()
        {
            var output = new List<WakeBloom>();

            WakePlanner.Plan(Grid(0.25f), new FieldPoint(0f, 4f), Settings(), output);

            Assert.LessOrEqual(output.Max(b => b.DelaySec), Radius / Speed + 1e-4f);
            Assert.Less(Radius / Speed, 2f, "a wake of 1 to 2 s");
        }

        [Test]
        public void Jitter_only_delays_never_hurries_and_stays_within_its_bound()
        {
            const float jitter = 0.15f;
            var origin = new FieldPoint(0f, 4f);
            var flowers = Grid(0.4f);
            var plain = new List<WakeBloom>();
            var shaken = new List<WakeBloom>();

            WakePlanner.Plan(flowers, origin, Settings(0f), plain);
            WakePlanner.Plan(flowers, origin, Settings(jitter), shaken);

            Assert.AreEqual(plain.Count, shaken.Count);
            for (var i = 0; i < plain.Count; i++)
            {
                Assert.AreEqual(plain[i].Flower, shaken[i].Flower);
                Assert.GreaterOrEqual(shaken[i].DelaySec, plain[i].DelaySec);
                Assert.LessOrEqual(shaken[i].DelaySec, plain[i].DelaySec + jitter + 1e-5f);
            }
            Assert.IsTrue(plain.Select((b, i) => shaken[i].DelaySec - b.DelaySec).Distinct().Count() > 3, "the ring is not a perfect circle");
        }

        [Test]
        public void The_same_input_gives_the_same_plan()
        {
            var flowers = Grid(0.3f);
            var first = new List<WakeBloom>();
            var second = new List<WakeBloom>();

            WakePlanner.Plan(flowers, new FieldPoint(1f, 4f), Settings(0.15f), first);
            WakePlanner.Plan(flowers, new FieldPoint(1f, 4f), Settings(0.15f), second);

            Assert.AreEqual(first.Count, second.Count);
            for (var i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Flower, second[i].Flower);
                Assert.AreEqual(first[i].DelaySec, second[i].DelaySec);
            }
        }

        [Test]
        public void A_cap_of_zero_plans_nothing()
        {
            var output = new List<WakeBloom>();

            WakePlanner.Plan(Grid(0.5f), new FieldPoint(0f, 4f), Settings(0f, 0), output);

            Assert.IsEmpty(output);
        }

        [Test]
        public void The_settings_reject_nonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WakeSettings(0f, Speed, MaxFlowers, 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WakeSettings(Radius, 0f, MaxFlowers, 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WakeSettings(Radius, Speed, -1, 0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WakeSettings(Radius, Speed, MaxFlowers, -0.1f));
        }

        // ---- where the wave starts -------------------------------------------------------------------------------------------------------

        static MeadowLayout Layout() => new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);

        static HandMotionTracker TrackerWith(params Pose[] hands)
        {
            var holder = new HandSettingsHolder();
            holder.Set(new HandSettingsDto());
            var tracker = new HandMotionTracker(holder);
            tracker.Update(Frame(1, 0.0, hands));
            return tracker;
        }

        [Test]
        public void The_wave_starts_at_the_hand()
        {
            var layout = Layout();
            var tracker = TrackerWith(new Pose(0, 0.25f, 0.6f));

            var origin = WakeOrigin.Of(tracker, layout);

            var expected = layout.HandToField(0.25f, 0.6f);
            Assert.AreEqual(expected.X, origin.X, 1e-4f);
            Assert.AreEqual(expected.Z, origin.Z, 1e-4f);
        }

        [Test]
        public void With_no_hand_the_wave_starts_in_the_middle_of_the_field()
        {
            var layout = Layout();
            var tracker = TrackerWith();

            var origin = WakeOrigin.Of(tracker, layout);

            var middle = layout.HandToField(0.5f, 0.5f);
            Assert.AreEqual(middle.X, origin.X, 1e-4f);
            Assert.AreEqual(middle.Z, origin.Z, 1e-4f);
        }

        [Test]
        public void With_no_motion_source_at_all_the_wave_starts_in_the_middle()
        {
            var layout = Layout();

            var origin = WakeOrigin.Of(null, layout);

            var middle = layout.HandToField(0.5f, 0.5f);
            Assert.AreEqual(middle.X, origin.X, 1e-4f);
            Assert.AreEqual(middle.Z, origin.Z, 1e-4f);
        }

        [Test]
        public void With_two_hands_the_wave_starts_between_them()
        {
            var layout = Layout();
            var tracker = TrackerWith(new Pose(0, 0.2f, 0.5f), new Pose(1, 0.8f, 0.5f));

            var origin = WakeOrigin.Of(tracker, layout);

            var between = layout.HandToField(0.5f, 0.5f);
            Assert.AreEqual(between.X, origin.X, 1e-3f);
            Assert.AreEqual(between.Z, origin.Z, 1e-3f);
        }

        [Test]
        public void With_a_ground_finder_the_wave_starts_under_the_hand_on_the_screen()
        {
            var layout = Layout();
            var tracker = TrackerWith(new Pose(0, 0.25f, 0.8f));
            float askedX = -1f, askedY = -1f;

            var origin = WakeOrigin.Of(tracker, layout, (float x01, float y01, out FieldPoint ground) => { askedX = x01; askedY = y01; ground = new FieldPoint(-2.5f, 4.5f); return true; });

            Assert.AreEqual(0.25f, askedX, 1e-6f);
            Assert.AreEqual(0.8f, askedY, 1e-6f);
            Assert.AreEqual(-2.5f, origin.X, 1e-6f);
            Assert.AreEqual(4.5f, origin.Z, 1e-6f);
        }

        [Test]
        public void If_the_ground_cannot_be_found_the_wave_starts_where_the_hand_is_on_the_field()
        {
            var layout = Layout();
            var tracker = TrackerWith(new Pose(0, 0.25f, 0.6f));

            var origin = WakeOrigin.Of(tracker, layout, (float x01, float y01, out FieldPoint ground) => { ground = default; return false; });

            var expected = layout.HandToField(0.25f, 0.6f);
            Assert.AreEqual(expected.X, origin.X, 1e-4f);
            Assert.AreEqual(expected.Z, origin.Z, 1e-4f);
        }
    }
}
