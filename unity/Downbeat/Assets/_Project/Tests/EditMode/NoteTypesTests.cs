using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NoteTypesTests
    {
        [Test]
        public void Intent_WithoutMidi_HasNone()
        {
            var intent = new NoteIntent(Part.Harp, 3, 0.5f);

            Assert.AreEqual(-1, intent.Midi);
            Assert.IsFalse(intent.HasMidi);
        }

        [Test]
        public void Intent_WithMidi_KeepsItAndReportsIt()
        {
            var intent = new NoteIntent(Part.Harp, 3, 0.5f, midi: 88);

            Assert.AreEqual(88, intent.Midi);
            Assert.IsTrue(intent.HasMidi);
        }

        [Test]
        public void Intent_WithMidiZero_HasIt()
        {
            Assert.IsTrue(new NoteIntent(Part.Harp, 3, 0.5f, midi: 0).HasMidi);
        }

        [Test]
        public void AnyNegativeMidi_MeansNone()
        {
            Assert.AreEqual(-1, new NoteIntent(Part.Harp, 3, 0.5f, midi: -7).Midi);
            Assert.AreEqual(-1, new ScheduledNote(4, 2.5, 2.3, Part.Bell, 2, 0.6f, false, midi: -7).Midi);
        }

        [Test]
        public void ScheduledNote_WithoutMidi_HasNone()
        {
            var note = new ScheduledNote(4, 2.5, 2.3, Part.Bell, 2, 0.6f, false);

            Assert.AreEqual(-1, note.Midi);
        }

        [Test]
        public void WithAudioTime_KeepsTheMidi()
        {
            var note = new ScheduledNote(4, 2.5, 2.3, Part.Bell, 2, 0.6f, true, 0.25f, 0.75f, midi: 88);

            var moved = note.WithAudioTime(2.75);

            Assert.AreEqual(88, moved.Midi);
            Assert.AreEqual(2.75, moved.AudioTime);
            Assert.AreEqual(2, moved.PitchStep);
            Assert.AreEqual(0.25f, moved.X01);
            Assert.AreEqual(0.75f, moved.Y01);
            Assert.IsTrue(moved.WasPushed);
        }
    }
}
