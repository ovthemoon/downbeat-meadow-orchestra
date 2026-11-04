using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The <c>cutoff</c> block of content.json: its ranges, the defaults of a file without it, and its place among the sections.</summary>
    public class CutoffContentTests
    {
        static List<string> Check(CutoffDto dto)
        {
            var errors = new List<string>();
            CutoffContentValidator.Check(dto, errors);
            return errors;
        }

        /// <summary>The shipped content as JSON text without its cutoff block: a file from before the block.</summary>
        static string ShippedJsonWithoutTheBlock()
        {
            var json = Regex.Replace(JsonUtility.ToJson(TestContent.LoadShipped()), "\"cutoff\":\\{[^}]*\\},?", string.Empty);
            Assert.IsFalse(json.Contains("\"cutoff\""), "the test file has no cutoff block");
            return json;
        }

        [Test]
        public void TheDefaultsTheNullBlockAndTheShippedContentPass()
        {
            Assert.IsEmpty(Check(new CutoffDto()));
            Assert.IsEmpty(Check(null), "no block at all: the defaults apply");
            Assert.IsEmpty(ContentValidator.Validate(TestContent.LoadShipped()));
        }

        [Test]
        public void TheShippedContent_HasTheBlock_WithTheValuesOfTheCSharpDefaults()
        {
            var shipped = TestContent.LoadShipped().cutoff;
            var defaults = new CutoffDto();

            Assert.IsNotNull(shipped, "content/content.json has the cutoff block");
            Assert.AreEqual(defaults.spreadPalms, shipped.spreadPalms, 1e-6f, "spreadPalms (tools/lib/content-build.mjs has the same default)");
            Assert.AreEqual(defaults.minApartPalms, shipped.minApartPalms, 1e-6f, "minApartPalms");
            Assert.AreEqual(defaults.windowSec, shipped.windowSec, 1e-6f, "windowSec");
            Assert.AreEqual(defaults.holdSec, shipped.holdSec, 1e-6f, "holdSec");
            Assert.AreEqual(defaults.fadeSec, shipped.fadeSec, 1e-6f, "fadeSec");
            Assert.AreEqual(defaults.restartLockSec, shipped.restartLockSec, 1e-6f, "restartLockSec");
            Assert.AreEqual(defaults.endWordSec, shipped.endWordSec, 1e-6f, "endWordSec");
        }

        [Test]
        public void TheDefaultsAreTheNumbersOfThePlan()
        {
            var d = new CutoffDto();

            Assert.AreEqual(2f, d.spreadPalms);
            Assert.AreEqual(3.5f, d.minApartPalms);
            Assert.AreEqual(1f, d.windowSec);
            Assert.AreEqual(0.2f, d.holdSec);
            Assert.AreEqual(0.25f, d.fadeSec);
            Assert.AreEqual(1.5f, d.restartLockSec);
            Assert.AreEqual(1.5f, d.endWordSec);
            Assert.AreEqual(ExperienceSettings.DefaultRestartLockSec, d.restartLockSec, 1e-9, "the state machine's own default lock is the content default");
        }

        [Test]
        public void EveryNumberHasItsRange()
        {
            var cases = new (string Name, Action<CutoffDto> Low, Action<CutoffDto> High, Action<CutoffDto> LowestOk, Action<CutoffDto> HighestOk)[]
            {
                ("spreadPalms", d => d.spreadPalms = 0.4f, d => d.spreadPalms = 8.1f, d => d.spreadPalms = 0.5f, d => d.spreadPalms = 8f),
                ("minApartPalms", d => d.minApartPalms = 0.9f, d => d.minApartPalms = 12.1f, d => d.minApartPalms = 1f, d => d.minApartPalms = 12f),
                ("windowSec", d => d.windowSec = 0.2f, d => d.windowSec = 3.1f, d => d.windowSec = 0.3f, d => d.windowSec = 3f),
                ("holdSec", d => d.holdSec = -0.1f, d => d.holdSec = 1.1f, d => d.holdSec = 0f, d => d.holdSec = 1f),
                ("fadeSec", d => d.fadeSec = 0.04f, d => d.fadeSec = 1.1f, d => d.fadeSec = 0.05f, d => d.fadeSec = 1f),
                ("restartLockSec", d => d.restartLockSec = -0.1f, d => d.restartLockSec = 5.1f, d => d.restartLockSec = 0f, d => d.restartLockSec = 5f),
                ("endWordSec", d => d.endWordSec = 0.2f, d => d.endWordSec = 5.1f, d => d.endWordSec = 0.3f, d => d.endWordSec = 5f),
            };

            foreach (var (name, low, high, lowestOk, highestOk) in cases)
            {
                var tooLow = new CutoffDto(); low(tooLow);
                var tooHigh = new CutoffDto(); high(tooHigh);
                var edgeLow = new CutoffDto(); lowestOk(edgeLow);
                var edgeHigh = new CutoffDto(); highestOk(edgeHigh);

                Assert.That(Check(tooLow), Has.Some.Contains("cutoff." + name), name + " too low");
                Assert.That(Check(tooHigh), Has.Some.Contains("cutoff." + name), name + " too high");
                Assert.IsEmpty(Check(edgeLow), name + " at the lowest value that is allowed");
                Assert.IsEmpty(Check(edgeHigh), name + " at the highest value that is allowed");
            }
        }

        [Test]
        public void ANumberThatIsNotANumber_IsRefused()
        {
            var dto = new CutoffDto { spreadPalms = float.NaN, holdSec = float.PositiveInfinity };

            var errors = Check(dto);

            Assert.That(errors, Has.Some.Contains("cutoff.spreadPalms"));
            Assert.That(errors, Has.Some.Contains("cutoff.holdSec"));
        }

        [Test]
        public void TheContentValidator_ChecksTheBlock()
        {
            var content = TestContent.LoadShipped();
            content.cutoff.minApartPalms = 20f;

            Assert.That(ContentValidator.Validate(content), Has.Some.Contains("cutoff.minApartPalms"));
        }

        [Test]
        public void AContentFileFromBeforeTheBlock_StillLoads_IsValid_AndGetsTheDefaults()
        {
            var content = JsonUtility.FromJson<ContentDto>(ShippedJsonWithoutTheBlock());

            Assert.IsEmpty(ContentValidator.Validate(content), "an older content.json still passes the checks");
            var settings = CutoffSettings.From(content.cutoff);
            var defaults = CutoffSettings.Default;
            Assert.AreEqual(defaults.SpreadPalms, settings.SpreadPalms);
            Assert.AreEqual(defaults.MinApartPalms, settings.MinApartPalms);
            Assert.AreEqual(defaults.WindowSec, settings.WindowSec);
            Assert.AreEqual(defaults.HoldSec, settings.HoldSec);
            Assert.AreEqual(defaults.FadeSec, settings.FadeSec);
            Assert.AreEqual(defaults.RestartLockSec, settings.RestartLockSec);
            Assert.AreEqual(defaults.EndWordSec, settings.EndWordSec);
        }

        [Test]
        public void ABlockThatNamesOnlySomeNumbers_KeepsTheDefaultsForTheOthers()
        {
            var json = ShippedJsonWithoutTheBlock().Replace("\"conduct\":{", "\"cutoff\":{\"spreadPalms\":3.0},\"conduct\":{");

            var content = JsonUtility.FromJson<ContentDto>(json);

            Assert.IsEmpty(ContentValidator.Validate(content));
            Assert.AreEqual(3f, content.cutoff.spreadPalms);
            Assert.AreEqual(new CutoffDto().minApartPalms, content.cutoff.minApartPalms);
            Assert.AreEqual(new CutoffDto().restartLockSec, content.cutoff.restartLockSec);
        }

        [Test]
        public void TheSettings_TakeTheContentNumbers()
        {
            var settings = CutoffSettings.From(new CutoffDto { spreadPalms = 3f, minApartPalms = 5f, windowSec = 0.8f, holdSec = 0.3f, fadeSec = 0.4f, restartLockSec = 2f, endWordSec = 2.5f });

            Assert.AreEqual(3f, settings.SpreadPalms);
            Assert.AreEqual(5f, settings.MinApartPalms);
            Assert.AreEqual(0.8, settings.WindowSec, 1e-6);
            Assert.AreEqual(0.3, settings.HoldSec, 1e-6);
            Assert.AreEqual(0.4, settings.FadeSec, 1e-6);
            Assert.AreEqual(2.0, settings.RestartLockSec, 1e-6);
            Assert.AreEqual(2.5, settings.EndWordSec, 1e-6);
        }

        [Test]
        public void TheCutoffBlock_IsNamedWhenItChanges_AndWaitsForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.cutoff.spreadPalms += 1f;

            CollectionAssert.AreEqual(new[] { "cutoff" }, ContentSections.Changed(before, after));
            CollectionAssert.AreEqual(new[] { "cutoff" }, ContentSections.NeedingRestart(before, after), "F5 says so instead of silently ignoring it");
        }
    }
}
