using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NoteVisualQueueTests
    {
        static ScheduledNote Note(double time, Part part = Part.Harp) => new ScheduledNote(0, time, 0, part, 0, 0.5f, false);

        [Test]
        public void A_note_is_released_only_when_it_is_heard()
        {
            var queue = new NoteVisualQueue();
            queue.Enqueue(Note(10.0));
            var due = new List<ScheduledNote>();
            Assert.AreEqual(0, queue.DrainDue(9.8, 0.0, due));
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual(1, queue.DrainDue(10.0, 0.0, due));
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void The_lead_releases_a_note_slightly_early()
        {
            var queue = new NoteVisualQueue();
            queue.Enqueue(Note(10.0));
            var due = new List<ScheduledNote>();
            Assert.AreEqual(1, queue.DrainDue(9.99, 0.01, due));
        }

        [Test]
        public void Due_notes_come_out_in_time_order_and_later_ones_stay()
        {
            var queue = new NoteVisualQueue();
            queue.Enqueue(Note(10.3, Part.Bell));
            queue.Enqueue(Note(10.1, Part.Harp));
            queue.Enqueue(Note(11.0, Part.Recorder));
            var due = new List<ScheduledNote>();
            Assert.AreEqual(2, queue.DrainDue(10.4, 0.0, due));
            Assert.AreEqual(Part.Harp, due[0].Part);
            Assert.AreEqual(Part.Bell, due[1].Part);
            Assert.AreEqual(1, queue.Count);
        }

        [Test]
        public void Draining_appends_to_the_given_list_without_touching_what_is_already_there()
        {
            var queue = new NoteVisualQueue();
            queue.Enqueue(Note(1.0));
            var due = new List<ScheduledNote> { Note(0.5) };
            queue.DrainDue(2.0, 0.0, due);
            Assert.AreEqual(2, due.Count);
            Assert.AreEqual(0.5, due[0].AudioTime);
        }
    }
}
