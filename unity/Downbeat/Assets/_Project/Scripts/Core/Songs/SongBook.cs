using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The songs of the content that the instruments can play, and the reason for every song that was left out.</summary>
    public sealed class SongBookResult
    {
        public SongBookResult(IReadOnlyList<SongScore> songs, IReadOnlyList<string> rejections)
        {
            Songs = songs ?? throw new ArgumentNullException(nameof(songs));
            Rejections = rejections ?? throw new ArgumentNullException(nameof(rejections));
        }

        /// <summary>The songs that passed every check, in the order of the content.</summary>
        public IReadOnlyList<SongScore> Songs { get; }

        /// <summary>One line for each song that was left out, naming it and the first thing wrong with it.</summary>
        public IReadOnlyList<string> Rejections { get; }
    }

    /// <summary>
    /// Reads <c>songs</c> of the content into scores and checks each one against the instruments: the frame of the app (BPM, beats per bar, a straight rhythm), the shape of the bars,
    /// the names, whether the three instruments of the upper row can sound every melody note, whether the notes are far enough apart for them, and whether the lower row can sound
    /// every tone its <c>conduct.songPhrases</c> can ask of a bar. A song that fails is left out with a reason; the other songs and the content are not affected.
    /// </summary>
    public static class SongBook
    {
        public static SongBookResult Build(ContentDto content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var entries = content.songs;
            var songs = new List<SongScore>();
            var rejections = new List<string>();
            if (entries == null || entries.Length == 0) return new SongBookResult(songs.AsReadOnly(), rejections.AsReadOnly());

            var checker = new SongChecker(content);
            var acceptedIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Length; i++)
            {
                var problem = checker.Check(entries[i], acceptedIds, out var score);
                if (problem != null)
                {
                    rejections.Add($"{Label(entries[i], i)}: {problem}");
                    continue;
                }
                acceptedIds.Add(score.Id);
                songs.Add(score);
            }
            return new SongBookResult(songs.AsReadOnly(), rejections.AsReadOnly());
        }

        static string Label(SongEntryDto entry, int index) =>
            entry == null || string.IsNullOrWhiteSpace(entry.id) ? $"songs[{index}]" : $"song '{entry.id}'";

        /// <summary>The checks, in the order of the design: the frame, the bars, the names, the melody, the gaps, the accompaniment. The first problem of a song is the one reported.</summary>
        sealed class SongChecker
        {
            const float StraightSwing = 0.5f;
            const int DefaultMinGapSlots = 1;
            const int ChordNotes = 3;

            static readonly Part[] MelodyParts = { Part.Harp, Part.Recorder, Part.Bell };

            readonly string _frameProblem;
            readonly float _bpm;
            readonly int _beatsPerBar;
            readonly int _slotsPerBar;
            readonly InstrumentDto[] _instrumentByPart = new InstrumentDto[Parts.Count];
            readonly int[][] _sampleIndexesByPart = new int[Parts.Count][];
            readonly int[] _minGapSlotsByPart = new int[Parts.Count];
            readonly List<Accompaniment> _accompaniment = new List<Accompaniment>();

            /// <summary>A part of the lower row and the tone numbers its song phrase uses.</summary>
            readonly struct Accompaniment
            {
                public Accompaniment(Part part, int[] tones)
                {
                    Part = part;
                    Tones = tones;
                }

                public Part Part { get; }
                public int[] Tones { get; }
            }

            public SongChecker(ContentDto content)
            {
                var song = content.song;
                if (song == null) { _frameProblem = "the content has no song block to take the tempo from"; return; }
                _bpm = song.bpm;
                _beatsPerBar = song.beatsPerBar;
                _slotsPerBar = song.slotsPerBeat * song.beatsPerBar;
                if (_slotsPerBar <= 0) { _frameProblem = "song.slotsPerBeat and song.beatsPerBar must be more than 0"; return; }

                for (var p = 0; p < Parts.Count; p++) _minGapSlotsByPart[p] = DefaultMinGapSlots;
                if (song.partMinGapSlots != null)
                {
                    foreach (var gap in song.partMinGapSlots)
                    {
                        if (gap != null && Parts.TryParse(gap.part, out var part)) _minGapSlotsByPart[(int)part] = gap.slots;
                    }
                }
                ReadInstruments(content.instruments);
                ReadAccompaniment(content.conduct?.songPhrases);
            }

            void ReadInstruments(InstrumentDto[] instruments)
            {
                if (instruments == null) return;
                foreach (var instrument in instruments)
                {
                    if (instrument == null || instrument.samples == null || !Parts.TryParse(instrument.part, out var part)) continue;
                    var indexes = new int[instrument.samples.Length];
                    for (var i = 0; i < indexes.Length; i++) indexes[i] = i;
                    _instrumentByPart[(int)part] = instrument;
                    _sampleIndexesByPart[(int)part] = indexes;
                }
            }

            /// <summary>Only the lower row plays them; a phrase for the upper row, an unknown part or a tone outside the phrase's range is the content check's business, not a song's.</summary>
            void ReadAccompaniment(ConductPhraseDto[] phrases)
            {
                if (phrases == null) return;
                foreach (var phrase in phrases)
                {
                    if (phrase == null || phrase.steps == null || !Parts.TryParse(phrase.part, out var part) || Parts.IsSky(part)) continue;
                    var used = new bool[SongBar.LastToneIndex + 1];
                    foreach (var tone in phrase.steps)
                    {
                        if (tone >= 0 && tone < used.Length) used[tone] = true;
                    }
                    var tones = new List<int>();
                    for (var tone = 0; tone < used.Length; tone++)
                    {
                        if (used[tone]) tones.Add(tone);
                    }
                    _accompaniment.Add(new Accompaniment(part, tones.ToArray()));
                }
            }

            /// <summary>The first thing wrong with the song, or null; <paramref name="score"/> is the song when nothing is.</summary>
            public string Check(SongEntryDto entry, ISet<string> acceptedIds, out SongScore score)
            {
                score = null;
                if (_frameProblem != null) return _frameProblem;
                if (entry == null) return "the entry is empty";

                var problem = CheckTempo(entry) ?? CheckBars(entry) ?? CheckNames(entry, acceptedIds);
                if (problem != null) return problem;

                var built = ToScore(entry);
                problem = CheckMelodyPlayable(built) ?? CheckMelodyGaps(built) ?? CheckAccompaniment(built);
                if (problem != null) return problem;

                score = built;
                return null;
            }

            string CheckTempo(SongEntryDto entry)
            {
                if (entry.bpm != _bpm) return $"its bpm {entry.bpm} is not the {_bpm} of the app";
                if (entry.beatsPerBar != _beatsPerBar) return $"its beatsPerBar {entry.beatsPerBar} is not the {_beatsPerBar} of the app";
                if (entry.swing != StraightSwing) return $"its swing {entry.swing} is not {StraightSwing}: only a straight rhythm is played";
                return null;
            }

            string CheckBars(SongEntryDto entry)
            {
                if (entry.bars == null || entry.bars.Length == 0) return "it has no bars";
                for (var b = 0; b < entry.bars.Length; b++)
                {
                    var bar = entry.bars[b];
                    if (bar == null) return $"bar {b + 1} is empty";
                    if (bar.chord == null || bar.chord.Length != ChordNotes) return $"bar {b + 1} needs a chord of {ChordNotes} notes";
                    var problem = CheckSlots(bar.melody, b);
                    if (problem != null) return problem;
                }
                return null;
            }

            string CheckSlots(SongNoteDto[] melody, int barIndex)
            {
                if (melody == null) return null;
                var previous = -1;
                foreach (var note in melody)
                {
                    if (note == null) return $"bar {barIndex + 1} has an empty melody note";
                    if (note.slot < 0 || note.slot >= _slotsPerBar) return $"bar {barIndex + 1}: slot {note.slot} is outside 0..{_slotsPerBar - 1}";
                    if (note.slot <= previous) return $"bar {barIndex + 1}: slot {note.slot} does not come after slot {previous}";
                    previous = note.slot;
                }
                return null;
            }

            static string CheckNames(SongEntryDto entry, ISet<string> acceptedIds)
            {
                if (string.IsNullOrWhiteSpace(entry.id)) return "its id is empty";
                if (string.IsNullOrWhiteSpace(entry.title)) return "its title is empty";
                if (acceptedIds.Contains(entry.id)) return "its id is already used by an earlier song";
                return null;
            }

            static SongScore ToScore(SongEntryDto entry)
            {
                var bars = new SongBar[entry.bars.Length];
                for (var b = 0; b < bars.Length; b++)
                {
                    var bar = entry.bars[b];
                    var melody = new SongNote[bar.melody?.Length ?? 0];
                    for (var n = 0; n < melody.Length; n++) melody[n] = new SongNote(bar.melody[n].slot, bar.melody[n].midi);
                    bars[b] = new SongBar(bar.bass, bar.chord, melody);
                }
                var octave = entry.melodyOctave;
                return new SongScore(entry.id, entry.title, entry.credit, bars, octave?.harp ?? 0, octave?.recorder ?? 0, octave?.bell ?? 0);
            }

            bool CanPlay(Part part, int midi)
            {
                var instrument = _instrumentByPart[(int)part];
                return instrument != null && SamplePicker.TryPickMidi(instrument.samples, _sampleIndexesByPart[(int)part], instrument.maxPitchShift, midi, out _);
            }

            string CheckMelodyPlayable(SongScore score)
            {
                for (var b = 0; b < score.BarCount; b++)
                {
                    foreach (var note in score.Bars[b].Melody)
                    {
                        foreach (var part in MelodyParts)
                        {
                            var octave = score.OctaveFor(part);
                            if (CanPlay(part, note.Midi + octave)) continue;
                            var shifted = octave == 0 ? "" : $" (as {note.Midi + octave} with its melodyOctave {octave})";
                            return $"melody note {note.Midi} of bar {b + 1}, slot {note.Slot} cannot be played by the {Parts.Name(part)}{shifted}";
                        }
                    }
                }
                return null;
            }

            /// <summary>The gap between two melody notes counts across the bar line, and has to suit every instrument of the upper row, because a hand may be in any of them.</summary>
            string CheckMelodyGaps(SongScore score)
            {
                var hasPrevious = false;
                var previousBar = 0;
                var previousSlot = 0;
                for (var b = 0; b < score.BarCount; b++)
                {
                    foreach (var note in score.Bars[b].Melody)
                    {
                        if (hasPrevious)
                        {
                            var gap = (b - previousBar) * _slotsPerBar + note.Slot - previousSlot;
                            foreach (var part in MelodyParts)
                            {
                                if (gap >= _minGapSlotsByPart[(int)part]) continue;
                                return $"the melody notes of bar {previousBar + 1}, slot {previousSlot} and bar {b + 1}, slot {note.Slot} are {Slots(gap)} apart, and the {Parts.Name(part)} needs {Slots(_minGapSlotsByPart[(int)part])}";
                            }
                        }
                        hasPrevious = true;
                        previousBar = b;
                        previousSlot = note.Slot;
                    }
                }
                return null;
            }

            string CheckAccompaniment(SongScore score)
            {
                foreach (var line in _accompaniment)
                {
                    for (var b = 0; b < score.BarCount; b++)
                    {
                        foreach (var tone in line.Tones)
                        {
                            var midi = score.Bars[b].ChordTone(tone);
                            if (CanPlay(line.Part, midi)) continue;
                            return $"tone {tone} of bar {b + 1} is {midi}, which the {Parts.Name(line.Part)} cannot play";
                        }
                    }
                }
                return null;
            }

            static string Slots(int count) => count == 1 ? "1 slot" : $"{count} slots";
        }
    }
}
