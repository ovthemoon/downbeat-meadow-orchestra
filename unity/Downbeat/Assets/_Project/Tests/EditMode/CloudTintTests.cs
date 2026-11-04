using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The colours the sky behind a cloud is multiplied by (worked out once per frame on the CPU, so that the sky shader does not have to do it for every pixel).</summary>
    public class CloudTintTests
    {
        static float Luma((float R, float G, float B) c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

        [Test]
        public void ANeutralColour_OnlyChangesTheBrightness_WhateverTheHueShare()
        {
            foreach (var share in new[] { 0f, 0.4f, 1f })
            {
                var tint = CloudTint.Of(0.5f, 0.5f, 0.5f, share, 1.35f);
                Assert.AreEqual(1.35f, tint.R, 1e-5f);
                Assert.AreEqual(1.35f, tint.G, 1e-5f);
                Assert.AreEqual(1.35f, tint.B, 1e-5f);
            }
        }

        [Test]
        public void WithNoHueShare_TheColourOfTheCloudDoesNotTintTheSky()
        {
            var tint = CloudTint.Of(0.9f, 0.5f, 0.3f, 0f, 0.62f);

            Assert.AreEqual(0.62f, tint.R, 1e-5f);
            Assert.AreEqual(0.62f, tint.G, 1e-5f);
            Assert.AreEqual(0.62f, tint.B, 1e-5f);
        }

        [Test]
        public void WithAFullHueShare_TheSkyTakesTheHueOfTheColour_AndKeepsTheBrightness()
        {
            var tint = CloudTint.Of(0.9f, 0.5f, 0.3f, 1f, 1.35f);

            Assert.Greater(tint.R, tint.G, "a warm colour tints the sky warm");
            Assert.Greater(tint.G, tint.B);
            Assert.AreEqual(1.35f, Luma(tint), 1e-4f, "the brightness of the side is what the settings say, whatever the hue");
        }

        [Test]
        public void TheHueShare_IsAMixBetweenNoTintAndTheFullTint()
        {
            var none = CloudTint.Of(0.9f, 0.5f, 0.3f, 0f, 1f);
            var full = CloudTint.Of(0.9f, 0.5f, 0.3f, 1f, 1f);
            var part = CloudTint.Of(0.9f, 0.5f, 0.3f, 0.4f, 1f);

            Assert.AreEqual(none.R + 0.4f * (full.R - none.R), part.R, 1e-5f);
            Assert.AreEqual(none.G + 0.4f * (full.G - none.G), part.G, 1e-5f);
            Assert.AreEqual(none.B + 0.4f * (full.B - none.B), part.B, 1e-5f);
        }

        [Test]
        public void ABlackColour_GivesNoInfinityOrNaN()
        {
            var tint = CloudTint.Of(0f, 0f, 0f, 1f, 1f);

            Assert.IsFalse(float.IsNaN(tint.R) || float.IsInfinity(tint.R));
            Assert.IsFalse(float.IsNaN(tint.G) || float.IsInfinity(tint.G));
            Assert.IsFalse(float.IsNaN(tint.B) || float.IsInfinity(tint.B));
        }
    }
}
