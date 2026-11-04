using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The sky plays the chord of the bar of a song: of the chord only the notes that are degrees of the scale can be played, as steps of the scale.</summary>
    public static class SongChords
    {
        const int SemitonesPerOctave = 12;

        /// <summary>
        /// The first-octave scale steps whose note is one of the chord (in any octave), ascending, no step twice. The chord notes that are not degrees of the scale are left out,
        /// so F (fa la do) gives la and do in A minor pentatonic. Empty when no chord note is a degree.
        /// </summary>
        /// <param name="scaleRootMidi">The MIDI note of step 0 of the scale.</param>
        public static int[] SkyStepsFor(IReadOnlyList<int> chordMidi, ScaleSpec scale, int scaleRootMidi)
        {
            if (chordMidi == null) throw new ArgumentNullException(nameof(chordMidi));
            if (scale == null) throw new ArgumentNullException(nameof(scale));

            var steps = new List<int>();
            for (var step = 0; step < scale.DegreesPerOctave; step++)
            {
                var degreeClass = PitchClass(scale.MidiForStep(scaleRootMidi, step));
                for (var i = 0; i < chordMidi.Count; i++)
                {
                    if (PitchClass(chordMidi[i]) != degreeClass) continue;
                    steps.Add(step);
                    break;
                }
            }
            return steps.ToArray();
        }

        static int PitchClass(int midi) => ((midi % SemitonesPerOctave) + SemitonesPerOctave) % SemitonesPerOctave;
    }
}
