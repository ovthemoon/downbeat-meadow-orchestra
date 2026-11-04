using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Collects what the frames, the tracker and the audio did since the last row of the health log and turns it into a <see cref="HealthRow"/>
    /// (<see cref="Snapshot"/>), then starts over. Pure logic: the caller says what happened and when.
    /// </summary>
    public sealed class HealthAccumulator
    {
        const float LongFrameSec = 0.1f; // compared as a float: a frame of exactly 100 ms is not over
        const double FullSecondSec = 1.0 - 1e-6; // a second of frame times that adds up to 0.99999998 in float is a full second
        const double MillisecondsPerSecond = 1000.0;
        const double P95 = 0.95;
        const double BytesPerMegabyte = 1024.0 * 1024.0;

        readonly List<float> _frameSec = new List<float>(8192);
        readonly List<double> _trackerAgesMs = new List<double>(256);
        double _seconds;
        int _frames;
        int _framesOver100Ms;
        double _bucketSeconds;
        int _bucketFrames;
        double _minBucketFps = double.PositiveInfinity;
        int _trackerPackets;
        int _handCalls;
        int _handsPresent;
        double _lastAudioNow = double.NaN;
        double _maxAudioStep;
        AudioStats _lastAudio;
        int _lastErrors;

        public void AddFrame(float deltaSec)
        {
            if (float.IsNaN(deltaSec) || deltaSec <= 0f) return;
            _frameSec.Add(deltaSec);
            _frames++;
            _seconds += deltaSec;
            if (deltaSec > LongFrameSec) _framesOver100Ms++;

            _bucketSeconds += deltaSec;
            _bucketFrames++;
            if (_bucketSeconds < FullSecondSec) return;
            _minBucketFps = Math.Min(_minBucketFps, _bucketFrames / _bucketSeconds);
            _bucketSeconds = 0.0;
            _bucketFrames = 0;
        }

        /// <summary>A hand frame arrived from the tracker; <paramref name="ageMs"/> is how old it was when it did.</summary>
        public void AddTrackerPacket(double ageMs)
        {
            _trackerPackets++;
            if (!double.IsNaN(ageMs) && ageMs >= 0.0) _trackerAgesMs.Add(ageMs);
        }

        /// <summary>Once per frame: whether somebody's hands were there.</summary>
        public void SetHandsPresent(bool present)
        {
            _handCalls++;
            if (present) _handsPresent++;
        }

        /// <summary>Once per frame with the audio clock: its largest step between two frames is the audio side of a stall (the clock ran on while the main thread did not).</summary>
        public void ObserveAudioClock(double audioNowSec)
        {
            if (!double.IsNaN(_lastAudioNow) && audioNowSec > _lastAudioNow) _maxAudioStep = Math.Max(_maxAudioStep, audioNowSec - _lastAudioNow);
            _lastAudioNow = audioNowSec;
        }

        /// <param name="audio">Cumulative audio counts; the row holds what happened since the previous row.</param>
        /// <param name="errors">Cumulative error count; the row holds what happened since the previous row.</param>
        public HealthRow Snapshot(DateTime utcNow, double uptimeSec, long monoBytes, long totalAllocatedBytes, AudioStats audio, int errors)
        {
            var fpsAverage = _seconds > 0.0 ? _frames / _seconds : 0.0;
            var row = new HealthRow(
                utcNow, uptimeSec, fpsAverage, double.IsPositiveInfinity(_minBucketFps) ? fpsAverage : _minBucketFps,
                PercentileMs(_frameSec), MaxMs(_frameSec), _framesOver100Ms, _maxAudioStep,
                monoBytes / BytesPerMegabyte, totalAllocatedBytes / BytesPerMegabyte,
                _seconds > 0.0 ? _trackerPackets / _seconds : 0.0, PercentileMs(_trackerAgesMs, 1.0, double.NaN),
                _handCalls > 0 ? (double)_handsPresent / _handCalls : 0.0,
                audio.Scheduled - _lastAudio.Scheduled, audio.Late - _lastAudio.Late, audio.Stolen - _lastAudio.Stolen, errors - _lastErrors);
            Reset(audio, errors);
            return row;
        }

        void Reset(AudioStats audio, int errors)
        {
            _frameSec.Clear();
            _trackerAgesMs.Clear();
            _seconds = 0.0;
            _frames = 0;
            _framesOver100Ms = 0;
            _bucketSeconds = 0.0;
            _bucketFrames = 0;
            _minBucketFps = double.PositiveInfinity;
            _trackerPackets = 0;
            _handCalls = 0;
            _handsPresent = 0;
            _maxAudioStep = 0.0;
            _lastAudio = audio;
            _lastErrors = errors;
        }

        static double MaxMs(List<float> sec)
        {
            var max = 0f;
            for (var i = 0; i < sec.Count; i++) max = Math.Max(max, sec[i]);
            return max * MillisecondsPerSecond;
        }

        static double PercentileMs(List<float> sec)
        {
            if (sec.Count == 0) return 0.0;
            var sorted = sec.ToArray();
            Array.Sort(sorted);
            return sorted[NearestRank(sorted.Length)] * MillisecondsPerSecond;
        }

        static double PercentileMs(List<double> values, double scale, double empty)
        {
            if (values.Count == 0) return empty;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            return sorted[NearestRank(sorted.Length)] * scale;
        }

        /// <summary>Zero-based index of the 95th percentile by the nearest-rank method.</summary>
        static int NearestRank(int count) => Math.Max(0, (int)Math.Ceiling(P95 * count) - 1);
    }
}
