using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Maps (part, pitch step) to a sample and a playback rate. Uses the pitch measured from the recordings
    /// (<see cref="SampleDto.soundingMidi"/>), never the file names (VCSL marimba and recorder file names are an octave low).
    /// The sample closest to the target pitch is used; playback rate corrects the semitone shift and the measured tuning error.
    /// </summary>
    public sealed class PitchMapper
    {
        readonly InstrumentDto[] _instrumentByPart = new InstrumentDto[Parts.Count];
        readonly int[][] _globalIndexByPart = new int[Parts.Count][];
        readonly List<SampleDto> _allSamples = new List<SampleDto>();
        readonly ScaleSpec _scale;

        public PitchMapper(IReadOnlyList<InstrumentDto> instruments, ScaleSpec scale)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            if (instruments == null) throw new ArgumentNullException(nameof(instruments));
            foreach (var instrument in instruments)
            {
                if (!Parts.TryParse(instrument.part, out var part)) throw new ArgumentException($"unknown part '{instrument.part}'");
                if (_instrumentByPart[(int)part] != null) throw new ArgumentException($"part '{instrument.part}' has two instruments");
                if (instrument.samples == null || instrument.samples.Length == 0) throw new ArgumentException($"instrument '{instrument.id}' has no samples");
                var indexes = new int[instrument.samples.Length];
                for (var i = 0; i < indexes.Length; i++)
                {
                    indexes[i] = _allSamples.Count;
                    _allSamples.Add(instrument.samples[i]);
                }
                _instrumentByPart[(int)part] = instrument;
                _globalIndexByPart[(int)part] = indexes;
            }
            for (var p = 0; p < Parts.Count; p++)
            {
                if (_instrumentByPart[p] == null) throw new ArgumentException($"no instrument for part {(Part)p}");
            }
        }

        /// <summary>All samples in load order; <see cref="SampleResolution.SampleIndex"/> indexes this list.</summary>
        public IReadOnlyList<SampleDto> AllSamples => _allSamples;
        public int PitchSteps => _scale.PitchSteps;

        public InstrumentDto InstrumentFor(Part part) => _instrumentByPart[(int)part];

        public bool TryResolve(Part part, int pitchStep, out SampleResolution resolution)
        {
            var instrument = _instrumentByPart[(int)part];
            return SamplePicker.TryPick(instrument.samples, _globalIndexByPart[(int)part], instrument.rootMidi, instrument.maxPitchShift, _scale, pitchStep, out resolution);
        }

        /// <summary>The same for a pitch given as a MIDI note, which need not be a note of the scale (the notes of a song carry their MIDI).</summary>
        public bool TryResolveMidi(Part part, int midi, out SampleResolution resolution)
        {
            var instrument = _instrumentByPart[(int)part];
            return SamplePicker.TryPickMidi(instrument.samples, _globalIndexByPart[(int)part], instrument.maxPitchShift, midi, out resolution);
        }
    }
}
