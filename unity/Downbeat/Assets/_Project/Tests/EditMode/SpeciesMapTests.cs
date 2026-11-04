using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SpeciesMapTests
    {
        const float Blend = 0.08f;

        static float W(FlowerSpecies species, float x) => SpeciesMap.Weight(species, x, Blend);

        [Test]
        public void TheGroundRow_HasTheFlowerOfItsColumn_AndTheSkyRowHasNone()
        {
            Assert.AreEqual(FlowerSpecies.Bellflower, SpeciesMap.ForPart(Part.LowString));
            Assert.AreEqual(FlowerSpecies.Dandelion, SpeciesMap.ForPart(Part.Marimba));
            Assert.AreEqual(FlowerSpecies.Poppy, SpeciesMap.ForPart(Part.HandDrum));
            foreach (var sky in new[] { Part.Harp, Part.Recorder, Part.Bell }) Assert.IsNull(SpeciesMap.ForPart(sky), $"{sky} plays in the air: no flower");
            foreach (var ground in new[] { Part.LowString, Part.Marimba, Part.HandDrum }) Assert.AreEqual(ground, SpeciesMap.ToPart(SpeciesMap.ForPart(ground).Value));
        }

        [Test]
        public void ThePlaceOfAPartOnTheGround_IsTheMiddleOfItsColumn()
        {
            foreach (Part part in System.Enum.GetValues(typeof(Part)))
            {
                var centre = new[] { SpeciesMap.LeftCentre, SpeciesMap.MiddleCentre, SpeciesMap.RightCentre }[Parts.Column(part)];
                Assert.AreEqual(centre, SpeciesMap.CentreOf(part), $"{part}");
            }
        }

        [Test]
        public void ZoneCentres_BelongToTheirOwnSpecies()
        {
            Assert.AreEqual(1f, W(FlowerSpecies.Bellflower, 0.17f), 1e-5f);
            Assert.AreEqual(1f, W(FlowerSpecies.Dandelion, 0.5f), 1e-5f);
            Assert.AreEqual(1f, W(FlowerSpecies.Poppy, 0.83f), 1e-5f);
            Assert.AreEqual(0f, W(FlowerSpecies.Dandelion, 0.17f), 1e-5f);
            Assert.AreEqual(0f, W(FlowerSpecies.Poppy, 0.5f), 1e-5f);
        }

        [Test]
        public void WeightsSumToOne_EverywhereAndNeverGoNegative()
        {
            for (var i = 0; i <= 100; i++)
            {
                var x = i / 100f;
                var sum = W(FlowerSpecies.Bellflower, x) + W(FlowerSpecies.Dandelion, x) + W(FlowerSpecies.Poppy, x);

                Assert.AreEqual(1f, sum, 1e-5f, $"x {x}");
                Assert.GreaterOrEqual(W(FlowerSpecies.Bellflower, x), 0f);
                Assert.GreaterOrEqual(W(FlowerSpecies.Dandelion, x), 0f);
                Assert.GreaterOrEqual(W(FlowerSpecies.Poppy, x), 0f);
            }
        }

        [Test]
        public void TheBorderBetweenTwoZones_MixesThemOverTheBlendWidth_AndOnlyThere()
        {
            var border = 1f / 3f;

            Assert.AreEqual(0.5f, W(FlowerSpecies.Bellflower, border), 1e-4f);
            Assert.AreEqual(1f, W(FlowerSpecies.Bellflower, border - Blend / 2f - 0.001f), 1e-5f, "pure on the left of the mix");
            Assert.AreEqual(1f, W(FlowerSpecies.Dandelion, border + Blend / 2f + 0.001f), 1e-5f, "pure on the right of it");
        }

        [Test]
        public void ClampsOutOfRange()
        {
            Assert.AreEqual(W(FlowerSpecies.Bellflower, 0f), W(FlowerSpecies.Bellflower, -2f), 1e-6f);
            Assert.AreEqual(W(FlowerSpecies.Poppy, 1f), W(FlowerSpecies.Poppy, 5f), 1e-6f);
        }

        [TestCase(0.05f, Part.Harp)]
        [TestCase(0.40f, Part.Recorder)]
        [TestCase(0.95f, Part.Bell)]
        public void WhichInstrumentPlays_StaysWithTheCellsOfTheScreen(float x, Part expected)
        {
            Assert.AreEqual(expected, CellPicker.Pick(x, 0.1f, Part.Harp, false, 0f, CellSettings.Default), "the blend only changes how the flowers look, never which instrument plays");
        }
    }
}
