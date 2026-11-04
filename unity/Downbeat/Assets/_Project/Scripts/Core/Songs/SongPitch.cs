using System;

namespace Downbeat.Core
{
    /// <summary>
    /// The scale step an instrument gets for a MIDI note of a song. A song note sounds at its MIDI, but the flowers, the sky effects and the record go by the scale step,
    /// so a note takes the step of this instrument that sounds nearest to it (the lower one on a tie), kept inside the scale of the instrument.
    /// </summary>
    public sealed class SongPitch
    {
        readonly ScaleSpec _scale;
        readonly int[] _rootMidiByPart;

        /// <param name="rootMidiByPart">The MIDI note of step 0 of each instrument, in the order of <see cref="Part"/>.</param>
        public SongPitch(ScaleSpec scale, int[] rootMidiByPart)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            if (rootMidiByPart == null) throw new ArgumentNullException(nameof(rootMidiByPart));
            if (rootMidiByPart.Length != Parts.Count) throw new ArgumentException($"one root for each of the {Parts.Count} parts, not {rootMidiByPart.Length}", nameof(rootMidiByPart));
            _rootMidiByPart = (int[])rootMidiByPart.Clone();
        }

        /// <summary>The scale of <c>song</c> and the root of every instrument of the content.</summary>
        public static SongPitch From(ContentDto content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.song == null) throw new ArgumentException("content has no song block", nameof(content));
            if (content.instruments == null) throw new ArgumentException("content has no instruments", nameof(content));
            var roots = new int[Parts.Count];
            var found = new bool[Parts.Count];
            foreach (var instrument in content.instruments)
            {
                if (instrument == null || !Parts.TryParse(instrument.part, out var part)) throw new ArgumentException($"unknown part '{instrument?.part}'", nameof(content));
                roots[(int)part] = instrument.rootMidi;
                found[(int)part] = true;
            }
            for (var p = 0; p < Parts.Count; p++)
            {
                if (!found[p]) throw new ArgumentException($"no instrument for part {(Part)p}", nameof(content));
            }
            return new SongPitch(new ScaleSpec(content.song.scaleSteps, content.song.pitchSteps), roots);
        }

        /// <summary>The step of <paramref name="part"/> whose note is nearest to <paramref name="midi"/>; the lower step when two are as near.</summary>
        public int StepFor(Part part, int midi)
        {
            var root = _rootMidiByPart[(int)part];
            var best = 0;
            var bestDistance = long.MaxValue;
            for (var step = 0; step < _scale.PitchSteps; step++)
            {
                var distance = Math.Abs((long)midi - _scale.MidiForStep(root, step));
                if (distance >= bestDistance) continue;
                best = step;
                bestDistance = distance;
            }
            return best;
        }
    }
}
