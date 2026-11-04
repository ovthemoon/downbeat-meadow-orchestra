using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>Where across and up and down the picture a note was played travels with the note (the flowers of the right place bloom, and the effects of the sky appear at the hand, not just of the right instrument).</summary>
    public class NoteXPlumbingTests
    {
        [Test]
        public void NoteIntent_HasNoXUnlessGiven_AndClampsIt()
        {
            Assert.IsTrue(float.IsNaN(new NoteIntent(Part.Harp, 0, 0.5f).X01));
            Assert.AreEqual(0.25f, new NoteIntent(Part.Harp, 0, 0.5f, x01: 0.25f).X01, 1e-6f);
            Assert.AreEqual(1f, new NoteIntent(Part.Harp, 0, 0.5f, x01: 4f).X01, 1e-6f);
            Assert.AreEqual(0f, new NoteIntent(Part.Harp, 0, 0.5f, x01: -4f).X01, 1e-6f);
        }

        [Test]
        public void Scheduler_CarriesTheXOfTheIntentToTheScheduledNote()
        {
            var scheduler = new QuantizedNoteScheduler(new BeatGrid(2.0, 96, 4), new SchedulerSettings(0.1, 2, new[] { 1, 1, 1, 1, 1, 1 }));

            Assert.IsTrue(scheduler.TrySchedule(3.0, new NoteIntent(Part.HandDrum, 4, 0.7f, x01: 0.81f, y01: 0.35f), out var note));

            Assert.AreEqual(0.81f, note.X01, 1e-6f);
            Assert.AreEqual(0.35f, note.Y01, 1e-6f);
        }

        [Test]
        public void ScheduledNote_WithAudioTime_KeepsTheX()
        {
            var note = new ScheduledNote(3, 4.0, 3.5, Part.Recorder, 2, 0.5f, false, 0.4f, 0.6f);

            Assert.AreEqual(0.4f, note.WithAudioTime(4.2).X01, 1e-6f);
            Assert.AreEqual(0.6f, note.WithAudioTime(4.2).Y01, 1e-6f);
        }

        [Test]
        public void NoteIntent_HasNoYUnlessGiven_AndClampsIt()
        {
            Assert.IsTrue(float.IsNaN(new NoteIntent(Part.Harp, 0, 0.5f).Y01));
            Assert.AreEqual(0.25f, new NoteIntent(Part.Harp, 0, 0.5f, y01: 0.25f).Y01, 1e-6f);
            Assert.AreEqual(1f, new NoteIntent(Part.Harp, 0, 0.5f, y01: 4f).Y01, 1e-6f);
            Assert.AreEqual(0f, new NoteIntent(Part.Harp, 0, 0.5f, y01: -4f).Y01, 1e-6f);
        }

        [TestCase(0.10f)]
        [TestCase(0.50f)]
        [TestCase(0.90f)]
        public void HandStrike_CarriesWhereTheHandWasWhenItStruck(float x)
        {
            var strikes = Run(NewInterpreter(), StrikeFrames(x, 0.4f, 7f));

            Assert.AreEqual(1, strikes.Count);
            Assert.AreEqual(x, strikes[0].X01, 1e-3f);
        }
    }
}
