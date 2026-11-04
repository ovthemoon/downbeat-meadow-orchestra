using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Holds scheduled notes until the moment they are heard. <paramref name="heardNow"/> is the audio time of the sample
    /// currently leaving the speakers, so a flash released here is aligned with the sound and not with the schedule call.
    /// </summary>
    public sealed class NoteVisualQueue
    {
        readonly List<ScheduledNote> _pending = new List<ScheduledNote>(64);

        public int Count => _pending.Count;

        public void Enqueue(in ScheduledNote note) => _pending.Add(note);

        /// <summary>Drops the notes whose audio time is after <paramref name="audioTime"/> (their sound was cancelled: nothing is heard, so nothing flashes); returns how many.</summary>
        public int RemoveAfter(double audioTime)
        {
            var removed = _pending.RemoveAll(note => note.AudioTime > audioTime);
            return removed;
        }

        /// <summary>Moves every note whose audio time is at or before <paramref name="heardNow"/> + <paramref name="leadSec"/> to <paramref name="due"/> (ascending by time).</summary>
        public int DrainDue(double heardNow, double leadSec, List<ScheduledNote> due)
        {
            var limit = heardNow + leadSec;
            var start = due.Count;
            var keep = 0;
            for (var i = 0; i < _pending.Count; i++)
            {
                var note = _pending[i];
                if (note.AudioTime <= limit) due.Add(note);
                else _pending[keep++] = note; // compact in place, no closure allocation
            }
            if (keep == _pending.Count) return 0;
            _pending.RemoveRange(keep, _pending.Count - keep);
            due.Sort(start, due.Count - start, NoteTimeComparer.Instance);
            return due.Count - start;
        }

        sealed class NoteTimeComparer : IComparer<ScheduledNote>
        {
            public static readonly NoteTimeComparer Instance = new NoteTimeComparer();
            public int Compare(ScheduledNote a, ScheduledNote b) => a.AudioTime.CompareTo(b.AudioTime);
        }
    }
}
