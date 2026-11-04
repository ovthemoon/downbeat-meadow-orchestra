using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The choice shared by the hand instruments and the nature instruments: for a pitch step (or a MIDI note), the sample whose measured pitch is nearest, and the playback rate that
    /// corrects the semitone shift and the measured tuning error. It uses the pitch measured from the recordings (<see cref="SampleDto.soundingMidi"/>), never file names.
    /// </summary>
    internal static class SamplePicker
    {
        const double CentsPerOctave = 1200.0;
        const double CentsPerSemitone = 100.0;
        const double MillisecondsPerSecond = 1000.0;

        /// <param name="globalIndexes">For each sample of the instrument, its index in the list the audio engine loads.</param>
        public static bool TryPick(SampleDto[] samples, int[] globalIndexes, int rootMidi, int maxPitchShift, ScaleSpec scale, int pitchStep, out SampleResolution resolution)
        {
            resolution = default;
            if (pitchStep < 0 || pitchStep >= scale.PitchSteps) return false;
            return TryPickMidi(samples, globalIndexes, maxPitchShift, scale.MidiForStep(rootMidi, pitchStep), out resolution);
        }

        /// <summary>The same choice for a pitch given as a MIDI note (the notes of a song), which need not be a note of the scale.</summary>
        /// <param name="globalIndexes">For each sample of the instrument, its index in the list the audio engine loads.</param>
        public static bool TryPickMidi(SampleDto[] samples, int[] globalIndexes, int maxPitchShift, int targetMidi, out SampleResolution resolution)
        {
            resolution = default;

            var best = -1;
            var bestDistance = long.MaxValue;
            for (var i = 0; i < samples.Length; i++)
            {
                // A long: the target of a song comes from a file and may be any int.
                var distance = Math.Abs((long)targetMidi - samples[i].soundingMidi);
                // On a tie prefer the higher sample (pitched down): it keeps more of the original brightness.
                if (distance < bestDistance || (distance == bestDistance && samples[i].soundingMidi > samples[best].soundingMidi))
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (bestDistance > maxPitchShift) return false;

            var sample = samples[best];
            var semitones = targetMidi - sample.soundingMidi;
            var cents = semitones * CentsPerSemitone - sample.tuneCents;
            var rate = Math.Pow(2.0, cents / CentsPerOctave);
            var onsetOffsetSec = sample.perceivedOnsetOffsetMs / MillisecondsPerSecond / rate;
            resolution = new SampleResolution(globalIndexes[best], sample.id, targetMidi, semitones, rate, onsetOffsetSec);
            return true;
        }
    }
}
