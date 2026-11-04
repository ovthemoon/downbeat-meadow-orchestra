using System.Linq;
using Downbeat.Core;

namespace Downbeat.Tests
{
    /// <summary>Songs built in code for the tests: the shipped content with its <c>songs</c> replaced, and the pieces of a score.</summary>
    static class SongFixtures
    {
        public static SongBarDto Bar(int bass, int[] chord, params (int slot, int midi)[] melody) => new SongBarDto
        {
            bass = bass,
            chord = chord,
            melody = melody.Select(n => new SongNoteDto { slot = n.slot, midi = n.midi }).ToArray(),
        };

        /// <summary>A song in the frame of the app (96 BPM, 4/4, straight rhythm) with no change of octave.</summary>
        public static SongEntryDto Song(string id, params SongBarDto[] bars) => new SongEntryDto
        {
            id = id,
            title = "Title of " + id,
            credit = "test",
            bpm = 96,
            beatsPerBar = 4,
            swing = 0.5f,
            melodyOctave = new MelodyOctaveDto(),
            bars = bars,
        };

        public static ConductPhraseDto Phrase(string part, params int[] steps) => new ConductPhraseDto { part = part, steps = steps };

        /// <summary>The shipped content with these songs, and the accompaniment the shipped songs are meant to have (low string 0 0 2 0, marimba 1 2 3 2).</summary>
        public static ContentDto ContentWith(params SongEntryDto[] songs)
        {
            var content = TestContent.LoadShipped();
            content.songs = songs;
            content.conduct.songPhrases = new[] { Phrase("lowstring", 0, 0, 2, 0), Phrase("marimba", 1, 2, 3, 2) };
            return content;
        }

        /// <summary>A bar in A minor and one in F, both inside what every instrument can play.</summary>
        public static SongBarDto AminorBar(params (int slot, int midi)[] melody) => Bar(57, new[] { 69, 72, 76 }, melody);

        public static SongBarDto FmajorBar(params (int slot, int midi)[] melody) => Bar(65, new[] { 65, 69, 72 }, melody);

        /// <summary>A song every check passes: two bars, melody notes 6, 2, 8 (across the bar line) and 4 slots apart.</summary>
        public static SongEntryDto ValidSong(string id = "quiet-meadow") => Song(id,
            AminorBar((0, 76), (6, 79), (8, 76)),
            FmajorBar((0, 72), (4, 76)));
    }
}
