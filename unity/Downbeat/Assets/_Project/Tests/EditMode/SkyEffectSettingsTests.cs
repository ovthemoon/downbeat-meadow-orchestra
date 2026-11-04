using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The numbers of the effects in the air (how many, how long, how big, how many fewer on the Low tier) and the checks of their content block.</summary>
    public class SkyEffectSettingsTests
    {
        const float Tolerance = 1e-5f;

        static SkyEffectSettings Defaults() => SkyEffectSettings.From(null);

        static ContentDto Shipped() => TestContent.LoadShipped();

        static bool Reports(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        // ---- counts -----------------------------------------------------------------------------------------------------

        [Test]
        public void WithoutAContentBlock_TheDefaultsApply()
        {
            var settings = Defaults();
            var dto = new SkyEffectsDto();

            Assert.AreEqual(dto.ringCount, settings.RingCount);
            Assert.AreEqual(dto.seedCount, settings.SeedCount);
            Assert.AreEqual(dto.starCount, settings.StarCount);
            Assert.AreEqual(dto.distance, settings.Distance, Tolerance);
            Assert.AreEqual(dto.lowCountScale, settings.LowCountScale, Tolerance);
        }

        [Test]
        public void AtFullStrength_TheHighTierMakesTheCountsOfTheContent()
        {
            var settings = Defaults();

            Assert.AreEqual(settings.RingCount, settings.CountFor(SkyEffectKind.LightRing, 1f, false));
            Assert.AreEqual(settings.SeedCount, settings.CountFor(SkyEffectKind.Seeds, 1f, false));
            Assert.AreEqual(settings.StarCount, settings.CountFor(SkyEffectKind.StarDust, 1f, false));
        }

        [Test]
        public void TheLowTier_MakesHalfOfEachCount()
        {
            var settings = Defaults();

            foreach (var kind in new[] { SkyEffectKind.LightRing, SkyEffectKind.Seeds, SkyEffectKind.StarDust })
            {
                var high = settings.CountFor(kind, 1f, false);
                var low = settings.CountFor(kind, 1f, true);
                Assert.AreEqual(Mathf_RoundHalfUp(high * settings.LowCountScale), low, $"{kind}: {high} on High, {low} on Low");
                Assert.Less(low, high, kind.ToString());
            }
        }

        [Test]
        public void ASofterNote_LetsFewerSeedsAndSpecksGo_ButTheRingKeepsItsPoints()
        {
            var settings = Defaults();

            Assert.Less(settings.CountFor(SkyEffectKind.Seeds, 0f, false), settings.CountFor(SkyEffectKind.Seeds, 1f, false));
            Assert.Less(settings.CountFor(SkyEffectKind.StarDust, 0f, false), settings.CountFor(SkyEffectKind.StarDust, 1f, false));
            Assert.GreaterOrEqual(settings.CountFor(SkyEffectKind.Seeds, 0f, false), 1, "even the softest note lifts a seed");
            Assert.AreEqual(settings.CountFor(SkyEffectKind.LightRing, 0f, false), settings.CountFor(SkyEffectKind.LightRing, 1f, false), "a ring is a whole ring: a softer note makes it smaller, not broken");
            Assert.Less(settings.RadiusFor(0f), settings.RadiusFor(1f));
            Assert.AreEqual(settings.RingRadius, settings.RadiusFor(1f), Tolerance);
        }

        [Test]
        public void AStrengthOutsideZeroToOne_IsClamped()
        {
            var settings = Defaults();

            Assert.AreEqual(settings.CountFor(SkyEffectKind.Seeds, 1f, false), settings.CountFor(SkyEffectKind.Seeds, 5f, false));
            Assert.AreEqual(settings.CountFor(SkyEffectKind.Seeds, 0f, false), settings.CountFor(SkyEffectKind.Seeds, -2f, false));
            Assert.AreEqual(settings.RadiusFor(1f), settings.RadiusFor(9f), Tolerance);
        }

        [Test]
        public void ACountOfZero_SwitchesAnEffectOff_OnBothTiers()
        {
            var settings = SkyEffectSettings.From(new SkyEffectsDto { ringCount = 0, seedCount = 0, starCount = 0 });

            foreach (var kind in new[] { SkyEffectKind.LightRing, SkyEffectKind.Seeds, SkyEffectKind.StarDust })
            {
                Assert.AreEqual(0, settings.CountFor(kind, 1f, false), kind.ToString());
                Assert.AreEqual(0, settings.CountFor(kind, 1f, true), kind.ToString());
            }
        }

        [Test]
        public void ALowTierWithTinyCounts_StillMakesOneParticle()
        {
            var settings = SkyEffectSettings.From(new SkyEffectsDto { seedCount = 1, lowCountScale = 0.1f });

            Assert.AreEqual(1, settings.CountFor(SkyEffectKind.Seeds, 1f, true));
        }

        static int Mathf_RoundHalfUp(float value) => (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);

        // ---- time and tier ----------------------------------------------------------------------------------------------

        [Test]
        public void TheRingIsQuick_TheSeedsLiveLongest_TheStarsFallForAWhile()
        {
            var settings = Defaults();

            Assert.AreEqual(settings.LifeSec, settings.LifeFor(SkyEffectKind.Seeds), Tolerance);
            Assert.Less(settings.LifeFor(SkyEffectKind.LightRing), settings.LifeFor(SkyEffectKind.StarDust));
            Assert.Less(settings.LifeFor(SkyEffectKind.StarDust), settings.LifeFor(SkyEffectKind.Seeds));
        }

        [Test]
        public void OnlyTheTierCalledLow_IsTheLowTier()
        {
            var tiers = Shipped().meadow.quality;

            Assert.IsFalse(SkyEffectSettings.IsLowTier(tiers[0]), "the first tier (" + tiers[0].name + ") is the full one");
            Assert.IsTrue(SkyEffectSettings.IsLowTier(tiers.First(t => t.name == "Low")));
            Assert.IsTrue(SkyEffectSettings.IsLowTier(new MeadowQualityDto { name = "low" }), "any case");
            Assert.IsFalse(SkyEffectSettings.IsLowTier(null));
        }

        // ---- the content ------------------------------------------------------------------------------------------------

        [Test]
        public void TheDtoDefaults_AreTheValuesOfTheShippedContent()
        {
            var shipped = Shipped().meadow.skyEffects;
            var defaults = new SkyEffectsDto();

            Assert.IsNotNull(shipped, "meadow.skyEffects is in the shipped content");
            Assert.AreEqual(defaults.ringCount, shipped.ringCount, "tools/lib/content-build.mjs has the same defaults");
            Assert.AreEqual(defaults.seedCount, shipped.seedCount);
            Assert.AreEqual(defaults.starCount, shipped.starCount);
            Assert.AreEqual(defaults.lifeSec, shipped.lifeSec, Tolerance);
            Assert.AreEqual(defaults.distance, shipped.distance, Tolerance);
            Assert.AreEqual(defaults.ringRadius, shipped.ringRadius, Tolerance);
            Assert.AreEqual(defaults.glow, shipped.glow, Tolerance);
            Assert.AreEqual(defaults.maxParticles, shipped.maxParticles);
            Assert.AreEqual(defaults.lowCountScale, shipped.lowCountScale, Tolerance);
        }

        /// <summary>The text of a content file with one block taken out (from the key to the closing brace), as a file from before that block was added.</summary>
        static string WithoutBlock(string json, string key)
        {
            var start = json.IndexOf("\"" + key + "\": {", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, key + " is in the shipped file");
            var open = json.IndexOf('{', start);
            var depth = 0;
            var end = open;
            for (; end < json.Length; end++)
            {
                if (json[end] == '{') depth++;
                else if (json[end] == '}' && --depth == 0) break;
            }
            var after = json.Substring(end + 1);
            var before = json.Substring(0, start).TrimEnd();
            if (after.TrimStart().StartsWith(",")) after = after.TrimStart().Substring(1);
            else before = before.TrimEnd(',');   // it was the last key of its block: the comma before it goes
            return before + after;
        }

        [Test]
        public void AContentFileFromBeforeTheEffects_IsValid_AndGivesTheDefaults()
        {
            var json = System.IO.File.ReadAllText(TestContent.RepoContentPath);
            foreach (var key in new[] { "skyEffects", "longWave", "drumRing" }) json = WithoutBlock(json, key);

            var content = UnityEngine.JsonUtility.FromJson<ContentDto>(json);

            Assert.IsEmpty(ContentValidator.Validate(content), "older files stay valid");
            var shipped = Shipped().meadow;
            Assert.AreEqual(SkyEffectSettings.From(shipped.skyEffects).RingCount, SkyEffectSettings.From(content.meadow.skyEffects).RingCount);
            Assert.AreEqual(SkyEffectSettings.From(shipped.skyEffects).Distance, SkyEffectSettings.From(content.meadow.skyEffects).Distance, Tolerance);
            var longWave = WindWaves.Long(new FieldPoint(-3f, 5f), 1f, 0.0, content.meadow.wind.longWave);
            var shippedLong = WindWaves.Long(new FieldPoint(-3f, 5f), 1f, 0.0, shipped.wind.longWave);
            Assert.AreEqual(shippedLong.Amplitude, longWave.Amplitude, Tolerance);
            Assert.AreEqual(shippedLong.WidthScale, longWave.WidthScale, Tolerance);
            var drum = WindWaves.Drum(new FieldPoint(3f, 5f), 1f, 0.0, content.meadow.wind.drumRing);
            var shippedDrum = WindWaves.Drum(new FieldPoint(3f, 5f), 1f, 0.0, shipped.wind.drumRing);
            Assert.AreEqual(shippedDrum.Amplitude, drum.Amplitude, Tolerance);
            Assert.AreEqual(shippedDrum.LifeSec, drum.LifeSec, Tolerance);
        }

        [Test]
        public void TheShippedBlock_IsValid()
        {
            Assert.IsEmpty(ContentValidator.Validate(Shipped()));
        }

        [Test]
        public void ContentWithoutTheBlock_IsValid_AndUsesTheDefaults()
        {
            var content = Shipped();
            content.meadow.skyEffects = null;
            content.meadow.wind.longWave = null;
            content.meadow.wind.drumRing = null;

            Assert.IsEmpty(ContentValidator.Validate(content), "older files stay valid");
        }

        [Test]
        public void ABadNumber_IsReportedWithItsName()
        {
            var content = Shipped();
            content.meadow.skyEffects.ringCount = 500;
            content.meadow.skyEffects.seedCount = -1;
            content.meadow.skyEffects.starCount = 101;
            content.meadow.skyEffects.lifeSec = 0f;
            content.meadow.skyEffects.distance = 1f;
            content.meadow.skyEffects.ringRadius = 9f;
            content.meadow.skyEffects.glow = 31f;
            content.meadow.skyEffects.maxParticles = 4;
            content.meadow.skyEffects.lowCountScale = 1.5f;

            foreach (var name in new[] { "ringCount", "seedCount", "starCount", "lifeSec", "distance", "ringRadius", "glow", "maxParticles", "lowCountScale" })
            {
                Assert.IsTrue(Reports(content, "meadow.skyEffects." + name + " must be"), name);
            }
        }

        [Test]
        public void ANumberThatIsNotANumber_IsReported()
        {
            var content = Shipped();
            content.meadow.skyEffects.distance = float.NaN;

            Assert.IsTrue(Reports(content, "meadow.skyEffects.distance must be"));
        }

        [Test]
        public void ABadReactionOfTheGrass_IsReportedWithItsName()
        {
            var content = Shipped();
            content.meadow.wind.longWave.amplitude = 9f;
            content.meadow.wind.longWave.velocityGain = -1f;
            content.meadow.wind.longWave.speed = 300f;
            content.meadow.wind.longWave.lifeSec = 0f;
            content.meadow.wind.longWave.widthScale = 0.1f;
            content.meadow.wind.drumRing.widthScale = 20f;
            content.meadow.wind.drumRing.lifeSec = 40f;

            foreach (var name in new[] { "longWave.amplitude", "longWave.velocityGain", "longWave.speed", "longWave.lifeSec", "longWave.widthScale", "drumRing.widthScale", "drumRing.lifeSec" })
            {
                Assert.IsTrue(Reports(content, "meadow.wind." + name + " must be"), name);
            }
        }
    }
}
