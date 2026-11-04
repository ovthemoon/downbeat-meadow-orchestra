namespace Downbeat.Core
{
    /// <summary>
    /// The audio clock for the eyes. Unity's audio clock (<c>AudioSettings.dspTime</c>) moves once per audio block (21 ms at 1024 samples and 48 kHz), so a flower that follows it
    /// would step by up to 21 ms from frame to frame. This one follows the wall clock between the audio clock's steps: right after a step the audio time is exact, so the best
    /// estimate of "audio time minus wall time" is the largest value seen in the last half second. The result is never behind the audio clock, never goes back, and never runs more than
    /// <see cref="MaxAheadSec"/> ahead of it (a stopped audio device cannot make the eyes run away); a jump of the audio clock is followed at once.
    /// </summary>
    public sealed class SmoothClock
    {
        public const double MaxAheadSec = 0.05;
        const double WindowSec = 0.5;
        const double JumpSec = 1.0;
        const int Capacity = 512;

        readonly double[] _wall = new double[Capacity];
        readonly double[] _offset = new double[Capacity];
        int _head;
        int _count;
        double _last = double.NegativeInfinity;

        /// <summary>Feed the audio clock and the wall clock of this frame; returns the audio time now.</summary>
        public double Update(double audioTime, double wallTime)
        {
            if (_count > 0 && System.Math.Abs(audioTime - _last) > JumpSec)
            {
                _count = 0; // the audio clock was moved (a seek, or a device reset that starts it again): start again, with no memory of the old time
                _last = double.NegativeInfinity;
            }

            var slot = (_head + _count) % Capacity;
            if (_count == Capacity) { _head = (_head + 1) % Capacity; slot = (_head + _count - 1) % Capacity; } else _count++;
            _wall[slot] = wallTime;
            _offset[slot] = audioTime - wallTime;
            while (_count > 1 && wallTime - _wall[_head] > WindowSec)
            {
                _head = (_head + 1) % Capacity;
                _count--;
            }

            var best = double.NegativeInfinity;
            for (var i = 0; i < _count; i++)
            {
                var offset = _offset[(_head + i) % Capacity];
                if (offset > best) best = offset;
            }

            var estimate = wallTime + best;
            if (estimate < audioTime) estimate = audioTime;
            if (estimate > audioTime + MaxAheadSec) estimate = audioTime + MaxAheadSec;
            if (estimate < _last) estimate = _last;
            _last = estimate;
            return estimate;
        }
    }
}
