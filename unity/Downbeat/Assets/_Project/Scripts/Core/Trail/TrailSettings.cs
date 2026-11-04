using System;

namespace Downbeat.Core
{
    /// <summary>The numbers of the note trail and the cell names, as an immutable snapshot of <see cref="TrailDto"/> (the content file can be replaced while the views go on).</summary>
    public sealed class TrailSettings
    {
        TrailSettings(float notesPerSec, int maxNotes, float lifeSec, float size, float labelSec, float lowCountScale)
        {
            NotesPerSec = notesPerSec;
            MaxNotes = maxNotes;
            LifeSec = lifeSec;
            Size = size;
            LabelSec = labelSec;
            LowCountScale = lowCountScale;
        }

        /// <summary>The most notes a second for a hand that moves as much as can be.</summary>
        public float NotesPerSec { get; }

        /// <summary>The most notes of one hand alive at once.</summary>
        public int MaxNotes { get; }

        public float LifeSec { get; }

        /// <summary>The height of a note, as a share of the height of the picture.</summary>
        public float Size { get; }

        /// <summary>How long the name of a cell shows before it fades out.</summary>
        public float LabelSec { get; }

        /// <summary>The share of <see cref="NotesPerSec"/> and <see cref="MaxNotes"/> the Low tier keeps.</summary>
        public float LowCountScale { get; }

        /// <summary>From the content block (null: the defaults). Values that the content validator would have refused are not checked again here.</summary>
        public static TrailSettings From(TrailDto dto)
        {
            var d = dto ?? new TrailDto();
            return new TrailSettings(d.notesPerSec, d.maxNotes, d.lifeSec, d.size, d.labelSec, d.lowCountScale);
        }

        /// <summary>The same settings with the notes a second and the notes alive multiplied by <paramref name="scale"/> (1 = as they are, 0 = no trail); the other numbers stay.</summary>
        public TrailSettings WithCountScale(float scale)
        {
            var s = Math.Max(0f, scale);
            return new TrailSettings(NotesPerSec * s, (int)Math.Round(MaxNotes * s, MidpointRounding.AwayFromZero), LifeSec, Size, LabelSec, LowCountScale);
        }
    }
}
