using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class BeatGridTests
    {
        const double Tolerance = 1e-12;

        [Test]
        public void SlotDuration_at_96bpm_sixteenth_is_156_25_ms()
        {
            Assert.AreEqual(0.15625, BeatGrid.ComputeSlotDuration(96, 4), Tolerance);
        }

        [Test]
        public void SlotTime_is_computed_from_index_without_accumulated_error()
        {
            var grid = new BeatGrid(origin: 10.0, bpm: 96, slotsPerBeat: 4);
            // After one hour of slots (23040), an accumulating adder would drift; the index form must not.
            Assert.AreEqual(10.0 + 23040 * 0.15625, grid.SlotTime(23040), 1e-9);
            Assert.AreEqual(10.0, grid.SlotTime(0), Tolerance);
        }

        [Test]
        public void FirstSlotAtOrAfter_returns_same_slot_when_time_is_on_the_grid()
        {
            var grid = new BeatGrid(1.0, 96, 4);
            Assert.AreEqual(5, grid.FirstSlotAtOrAfter(grid.SlotTime(5)));
        }

        [Test]
        public void FirstSlotAtOrAfter_rounds_up_between_slots()
        {
            var grid = new BeatGrid(0.0, 96, 4);
            Assert.AreEqual(3, grid.FirstSlotAtOrAfter(0.40)); // 0.40 s is between slot 2 (0.3125) and 3 (0.46875)
        }

        [Test]
        public void FirstSlotAtOrAfter_before_origin_gives_negative_or_zero_slot()
        {
            var grid = new BeatGrid(5.0, 120, 4);
            Assert.AreEqual(0, grid.FirstSlotAtOrAfter(4.9));
            Assert.AreEqual(-8, grid.FirstSlotAtOrAfter(4.0));
        }

        [Test]
        public void SlotForTrigger_keeps_the_minimum_lead()
        {
            var grid = new BeatGrid(0.0, 96, 4);
            const double minLead = 0.100;
            for (double now = 0.0; now < 3.0; now += 0.0137)
            {
                var slot = grid.SlotForTrigger(now, minLead);
                Assert.GreaterOrEqual(grid.SlotTime(slot), now + minLead - 1e-9);
                Assert.Less(grid.SlotTime(slot - 1), now + minLead, "must be the earliest such slot");
            }
        }

        [Test]
        public void LastSlotAtOrBefore_and_FirstSlotAtOrAfter_bracket_an_off_grid_time()
        {
            var grid = new BeatGrid(0.0, 96, 4);
            var t = 1.0;
            Assert.AreEqual(grid.FirstSlotAtOrAfter(t) - 1, grid.LastSlotAtOrBefore(t));
        }

        [Test]
        public void Constructor_rejects_invalid_arguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeatGrid(0, 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeatGrid(0, -10, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeatGrid(0, 96, 0));
        }

        [Test]
        public void BarTimeAtOrAfter_returns_the_next_bar_line_and_keeps_a_time_that_is_already_on_one()
        {
            var grid = new BeatGrid(10.0, 96, 4); // 16 slots = 2.5 s per bar
            Assert.AreEqual(12.5, grid.BarTimeAtOrAfter(10.7, 16), 1e-9);
            Assert.AreEqual(12.5, grid.BarTimeAtOrAfter(12.5, 16), 1e-9);
            Assert.AreEqual(15.0, grid.BarTimeAtOrAfter(12.51, 16), 1e-9);
            Assert.AreEqual(10.0, grid.BarTimeAtOrAfter(9.0, 16), 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.BarTimeAtOrAfter(1, 0));
        }
    }
}
