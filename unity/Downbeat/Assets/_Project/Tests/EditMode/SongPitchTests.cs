using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The scale step an instrument gets for a MIDI note of a song (what the flowers, the sky effects and the record go by).</summary>
    public class SongPitchTests
    {
        static SongPitch Shipped() => SongPitch.From(TestContent.LoadShipped());

        [Test]
        public void StepFor_RecorderBelowItsRoot_ClampsToZero()
        {
            Assert.AreEqual(0, Shipped().StepFor(Part.Recorder, 74), "the recorder's step 0 is A5 = 81");
        }

        [Test]
        public void StepFor_HarpOnScale()
        {
            var pitch = Shipped();

            Assert.AreEqual(0, pitch.StepFor(Part.Harp, 69));
            Assert.AreEqual(1, pitch.StepFor(Part.Harp, 72));
            Assert.AreEqual(4, pitch.StepFor(Part.Harp, 79));
            Assert.AreEqual(9, pitch.StepFor(Part.Harp, 91));
        }

        [Test]
        public void StepFor_NotOnScale_TakesNearest()
        {
            Assert.AreEqual(3, Shipped().StepFor(Part.Harp, 77), "the harp has 76 (step 3) and 79 (step 4): 77 is nearer 76");
            Assert.AreEqual(4, Shipped().StepFor(Part.Harp, 78), "78 is nearer 79");
        }

        [Test]
        public void StepFor_HalfwayBetweenTwoSteps_TakesTheLowerOne()
        {
            var pitch = Shipped();

            Assert.AreEqual(1, pitch.StepFor(Part.Harp, 73), "between 72 (step 1) and 74 (step 2)");
            Assert.AreEqual(2, pitch.StepFor(Part.Harp, 75), "between 74 (step 2) and 76 (step 3)");
        }

        [Test]
        public void StepFor_AboveTheTop_ClampsToTheLastStep()
        {
            var pitch = Shipped();

            Assert.AreEqual(9, pitch.StepFor(Part.Harp, 120));
            Assert.AreEqual(0, pitch.StepFor(Part.Harp, 10));
        }

        [Test]
        public void StepFor_UsesTheRootOfEachInstrument()
        {
            var pitch = Shipped();

            // the same note, 88 (E6): the recorder starts at 81, the harp at 69, and the low string at 57 tops out at 79
            Assert.AreEqual(3, pitch.StepFor(Part.Recorder, 88));
            Assert.AreEqual(8, pitch.StepFor(Part.Harp, 88));
            Assert.AreEqual(9, pitch.StepFor(Part.LowString, 88));
        }

        [Test]
        public void From_TakesTheScaleAndTheRootsFromTheContent()
        {
            var content = TestContent.LoadShipped();
            var pitch = SongPitch.From(content);
            var scale = new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps);

            foreach (var instrument in content.instruments)
            {
                Assert.IsTrue(Parts.TryParse(instrument.part, out var part));
                for (var step = 0; step < scale.PitchSteps; step++)
                {
                    Assert.AreEqual(step, pitch.StepFor(part, scale.MidiForStep(instrument.rootMidi, step)), $"{part} step {step}");
                }
            }
        }

        [Test]
        public void Constructor_NeedsTheScaleAndOneRootForEveryPart()
        {
            var scale = TestContent.AMinorPentatonic();

            Assert.Throws<ArgumentNullException>(() => new SongPitch(null, new int[Parts.Count]));
            Assert.Throws<ArgumentNullException>(() => new SongPitch(scale, null));
            Assert.Throws<ArgumentException>(() => new SongPitch(scale, new int[Parts.Count - 1]));
        }

        [Test]
        public void From_RefusesContentWithoutTheSongBlockOrAnInstrument()
        {
            var noSong = TestContent.LoadShipped();
            noSong.song = null;
            var noMarimba = TestContent.LoadShipped();
            noMarimba.instruments = Array.FindAll(noMarimba.instruments, i => i.part != "marimba");

            Assert.Throws<ArgumentException>(() => SongPitch.From(noSong));
            Assert.Throws<ArgumentException>(() => SongPitch.From(noMarimba));
        }

        [Test]
        public void TheRootsAreCopied()
        {
            var roots = new[] { 69, 81, 69, 57, 69, 45 };
            var pitch = new SongPitch(TestContent.AMinorPentatonic(), roots);

            roots[0] = 100;

            Assert.AreEqual(1, pitch.StepFor(Part.Harp, 72));
        }
    }

    /// <summary>The scale steps of the sky that a chord of a song gives (F: la and do, G: sol and re).</summary>
    public class SongChordsTests
    {
        static readonly ScaleSpec AMinor = TestContent.AMinorPentatonic();
        const int ScaleRoot = 69;

        [Test]
        public void SkyStepsFor_F_IsLaDo()
        {
            Assert.AreEqual(new[] { 0, 1 }, SongChords.SkyStepsFor(new[] { 65, 69, 72 }, AMinor, ScaleRoot), "fa is not in the scale");
        }

        [Test]
        public void SkyStepsFor_G_IsReSol()
        {
            Assert.AreEqual(new[] { 2, 4 }, SongChords.SkyStepsFor(new[] { 67, 71, 74 }, AMinor, ScaleRoot), "si is not in the scale");
        }

        [Test]
        public void SkyStepsFor_Am_IsLaDoMi()
        {
            Assert.AreEqual(new[] { 0, 1, 3 }, SongChords.SkyStepsFor(new[] { 69, 72, 76 }, AMinor, ScaleRoot));
        }

        [Test]
        public void SkyStepsFor_C_IsDoMiSol()
        {
            Assert.AreEqual(new[] { 1, 3, 4 }, SongChords.SkyStepsFor(new[] { 72, 76, 79 }, AMinor, ScaleRoot));
        }

        [Test]
        public void SkyStepsFor_IsAscending_AndTheOctaveOfTheChordDoesNotMatter()
        {
            Assert.AreEqual(new[] { 0, 1, 3 }, SongChords.SkyStepsFor(new[] { 88, 81, 60 }, AMinor, ScaleRoot), "E, A, C in any order and octave");
        }

        [Test]
        public void SkyStepsFor_ANoteTwiceCountsOnce()
        {
            Assert.AreEqual(new[] { 0, 1 }, SongChords.SkyStepsFor(new[] { 69, 81, 72 }, AMinor, ScaleRoot));
        }

        [Test]
        public void SkyStepsFor_AChordWithNoNoteOfTheScale_IsEmpty()
        {
            // F sharp, B flat, C sharp: none of A C D E G
            var steps = SongChords.SkyStepsFor(new[] { 66, 70, 73 }, AMinor, ScaleRoot);

            Assert.IsNotNull(steps);
            Assert.IsEmpty(steps);
        }

        [Test]
        public void SkyStepsFor_NoChord_IsEmpty()
        {
            Assert.IsEmpty(SongChords.SkyStepsFor(new int[0], AMinor, ScaleRoot));
        }

        [Test]
        public void SkyStepsFor_FollowsTheRootOfTheScale()
        {
            // the same pitches with the scale starting a whole tone higher (B D E F# A): the chord A C E has A (the last degree) and E (the third)
            var scale = new ScaleSpec(new[] { 0, 3, 5, 7, 10 }, 10);

            Assert.AreEqual(new[] { 2, 4 }, SongChords.SkyStepsFor(new[] { 69, 72, 76 }, scale, 71));
        }

        [Test]
        public void SkyStepsFor_NeedsAChordAndAScale()
        {
            Assert.Throws<ArgumentNullException>(() => SongChords.SkyStepsFor(null, AMinor, ScaleRoot));
            Assert.Throws<ArgumentNullException>(() => SongChords.SkyStepsFor(new[] { 69 }, null, ScaleRoot));
        }
    }
}
