using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downbeat.Tests
{
    /// <summary>The project is a Universal Render Pipeline project with a High and a Low tier (Task 1). Pins what tools/ and later tasks rely on.</summary>
    public class UrpSetupTests
    {
        const string HighName = "URP_High";
        const string LowName = "URP_Low";
        const string VolumeProfilePath = "Assets/_Project/Settings/MeadowVolumeProfile.asset";

        static UniversalRenderPipelineAsset TierAsset(string tierName)
        {
            var names = QualitySettings.names;
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i] == tierName) return QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
            }
            return null;
        }

        [Test]
        public void ColorSpace_IsLinear()
        {
            Assert.AreEqual(ColorSpace.Linear, PlayerSettings.colorSpace);
        }

        [Test]
        public void DefaultRenderPipeline_IsTheHighTierAsset()
        {
            var asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

            Assert.IsNotNull(asset, "GraphicsSettings needs a Universal Render Pipeline asset");
            Assert.AreEqual(HighName, asset.name);
        }

        [Test]
        public void QualityLevels_AreExactlyHighAndLow_EachWithItsOwnPipelineAsset()
        {
            CollectionAssert.AreEqual(new[] { "High", "Low" }, QualitySettings.names);

            var high = TierAsset("High");
            var low = TierAsset("Low");

            Assert.IsNotNull(high);
            Assert.IsNotNull(low);
            Assert.AreEqual(HighName, high.name);
            Assert.AreEqual(LowName, low.name);
        }

        [Test]
        public void HighTier_UsesShadowsAndMsaa_LowTierUsesNeither()
        {
            var high = TierAsset("High");
            var low = TierAsset("Low");

            Assert.IsTrue(high.supportsMainLightShadows);
            Assert.GreaterOrEqual(high.msaaSampleCount, 4);
            Assert.IsFalse(low.supportsMainLightShadows);
            Assert.AreEqual(1, low.msaaSampleCount);
        }

        [Test]
        public void BothTiers_RenderHdr_BecauseBloomNeedsIt()
        {
            Assert.IsTrue(TierAsset("High").supportsHDR);
            Assert.IsTrue(TierAsset("Low").supportsHDR);
        }

        [Test]
        public void MeadowVolumeProfile_HasBloomColorAdjustmentsAndNeutralTonemapping()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

            Assert.IsNotNull(profile, VolumeProfilePath);
            Assert.IsTrue(profile.TryGet<Bloom>(out var bloom));
            Assert.IsTrue(bloom.active);
            Assert.IsTrue(profile.TryGet<ColorAdjustments>(out _));
            Assert.IsTrue(profile.TryGet<Tonemapping>(out var tonemapping));
            Assert.AreEqual(TonemappingMode.Neutral, tonemapping.mode.value, "ACES darkens and tints the finished backdrop plates; Neutral keeps them");
        }
    }
}
