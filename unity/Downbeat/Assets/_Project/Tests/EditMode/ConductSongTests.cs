using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.SongFixtures;
using Pose = Downbeat.Tests.HandTestData.Pose;

namespace Downbeat.Tests
{
    /// <summary>
    /// Songs inside the conducting engine (the design in docs/superpowers/specs/2026-11-01-song-mode-design.md, 4.1 to 4.3): with a song prepared, the hands of the upper row play its melody on the slots of the score
    /// and the hands of the lower row play the chord of the bar, while the movement of a hand still says how loud it plays and how often the lower row plays. The song starts on the bar line 2 bars into the run
    /// (slot 32, 15.0 s), has 4 bars (slots 32 to 95) and so is over at slot 96 (25.0 s); the hands are there from 9.0 s.
    /// </summary>
    public class ConductSongTests
    {
        const double Fps30 = 1.0 / 30.0;
        const double Fps6 = 1.0 / 6.0;
        const long StartSlot = 2 * ConductRig.SlotsPerBar;
        const int Bars = 4;
        const long EndSlot = StartSlot + Bars * ConductRig.SlotsPerBar;
        /// <summary>Long enough to play the whole song and a few bars after it.</summary>
        const double RunSec = 20.0;
        const double SettleSec = 2.0;

        /// <summary>A song of four bars: A minor, F, A minor with a busy melody, and F with a single note. The harp plays it <paramref name="harpOctave"/> semitones from the score.</summary>
        static SongEntryDto FourBarSong(int harpOctave = 0) => WithOctaves(Song("four-bars",
            AminorBar((0, 76), (6, 79), (8, 76)),
            FmajorBar((0, 72), (4, 76)),
            AminorBar((0, 79), (2, 76), (4, 79), (7, 72), (10, 76), (14, 79)),
            FmajorBar((0, 72))), harpOctave);

        static SongEntryDto WithOctaves(SongEntryDto entry, int harpOctave)
        {
            entry.melodyOctave = new MelodyOctaveDto { harp = harpOctave };
            return entry;
        }

        /// <summary>The shipped content with the four bar song, the song built from it, and rigs that play it.</summary>
        sealed class SongSetup
        {
            public SongSetup(int harpOctave = 0)
            {
                Content = ContentWith(FourBarSong(harpOctave));
                var book = SongBook.Build(Content);
                Assert.IsEmpty(book.Rejections, string.Join("; ", book.Rejections));
                Score = book.Songs.Single();
                Pitch = SongPitch.From(Content);
            }

            public ContentDto Content { get; }
            public SongScore Score { get; }
            public SongPitch Pitch { get; }

            /// <summary>A rig with the song prepared and started on <see cref="StartSlot"/> (only prepared when <paramref name="start"/> is false).</summary>
            public ConductRig Rig(bool start = true)
            {
                var rig = new ConductRig(Content.conduct);
                if (start) rig.PrepareAndStart(Score, StartSlot, Pitch);
                else rig.Engine.PrepareSong(Score, Pitch);
                return rig;
            }

            /// <summary>The same settings, no song.</summary>
            public ConductRig FreePlayRig() => new ConductRig(Content.conduct);

            /// <summary>Slot and MIDI of every melody note of the song as <paramref name="part"/> plays it, in order.</summary>
            public List<(long slot, int midi)> ScoreNotes(Part part)
            {
                var notes = new List<(long, int)>();
                for (var b = 0; b < Score.BarCount; b++)
                {
                    foreach (var note in Score.Bars[b].Melody) notes.Add((StartSlot + b * ConductRig.SlotsPerBar + note.Slot, note.Midi + Score.OctaveFor(part)));
                }
                return notes;
            }

            /// <summary>The bar of the song that a slot is played in: the first before the song starts and the last once it is over.</summary>
            public SongBar BarAt(long slot)
            {
                var index = (int)Math.Max(0, Math.Min(Score.BarCount - 1, FloorDiv(slot - StartSlot, ConductRig.SlotsPerBar)));
                return Score.Bars[index];
            }
        }

        static long FloorDiv(long value, int divisor) => (long)Math.Floor((double)value / divisor);

        static List<(long slot, int midi)> Played(ConductRig rig, Part part) => rig.Of(part).Select(n => (rig.SlotOf(n), n.Midi)).ToList();

        static Func<double, Pose[]> HandOut(Func<double, Pose[]> hand, double fromSec) => ConductRig.Gone(hand, 0.0, fromSec);

        // ---- the melody --------------------------------------------------------------------------------------------------------------------

        [Test]
        public void Melody_PlaysOnlyTheScoreSlots_WithTheScorePitch([Values(Fps30, Fps6)] double pictureEverySec)
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.Harp), pictureEverySec);

            CollectionAssert.AreEqual(setup.ScoreNotes(Part.Harp), Played(rig, Part.Harp), "the slots and the MIDI notes of the score, bar by bar, and nothing else");
            foreach (var note in rig.Of(Part.Harp)) Assert.AreEqual(setup.Pitch.StepFor(Part.Harp, note.Midi), note.PitchStep, "the step is the one of the harp that sounds nearest to the note");
        }

        [Test]
        public void Melody_AddsTheOctaveOfTheInstrument()
        {
            var setup = new SongSetup(harpOctave: -12);
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps30);

            CollectionAssert.AreEqual(setup.ScoreNotes(Part.Harp), Played(rig, Part.Harp));
            Assert.AreEqual(setup.Score.Bars[0].Melody[0].Midi - 12, rig.Of(Part.Harp).First().Midi);
        }

        [Test]
        public void Melody_KeepsTheVelocityRule_AStillHandIsSoft_AndTheBarLineIsAccented()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps30);

            var onBarLine = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) % ConductRig.SlotsPerBar == 0).ToList();
            var between = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) % ConductRig.SlotsPerBar != 0).ToList();
            Assert.IsNotEmpty(onBarLine);
            Assert.IsNotEmpty(between);
            Assert.That(between.Select(n => n.Velocity), Has.All.EqualTo(rig.Settings.MinVelocity).Within(0.02f));
            Assert.That(onBarLine.Select(n => n.Velocity), Has.All.EqualTo(rig.Settings.MinVelocity + rig.Settings.DownbeatAccent).Within(0.02f));
        }

        [Test]
        public void Melody_PlaysLouder_WhenTheHandIsWavedAbout()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.CircleIn(Part.Harp, 5f), Fps30);

            var between = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) >= StartSlot && rig.SlotOf(n) % ConductRig.SlotsPerBar != 0).ToList();
            Assert.IsNotEmpty(between);
            Assert.That(between.Select(n => n.Velocity), Has.All.GreaterThan(rig.Settings.MinVelocity + 0.3f), "movement is loudness, in a song as in free play");
        }

        [Test]
        public void Melody_IgnoresHowFastTheHandMoves_TheScoreSetsTheRhythm()
        {
            var setup = new SongSetup();
            var still = setup.Rig();
            var waving = setup.Rig();

            still.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps30);
            waving.Run(RunSec, ConductRig.CircleIn(Part.Harp, 5f), Fps30);

            CollectionAssert.AreEqual(Played(still, Part.Harp), Played(waving, Part.Harp));
        }

        [Test]
        public void Melody_WaitsForBarOne_WhenTheHandIsAlreadyUp()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps30);

            Assert.That(rig.Notes.Where(n => Parts.IsSky(n.Part)).Select(rig.SlotOf), Has.All.GreaterThanOrEqualTo(StartSlot), "the upper row is silent before bar 1");
            Assert.AreEqual(StartSlot + setup.Score.Bars[0].Melody[0].Slot, rig.SlotOf(rig.Of(Part.Harp).First()), "and its first note is the first score note of bar 1");
        }

        [Test]
        public void Melody_IsSilent_WhenTheSongHasBeenPreparedButNotStarted()
        {
            var setup = new SongSetup();
            var rig = setup.Rig(start: false);

            rig.Run(RunSec, ConductRig.Both(ConductRig.StillIn(Part.Harp, 0), ConductRig.StillIn(Part.LowString, 1)), Fps30);

            Assert.IsEmpty(rig.Of(Part.Harp));
            Assert.IsNotEmpty(rig.Of(Part.LowString), "the lower row does play");
            Assert.AreEqual(long.MaxValue, rig.Engine.SongEndSlot);
        }

        [Test]
        public void Melody_SilentWithoutAnUpperHand([Values(Part.LowString, Part.Marimba, Part.HandDrum)] Part lowerPart)
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.CircleIn(lowerPart, 3f), Fps30);

            Assert.IsNotEmpty(rig.Of(lowerPart));
            Assert.IsEmpty(rig.Notes.Where(n => Parts.IsSky(n.Part)), "no hand in the upper row, no melody: the song leaves no guide tune behind");
        }

        [Test]
        public void Melody_TwoUpperHands_PlayTheSameSlots()
        {
            var setup = new SongSetup(harpOctave: -12);
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.Both(ConductRig.StillIn(Part.Harp, 0), ConductRig.StillIn(Part.Bell, 1)), Fps30);

            var harp = Played(rig, Part.Harp);
            var bell = Played(rig, Part.Bell);
            Assert.AreEqual(harp.Select(n => n.slot), bell.Select(n => n.slot), "the same slots");
            CollectionAssert.AreEqual(setup.ScoreNotes(Part.Bell), bell, "the bell plays the score as it is");
            CollectionAssert.AreEqual(setup.ScoreNotes(Part.Harp), harp, "the harp plays it an octave lower, as the song says");
        }

        [Test]
        public void Melody_AtSixPicturesASecond_StaysOnTheScore()
        {
            var setup = new SongSetup();
            var fast = setup.Rig();
            var slow = setup.Rig();

            fast.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps30);
            slow.Run(RunSec, ConductRig.StillIn(Part.Harp), Fps6);

            CollectionAssert.AreEqual(Played(fast, Part.Harp), Played(slow, Part.Harp));
            CollectionAssert.AreEqual(setup.ScoreNotes(Part.Harp), Played(slow, Part.Harp));
        }

        [Test]
        public void Melody_HandEntersMidBar_StartsAtTheNextScoreNote([Values(Fps30, Fps6)] double pictureEverySec)
        {
            var setup = new SongSetup();
            var rig = setup.Rig();
            var enterSlot = StartSlot + 2 * ConductRig.SlotsPerBar + 5;   // bar 3 of the song, between its notes on slots 4 and 7
            var enterSec = rig.Grid.SlotTime(enterSlot) - (ConductRig.GridOrigin - 1.0);

            rig.Run(RunSec, HandOut(ConductRig.StillIn(Part.Harp), enterSec), pictureEverySec);

            var played = Played(rig, Part.Harp);
            Assert.IsNotEmpty(played);
            Assert.Greater(played[0].slot, enterSlot, "nothing for the notes that were over before the hand was there");
            var score = setup.ScoreNotes(Part.Harp);
            var firstIndex = score.FindIndex(n => n.slot == played[0].slot);
            Assert.GreaterOrEqual(firstIndex, 0, "the first note is a note of the score");
            CollectionAssert.AreEqual(score.Skip(firstIndex).ToList(), played, "and from there on every note of the score, none missed and none made up");
            Assert.LessOrEqual(played[0].slot, StartSlot + 2 * ConductRig.SlotsPerBar + 14, "it does not wait for the next bar");
        }

        [Test]
        public void Melody_AHandThatLeaves_FadesOut_AndTheNotesAreSoftAsItDoes()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();
            var leaveSlot = StartSlot + 2 * ConductRig.SlotsPerBar + 1;
            var leaveSec = rig.Grid.SlotTime(leaveSlot) - (ConductRig.GridOrigin - 1.0);

            rig.Run(RunSec, ConductRig.Gone(ConductRig.StillIn(Part.Harp), leaveSec), Fps30);

            var goneAfterSec = leaveSec + Math.Max(rig.Settings.HoldMs, rig.Hand.Settings.MaxGapMs) / 1000.0 + rig.Settings.ReleaseSec + 0.3;
            var lateSlot = rig.Grid.FirstSlotAtOrAfter(ConductRig.GridOrigin - 1.0 + goneAfterSec);
            Assert.That(rig.Of(Part.Harp).Select(rig.SlotOf), Has.None.GreaterThanOrEqualTo(lateSlot), "the voice has faded away and the score goes on without a sound");
            var fading = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) > leaveSlot && rig.SlotOf(n) % ConductRig.SlotsPerBar != 0).Select(n => n.Velocity).ToList();
            Assert.IsNotEmpty(fading, "some notes of the score still fall in the fade");
            for (var i = 1; i < fading.Count; i++) Assert.LessOrEqual(fading[i], fading[i - 1] + 1e-4f, "each note of the fade is no louder than the one before");
            Assert.Less(fading.Last(), rig.Settings.MinVelocity * 0.5f, "and the last of them is much softer than a hand held still plays");
        }

        // ---- the accompaniment -------------------------------------------------------------------------------------------------------------

        [Test]
        public void Accompaniment_Marimba_PlaysThePhraseOfTheBarsChord()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.CircleIn(Part.Marimba, 5f), Fps30);

            var phrase = rig.Settings.SongPhrase(Part.Marimba);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 2 }, phrase.ToArray());
            var notes = rig.Of(Part.Marimba).Where(n => rig.SlotOf(n) >= ConductRig.SlotsPerBar).ToList();
            Assert.GreaterOrEqual(notes.Count, 80);
            foreach (var bar in notes.GroupBy(n => FloorDiv(rig.SlotOf(n), ConductRig.SlotsPerBar)))
            {
                var songBar = setup.BarAt(bar.Key * ConductRig.SlotsPerBar);
                var allowed = new[] { songBar.Bass }.Concat(songBar.Chord).Concat(songBar.Chord.Select(c => c + 12)).ToArray();
                var midis = bar.Select(n => n.Midi).ToList();
                Assert.That(midis, Has.All.Matches<int>(m => allowed.Contains(m)), "bar line " + bar.Key + ": inside the chord of its bar");
                for (var i = 0; i < midis.Count; i++) Assert.AreEqual(songBar.ChordTone(phrase[i % phrase.Count]), midis[i], $"bar line {bar.Key}, note {i}: the phrase starts again on every bar line");
                foreach (var note in bar) Assert.AreEqual(setup.Pitch.StepFor(Part.Marimba, note.Midi), note.PitchStep);
            }
        }

        [Test]
        public void Accompaniment_LowString_PlaysBassBassChordBass_OnTheChordsOfTheBars()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.LowString), Fps30);

            // from the bar at slot 16, a bar before the song, to the bar at slot 96, the first after it
            for (long barLine = ConductRig.SlotsPerBar; barLine <= EndSlot; barLine += ConductRig.SlotsPerBar)
            {
                var songBar = setup.BarAt(barLine);
                var midis = rig.Of(Part.LowString).Where(n => rig.SlotOf(n) >= barLine && rig.SlotOf(n) < barLine + ConductRig.SlotsPerBar).Select(n => n.Midi).ToList();
                CollectionAssert.AreEqual(new[] { songBar.Bass, songBar.Bass, songBar.Chord[1], songBar.Bass }, midis, $"the bar from slot {barLine}");
            }
        }

        [Test]
        public void Accompaniment_PlaysBarOnesChordBeforeTheSong_AndTheLastBarsChordAfterIt()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.StillIn(Part.Marimba), Fps30);

            var before = rig.Of(Part.Marimba).Where(n => rig.SlotOf(n) >= ConductRig.SlotsPerBar && rig.SlotOf(n) < StartSlot).Select(n => n.Midi).ToList();
            var after = rig.Of(Part.Marimba).Where(n => rig.SlotOf(n) >= EndSlot).Select(n => n.Midi).ToList();
            Assert.IsNotEmpty(before);
            Assert.IsNotEmpty(after);
            var first = setup.Score.Bars[0];
            var last = setup.Score.Bars[Bars - 1];
            Assert.That(before, Has.All.Matches<int>(m => first.Chord.Contains(m)), "the chord of bar 1");
            Assert.That(after, Has.All.Matches<int>(m => last.Chord.Contains(m)), "the chord of the last bar");
        }

        [Test]
        public void Accompaniment_KeepsTheDensityOfTheHand()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.CircleIn(Part.Marimba, 2f), Fps30);

            Assert.That(rig.Gaps(Part.Marimba, SettleSec), Has.All.EqualTo(2L), "half a beat apart, as in free play");
        }

        [Test]
        public void HandDrum_KeepsItsPhrase_InASong()
        {
            var setup = new SongSetup();
            var song = setup.Rig();
            var free = setup.FreePlayRig();

            song.Run(RunSec, ConductRig.CircleIn(Part.HandDrum, 3f), Fps30);
            free.Run(RunSec, ConductRig.CircleIn(Part.HandDrum, 3f), Fps30);

            Assert.IsNotEmpty(song.Notes);
            Assert.That(song.Notes.Select(n => n.HasMidi), Has.All.False, "the hand drum has no song phrase and sounds by its step");
            CollectionAssert.AreEqual(Snapshot(free), Snapshot(song), "the same notes, step by step, as in free play");
        }

        // ---- the energy --------------------------------------------------------------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(2f)]
        [TestCase(5f)]
        public void Melody_EnergyPerBar_MatchesConducting(float palmsPerSecond)
        {
            var setup = new SongSetup();
            var song = setup.Rig();
            var free = setup.FreePlayRig();
            Func<double, Pose[]> hand = palmsPerSecond > 0f ? ConductRig.CircleIn(Part.Harp, palmsPerSecond) : ConductRig.StillIn(Part.Harp);

            song.Run(RunSec, hand, Fps30);
            free.Run(RunSec, hand, Fps30);

            float Sum(ConductRig rig) => rig.Of(Part.Harp).Where(n => rig.SlotOf(n) >= StartSlot && rig.SlotOf(n) < EndSlot).Sum(n => n.EnergyWeight);
            var conducting = Sum(free);
            Assert.Greater(conducting, 0f);
            Assert.AreEqual(conducting, Sum(song), conducting * 0.15f, "a few notes to a bar carry the energy the many notes of conducting would have");
        }

        // ---- the end, and back to free play ------------------------------------------------------------------------------------------------

        [Test]
        public void NothingFromTheEndSlotOn()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(RunSec, ConductRig.Both(ConductRig.StillIn(Part.Harp, 0), ConductRig.StillIn(Part.Bell, 1)), Fps30);

            Assert.AreEqual(EndSlot, rig.Engine.SongEndSlot);
            var upper = rig.Notes.Where(n => Parts.IsSky(n.Part)).ToList();
            Assert.IsNotEmpty(upper);
            Assert.That(upper.Select(rig.SlotOf), Has.All.LessThan(EndSlot), "no melody note from the end slot on");
            var lastBar = setup.Score.Bars[Bars - 1];
            Assert.AreEqual(EndSlot - ConductRig.SlotsPerBar + lastBar.Melody.Last().Slot, upper.Max(n => rig.SlotOf(n)), "the last note is the one note of the last bar");
        }

        [Test]
        public void ClearSong_GivesFreePlayBack_WhenItIsClearedBeforeTheHandsPlay()
        {
            var setup = new SongSetup();
            var cleared = setup.Rig();
            cleared.Engine.ClearSong();
            var fresh = setup.FreePlayRig();
            var hands = ConductRig.Both(ConductRig.CircleIn(Part.Harp, 5f, id: 0), ConductRig.CircleIn(Part.Marimba, 3f, id: 1));

            cleared.Run(RunSec, hands, Fps30);
            fresh.Run(RunSec, hands, Fps30);

            Assert.IsNotEmpty(fresh.Notes);
            CollectionAssert.AreEqual(Snapshot(fresh), Snapshot(cleared));
            Assert.IsFalse(cleared.Engine.HasSong);
        }

        [Test]
        public void ClearSong_GivesFreePlayBack_WhenItIsClearedInTheMiddleOfTheSong()
        {
            var setup = new SongSetup();
            var cleared = setup.Rig();
            var fresh = setup.FreePlayRig();
            var hands = ConductRig.Both(ConductRig.CircleIn(Part.Harp, 5f, id: 0), ConductRig.CircleIn(Part.LowString, 3f, id: 1));

            cleared.Run(7.0, hands, Fps30);    // into the song (it starts at 15.0 s; this is 16.0 s)
            cleared.Engine.ClearSong();
            cleared.Run(RunSec - 7.0, hands, Fps30);
            fresh.Run(RunSec, hands, Fps30);

            const double SettledSec = 3.0;
            var clearedAt = ConductRig.GridOrigin - 1.0 + 7.0;
            List<object> After(ConductRig rig) => rig.Notes.Where((n, i) => rig.MadeAt[i] >= clearedAt + SettledSec).Select(n => (object)(n.Part, n.PitchStep, n.Midi, n.Velocity, n.RequestedAt, n.EnergyWeight)).ToList();
            Assert.IsNotEmpty(After(fresh));
            CollectionAssert.AreEqual(After(fresh), After(cleared), "once the voices have caught up, the notes are free play's");
        }

        [Test]
        public void Reset_KeepsTheSong_ForTheNextHands()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Run(7.0, ConductRig.StillIn(Part.Harp), Fps30);
            rig.Engine.Reset();
            rig.Run(RunSec - 7.0, ConductRig.StillIn(Part.Harp), Fps30);

            Assert.IsTrue(rig.Engine.HasSong);
            Assert.IsTrue(rig.Engine.IsSongStarted);
            Assert.AreEqual(EndSlot, rig.Engine.SongEndSlot);
            var scoreMidis = setup.ScoreNotes(Part.Harp).Select(n => n.midi).ToList();
            Assert.That(rig.Of(Part.Harp).Select(n => n.Midi), Has.All.Matches<int>(m => scoreMidis.Contains(m)), "still the notes of the score");
            Assert.IsNotEmpty(rig.Of(Part.Harp).Where(n => rig.SlotOf(n) >= StartSlot + 2 * ConductRig.SlotsPerBar), "and they go on after the reset, on the clock of the song");
        }

        // ---- the state of the engine -------------------------------------------------------------------------------------------------------

        [Test]
        public void TheSong_Is_PreparedStartedAndCleared()
        {
            var setup = new SongSetup();
            var engine = new ConductRig().Engine;
            Assert.IsFalse(engine.HasSong);
            Assert.IsFalse(engine.IsSongStarted);
            Assert.AreEqual(long.MaxValue, engine.SongEndSlot);

            engine.PrepareSong(setup.Score, setup.Pitch);
            Assert.IsTrue(engine.HasSong);
            Assert.IsFalse(engine.IsSongStarted, "prepared is not started");
            Assert.AreEqual(long.MaxValue, engine.SongEndSlot);

            engine.StartSong(StartSlot);
            Assert.IsTrue(engine.IsSongStarted);
            Assert.AreEqual(StartSlot + Bars * ConductRig.SlotsPerBar, engine.SongEndSlot);

            engine.PrepareSong(setup.Score, setup.Pitch);
            Assert.IsFalse(engine.IsSongStarted, "a song prepared again begins again");

            engine.StartSong(StartSlot);
            engine.ClearSong();
            Assert.IsFalse(engine.HasSong);
            Assert.IsFalse(engine.IsSongStarted);
        }

        [Test]
        public void TheSong_IsDropped_WhenTheGridChanges()
        {
            var setup = new SongSetup();
            var rig = setup.Rig();

            rig.Engine.OnGridReady(new BeatGrid(ConductRig.GridOrigin + 1.0, ConductRig.Bpm, ConductRig.SlotsPerBeat));

            Assert.IsTrue(rig.Engine.HasSong, "the song is still the one to play");
            Assert.IsFalse(rig.Engine.IsSongStarted, "but its slots belonged to the old grid");
        }

        [Test]
        public void StartSong_NeedsASong_AndABarLine()
        {
            var setup = new SongSetup();
            var engine = new ConductRig().Engine;

            Assert.Throws<InvalidOperationException>(() => engine.StartSong(StartSlot));
            engine.PrepareSong(setup.Score, setup.Pitch);
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.StartSong(StartSlot + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.StartSong(-ConductRig.SlotsPerBar));
            Assert.Throws<ArgumentNullException>(() => engine.PrepareSong(null, setup.Pitch));
            Assert.Throws<ArgumentNullException>(() => engine.PrepareSong(setup.Score, null));
        }

        [Test]
        public void LastScannedSlot_IsTheNewestSlotLookedAt()
        {
            var rig = new ConductRig();
            Assert.AreEqual(long.MinValue, rig.Engine.LastScannedSlot);

            rig.Run(2.0, ConductRig.StillIn(Part.Harp), Fps30);   // up to 11.0 s
            Assert.AreEqual(rig.Grid.SlotForTrigger(rig.AudioNow, ConductRig.LeadSec), rig.Engine.LastScannedSlot);
        }

        // ---- the player --------------------------------------------------------------------------------------------------------------------

        sealed class PlayerSetup
        {
            public PlayerSetup()
            {
                Content = TestContent.LoadShipped();
                Score = SongBook.Build(Content).Songs.First();
                Player = new ConductPlayer(() => Now);
                Player.Configure(Content, null);
                Player.OnGridReady(Grid);
                var holder = new HandSettingsHolder();
                holder.Set(Content.hand);
                _motion = new HandMotionTracker(holder);
            }

            readonly HandMotionTracker _motion;
            public double Now = ConductRig.GridOrigin - 1.0;
            public ContentDto Content { get; }
            public SongScore Score { get; }
            public ConductPlayer Player { get; }
            public BeatGrid Grid { get; } = new BeatGrid(ConductRig.GridOrigin, ConductRig.Bpm, ConductRig.SlotsPerBeat);

            /// <summary>One frame of the app at this time on the audio clock, with no hand in the picture.</summary>
            public void Frame(double atSec)
            {
                Now = atSec;
                Player.Update(_motion, 1f, new List<NoteIntent>());
            }
        }

        [Test]
        public void Player_ConfigureBuildsThePitch_ButPreparesNoSong()
        {
            var setup = new PlayerSetup();

            Assert.IsFalse(setup.Player.HasSong, "an EditMode rig that configures the player is in free play");
            Assert.AreEqual(long.MaxValue, setup.Player.SongEndSlot);

            setup.Player.PrepareSong(setup.Score);
            Assert.IsTrue(setup.Player.HasSong);
            setup.Player.ClearSong();
            Assert.IsFalse(setup.Player.HasSong);
        }

        [Test]
        public void Player_PrepareSong_NeedsAConfiguredPlayer()
        {
            var setup = new PlayerSetup();
            var unconfigured = new ConductPlayer(() => 0.0);

            Assert.Throws<InvalidOperationException>(() => unconfigured.PrepareSong(setup.Score));
        }

        [Test]
        public void Player_StartSongAfter_IsTheFirstBarLineAfterWhatWasScanned()
        {
            var setup = new PlayerSetup();
            setup.Player.PrepareSong(setup.Score);
            setup.Frame(12.3);   // the slot ahead of this time is slot 16, a bar line, and the engine has scanned it

            var start = setup.Player.StartSongAfter(12.3);

            Assert.AreEqual(32L, start, "slot 16 has been scanned, so the song cannot start on it");
            Assert.AreEqual(32L + setup.Score.BarCount * ConductRig.SlotsPerBar, setup.Player.SongEndSlot);
            Assert.AreEqual(setup.Grid.SlotTime(32), setup.Player.SlotTime(32), 1e-9);
        }

        [TestCase(12.0, 16L)]    // the slot ahead of 12.0 s is slot 14: the bar line at 16 is next
        [TestCase(12.3, 32L)]    // slot 16
        [TestCase(14.7, 32L)]    // slot 31
        [TestCase(15.0, 48L)]    // slot 33
        public void Player_StartSongAfter_IsAfterTheSlotOfNow_EvenBeforeTheEngineHasScannedIt(double nowSec, long expectedStart)
        {
            var setup = new PlayerSetup();
            setup.Player.PrepareSong(setup.Score);

            var start = setup.Player.StartSongAfter(nowSec);

            Assert.AreEqual(expectedStart, start);
            Assert.Greater(start, setup.Grid.SlotForTrigger(nowSec, setup.Content.audio.minLeadSec));
        }

        [Test]
        public void Player_StartSongAfter_NeverPicksAScannedSlot_WhenTheEngineIsAheadOfTheClockItIsGiven()
        {
            var setup = new PlayerSetup();
            setup.Player.PrepareSong(setup.Score);
            setup.Frame(20.0);   // scanned up to slot 64 + 1

            var start = setup.Player.StartSongAfter(10.0);   // a clock far behind

            Assert.Greater(start, setup.Grid.SlotForTrigger(20.0, setup.Content.audio.minLeadSec));
            Assert.AreEqual(0L, start % ConductRig.SlotsPerBar);
        }

        [Test]
        public void Player_StartSongAfter_NeedsAPreparedSong()
        {
            var setup = new PlayerSetup();

            Assert.Throws<InvalidOperationException>(() => setup.Player.StartSongAfter(12.0));
        }

        /// <summary>Everything about a note that the song or free play decides, to compare two runs.</summary>
        static List<object> Snapshot(ConductRig rig) =>
            rig.Notes.Select(n => (object)(n.Part, n.PitchStep, n.Midi, n.Velocity, n.RequestedAt, n.EnergyWeight, n.X01)).ToList();
    }
}
