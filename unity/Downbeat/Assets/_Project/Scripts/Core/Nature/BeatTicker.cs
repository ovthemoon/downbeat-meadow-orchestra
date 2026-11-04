using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    public readonly struct BeatTick
    {
        public BeatTick(long beat, double audioTime, bool isDownbeat)
        {
            Beat = beat;
            AudioTime = audioTime;
            IsDownbeat = isDownbeat;
        }

        public long Beat { get; }
        public double AudioTime { get; }
        /// <summary>The first beat of a bar.</summary>
        public bool IsDownbeat { get; }
    }

    /// <summary>
    /// Hands out each beat of the grid once, when it is due. A beat that is much older than the frame asking for it (the main thread stalled) is skipped, not fired
    /// late and in a bunch.
    /// </summary>
    public sealed class BeatTicker
    {
        readonly BeatGrid _grid;
        readonly int _beatsPerBar;
        readonly double _maxLateSec;
        long _nextBeat;
        bool _started;

        public BeatTicker(BeatGrid grid, int beatsPerBar, double maxLateSec)
        {
            if (beatsPerBar < 1) throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar, "must be at least 1");
            if (!(maxLateSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(maxLateSec), maxLateSec, "must not be negative");
            _grid = grid;
            _beatsPerBar = beatsPerBar;
            _maxLateSec = maxLateSec;
        }

        /// <summary>Appends the beats whose time is at or before <paramref name="now"/> + <paramref name="leadSec"/> and that have not been handed out yet.</summary>
        public void Collect(double now, double leadSec, List<BeatTick> output)
        {
            if (!_started)
            {
                _started = true;
                _nextBeat = FirstBeatAtOrAfter(now - _maxLateSec);
            }
            var limit = now + leadSec;
            var oldest = FirstBeatAtOrAfter(now - _maxLateSec);
            if (_nextBeat < oldest) _nextBeat = oldest; // skip what a stall left behind
            while (BeatTime(_nextBeat) <= limit)
            {
                output.Add(new BeatTick(_nextBeat, BeatTime(_nextBeat), FloorMod(_nextBeat, _beatsPerBar) == 0));
                _nextBeat++;
            }
        }

        double BeatTime(long beat) => _grid.SlotTime(beat * _grid.SlotsPerBeat);

        long FirstBeatAtOrAfter(double time) => (long)Math.Ceiling((time - _grid.Origin) / (_grid.SlotDuration * _grid.SlotsPerBeat));

        static long FloorMod(long value, long modulus)
        {
            var r = value % modulus;
            return r < 0 ? r + modulus : r;
        }
    }
}
