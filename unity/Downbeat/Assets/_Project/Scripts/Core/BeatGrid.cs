using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Immutable beat grid. Slot times are computed from the integer slot index
    /// (T(n) = origin + n * slotDuration), never by accumulating additions, so error does not build up.
    /// </summary>
    public readonly struct BeatGrid
    {
        const double SlotEpsilonSeconds = 1e-9;

        public double Origin { get; }
        public double Bpm { get; }
        public int SlotsPerBeat { get; }
        public double SlotDuration { get; }

        public BeatGrid(double origin, double bpm, int slotsPerBeat)
        {
            if (!(bpm > 0)) throw new ArgumentOutOfRangeException(nameof(bpm), "bpm must be positive");
            if (slotsPerBeat < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerBeat), "slotsPerBeat must be at least 1");
            Origin = origin;
            Bpm = bpm;
            SlotsPerBeat = slotsPerBeat;
            SlotDuration = ComputeSlotDuration(bpm, slotsPerBeat);
        }

        public static double ComputeSlotDuration(double bpm, int slotsPerBeat) => 60.0 / bpm / slotsPerBeat;

        /// <summary>Time of slot n in the grid's time base.</summary>
        public double SlotTime(long n) => Origin + n * SlotDuration;

        /// <summary>Earliest slot whose time is at or after <paramref name="time"/> (tolerates 1 ns of float error).</summary>
        public long FirstSlotAtOrAfter(double time) =>
            (long)Math.Ceiling((time - Origin) / SlotDuration - SlotEpsilonSeconds / SlotDuration);

        /// <summary>
        /// Slot for a trigger at <paramref name="now"/> that must keep at least <paramref name="minLead"/> seconds of lead time.
        /// </summary>
        public long SlotForTrigger(double now, double minLead) => FirstSlotAtOrAfter(now + minLead);

        /// <summary>Time of the first bar line (every <paramref name="slotsPerBar"/> slots from the origin) at or after <paramref name="time"/>.</summary>
        public double BarTimeAtOrAfter(double time, int slotsPerBar)
        {
            if (slotsPerBar < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerBar));
            var bar = (long)Math.Ceiling((double)FirstSlotAtOrAfter(time) / slotsPerBar);
            return SlotTime(bar * slotsPerBar);
        }

        /// <summary>Latest slot whose time is at or before <paramref name="time"/>.</summary>
        public long LastSlotAtOrBefore(double time) =>
            (long)Math.Floor((time - Origin) / SlotDuration + SlotEpsilonSeconds / SlotDuration);
    }
}
