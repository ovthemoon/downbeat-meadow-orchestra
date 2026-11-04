using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using static Downbeat.Tests.SongFixtures;

namespace Downbeat.Tests
{
    /// <summary>The <c>songs</c> of content.json: what a score is, and every check that decides whether the instruments can play it.</summary>
    public class SongBookTests
    {
        // ---- the shape of the data ----------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheJsonOfASong_ReadsThroughJsonUtility()
        {
            const string json = @"{
              ""songs"": [{ ""id"": ""quiet-meadow"", ""title"": ""Quiet Meadow"", ""credit"": ""test"", ""bpm"": 96, ""beatsPerBar"": 4,
                ""melodyOctave"": { ""harp"": -12, ""recorder"": 0, ""bell"": 0 },
                ""bars"": [ { ""bass"": 57, ""chord"": [69, 72, 76], ""melody"": [ { ""slot"": 0, ""midi"": 76 }, { ""slot"": 6, ""midi"": 79 } ] } ] }],
              ""conduct"": { ""songPhrases"": [ { ""part"": ""lowstring"", ""steps"": [0, 0, 2, 0] } ] },
              ""experience"": { ""wakingSec"": 1 }
            }";

            var content = JsonUtility.FromJson<ContentDto>(json);

            var song = content.songs.Single();
            Assert.AreEqual("quiet-meadow", song.id);
            Assert.AreEqual(0.5f, song.swing, "a song without a swing is straight");
            Assert.AreEqual(-12, song.melodyOctave.harp);
            Assert.AreEqual(new[] { 69, 72, 76 }, song.bars[0].chord);
            Assert.AreEqual(6, song.bars[0].melody[1].slot);
            Assert.AreEqual(79, song.bars[0].melody[1].midi);
            Assert.AreEqual(new[] { 0, 0, 2, 0 }, content.conduct.songPhrases.Single().steps);
            Assert.IsTrue(content.experience.songMode, "the songs are on unless the content says otherwise");
            Assert.AreEqual(3f, content.experience.songTitleSec);
        }

        [Test]
        public void ChordTone_CountsTheBassThenTheChordThenTheChordAnOctaveUp()
        {
            var bar = SongBookResultOf(ValidSong()).Bars[0];

            var tones = Enumerable.Range(0, SongBar.LastToneIndex + 1).Select(bar.ChordTone).ToArray();

            Assert.AreEqual(new[] { 57, 69, 72, 76, 81, 84, 88 }, tones);
            Assert.AreEqual(93, bar.ChordTone(7), "and on, an octave higher every three");
            Assert.Throws<ArgumentOutOfRangeException>(() => bar.ChordTone(-1));
        }

        [Test]
        public void TryMelodyAt_FindsTheNoteOfASlot_AndNothingElse()
        {
            var bar = SongBookResultOf(ValidSong()).Bars[0];

            Assert.IsTrue(bar.TryMelodyAt(6, out var midi));
            Assert.AreEqual(79, midi);
            Assert.IsTrue(bar.TryMelodyAt(0, out midi));
            Assert.AreEqual(76, midi);
            Assert.IsFalse(bar.TryMelodyAt(7, out midi));
            Assert.IsFalse(bar.TryMelodyAt(-1, out _));
            Assert.IsFalse(bar.TryMelodyAt(99, out _));
        }

        static SongScore SongBookResultOf(SongEntryDto song) => SongBook.Build(ContentWith(song)).Songs.Single();

        // ---- a good song --------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_AcceptsAValidSong()
        {
            var result = SongBook.Build(ContentWith(ValidSong()));

            Assert.IsEmpty(result.Rejections);
            var score = result.Songs.Single();
            Assert.AreEqual("quiet-meadow", score.Id);
            Assert.AreEqual("Title of quiet-meadow", score.Title);
            Assert.AreEqual("test", score.Credit);
            Assert.AreEqual(2, score.BarCount);
            Assert.AreEqual(2, score.Bars.Count);
            Assert.IsTrue(score.Bars[0].TryMelodyAt(6, out var midi));
            Assert.AreEqual(79, midi);
            Assert.AreEqual(57, score.Bars[0].Bass);
            Assert.AreEqual(new[] { 65, 69, 72 }, score.Bars[1].Chord.ToArray());
        }

        [Test]
        public void Build_WithoutSongs_IsEmptyAndHasNothingToSay()
        {
            var none = ContentWith();
            var missing = ContentWith();
            missing.songs = null;

            foreach (var content in new[] { none, missing })
            {
                var result = SongBook.Build(content);
                Assert.IsEmpty(result.Songs);
                Assert.IsEmpty(result.Rejections);
            }
        }

        [Test]
        public void Build_ReadsTheShippedContent_WhateverSongsItHolds()
        {
            var shipped = TestContent.LoadShipped();

            var result = SongBook.Build(shipped);

            Assert.IsEmpty(result.Rejections, "whatever the content ships is playable");
            Assert.AreEqual(shipped.songs?.Length ?? 0, result.Songs.Count);
        }

        // ---- the frame of the app ------------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_RejectsAnotherTempo()
        {
            var fast = ValidSong();
            fast.bpm = 120;
            var waltz = ValidSong("waltz");
            waltz.beatsPerBar = 3;

            var result = SongBook.Build(ContentWith(fast, waltz));

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(2, result.Rejections.Count);
            StringAssert.Contains("quiet-meadow", result.Rejections[0]);
            StringAssert.Contains("120", result.Rejections[0]);
            StringAssert.Contains("waltz", result.Rejections[1]);
        }

        [TestCase(0.6f)]
        [TestCase(0.4f)]
        [TestCase(float.NaN)]
        public void Build_RejectsSwing(float swing)
        {
            var song = ValidSong();
            song.swing = swing;

            var result = SongBook.Build(ContentWith(song));

            Assert.IsEmpty(result.Songs);
            StringAssert.Contains("swing", result.Rejections.Single());
        }

        [Test]
        public void Build_WithoutTheSongBlock_RejectsEverySongInsteadOfThrowing()
        {
            var content = ContentWith(ValidSong("a"), ValidSong("b"));
            content.song = null;

            var result = SongBook.Build(content);

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(2, result.Rejections.Count);
            StringAssert.Contains("song block", result.Rejections[0]);
        }

        [Test]
        public void Build_ANoteOfAnyInt_IsRejectedWithoutThrowing()
        {
            var low = Song("low", AminorBar((0, int.MinValue)));
            var high = Song("high", AminorBar((0, int.MaxValue)));
            var wraps = Song("wraps", AminorBar((0, int.MaxValue)));
            wraps.melodyOctave = new MelodyOctaveDto { harp = 12, recorder = 12, bell = 12 };

            var result = SongBook.Build(ContentWith(low, high, wraps));

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(3, result.Rejections.Count);
        }

        // ---- the shape of the bars -----------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_RejectsASongWithoutBars()
        {
            var none = Song("none");
            var missing = Song("missing");
            missing.bars = null;

            var result = SongBook.Build(ContentWith(none, missing));

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(2, result.Rejections.Count);
            StringAssert.Contains("bars", result.Rejections[0]);
        }

        [Test]
        public void Build_RejectsABarWithoutThreeChordNotes()
        {
            var two = Song("two", Bar(57, new[] { 69, 72 }));
            var four = Song("four", Bar(57, new[] { 69, 72, 76, 79 }));
            var none = Song("none", Bar(57, null));

            var result = SongBook.Build(ContentWith(two, four, none));

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(3, result.Rejections.Count);
            Assert.That(result.Rejections, Has.All.Contains("chord"));
        }

        [TestCase(16)]
        [TestCase(-1)]
        [TestCase(99)]
        public void Build_RejectsASlotOutsideTheBar(int slot)
        {
            var result = SongBook.Build(ContentWith(Song("far", AminorBar((slot, 76)))));

            Assert.IsEmpty(result.Songs);
            StringAssert.Contains("slot " + slot, result.Rejections.Single());
        }

        [Test]
        public void Build_RejectsSlotsThatDoNotGrow()
        {
            var backwards = Song("backwards", AminorBar((8, 76), (4, 76)));
            var twice = Song("twice", AminorBar((8, 76), (8, 79)));

            var result = SongBook.Build(ContentWith(backwards, twice));

            Assert.IsEmpty(result.Songs);
            Assert.AreEqual(2, result.Rejections.Count);
            StringAssert.Contains("backwards", result.Rejections[0]);
            StringAssert.Contains("twice", result.Rejections[1]);
        }

        [Test]
        public void Build_AcceptsABarWithoutMelody()
        {
            var result = SongBook.Build(ContentWith(Song("rest", AminorBar(), AminorBar((0, 76)))));

            Assert.IsEmpty(result.Rejections);
            Assert.IsFalse(result.Songs.Single().Bars[0].TryMelodyAt(0, out _));
        }

        // ---- the names -----------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_RejectsDuplicateIdsAndEmptyTitle()
        {
            var first = ValidSong("same");
            var second = ValidSong("same");
            var untitled = ValidSong("untitled");
            untitled.title = "";
            var blankTitle = ValidSong("blank");
            blankTitle.title = "  ";
            var nameless = ValidSong("");

            var result = SongBook.Build(ContentWith(first, second, untitled, blankTitle, nameless));

            Assert.AreEqual(new[] { "same" }, result.Songs.Select(s => s.Id).ToArray(), "the first one of an id stays");
            Assert.AreEqual(4, result.Rejections.Count);
            StringAssert.Contains("same", result.Rejections[0]);
            StringAssert.Contains("untitled", result.Rejections[1]);
            StringAssert.Contains("title", result.Rejections[1]);
            StringAssert.Contains("title", result.Rejections[2]);
            StringAssert.Contains("id", result.Rejections[3]);
        }

        [Test]
        public void Build_AnIdOfARejectedSong_CanBeUsedAgain()
        {
            var bad = ValidSong("again");
            bad.bpm = 120;
            var good = ValidSong("again");

            var result = SongBook.Build(ContentWith(bad, good));

            Assert.AreEqual(1, result.Songs.Count);
            Assert.AreEqual(1, result.Rejections.Count);
        }

        [Test]
        public void Build_AnEmptyEntryIsRejectedWithoutThrowing()
        {
            var result = SongBook.Build(ContentWith(null, ValidSong()));

            Assert.AreEqual(1, result.Songs.Count);
            StringAssert.Contains("songs[0]", result.Rejections.Single());
        }

        // ---- what the instruments can play ---------------------------------------------------------------------------------------------------

        [Test]
        public void Build_RejectsAMelodyNoteTheBellCannotPlay()
        {
            // the nearest bell samples are 74 and 80, and a bell may be moved 2 semitones: 77 is 3 away from both
            var song = Song("high-fa", AminorBar((0, 76), (4, 77)));

            var result = SongBook.Build(ContentWith(song));

            Assert.IsEmpty(result.Songs);
            var reason = result.Rejections.Single();
            StringAssert.Contains("high-fa", reason);
            StringAssert.Contains("77", reason);
            StringAssert.Contains("bell", reason);
        }

        [Test]
        public void Build_UsesMelodyOctavePerPart()
        {
            var lowHarp = Song("low-harp", AminorBar((0, 79)));
            lowHarp.melodyOctave = new MelodyOctaveDto { harp = -12 };
            var high = Song("high", AminorBar((0, 93)));
            var tooLowForTheHarp = Song("too-low", AminorBar((0, 70)));   // the recorder and the bell play 70
            tooLowForTheHarp.melodyOctave = new MelodyOctaveDto { harp = -24 };
            var tooHighForTheHarp = Song("too-high", AminorBar((0, 93)));   // the recorder and the bell play 93
            tooHighForTheHarp.melodyOctave = new MelodyOctaveDto { harp = 12 };

            var result = SongBook.Build(ContentWith(lowHarp, high, tooLowForTheHarp, tooHighForTheHarp));

            Assert.AreEqual(new[] { "low-harp", "high" }, result.Songs.Select(s => s.Id).ToArray(),
                "79 sounds as 67 on the harp (it plays 57..98), and 93 is still inside what all three can play");
            Assert.AreEqual(-12, result.Songs[0].OctaveFor(Part.Harp));
            Assert.AreEqual(0, result.Songs[0].OctaveFor(Part.Recorder));
            Assert.AreEqual(0, result.Songs[0].OctaveFor(Part.Bell));
            Assert.AreEqual(2, result.Rejections.Count);
            StringAssert.Contains("too-low", result.Rejections[0]);
            StringAssert.Contains("harp", result.Rejections[0]);
            StringAssert.Contains("70", result.Rejections[0]);
            StringAssert.Contains("too-high", result.Rejections[1]);
            StringAssert.Contains("harp", result.Rejections[1]);
        }

        [Test]
        public void OctaveFor_IsZeroForTheLowerRow()
        {
            var song = ValidSong();
            song.melodyOctave = new MelodyOctaveDto { harp = -12, recorder = 12, bell = 12 };
            song.bars = new[] { AminorBar((0, 72)) };

            var score = SongBook.Build(ContentWith(song)).Songs.Single();

            Assert.AreEqual(-12, score.OctaveFor(Part.Harp));
            Assert.AreEqual(12, score.OctaveFor(Part.Recorder));
            Assert.AreEqual(12, score.OctaveFor(Part.Bell));
            Assert.AreEqual(0, score.OctaveFor(Part.LowString));
            Assert.AreEqual(0, score.OctaveFor(Part.Marimba));
            Assert.AreEqual(0, score.OctaveFor(Part.HandDrum));
        }

        [Test]
        public void Build_AMissingMelodyOctave_IsNoChange()
        {
            var song = ValidSong();
            song.melodyOctave = null;

            var score = SongBook.Build(ContentWith(song)).Songs.Single();

            Assert.AreEqual(0, score.OctaveFor(Part.Harp));
        }

        // ---- the gap between the notes -------------------------------------------------------------------------------------------------------

        [Test]
        public void Validation_CountsTheGapAcrossTheBarLine()
        {
            var tooClose = Song("too-close", AminorBar((15, 76)), AminorBar((0, 76)));
            var closeEnough = Song("close-enough", AminorBar((14, 76)), AminorBar((0, 76)));

            var result = SongBook.Build(ContentWith(tooClose, closeEnough));

            Assert.AreEqual(new[] { "close-enough" }, result.Songs.Select(s => s.Id).ToArray(), "the harp may repeat after 1 slot, the recorder and the bell need 2");
            var reason = result.Rejections.Single();
            StringAssert.Contains("too-close", reason);
            StringAssert.Contains("recorder", reason);
        }

        [Test]
        public void Validation_CountsTheGapInsideABar()
        {
            var result = SongBook.Build(ContentWith(Song("tight", AminorBar((4, 76), (5, 79)))));

            Assert.IsEmpty(result.Songs);
            StringAssert.Contains("1 slot", result.Rejections.Single());
        }

        // ---- the lower row ---------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_RejectsAnAccompanimentNoteThePartCannotPlay()
        {
            // chord rooted at 71: index 6 is chord[2] + 12 = 90, and the low string plays up to 79 + 2
            var song = Song("too-high-chord", Bar(59, new[] { 71, 74, 78 }, (0, 76)));
            var content = ContentWith(song);
            content.conduct.songPhrases = new[] { Phrase("lowstring", 0, 6) };

            var result = SongBook.Build(content);

            Assert.IsEmpty(result.Songs);
            var reason = result.Rejections.Single();
            StringAssert.Contains("too-high-chord", reason);
            StringAssert.Contains("lowstring", reason);
            StringAssert.Contains("90", reason);
        }

        [Test]
        public void Build_AcceptsAnAccompanimentTheOtherIndexesKeepInsideTheRange()
        {
            var song = Song("fine-chord", Bar(59, new[] { 71, 74, 78 }, (0, 76)));
            var content = ContentWith(song);
            content.conduct.songPhrases = new[] { Phrase("lowstring", 0, 0, 2, 0), Phrase("marimba", 1, 2, 3, 2) };

            var result = SongBook.Build(content);

            Assert.IsEmpty(result.Rejections);
        }

        [Test]
        public void Build_ChecksOnlyTheIndexesAPhraseUses()
        {
            var song = Song("only-used", Bar(59, new[] { 71, 74, 78 }, (0, 76)));
            var content = ContentWith(song);
            content.conduct.songPhrases = new[] { Phrase("lowstring", 0, 1, 2, 3) };   // no index 4..6, which would be too high

            Assert.IsEmpty(SongBook.Build(content).Rejections);
        }

        [Test]
        public void Build_WithoutSongPhrases_HasNoAccompanimentToCheck()
        {
            var song = Song("no-phrases", Bar(59, new[] { 71, 74, 78 }, (0, 76)));
            var withNull = ContentWith(song);
            withNull.conduct.songPhrases = null;
            var withoutConduct = ContentWith(song);
            withoutConduct.conduct = null;

            Assert.IsEmpty(SongBook.Build(withNull).Rejections);
            Assert.IsEmpty(SongBook.Build(withoutConduct).Rejections);
        }

        [Test]
        public void Build_PhrasesOfTheUpperRow_AreNotAccompaniment()
        {
            // index 6 of this bar is 108, which the harp cannot play; the harp plays the melody, so the phrase is not checked
            var song = Song("harp-phrase", Bar(59, new[] { 90, 93, 96 }, (0, 76)));
            var content = ContentWith(song);
            content.conduct.songPhrases = new[] { Phrase("harp", 6) };

            Assert.IsEmpty(SongBook.Build(content).Rejections);
        }

        // ---- conduct.songPhrases in the content check ----------------------------------------------------------------------------------------

        static List<string> CheckConduct(params ConductPhraseDto[] songPhrases)
        {
            var errors = new List<string>();
            ConductContentValidator.Check(new ConductDto { songPhrases = songPhrases }, new SongDto { pitchSteps = 10 }, errors);
            return errors;
        }

        [Test]
        public void SongPhrases_OfTheLowerRow_AreAccepted_AndAPartMayBeLeftOut()
        {
            Assert.IsEmpty(CheckConduct(Phrase("lowstring", 0, 0, 2, 0), Phrase("marimba", 1, 2, 3, 2)), "the hand drum plays its phrase of before");
            Assert.IsEmpty(CheckConduct(Phrase("lowstring", 0, 1, 2, 3, 4, 5, 6)), "0..6 are the bass, the chord, and the chord an octave up");
            Assert.IsEmpty(CheckConduct(), "none at all: a song has no accompaniment of its own");
            Assert.IsEmpty(CheckConduct(null));
        }

        [Test]
        public void SongPhrases_AnUnknownPartIsRefused()
        {
            Assert.That(CheckConduct(Phrase("brass", 0)), Has.Some.Contains("songPhrases: unknown part 'brass'"));
            Assert.That(CheckConduct(new ConductPhraseDto[] { null }), Has.Some.Contains("songPhrases: unknown part"));
        }

        [TestCase(7)]
        [TestCase(-1)]
        [TestCase(100)]
        public void SongPhrases_AnIndexOutsideZeroToSixIsRefused(int index)
        {
            var errors = CheckConduct(Phrase("marimba", 1, index));

            Assert.That(errors, Has.Some.Contains($"songPhrases.marimba: index {index} is outside 0..{SongBar.LastToneIndex}"));
        }

        [Test]
        public void SongPhrases_NeedAtLeastOneStep_AndNotMoreThanAPhraseMay()
        {
            Assert.That(CheckConduct(Phrase("lowstring")), Has.Some.Contains("songPhrases.lowstring.steps must hold 1..32 steps"));
            Assert.That(CheckConduct(new ConductPhraseDto { part = "lowstring" }), Has.Some.Contains("songPhrases.lowstring.steps"));
            Assert.That(CheckConduct(Phrase("lowstring", Enumerable.Repeat(0, 33).ToArray())), Has.Some.Contains("songPhrases.lowstring.steps"));
        }

        [Test]
        public void SongPhrases_APartTwice_IsRefused()
        {
            Assert.That(CheckConduct(Phrase("marimba", 1), Phrase("perc", 2)), Has.Some.Contains("songPhrases: duplicate part"), "perc is the old name of the marimba");
        }

        [Test]
        public void SongPhrases_ASongNeverFailsTheContent()
        {
            var content = ContentWith(Song("bad", AminorBar((0, 77))));

            Assert.IsEmpty(ContentValidator.Validate(content), "a bad song is a rejection of the song book, not an error of the content");
        }

        // ---- several songs -------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Build_KeepsTheOthers_WhenOneSongIsBad()
        {
            var first = ValidSong("first");
            var bad = Song("bad", AminorBar((0, 77)));
            var last = ValidSong("last");

            var result = SongBook.Build(ContentWith(first, bad, last));

            Assert.AreEqual(new[] { "first", "last" }, result.Songs.Select(s => s.Id).ToArray(), "the order of the content is kept");
            Assert.AreEqual(1, result.Rejections.Count);
            StringAssert.Contains("bad", result.Rejections[0]);
        }

        [Test]
        public void Build_ASongThatHasSeveralProblems_IsRejectedOnce()
        {
            var song = Song("many", AminorBar((16, 77)));
            song.bpm = 60;
            song.swing = 0.7f;

            var result = SongBook.Build(ContentWith(song));

            Assert.AreEqual(1, result.Rejections.Count);
        }

        [Test]
        public void TheScore_IsNotChangedByTheDataItWasBuiltFrom()
        {
            var song = ValidSong();
            var score = SongBook.Build(ContentWith(song)).Songs.Single();

            song.bars[0].chord[0] = 1;
            song.bars[0].melody[0].midi = 1;
            song.bars[0].bass = 1;

            Assert.AreEqual(57, score.Bars[0].Bass);
            Assert.AreEqual(69, score.Bars[0].Chord[0]);
            Assert.IsTrue(score.Bars[0].TryMelodyAt(0, out var midi));
            Assert.AreEqual(76, midi);
        }
    }
}
