using System;
using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>
    /// Puts the nature score on the audio clock. The notes are written (see <see cref="NatureLayerConductor"/>), not played by a hand, so they are scheduled a whole bar
    /// ahead (<c>nature.leadBars</c>, 2.5 s at 96 BPM): a main thread that stalls for up to that long (the Mac window showed about 1 s once) does not make a note late or lose it.
    /// A level change therefore shows up in the sound one bar later, which on a bar-based score is musical rather than late. Hand notes keep their 0.1 s.
    /// A note that a longer stall left in the past is dropped, never played late.
    /// </summary>
    public sealed class NatureAudioBridge
    {
        const double DbToLinearDivisor = 20.0;

        readonly IAudioEngine _audio;
        readonly BeatGrid _grid;
        readonly NatureLayerConductor _conductor;
        readonly NatureMapper _mapper;
        readonly double _leadSec;
        readonly double _minLeadSec;
        readonly List<NatureNote> _planned = new List<NatureNote>(32);
        readonly double[] _layerGain = new double[NatureLayers.Count];
        long _nextSlot;
        bool _started;

        public NatureAudioBridge(IAudioEngine audio, BeatGrid grid, NatureLayerConductor conductor, NatureMapper mapper, double leadSec, double minLeadSec)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _conductor = conductor ?? throw new ArgumentNullException(nameof(conductor));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            if (!(leadSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(leadSec), leadSec, "must be positive");
            if (!(minLeadSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(minLeadSec), minLeadSec, "must not be negative");
            _grid = grid;
            _leadSec = leadSec;
            _minLeadSec = minLeadSec;
            for (var l = 0; l < NatureLayers.Count; l++) _layerGain[l] = Math.Pow(10.0, _mapper.InstrumentFor((NatureLayer)l).gainDb / DbToLinearDivisor);
        }

        /// <summary>Notes put on the clock so far.</summary>
        public int Scheduled { get; private set; }
        /// <summary>Notes that were already (almost) past when they were planned, and so were not played.</summary>
        public int Dropped { get; private set; }
        /// <summary>Notes the mapper had no sample for (a content error the validator should have caught).</summary>
        public int Unmapped { get; private set; }

        /// <summary>
        /// Call once per frame with the exact audio clock. Plans the slots that came into the lead window since the last call at the energy <paramref name="level"/> of now,
        /// schedules them, and appends what was scheduled to <paramref name="scheduled"/> (the picture shows each when it is heard).
        /// </summary>
        public void Tick(double now, int level, List<NatureEvent> scheduled)
        {
            var horizon = _grid.LastSlotAtOrBefore(now + _leadSec) + 1;
            if (!_started)
            {
                _started = true;
                _nextSlot = _grid.FirstSlotAtOrAfter(now + _leadSec);
            }
            if (horizon <= _nextSlot) return;

            _planned.Clear();
            _conductor.Plan(_nextSlot, horizon, level, _planned);
            _nextSlot = horizon;
            foreach (var note in _planned) ScheduleNote(note, now, scheduled);
        }

        void ScheduleNote(NatureNote note, double now, List<NatureEvent> scheduled)
        {
            var heardAt = _grid.SlotTime(note.Slot);
            if (heardAt < now + _minLeadSec) { Dropped++; return; }
            if (!_mapper.TryResolve(note.Layer, note.PitchStep, out var resolution)) { Unmapped++; return; }

            // start the sample early by its attack so that the heard onset (not the file start) lands on the grid slot, like the hand notes
            _audio.Schedule(resolution.SampleIndex, heardAt - resolution.OnsetOffsetSec, resolution.PlaybackRate, note.Gain * _layerGain[(int)note.Layer]);
            Scheduled++;
            scheduled.Add(new NatureEvent(note, heardAt));
        }
    }
}
