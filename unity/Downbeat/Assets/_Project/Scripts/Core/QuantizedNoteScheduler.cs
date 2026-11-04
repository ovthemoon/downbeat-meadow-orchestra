using System;

namespace Downbeat.Core
{
    public sealed class SchedulerSettings
    {
        public double MinLeadSec { get; }
        public int MaxPushSlots { get; }
        readonly int[] _minGapSlots;

        public SchedulerSettings(double minLeadSec, int maxPushSlots, int[] minGapSlotsByPart)
        {
            if (minLeadSec < 0) throw new ArgumentOutOfRangeException(nameof(minLeadSec));
            if (maxPushSlots < 0) throw new ArgumentOutOfRangeException(nameof(maxPushSlots));
            if (minGapSlotsByPart == null || minGapSlotsByPart.Length != Parts.Count) throw new ArgumentException("one gap per part", nameof(minGapSlotsByPart));
            foreach (var gap in minGapSlotsByPart)
            {
                if (gap < 1) throw new ArgumentOutOfRangeException(nameof(minGapSlotsByPart), "a part needs at least 1 slot between notes");
            }
            MinLeadSec = minLeadSec;
            MaxPushSlots = maxPushSlots;
            _minGapSlots = (int[])minGapSlotsByPart.Clone();
        }

        public int MinGapSlots(Part part) => _minGapSlots[(int)part];
    }

    /// <summary>
    /// Puts note intents on the beat grid: the earliest slot that is at least <see cref="SchedulerSettings.MinLeadSec"/> away,
    /// at most one note per part per slot, and a minimum gap per part. A note that collides is pushed to the next allowed slot
    /// (up to <see cref="SchedulerSettings.MaxPushSlots"/> later) or dropped. Times are slot times of the grid, never "now".
    /// </summary>
    public sealed class QuantizedNoteScheduler
    {
        readonly BeatGrid _grid;
        readonly SchedulerSettings _settings;
        readonly long[] _lastSlotByPart = new long[Parts.Count];
        readonly bool[] _hasLast = new bool[Parts.Count];

        public QuantizedNoteScheduler(BeatGrid grid, SchedulerSettings settings)
        {
            _grid = grid;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public BeatGrid Grid => _grid;
        public int DroppedCount { get; private set; }
        public int PushedCount { get; private set; }

        public bool TrySchedule(double now, in NoteIntent intent, out ScheduledNote note)
        {
            var part = (int)intent.Part;
            var wanted = _grid.SlotForTrigger(now, _settings.MinLeadSec);
            var slot = wanted;
            if (_hasLast[part])
            {
                var earliest = _lastSlotByPart[part] + _settings.MinGapSlots(intent.Part);
                if (slot < earliest) slot = earliest;
            }
            if (slot - wanted > _settings.MaxPushSlots)
            {
                DroppedCount++;
                note = default;
                return false;
            }

            _lastSlotByPart[part] = slot;
            _hasLast[part] = true;
            var pushed = slot != wanted;
            if (pushed) PushedCount++;
            note = new ScheduledNote(slot, _grid.SlotTime(slot), now, intent.Part, intent.PitchStep, intent.Velocity, pushed, intent.X01, intent.Y01, intent.Midi);
            return true;
        }
    }
}
