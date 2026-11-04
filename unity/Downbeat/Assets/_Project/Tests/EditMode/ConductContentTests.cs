using System.Collections.Generic;
using System.IO;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The <c>conduct</c> block of content.json: ranges, the cross rules, the defaults of a file without it, and the mode override.</summary>
    public class ConductContentTests
    {
        static readonly SongDto Song = new SongDto { pitchSteps = 10 };

        static List<string> Check(ConductDto dto)
        {
            var errors = new List<string>();
            ConductContentValidator.Check(dto, Song, errors);
            return errors;
        }

        static ConductDto Good() => new ConductDto
        {
            phrases = new[]
            {
                new ConductPhraseDto { part = "harp", steps = new[] { 0, 2, 4, 7, 4, 2 } },
                new ConductPhraseDto { part = "recorder", steps = new[] { 7, 5, 4, 2 } },
                new ConductPhraseDto { part = "bell", steps = new[] { 9, 7, 9, 5 } },
                new ConductPhraseDto { part = "lowstring", steps = new[] { 0, 0, 2, 0 } },
                new ConductPhraseDto { part = "marimba", steps = new[] { 0, 0, 4, 0 } },
                new ConductPhraseDto { part = "handdrum", steps = new[] { 0, 0, 0, 0 } },
            },
        };

        [Test]
        public void TheDefaultsAndTheShippedContentPass()
        {
            Assert.IsEmpty(Check(Good()));
            Assert.IsEmpty(Check(new ConductDto()), "no phrases: the built-in ones apply");
            Assert.IsEmpty(Check(null), "no block at all: the defaults apply");
            var shipped = JsonUtility.FromJson<ContentDto>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "content.json"))));
            Assert.IsNotNull(shipped.conduct);
            Assert.AreEqual("conduct", shipped.conduct.mode);
            Assert.IsEmpty(ContentValidator.Validate(shipped));
        }

        // ---- the six parts of the shipped content, and a file of before ----------------------------------------------------------------------

        static readonly string[] SixNames = { "harp", "recorder", "bell", "lowstring", "marimba", "handdrum" };

        [Test]
        public void Content_ShippedHasSixInstrumentsAndPhrases()
        {
            var shipped = TestContent.LoadShipped();

            CollectionAssert.AreEqual(SixNames, shipped.instruments.Select(i => i.part).ToArray(), "one instrument for each cell, in the order of the Part enum");
            CollectionAssert.AreEqual(SixNames, shipped.song.partMinGapSlots.Select(g => g.part).ToArray());
            CollectionAssert.AreEqual(SixNames, shipped.conduct.phrases.Select(p => p.part).ToArray());
            Assert.AreEqual(4, shipped.schemaVersion);
            Assert.AreEqual(4f, shipped.conduct.loudPalms);
            Assert.IsEmpty(ContentValidator.Validate(shipped));
        }

        [Test]
        public void Content_ThreePartFile_IsRejected()
        {
            var old = TestContent.LoadShipped();
            old.schemaVersion = 3;
            old.instruments = old.instruments.Where(i => i.part == "harp" || i.part == "recorder" || i.part == "marimba").ToArray();
            old.instruments[0].part = "strings";
            old.instruments[1].part = "winds";
            old.instruments[2].part = "perc";
            old.song.partMinGapSlots = new[] { new PartGapDto { part = "strings", slots = 1 }, new PartGapDto { part = "winds", slots = 2 }, new PartGapDto { part = "perc", slots = 1 } };
            old.conduct.phrases = old.conduct.phrases.Take(3).ToArray();

            var errors = ContentValidator.Validate(old);

            Assert.That(errors, Has.Some.Contains("schemaVersion 3 is not 4"));
            Assert.That(errors, Has.Some.Contains("song.partMinGapSlots must list all 6 parts"));
            Assert.That(errors, Has.Some.Contains("conduct.phrases must list all 6 parts"));
        }

        [Test]
        public void AFileWithSixPartsButAnOldName_DoesNotCountAPartTwice()
        {
            var content = TestContent.LoadShipped();
            content.song.partMinGapSlots[0].part = "strings";   // the harp under its old name ...
            content.song.partMinGapSlots[5].part = "harp";      // ... and a second time

            Assert.That(ContentValidator.Validate(content), Has.Some.Contains("duplicate part"));
        }

        [Test]
        public void OldContentWithoutTheCellsBlock_UsesDefaults()
        {
            var json = File.ReadAllText(TestContent.RepoContentPath);
            var start = json.IndexOf("\"cells\"", System.StringComparison.Ordinal);
            var end = json.IndexOf("\"autoQuality\"", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "the shipped content has a cells block");
            var without = json.Substring(0, start) + json.Substring(end);
            Assert.IsFalse(without.Contains("\"cells\""), "the block is gone from the text");

            var content = JsonUtility.FromJson<ContentDto>(without);
            var cells = CellSettings.From(content.cells);

            Assert.IsEmpty(ContentValidator.Validate(content), "a file written before the cells needs no block");
            Assert.AreEqual(0.5f, cells.RowSplit);
            Assert.AreEqual(0.03f, cells.RowHysteresis);
            CollectionAssert.AreEqual(CellsDto.DefaultLabels, cells.Labels);
            CollectionAssert.AreEqual(CellsDto.DefaultColors, cells.Colors);
        }

        [Test]
        public void TheCellsOfTheShippedContent_AreTheDefaults()
        {
            var shipped = TestContent.LoadShipped();
            var defaults = new CellsDto();

            Assert.AreEqual(defaults.rowSplit, shipped.cells.rowSplit);
            Assert.AreEqual(defaults.rowHysteresis, shipped.cells.rowHysteresis);
            CollectionAssert.AreEqual(defaults.labels, shipped.cells.labels, "tools/lib/content-build.mjs and CellsDto say the same");
            CollectionAssert.AreEqual(defaults.colors, shipped.cells.colors);
        }

        [Test]
        public void TheCellsBlockHasItsRanges()
        {
            void Refuses(System.Action<CellsDto> change, string name)
            {
                var cells = new CellsDto();
                change(cells);
                var errors = new List<string>();
                CellsContentValidator.Check(cells, errors);
                Assert.That(errors, Has.Some.Contains(name), name);
            }

            Refuses(c => c.rowSplit = 0.2f, "cells.rowSplit");
            Refuses(c => c.rowSplit = 0.8f, "cells.rowSplit");
            Refuses(c => c.rowSplit = float.NaN, "cells.rowSplit");
            Refuses(c => c.rowHysteresis = -0.01f, "cells.rowHysteresis");
            Refuses(c => c.rowHysteresis = 0.2f, "cells.rowHysteresis");
            Refuses(c => c.labels = new[] { "a", "b" }, "cells.labels");
            Refuses(c => c.labels[3] = " ", "cells.labels[3]");
            Refuses(c => c.colors = new[] { "#FFFFFF" }, "cells.colors");
            Refuses(c => c.colors[2] = "blue", "cells.colors[2]");
            var fine = new List<string>();
            CellsContentValidator.Check(new CellsDto(), fine);
            CellsContentValidator.Check(null, fine);
            Assert.IsEmpty(fine);
        }

        [Test]
        public void TheCellsBlockIsASectionOfItsOwn_ThatWaitsForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.cells.rowSplit = 0.6f;

            CollectionAssert.AreEqual(new[] { "cells" }, Downbeat.Content.ContentSections.NeedingRestart(before, after).ToArray());
        }

        [Test]
        public void AnUnknownModeIsRefused()
        {
            var dto = Good();
            dto.mode = "dancing";

            Assert.That(Check(dto), Has.Some.Contains("conduct.mode"));
        }

        [TestCase(100f)]
        [TestCase(2000f)]
        public void TheMotionWindowHasARange(float value)
        {
            var dto = Good();
            dto.motionWindowMs = value;

            Assert.That(Check(dto), Has.Some.Contains("motionWindowMs"));
        }

        [Test]
        public void EveryNumberHasItsRange()
        {
            void Refuses(System.Action<ConductDto> change, string name)
            {
                var dto = Good();
                change(dto);
                Assert.That(Check(dto), Has.Some.Contains(name), name);
            }

            Refuses(d => d.stillPalms = -0.1f, "stillPalms");
            Refuses(d => d.stillPalms = 2.5f, "stillPalms");
            Refuses(d => d.minVelocity = 1.2f, "minVelocity");
            Refuses(d => d.minVelocity = -0.1f, "minVelocity");
            Refuses(d => d.loudPalms = d.stillPalms, "loudPalms");
            Refuses(d => d.loudPalms = 0.1f, "loudPalms");
            Refuses(d => d.loudPalms = 21f, "loudPalms");
            Refuses(d => d.loudPalms = float.NaN, "loudPalms");
            Refuses(d => d.downbeatAccent = 0.6f, "downbeatAccent");
            Refuses(d => d.releaseSec = 0.05f, "releaseSec");
            Refuses(d => d.releaseSec = 11f, "releaseSec");
            Refuses(d => d.crossfadeSec = 0.01f, "crossfadeSec");
            Refuses(d => d.crossfadeSec = d.releaseSec + 1f, "crossfadeSec");
            Refuses(d => d.holdMs = -1f, "holdMs");
            Refuses(d => d.holdMs = 6000f, "holdMs");
            Refuses(d => d.motionWindowMs = float.NaN, "motionWindowMs");
        }

        [Test]
        public void TheDensityThresholdsMustAscend()
        {
            var equalStill = Good();
            equalStill.halfBeatPalms = equalStill.stillPalms;
            var halfAboveQuarter = Good();
            halfAboveQuarter.halfBeatPalms = 5f;
            var equalHalf = Good();
            equalHalf.quarterBeatPalms = equalHalf.halfBeatPalms;

            Assert.That(Check(equalStill), Has.Some.Contains("halfBeatPalms must be more than conduct.stillPalms"));
            Assert.That(Check(halfAboveQuarter), Has.Some.Contains("quarterBeatPalms must be more than conduct.halfBeatPalms"));
            Assert.That(Check(equalHalf), Has.Some.Contains("quarterBeatPalms"));
        }

        [Test]
        public void APhraseStepMustBeAStepOfTheScale()
        {
            var dto = Good();
            dto.phrases[0].steps = new[] { 0, 10 };

            Assert.That(Check(dto), Has.Some.Contains("step 10 is outside 0..9"));
            dto.phrases[0].steps = new[] { 0, -1 };
            Assert.That(Check(dto), Has.Some.Contains("step -1"));
        }

        [Test]
        public void APhraseHasOneToThirtyTwoSteps_AndEveryPartHasOne()
        {
            var empty = Good();
            empty.phrases[1].steps = new int[0];
            var tooLong = Good();
            tooLong.phrases[4].steps = Enumerable.Repeat(1, 33).ToArray();
            var missing = Good();
            missing.phrases = missing.phrases.Take(3).ToArray();   // the three parts of before the six cells are not enough
            var twice = Good();
            twice.phrases[2].part = "recorder";
            var twiceUnderAnOldName = Good();
            twiceUnderAnOldName.phrases[2].part = "strings";   // the old name of the harp: the harp twice
            var unknown = Good();
            unknown.phrases[0].part = "brass";

            Assert.That(Check(empty), Has.Some.Contains("recorder.steps must hold 1..32"));
            Assert.That(Check(tooLong), Has.Some.Contains("marimba.steps must hold 1..32"));
            Assert.That(Check(missing), Has.Some.Contains("all 6 parts"));
            Assert.That(Check(twice), Has.Some.Contains("duplicate part"));
            Assert.That(Check(twiceUnderAnOldName), Has.Some.Contains("duplicate part"));
            Assert.That(Check(unknown), Has.Some.Contains("unknown part 'brass'"));
        }

        // ---- settings and the override ------------------------------------------------------------------------------------------------

        [Test]
        public void WithoutABlockTheHandConducts_WithTheDocumentedNumbers()
        {
            var settings = ConductSettings.From(null, null);

            Assert.AreEqual(PlayStyle.Conduct, settings.Style);
            Assert.AreEqual(400.0, settings.MotionWindowMs);
            Assert.AreEqual(0.3f, settings.StillPalms);
            Assert.AreEqual(4f, settings.LoudPalms);
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 7, 4, 2 }, settings.Phrase(Part.Harp).ToArray());
            CollectionAssert.AreEqual(new[] { 7, 5, 4, 2 }, settings.Phrase(Part.Recorder).ToArray());
            CollectionAssert.AreEqual(new[] { 9, 7, 9, 5 }, settings.Phrase(Part.Bell).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 2, 0 }, settings.Phrase(Part.LowString).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 4, 0 }, settings.Phrase(Part.Marimba).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, settings.Phrase(Part.HandDrum).ToArray());
        }

        [Test]
        public void AFileWithPhrasesForOnlySomeParts_KeepsTheBuiltInOnesForTheRest()
        {
            var dto = new ConductDto { phrases = new[] { new ConductPhraseDto { part = "strings", steps = new[] { 1, 1 } } } };   // an old name still names the harp

            var settings = ConductSettings.From(dto, null);

            CollectionAssert.AreEqual(new[] { 1, 1 }, settings.Phrase(Part.Harp).ToArray());
            CollectionAssert.AreEqual(new[] { 9, 7, 9, 5 }, settings.Phrase(Part.Bell).ToArray());
        }

        [Test]
        public void PlayModeOnTheCommandLine_BeatsTheContent()
        {
            var strike = new ConductDto { mode = "strike" };

            Assert.AreEqual(PlayStyle.Strike, ConductSettings.From(strike, null).Style);
            Assert.AreEqual(PlayStyle.Conduct, ConductSettings.From(strike, "conduct").Style);
            Assert.AreEqual(PlayStyle.Strike, ConductSettings.From(new ConductDto(), "STRIKE").Style, "any case");
            Assert.AreEqual(PlayStyle.Strike, ConductSettings.From(strike, "").Style, "an empty override changes nothing");
        }

        [Test]
        public void TheLatencyMeasurement_StrikesUnlessTheCommandLineSaysOtherwise()
        {
            Assert.IsNull(ConductSettings.ModeOverride(LaunchArgs.Parse(new[] { "Downbeat" })));
            Assert.AreEqual("strike", ConductSettings.ModeOverride(LaunchArgs.Parse(new[] { "Downbeat", "--latency-mode" })));
            Assert.AreEqual("conduct", ConductSettings.ModeOverride(LaunchArgs.Parse(new[] { "Downbeat", "--latency-mode", "--play-mode", "conduct" })));
            Assert.AreEqual("strike", ConductSettings.ModeOverride(LaunchArgs.Parse(new[] { "Downbeat", "--play-mode", "strike" })));
        }

        [Test]
        public void AnUnknownPlayMode_IsReported_AndTheContentDecides()
        {
            var settings = ConductSettings.From(new ConductDto { mode = "strike" }, "dancing", out var problem);

            Assert.AreEqual(PlayStyle.Strike, settings.Style);
            StringAssert.Contains("dancing", problem);
        }
    }
}
