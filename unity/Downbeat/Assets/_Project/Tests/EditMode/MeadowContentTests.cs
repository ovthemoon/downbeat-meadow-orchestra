using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
namespace Downbeat.Tests
{
    public class MeadowContentTests
    {
        static ContentDto Shipped() => TestContent.LoadShipped();

        static bool Reports(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        [Test]
        public void The_shipped_meadow_block_is_valid()
        {
            var errors = ContentValidator.Validate(Shipped());

            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [Test]
        public void The_shipped_content_has_no_character_theme_any_more()
        {
            var json = System.IO.File.ReadAllText(TestContent.RepoContentPath);

            StringAssert.DoesNotContain("\"performers\"", json);
            StringAssert.DoesNotContain("\"theme\"", json);
        }

        [Test]
        public void The_shipped_layout_builds_a_MeadowLayout()
        {
            var layout = Shipped().meadow.layout;

            Assert.DoesNotThrow(() => new MeadowLayout(layout.fieldWidth, layout.nearZ, layout.farZ, layout.horizonScreenY));
        }

        [Test]
        public void The_three_sky_stops_come_in_order_and_carry_a_plate_each()
        {
            var stops = Shipped().meadow.sky.stops;

            CollectionAssert.AreEqual(new[] { "blueHour", "mid", "sunset" }, stops.Select(s => s.id).ToArray());
            Assert.AreEqual(3, stops.Select(s => s.backdrop).Distinct().Count(), "three different plates");
            Assert.Less(stops[0].lightIntensity, stops[2].lightIntensity, "the climax is brighter than the quiet start");
        }

        [Test]
        public void Two_sky_stops_are_reported()
        {
            var content = Shipped();
            content.meadow.sky.stops = content.meadow.sky.stops.Take(2).ToArray();

            Assert.IsTrue(Reports(content, "exactly 3 stops"));
        }

        [Test]
        public void Stops_out_of_order_are_reported()
        {
            var content = Shipped();
            (content.meadow.sky.stops[0], content.meadow.sky.stops[2]) = (content.meadow.sky.stops[2], content.meadow.sky.stops[0]);

            Assert.IsTrue(Reports(content, "must be 'blueHour'"));
        }

        [Test]
        public void A_bad_colour_is_reported_with_its_name()
        {
            var content = Shipped();
            content.meadow.sky.stops[1].fogColor = "purple";
            content.meadow.terrain.groundMid = "#12";

            Assert.IsTrue(Reports(content, "meadow.sky.stops[1].fogColor"));
            Assert.IsTrue(Reports(content, "meadow.terrain.groundMid"));
        }

        [Test]
        public void An_empty_backdrop_path_is_reported()
        {
            var content = Shipped();
            content.meadow.sky.stops[2].backdrop = " ";

            Assert.IsTrue(Reports(content, "meadow.sky.stops[2].backdrop"));
        }

        [Test]
        public void A_far_edge_in_front_of_the_near_edge_is_reported()
        {
            var content = Shipped();
            content.meadow.layout.farZ = content.meadow.layout.nearZ - 0.5f;

            Assert.IsTrue(Reports(content, "meadow.layout.farZ"));
        }

        [Test]
        public void A_haze_band_that_has_no_height_is_reported()
        {
            var content = Shipped();
            content.meadow.haze.toRow = content.meadow.haze.fromRow;

            Assert.IsTrue(Reports(content, "meadow.haze.fromRow/toRow"));
        }

        [Test]
        public void A_ground_that_is_too_tall_is_reported()
        {
            var content = Shipped();
            content.meadow.terrain.maxHeight = 9f;

            Assert.IsTrue(Reports(content, "meadow.terrain.maxHeight"));
        }

        [Test]
        public void Every_plate_the_content_names_is_a_file_next_to_content_json()
        {
            var contentDir = System.IO.Path.GetDirectoryName(TestContent.RepoContentPath);
            foreach (var stop in Shipped().meadow.sky.stops)
            {
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(contentDir, stop.backdrop)), stop.backdrop);
            }
        }

        [Test]
        public void The_shipped_quality_tiers_are_named_like_the_unity_quality_levels_and_High_is_denser()
        {
            var quality = Shipped().meadow.quality;

            CollectionAssert.AreEqual(new[] { "High", "Low" }, quality.Select(q => q.name).ToArray());
            Assert.Greater(quality[0].grassPerSqm, quality[1].grassPerSqm);
        }

        [Test]
        public void The_shipped_grass_bands_follow_each_other_and_the_grass_is_gone_before_the_last_band_ends()
        {
            var grass = Shipped().meadow.grass;
            var rings = grass.rings;

            for (var i = 1; i < rings.Length; i++) Assert.GreaterOrEqual(rings[i].zFrom, rings[i - 1].zTo - 1e-4f, $"band {i} starts where band {i - 1} ends");
            Assert.Less(grass.nearDarkness, 1f, "the foreground is the darkest part of the field");
            Assert.Less(grass.nearDarkStart, grass.nearDarkEnd);
            Assert.Less(grass.farDim, 1f, "the middle distance is dimmed towards the plate");
            Assert.Greater(rings[0].widthScale, 1f, "the foreground blades are bigger than the base blade");
            Assert.Greater(grass.fadeFromRow, grass.fadeToRow, "rows count from the top: the fade runs upwards");
        }

        [Test]
        public void The_shipped_field_ends_where_the_grass_has_thinned_out_so_the_plate_supplies_the_mid_ground()
        {
            var meadow = Shipped().meadow;
            var layout = new MeadowLayout(meadow.layout.fieldWidth, meadow.layout.nearZ, meadow.layout.farZ, meadow.layout.horizonScreenY);

            Assert.Greater(layout.FarScreenY, meadow.grass.fadeToRow, "the top of the hand's range is below the row where the grass has gone");
            Assert.Less(layout.FarScreenY, meadow.grass.fadeFromRow, "and above the row where it is full: in the middle of the hand-over band");
        }

        [Test]
        public void Grass_bands_that_overlap_are_reported()
        {
            var content = Shipped();
            content.meadow.grass.rings[1].zFrom = content.meadow.grass.rings[0].zTo - 2f;

            Assert.IsTrue(Reports(content, "meadow.grass.rings[1].zFrom"));
        }

        [Test]
        public void A_missing_quality_tier_list_and_a_duplicate_tier_are_reported()
        {
            var content = Shipped();
            content.meadow.quality = new MeadowQualityDto[0];
            Assert.IsTrue(Reports(content, "meadow.quality needs at least one tier"));

            content = Shipped();
            content.meadow.quality[1].name = content.meadow.quality[0].name;
            Assert.IsTrue(Reports(content, "duplicate tier"));
        }

        [Test]
        public void Nonsense_wind_and_grass_numbers_are_reported_with_their_names()
        {
            var content = Shipped();
            content.meadow.wind.waveWidth = 0f;
            content.meadow.wind.beatSweep.lifeSec = 0f;
            content.meadow.grass.tipColor = "gold";
            content.meadow.grass.heightMax = content.meadow.grass.heightMin - 0.1f;

            Assert.IsTrue(Reports(content, "meadow.wind.waveWidth"));
            Assert.IsTrue(Reports(content, "meadow.wind.beatSweep.lifeSec"));
            Assert.IsTrue(Reports(content, "meadow.grass.tipColor"));
            Assert.IsTrue(Reports(content, "meadow.grass.heightMin/heightMax"));
        }

        [Test]
        public void The_shipped_waves_are_slow_wide_pulses_not_flicks()
        {
            var wind = Shipped().meadow.wind;

            Assert.AreEqual(2.6f, wind.waveWidth, 1e-5f, "wide: the front of a wave is several metres across");
            Assert.AreEqual(3.4f, wind.handRadius, 1e-5f, "the plan's 2.5 m, widened for the smooth parting of Task 7b and again for the feathered edge of the hand's gust (Part D)");
            Assert.AreEqual(7f, wind.beatSweep.speed, 1e-5f);
            Assert.AreEqual(0.07f, wind.beatSweep.amplitude, 1e-5f);
            Assert.AreEqual(4.5f, wind.noteRadial.speed, 1e-5f);
            Assert.AreEqual(0.3f, wind.noteRadial.amplitude, 1e-5f);
            // A wave is over one blade for about 2 x width / speed seconds. The old beat sweep (22 m/s, 1.8 m: 0.16 s, once a beat) measured as a flick of the whole field on every beat.
            Assert.GreaterOrEqual(2f * wind.waveWidth / wind.beatSweep.speed, 0.5f, "the sweep of a beat takes at least half a second to pass a blade");
            Assert.GreaterOrEqual(2f * wind.waveWidth / wind.noteRadial.speed, 0.5f, "so does the ring of a note");
        }

        [Test]
        public void The_shipped_breeze_has_one_direction_and_gusts_that_are_broad_and_a_few_seconds_apart()
        {
            var breeze = Shipped().meadow.breeze;

            Assert.IsNotNull(breeze, "meadow.breeze is in the content");
            Assert.GreaterOrEqual(breeze.gust.width, 2f, "a gust is several metres wide");
            var secondsApart = breeze.gust.spacing / breeze.gust.speed;
            Assert.GreaterOrEqual(secondsApart, 2f, "a few seconds apart, not a flutter");
            Assert.LessOrEqual(secondsApart, 8f);
            Assert.Less(breeze.gust.speed, 6f, "slow enough to follow with the eye");
            Assert.Greater(breeze.lean, 0f, "the grass leans the way the wind blows");
            Assert.Greater(breeze.gust.springBack, 0f, "the grass springs back");
            Assert.Greater(breeze.gust.lagMetres, 0f, "a blade's tip is later than its foot");
        }

        [Test]
        public void The_shipped_clouds_and_their_shadows_move_with_the_wind()
        {
            var clouds = Shipped().meadow.sky.clouds;

            Assert.IsNotNull(clouds, "meadow.sky.clouds is in the content");
            Assert.Greater(clouds.driftMps, 0f);
            var average = 0.55f * clouds.litBrightness + 0.45f * clouds.shadeBrightness;
            Assert.That(average, Is.InRange(0.9f, 1.1f), "a cloud lightens one side and darkens the other: the plate's average brightness stays (measured 0.55 of a cloud edge is lit)");
            Assert.Less(clouds.near.altitude, clouds.far.altitude, "two depths: the near layer is lower, so it seems to move faster");
            Assert.Less(clouds.fadeFromRow, clouds.fadeToRow);
            foreach (var stop in Shipped().meadow.sky.stops)
            {
                Assert.IsFalse(string.IsNullOrEmpty(stop.cloudLit), stop.id + " has a lit cloud colour");
                Assert.IsFalse(string.IsNullOrEmpty(stop.cloudShade), stop.id + " has a shaded cloud colour");
            }
        }

        [Test]
        public void Nonsense_breeze_numbers_are_reported_with_their_names()
        {
            var content = Shipped();
            content.meadow.breeze.directionDeg = 400f;
            content.meadow.breeze.gust.speed = 0f;
            content.meadow.breeze.gust.jitter = 0.9f;
            content.meadow.breeze.gust.weakest = 1.5f;
            content.meadow.breeze.sway.periodSec = 0f;
            content.meadow.breeze.stiffnessMin = 1.4f;
            content.meadow.breeze.stiffnessMax = 1.1f;
            content.meadow.breeze.plate.noneAtRow = 0.9f;

            Assert.IsTrue(Reports(content, "meadow.breeze.directionDeg"));
            Assert.IsTrue(Reports(content, "meadow.breeze.gust.speed"));
            Assert.IsTrue(Reports(content, "meadow.breeze.gust.jitter"));
            Assert.IsTrue(Reports(content, "meadow.breeze.gust.weakest"));
            Assert.IsTrue(Reports(content, "meadow.breeze.sway.periodSec"));
            Assert.IsTrue(Reports(content, "meadow.breeze.stiffnessMin/stiffnessMax"));
            Assert.IsTrue(Reports(content, "meadow.breeze.plate.fullFromRow/noneAtRow"));
        }

        [Test]
        public void A_missing_breeze_or_clouds_block_is_reported()
        {
            var content = Shipped();
            content.meadow.breeze = null;
            content.meadow.sky.clouds = null;

            Assert.IsTrue(Reports(content, "meadow.breeze is missing"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds is missing"));
        }

        [Test]
        public void Nonsense_cloud_numbers_are_reported_with_their_names()
        {
            var content = Shipped();
            content.meadow.sky.clouds.driftMps = -1f;
            content.meadow.sky.clouds.fadeFromRow = 0.3f;
            content.meadow.sky.clouds.fadeToRow = 0.2f;
            content.meadow.sky.clouds.litBrightness = 0.5f;
            content.meadow.sky.clouds.near.altitude = 0f;
            content.meadow.sky.clouds.far.opacity = 2f;
            content.meadow.sky.clouds.shadow.strength = 1.5f;
            content.meadow.sky.stops[1].cloudLit = "orange";

            Assert.IsTrue(Reports(content, "meadow.sky.clouds.driftMps"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds.fadeFromRow/fadeToRow"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds.litBrightness"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds.near.altitude"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds.far.opacity"));
            Assert.IsTrue(Reports(content, "meadow.sky.clouds.shadow.strength"));
            Assert.IsTrue(Reports(content, "meadow.sky.stops[1].cloudLit"));
        }

        [Test]
        public void The_shipped_ribbon_and_particles_follow_the_plan()
        {
            var meadow = Shipped().meadow;

            Assert.AreEqual(0.6f, meadow.ribbon.trailSec, 1e-5f, "a trail of 0.6 s");
            Assert.AreEqual(0.4f, meadow.ribbon.height, 1e-5f, "0.4 m above the ground");
            Assert.AreEqual(0.5f, meadow.ribbon.fadeSec, 1e-5f, "fades over 0.5 s");
            Assert.Greater(meadow.quality[0].fireflyMax, meadow.quality[1].fireflyMax);
            Assert.Greater(meadow.particles.pollenCount, 0, "a little pollen is always there");
            Assert.Greater(meadow.particles.windDrift, 0f, "the steady wind carries pollen, seeds and petals along");
        }

        [Test]
        public void Nonsense_particle_and_ribbon_numbers_are_reported_with_their_names()
        {
            var content = Shipped();
            content.meadow.particles.windFollow = -1f;
            content.meadow.particles.windDrift = -1f;
            content.meadow.particles.fireflyHeightMax = content.meadow.particles.fireflyHeightMin;
            content.meadow.ribbon.fadeSec = 0f;
            content.meadow.ribbon.color = "glow";

            Assert.IsTrue(Reports(content, "meadow.particles.windFollow"));
            Assert.IsTrue(Reports(content, "meadow.particles.windDrift"));
            Assert.IsTrue(Reports(content, "meadow.particles.fireflyHeightMin/Max"));
            Assert.IsTrue(Reports(content, "meadow.ribbon.fadeSec"));
            Assert.IsTrue(Reports(content, "meadow.ribbon.color"));
        }

        [Test]
        public void The_tuning_that_used_to_sit_in_the_code_is_in_the_content_and_validated()
        {
            var meadow = Shipped().meadow;
            Assert.Greater(meadow.particles.maxSeeds, 0);
            Assert.Greater(meadow.particles.fireflyBlinkCount, 0);
            Assert.Greater(meadow.ribbon.minVertexDistance, 0f);
            Assert.Greater(meadow.ribbon.followMinHz, 0f, "the shipped ribbon glides between tracker frames");
            Assert.Greater(meadow.ribbon.followSpeedHz, 0f);
            Assert.Greater(meadow.wind.handLookAheadSec, 0f);
            Assert.Greater(meadow.demo.radialDepth, 0f);

            var content = Shipped();
            content.meadow.particles.fireflyBlinkCount = 0;
            content.meadow.particles.spawnHeightMax = -1f;
            content.meadow.ribbon.headAlpha = 4f;
            content.meadow.ribbon.followBeta = -1f;
            content.meadow.ribbon.followSpeedHz = 0f;
            content.meadow.wind.handLookAheadSec = 0f;
            content.meadow.demo = null;

            Assert.IsTrue(Reports(content, "meadow.particles.fireflyBlinkCount"));
            Assert.IsTrue(Reports(content, "meadow.particles.spawnHeightMin/Max"));
            Assert.IsTrue(Reports(content, "meadow.ribbon.headAlpha"));
            Assert.IsTrue(Reports(content, "meadow.ribbon.followBeta"));
            Assert.IsTrue(Reports(content, "meadow.ribbon.followSpeedHz"));
            Assert.IsTrue(Reports(content, "meadow.wind.handLookAheadSec"));
            Assert.IsTrue(Reports(content, "meadow.demo is missing"));
        }

        [Test]
        public void The_waking_moment_is_short_calm_and_checked()
        {
            var wake = Shipped().meadow.wake;
            Assert.IsNotNull(wake);
            Assert.That(wake.radius / wake.speed, Is.InRange(0.5f, 2f), "the wave lasts 1 to 2 s");
            Assert.Less(wake.gustAmplitude, Shipped().meadow.wind.noteRadial.amplitude, "a soft gust, softer than a played note");
            Assert.DoesNotThrow(() => WakeSettings.From(wake));

            var content = Shipped();
            content.meadow.wake.radius = 30f;
            content.meadow.wake.speed = 1f;
            content.meadow.wake.maxFlowers = -1;
            content.meadow.wake.jitterSec = 3f;
            content.meadow.wake.gustAmplitude = 5f;
            content.meadow.wake.gustLifeSec = 0f;
            content.meadow.wake.gustGlow = 2f;

            Assert.IsTrue(Reports(content, "meadow.wake.radius"));
            Assert.IsTrue(Reports(content, "meadow.wake must be over within 3 s"));
            Assert.IsTrue(Reports(content, "meadow.wake.maxFlowers"));
            Assert.IsTrue(Reports(content, "meadow.wake.jitterSec"));
            Assert.IsTrue(Reports(content, "meadow.wake.gustAmplitude"));
            Assert.IsTrue(Reports(content, "meadow.wake.gustLifeSec"));
            Assert.IsTrue(Reports(content, "meadow.wake.gustGlow"));

            content.meadow.wake = null;
            Assert.IsTrue(Reports(content, "meadow.wake is missing"));
        }

        [Test]
        public void The_gust_of_a_hand_fits_inside_the_wind_of_the_hand_and_is_checked()
        {
            var wind = Shipped().meadow.wind;
            Assert.Greater(wind.handGustWidth, 0f);
            Assert.Less(wind.handGustWidth * 1.9f, wind.handRadius, "the swing back behind the hand is still inside the hand's wind");
            Assert.Greater(wind.handSpeedCap, 0f);

            var content = Shipped();
            content.meadow.wind.handGustWidth = content.meadow.wind.handRadius;
            content.meadow.wind.handSpeedCap = 0f;

            Assert.IsTrue(Reports(content, "meadow.wind.handGustWidth must be smaller than handRadius"));
            Assert.IsTrue(Reports(content, "meadow.wind.handSpeedCap"));
        }

        [Test]
        public void TheMostABladeBends_IsInTheContent_At120Degrees_TheDataClassAgrees_AndItIsChecked()
        {
            Assert.AreEqual(120f, Shipped().meadow.wind.maxBendDeg, "the shipped content (tools/lib/content-build.mjs has the same default)");
            Assert.AreEqual(Shipped().meadow.wind.maxBendDeg, new MeadowWindDto().maxBendDeg, "the data class default");
            Assert.AreEqual(120f, JsonUtility.FromJson<ContentDto>("{\"meadow\":{\"wind\":{\"bendScale\":2.2}}}").meadow.wind.maxBendDeg, "content from before the key gets it");

            foreach (var bad in new[] { 9.9f, 150.1f, 180f, 0f, -30f, float.NaN })
            {
                var content = Shipped();
                content.meadow.wind.maxBendDeg = bad;
                Assert.IsTrue(Reports(content, "meadow.wind.maxBendDeg"), "maxBendDeg " + bad);
            }
            foreach (var good in new[] { 10f, 45f, 89f, 150f })
            {
                var content = Shipped();
                content.meadow.wind.maxBendDeg = good;
                Assert.IsFalse(Reports(content, "meadow.wind.maxBendDeg"), "maxBendDeg " + good);
            }
        }

        [Test]
        public void TheNearRelief_IsGentle_GoneBeforeTheHandOver_AndChecked()
        {
            var terrain = Shipped().meadow.terrain;
            Assert.Greater(terrain.reliefHeight, 0f);
            Assert.LessOrEqual(terrain.reliefHeight, 0.15f, "gentle");
            Assert.Less(terrain.reliefToZ, 6f, "gone before the haze that hands the 3D ground to the plate");

            var content = Shipped();
            content.meadow.terrain.reliefHeight = 1f;
            content.meadow.terrain.reliefWavelength = 0.1f;
            content.meadow.terrain.reliefFromZ = 6f;
            content.meadow.terrain.reliefToZ = 5f;

            Assert.IsTrue(Reports(content, "meadow.terrain.reliefHeight"));
            Assert.IsTrue(Reports(content, "meadow.terrain.reliefWavelength"));
            Assert.IsTrue(Reports(content, "meadow.terrain.reliefFromZ/ToZ"));
        }
    }
}
