using System;

namespace Downbeat.Core
{
    /// <summary>A scale given as semitone offsets inside one octave (A minor pentatonic = 0, 3, 5, 7, 10 from A).</summary>
    public sealed class ScaleSpec
    {
        const int SemitonesPerOctave = 12;
        readonly int[] _steps;

        public ScaleSpec(int[] steps, int pitchSteps)
        {
            if (steps == null || steps.Length == 0) throw new ArgumentException("scale needs at least one step", nameof(steps));
            if (pitchSteps < steps.Length) throw new ArgumentOutOfRangeException(nameof(pitchSteps), "fewer pitch steps than scale degrees");
            for (var i = 0; i < steps.Length; i++)
            {
                if (steps[i] < 0 || steps[i] >= SemitonesPerOctave) throw new ArgumentOutOfRangeException(nameof(steps), "steps must be 0..11");
                if (i > 0 && steps[i] <= steps[i - 1]) throw new ArgumentException("steps must be strictly ascending", nameof(steps));
            }
            _steps = (int[])steps.Clone();
            PitchSteps = pitchSteps;
        }

        /// <summary>Number of distinct heights an input can choose (2 octaves of a 5-note scale = 10).</summary>
        public int PitchSteps { get; }
        public int DegreesPerOctave => _steps.Length;

        /// <summary>MIDI note of a pitch step for an instrument whose step 0 is <paramref name="rootMidi"/>.</summary>
        public int MidiForStep(int rootMidi, int pitchStep)
        {
            if (pitchStep < 0 || pitchStep >= PitchSteps) throw new ArgumentOutOfRangeException(nameof(pitchStep));
            return rootMidi + _steps[pitchStep % _steps.Length] + SemitonesPerOctave * (pitchStep / _steps.Length);
        }
    }
}
