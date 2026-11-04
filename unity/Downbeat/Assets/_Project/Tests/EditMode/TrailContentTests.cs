using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The <c>trail</c> block of content.json: the note trail behind a hand and the names of the cells.</summary>
    public class TrailContentTests
    {
        static List<string> Check(TrailDto dto)
        {
            var errors = new List<string>();
            TrailContentValidator.Check(dto, errors);
            return errors;
        }

        [Test]
        public void TheDefaultsAndTheShippedContentPass()
        {
            Assert.IsEmpty(Check(new TrailDto()));
            Assert.IsEmpty(Check(null), "no block at all: the defaults apply");
            Assert.IsEmpty(ContentValidator.Validate(TestContent.LoadShipped()), "the shipped content is valid");
            Assert.IsNotNull(TestContent.LoadShipped().trail, "and has the block");
        }

        [Test]
        public void TheDtoDefaults_AreTheValuesOfTheShippedContent_AndOfThePlan()
        {
            var shipped = TestContent.LoadShipped().trail;
            var defaults = new TrailDto();

            Assert.AreEqual(shipped.notesPerSec, defaults.notesPerSec, 1e-6f, "notesPerSec (tools/lib/content-build.mjs has the same default)");
            Assert.AreEqual(shipped.maxNotes, defaults.maxNotes, "maxNotes");
            Assert.AreEqual(shipped.lifeSec, defaults.lifeSec, 1e-6f, "lifeSec");
            Assert.AreEqual(shipped.size, defaults.size, 1e-6f, "size");
            Assert.AreEqual(shipped.labelSec, defaults.labelSec, 1e-6f, "labelSec");
            Assert.AreEqual(shipped.lowCountScale, defaults.lowCountScale, 1e-6f, "lowCountScale");
            Assert.AreEqual(6f, defaults.notesPerSec);
            Assert.AreEqual(40, defaults.maxNotes);
            Assert.AreEqual(1f, defaults.lifeSec);
            Assert.AreEqual(0.06f, defaults.size);
            Assert.AreEqual(1.2f, defaults.labelSec);
            Assert.AreEqual(0.5f, defaults.lowCountScale);
        }

        [Test]
        public void AContentFileWithoutTheBlock_StillLoadsAndPasses_AndGetsTheDefaults()
        {
            // a content.json from before the trail: no "trail" key at all
            var json = Regex.Replace(JsonUtility.ToJson(TestContent.LoadShipped()), ",?\"trail\":\\{[^{}]*\\}", string.Empty);
            Assert.IsFalse(json.Contains("\"trail\""), "the test content has no trail block");

            var content = JsonUtility.FromJson<ContentDto>(json);

            Assert.IsEmpty(ContentValidator.Validate(content));
            var settings = TrailSettings.From(content.trail);
            Assert.AreEqual(6f, settings.NotesPerSec);
            Assert.AreEqual(40, settings.MaxNotes);
        }

        [Test]
        public void ABlockWithOnlySomeKeys_TakesTheDefaultsForTheRest()
        {
            var content = JsonUtility.FromJson<ContentDto>("{\"trail\":{\"notesPerSec\":12}}");

            Assert.AreEqual(12f, content.trail.notesPerSec);
            Assert.AreEqual(40, content.trail.maxNotes);
            Assert.AreEqual(1.2f, content.trail.labelSec);
            Assert.IsEmpty(Check(content.trail));
        }

        [TestCase("notesPerSec", -1f)]
        [TestCase("notesPerSec", 31f)]
        [TestCase("maxNotes", -1f)]
        [TestCase("maxNotes", 201f)]
        [TestCase("lifeSec", 0.1f)]
        [TestCase("lifeSec", 4.5f)]
        [TestCase("size", 0.005f)]
        [TestCase("size", 0.2f)]
        [TestCase("labelSec", -0.1f)]
        [TestCase("labelSec", 6f)]
        [TestCase("lowCountScale", -0.1f)]
        [TestCase("lowCountScale", 1.5f)]
        [TestCase("notesPerSec", float.NaN)]
        public void ANumberOutsideItsRange_IsReportedWithItsName(string key, float value)
        {
            var dto = new TrailDto();
            typeof(TrailDto).GetField(key).SetValue(dto, key == "maxNotes" ? (object)(int)value : value);

            Assert.That(Check(dto), Has.Some.Contains("trail." + key));
        }

        [TestCase("notesPerSec", 0f)]
        [TestCase("notesPerSec", 30f)]
        [TestCase("maxNotes", 0f)]
        [TestCase("maxNotes", 200f)]
        [TestCase("lifeSec", 0.2f)]
        [TestCase("lifeSec", 4f)]
        [TestCase("size", 0.01f)]
        [TestCase("size", 0.1f)]
        [TestCase("labelSec", 0f)]
        [TestCase("labelSec", 5f)]
        [TestCase("lowCountScale", 0f)]
        [TestCase("lowCountScale", 1f)]
        public void TheEdgesOfTheRangesAreAllowed(string key, float value)
        {
            var dto = new TrailDto();
            typeof(TrailDto).GetField(key).SetValue(dto, key == "maxNotes" ? (object)(int)value : value);

            Assert.IsEmpty(Check(dto));
        }

        [Test]
        public void TheValidatorOfTheWholeFile_CallsTheTrailChecks()
        {
            var content = TestContent.LoadShipped();
            content.trail.lifeSec = 99f;

            Assert.That(ContentValidator.Validate(content), Has.Some.Contains("trail.lifeSec"));
        }

        [Test]
        public void ATrailChange_IsNamedByF5_AndWaitsForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.trail.notesPerSec += 2f;

            CollectionAssert.AreEqual(new[] { "trail" }, ContentSections.Changed(before, after));
            CollectionAssert.AreEqual(new[] { "trail" }, ContentSections.NeedingRestart(before, after));
        }

        [Test]
        public void TheSettings_AreASnapshotOfTheBlock()
        {
            var dto = new TrailDto { notesPerSec = 9f, maxNotes = 25, lifeSec = 2f, size = 0.05f, labelSec = 0.8f, lowCountScale = 0.25f };

            var settings = TrailSettings.From(dto);
            dto.notesPerSec = 1f;

            Assert.AreEqual(9f, settings.NotesPerSec);
            Assert.AreEqual(25, settings.MaxNotes);
            Assert.AreEqual(2f, settings.LifeSec);
            Assert.AreEqual(0.05f, settings.Size);
            Assert.AreEqual(0.8f, settings.LabelSec);
            Assert.AreEqual(0.25f, settings.LowCountScale);
            Assert.AreEqual(2.25f, settings.WithCountScale(settings.LowCountScale).NotesPerSec, 1e-5f);
        }
    }
}
