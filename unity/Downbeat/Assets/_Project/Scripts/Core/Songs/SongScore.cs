using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>A melody note of a bar: the slot it is played on (0 is the bar line) and its MIDI note.</summary>
    public readonly struct SongNote
    {
        public SongNote(int slot, int midi)
        {
            Slot = slot;
            Midi = midi;
        }

        public int Slot { get; }
        public int Midi { get; }
    }

    /// <summary>One bar of a song: the bass and the chord the lower row plays from, and the melody notes the upper row plays.</summary>
    public sealed class SongBar
    {
        /// <summary>The last tone number a phrase of the lower row may use (0 is the bass, 1..3 the chord, 4..6 the chord an octave up).</summary>
        public const int LastToneIndex = 6;

        const int ChordNotes = 3;
        const int SemitonesPerOctave = 12;

        readonly SongNote[] _melody;

        public SongBar(int bass, IReadOnlyList<int> chord, IReadOnlyList<SongNote> melody)
        {
            if (chord == null) throw new ArgumentNullException(nameof(chord));
            if (chord.Count != ChordNotes) throw new ArgumentException($"a chord has {ChordNotes} notes, not {chord.Count}", nameof(chord));
            Bass = bass;
            var notes = new int[ChordNotes];
            for (var i = 0; i < notes.Length; i++) notes[i] = chord[i];
            Chord = Array.AsReadOnly(notes);
            _melody = new SongNote[melody?.Count ?? 0];
            for (var i = 0; i < _melody.Length; i++) _melody[i] = melody[i];
            Melody = Array.AsReadOnly(_melody);
        }

        /// <summary>MIDI note of the bass of the bar.</summary>
        public int Bass { get; }

        /// <summary>MIDI notes of the chord of the bar, three.</summary>
        public IReadOnlyList<int> Chord { get; }

        /// <summary>The melody notes in the order of their slots.</summary>
        public IReadOnlyList<SongNote> Melody { get; }

        /// <summary>The melody note on a slot of the bar, if there is one.</summary>
        public bool TryMelodyAt(int slotInBar, out int midi)
        {
            foreach (var note in _melody)
            {
                if (note.Slot != slotInBar) continue;
                midi = note.Midi;
                return true;
            }
            midi = 0;
            return false;
        }

        /// <summary>The MIDI note of a tone of this bar: 0 is the bass, 1..3 the chord, and every further three are the chord an octave higher (4..6 one octave up).</summary>
        public int ChordTone(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), index, "0 is the bass, 1 and more the chord");
            if (index == 0) return Bass;
            var chordIndex = index - 1;
            return Chord[chordIndex % ChordNotes] + SemitonesPerOctave * (chordIndex / ChordNotes);
        }
    }

    /// <summary>A song the app can play: its bars, and the octave the instruments of the upper row play its melody in. Built by <see cref="SongBook"/> from content that has been checked.</summary>
    public sealed class SongScore
    {
        readonly int _harpOctave;
        readonly int _recorderOctave;
        readonly int _bellOctave;

        public SongScore(string id, string title, string credit, IReadOnlyList<SongBar> bars, int harpOctave = 0, int recorderOctave = 0, int bellOctave = 0)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("a song needs an id", nameof(id));
            if (bars == null || bars.Count == 0) throw new ArgumentException("a song needs at least one bar", nameof(bars));
            Id = id;
            Title = title ?? "";
            Credit = credit ?? "";
            var copy = new SongBar[bars.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = bars[i] ?? throw new ArgumentException($"bar {i} is missing", nameof(bars));
            Bars = Array.AsReadOnly(copy);
            _harpOctave = harpOctave;
            _recorderOctave = recorderOctave;
            _bellOctave = bellOctave;
        }

        public string Id { get; }
        public string Title { get; }
        public string Credit { get; }
        public IReadOnlyList<SongBar> Bars { get; }
        public int BarCount => Bars.Count;

        /// <summary>Semitones added to the melody for an instrument of the upper row; 0 for the lower row, which plays no melody.</summary>
        public int OctaveFor(Part part)
        {
            switch (part)
            {
                case Part.Harp: return _harpOctave;
                case Part.Recorder: return _recorderOctave;
                case Part.Bell: return _bellOctave;
                default: return 0;
            }
        }
    }
}
