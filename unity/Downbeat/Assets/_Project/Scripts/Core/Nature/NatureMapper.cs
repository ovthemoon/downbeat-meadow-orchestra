using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    public static class NatureLayers
    {
        public const int Count = 3;

        public static bool TryParse(string value, out NatureLayer layer)
        {
            switch (value)
            {
                case "fireflies": layer = NatureLayer.Fireflies; return true;
                case "sky": layer = NatureLayer.Sky; return true;
                case "hills": layer = NatureLayer.Hills; return true;
                default: layer = NatureLayer.Fireflies; return false;
            }
        }

        public static string Name(NatureLayer layer)
        {
            switch (layer)
            {
                case NatureLayer.Fireflies: return "fireflies";
                case NatureLayer.Sky: return "sky";
                default: return "hills";
            }
        }
    }

    /// <summary>
    /// Maps (nature layer, pitch step) to a sample and a playback rate, like <see cref="PitchMapper"/> does for the hand instruments. Its samples are loaded after the
    /// hand instruments' samples, so <see cref="SampleResolution.SampleIndex"/> counts on from <c>firstSampleIndex</c>.
    /// </summary>
    public sealed class NatureMapper
    {
        readonly NatureInstrumentDto[] _instrumentByLayer = new NatureInstrumentDto[NatureLayers.Count];
        readonly int[][] _globalIndexByLayer = new int[NatureLayers.Count][];
        readonly List<SampleDto> _allSamples = new List<SampleDto>();
        readonly ScaleSpec _scale;

        public NatureMapper(IReadOnlyList<NatureInstrumentDto> instruments, ScaleSpec scale, int firstSampleIndex)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            if (instruments == null) throw new ArgumentNullException(nameof(instruments));
            if (firstSampleIndex < 0) throw new ArgumentOutOfRangeException(nameof(firstSampleIndex), firstSampleIndex, "must not be negative");
            foreach (var instrument in instruments)
            {
                if (!NatureLayers.TryParse(instrument.layer, out var layer)) throw new ArgumentException($"unknown nature layer '{instrument.layer}'");
                if (_instrumentByLayer[(int)layer] != null) throw new ArgumentException($"nature layer '{instrument.layer}' has two instruments");
                if (instrument.samples == null || instrument.samples.Length == 0) throw new ArgumentException($"nature instrument '{instrument.id}' has no samples");
                var indexes = new int[instrument.samples.Length];
                for (var i = 0; i < indexes.Length; i++)
                {
                    indexes[i] = firstSampleIndex + _allSamples.Count;
                    _allSamples.Add(instrument.samples[i]);
                }
                _instrumentByLayer[(int)layer] = instrument;
                _globalIndexByLayer[(int)layer] = indexes;
            }
            for (var l = 0; l < NatureLayers.Count; l++)
            {
                if (_instrumentByLayer[l] == null) throw new ArgumentException($"no instrument for nature layer {(NatureLayer)l}");
            }
        }

        /// <summary>The samples of the nature instruments in load order, starting at <c>firstSampleIndex</c> of the engine's list.</summary>
        public IReadOnlyList<SampleDto> AllSamples => _allSamples;

        public NatureInstrumentDto InstrumentFor(NatureLayer layer) => _instrumentByLayer[(int)layer];

        public bool TryResolve(NatureLayer layer, int pitchStep, out SampleResolution resolution)
        {
            var instrument = _instrumentByLayer[(int)layer];
            return SamplePicker.TryPick(instrument.samples, _globalIndexByLayer[(int)layer], instrument.rootMidi, instrument.maxPitchShift, _scale, pitchStep, out resolution);
        }
    }
}
