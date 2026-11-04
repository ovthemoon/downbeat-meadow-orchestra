using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class QuantizedNoteSchedulerTests
    {
        const double Origin = 2.0;
        const double MinLead = 0.100;
        static readonly int[] OneSlotGaps = { 1, 1, 1, 1, 1, 1 };

        static QuantizedNoteScheduler Create(int maxPush = 2, int[] gaps = null) =>
            new QuantizedNoteScheduler(new BeatGrid(Origin, 96, 4), new SchedulerSettings(MinLead, maxPush, gaps ?? OneSlotGaps));

        static NoteIntent Intent(Part part = Part.Harp) => new NoteIntent(part, 0, 0.5f);

        [Test]
        public void Irregular_click_times_always_land_exactly_on_a_grid_slot()
        {
            var grid = new BeatGrid(Origin, 96, 4);
            var scheduler = Create(maxPush: 100);
            var random = new Random(12345);
            double now = Origin + 1.0;
            for (var i = 0; i < 400; i++)
            {
                now += 0.03 + random.NextDouble() * 0.9; // irregular spacing from 30 ms to 0.93 s
                // a new part each time so that gaps do not interfere
                Assert.IsTrue(scheduler.TrySchedule(now, Intent((Part)(i % Parts.Count)), out var note));
                var slotsFromOrigin = (note.AudioTime - Origin) / grid.SlotDuration;
                Assert.AreEqual(Math.Round(slotsFromOrigin), slotsFromOrigin, 1e-9, "must be an exact slot");
                Assert.GreaterOrEqual(note.AudioTime, now + MinLead - 1e-9, "must keep the minimum lead");
            }
        }

        [Test]
        public void Chooses_the_earliest_slot_with_enough_lead()
        {
            var scheduler = Create();
            var grid = scheduler.Grid;
            const double now = 3.30;
            scheduler.TrySchedule(now, Intent(), out var note);
            Assert.GreaterOrEqual(note.AudioTime, now + MinLead - 1e-9);
            Assert.Less(grid.SlotTime(note.Slot - 1), now + MinLead, "the previous slot would have been too early");
            Assert.AreEqual(now, note.RequestedAt);
            Assert.IsFalse(note.WasPushed);
        }

        [Test]
        public void Two_clicks_for_the_same_slot_push_the_second_one_to_the_next_slot()
        {
            var scheduler = Create();
            scheduler.TrySchedule(3.00, Intent(), out var first);
            scheduler.TrySchedule(3.01, Intent(), out var second);
            Assert.AreEqual(first.Slot + 1, second.Slot);
            Assert.IsTrue(second.WasPushed);
            Assert.AreEqual(1, scheduler.PushedCount);
        }

        [Test]
        public void A_click_pushed_further_than_the_limit_is_dropped()
        {
            var scheduler = Create(maxPush: 1);
            scheduler.TrySchedule(3.00, Intent(), out _);
            scheduler.TrySchedule(3.00, Intent(), out _); // pushed by 1, allowed
            Assert.IsFalse(scheduler.TrySchedule(3.00, Intent(), out _)); // would be pushed by 2
            Assert.AreEqual(1, scheduler.DroppedCount);
        }

        [Test]
        public void Minimum_gap_of_a_part_spaces_its_notes_but_other_parts_are_independent()
        {
            var scheduler = Create(maxPush: 10, gaps: new[] { 1, 4, 1, 1, 1, 1 });
            scheduler.TrySchedule(3.00, Intent(Part.Recorder), out var first);
            scheduler.TrySchedule(3.01, Intent(Part.Recorder), out var second);
            scheduler.TrySchedule(3.01, Intent(Part.Bell), out var other);
            Assert.AreEqual(first.Slot + 4, second.Slot, "the recorder needs 4 slots between notes");
            Assert.AreEqual(first.Slot, other.Slot, "a different part may use the same slot");
        }

        [Test]
        public void A_later_click_is_not_pushed_once_the_gap_has_passed()
        {
            var scheduler = Create(gaps: new[] { 2, 2, 2, 2, 2, 2 });
            scheduler.TrySchedule(3.00, Intent(), out var first);
            scheduler.TrySchedule(4.50, Intent(), out var later);
            Assert.IsFalse(later.WasPushed);
            Assert.Greater(later.Slot, first.Slot + 2);
        }

        [Test]
        public void Scheduler_CopiesTheMidi()
        {
            var scheduler = Create();

            scheduler.TrySchedule(3.00, new NoteIntent(Part.Harp, 5, 0.5f, midi: 88), out var note);

            Assert.AreEqual(88, note.Midi);
            Assert.AreEqual(5, note.PitchStep, "the visual step is not touched by the MIDI");
        }

        [Test]
        public void Scheduler_WithoutMidi_StaysWithout()
        {
            var scheduler = Create();

            scheduler.TrySchedule(3.00, Intent(), out var note);

            Assert.AreEqual(-1, note.Midi);
        }

        [Test]
        public void Settings_reject_invalid_values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SchedulerSettings(-1, 1, OneSlotGaps));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SchedulerSettings(0.1, -1, OneSlotGaps));
            Assert.Throws<ArgumentException>(() => new SchedulerSettings(0.1, 1, new[] { 1, 1, 1 }), "one gap for each of the six parts");
            Assert.Throws<ArgumentOutOfRangeException>(() => new SchedulerSettings(0.1, 1, new[] { 1, 0, 1, 1, 1, 1 }));
        }

        [Test]
        public void Intent_clamps_velocity_and_rejects_negative_steps()
        {
            Assert.AreEqual(1f, new NoteIntent(Part.HandDrum, 3, 5f).Velocity);
            Assert.AreEqual(0f, new NoteIntent(Part.HandDrum, 3, -2f).Velocity);
            Assert.Throws<ArgumentOutOfRangeException>(() => new NoteIntent(Part.HandDrum, -1, 0.5f));
        }
    }
}
