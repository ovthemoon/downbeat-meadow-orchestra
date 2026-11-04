using System;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>What the logger reads when a row is due (and the time it stamps it with).</summary>
    public interface IHealthSource
    {
        DateTime UtcNow { get; }
        /// <summary>Cumulative audio counts since the app started.</summary>
        AudioStats Audio { get; }
        /// <summary>Cumulative count of errors and exceptions logged since the app started.</summary>
        int Errors { get; }
        /// <summary>The app has given up on the sound (no device in <c>--audio-timeout</c> seconds): the heartbeat says <c>audio=failed</c>.</summary>
        bool AudioFailed { get; }
        /// <summary>How far the app has come: Loading until Boot has finished, Running after, Failed when it gave up for any reason (content, a sound file, the sound device, an error in Boot). The heartbeat says <c>app=loading|running|failed</c>.</summary>
        AppPhase App { get; }
        string ExperienceState { get; }
        int EnergyLevel { get; }
        int Sessions { get; }
        /// <summary>The render scale in force (1 = every pixel of the window).</summary>
        float RenderScale { get; }
        /// <summary>The step of the automatic quality ladder (0 = lightest); -1 when the tier is locked.</summary>
        int QualityLevel { get; }
        /// <summary>A starved hand tracker is being ignored (stepping down did not help it).</summary>
        bool StarveIgnored { get; }
        /// <summary>How many times the automatic quality changed the picture since this was last asked (it starts counting again at every row).</summary>
        int TakeQualityChanges();
        int NatureDropped { get; }
        long MonoBytes { get; }
        long AllocatedBytes { get; }
        long WorkingSetBytes { get; }
    }

    /// <summary>
    /// Once per frame the app tells it what the frame did; once per interval (60 s) it writes a row of the health CSV, every 5 s the heartbeat, and for every frame over 100 ms a line in the
    /// stall file. It runs from the first frame of the app, before the sound is up and also if it never is: the heartbeat and the rows are written while the app is loading (those frames are
    /// not counted: <c>countFrames</c> is false) and after it has failed, and they say what the sound is doing. The first frames after loading are left out (they carry the boot hitch).
    /// <see cref="Flush"/> writes the last partial row when the app quits.
    /// </summary>
    public sealed class HealthLogger
    {
        const int WarmupFrames = 2;
        const float StallFrameSec = 0.1f;
        const double MinFinalRowSec = 5.0;
        const double MillisecondsPerSecond = 1000.0;
        const double AudioStallSec = 3.0;           // the audio clock moves every 21 ms: three seconds without a step means the sound device is gone

        readonly HealthFileWriter _writer;
        readonly IHealthSource _source;
        readonly HealthAccumulator _accumulator = new HealthAccumulator();
        readonly double _rowIntervalSec;
        readonly double _heartbeatIntervalSec;
        bool _started;
        int _frameNumber;
        double _startReal;
        double _lastRowAt;
        double _nextRowAt;
        double _nextBeatAt;
        double _lastAudioNow = double.NaN;
        double _lastAudioMoveReal;
        bool _audioEverMoved;
        AudioPhase _beatPhase = AudioPhase.Starting;
        AppPhase _beatApp = AppPhase.Loading;
        bool _beatWritten;

        public HealthLogger(HealthFileWriter writer, IHealthSource source, double rowIntervalSec, double heartbeatIntervalSec)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (!(rowIntervalSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(rowIntervalSec), rowIntervalSec, "must be positive");
            if (!(heartbeatIntervalSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(heartbeatIntervalSec), heartbeatIntervalSec, "must be positive");
            _rowIntervalSec = rowIntervalSec;
            _heartbeatIntervalSec = heartbeatIntervalSec;
        }

        /// <summary>Rows written so far.</summary>
        public int Rows { get; private set; }

        /// <summary>The folder the files go to (null when none could be made).</summary>
        public string Directory => _writer.Directory;

        /// <summary>Adds a line to <c>exit-reasons.txt</c> (the player is ending itself: <see cref="SelfExit"/>).</summary>
        public void AppendExitRecord(string line) => _writer.AppendExitRecord(line);

        /// <summary>What the sound is doing now: Starting until the audio clock has moved once, then Ok or Stalled (the clock stood still for three seconds); Failed once the app has given up.</summary>
        public AudioPhase Phase { get; private set; } = AudioPhase.Starting;

        /// <param name="realtimeSec">A clock that runs in real time (Time.realtimeSinceStartup).</param>
        /// <param name="audioNowSec">The audio clock.</param>
        /// <param name="trackerFrameArrived">A new hand frame came in since the last call.</param>
        /// <param name="countFrames">False while the app is loading or after it failed: the frame is not counted in the row (it would show the boot hitch as a stall) but the heartbeat and the rows go on.</param>
        /// <param name="trackerAgeMs">How long the newest packet of that hand frame had waited in the app (NaN: not measured, as with a replay).</param>
        public void Tick(double realtimeSec, float deltaSec, double audioNowSec, bool handsPresent, bool trackerFrameArrived, bool countFrames = true, double trackerAgeMs = double.NaN)
        {
            if (!_started)
            {
                _started = true;
                _startReal = realtimeSec;
                _lastRowAt = realtimeSec;
                _nextRowAt = realtimeSec + _rowIntervalSec;
                _nextBeatAt = realtimeSec;
                _lastAudioMoveReal = realtimeSec;
            }
            var audioStep = double.IsNaN(_lastAudioNow) || audioNowSec < _lastAudioNow ? 0.0 : audioNowSec - _lastAudioNow;
            if (audioStep > 0.0)
            {
                _lastAudioMoveReal = realtimeSec;
                _audioEverMoved = true;
            }
            _lastAudioNow = audioNowSec;
            Phase = PhaseNow(realtimeSec);

            if (countFrames)
            {
                _frameNumber++;
                if (_frameNumber > WarmupFrames) CountFrame(realtimeSec, deltaSec, audioNowSec, audioStep, handsPresent, trackerFrameArrived, trackerAgeMs);
            }
            else _frameNumber = 0;   // the warm-up starts again when the app begins to run

            var app = _source.App;
            var changed = !_beatWritten || Phase != _beatPhase || app != _beatApp;   // the news that the sound came up, stopped or was given up on, or that the app is running, is not held back for the next beat
            if (changed || realtimeSec >= _nextBeatAt)
            {
                _writer.WriteHeartbeat(_source.UtcNow, Phase, app);
                _beatWritten = true;
                _beatPhase = Phase;
                _beatApp = app;
                _nextBeatAt = changed ? realtimeSec + _heartbeatIntervalSec : NextDue(_nextBeatAt, _heartbeatIntervalSec, realtimeSec);
            }
            if (realtimeSec >= _nextRowAt)
            {
                WriteRow(realtimeSec);
                _nextRowAt = NextDue(_nextRowAt, _rowIntervalSec, realtimeSec);
            }
        }

        /// <summary>The app is quitting: writes what has been collected since the last row if that is at least a few seconds (so a run that is stopped by hand does not lose its last minute).</summary>
        public void Flush(double realtimeSec)
        {
            if (_started && realtimeSec - _lastRowAt >= MinFinalRowSec) WriteRow(realtimeSec);
        }

        AudioPhase PhaseNow(double realtimeSec)
        {
            if (_source.AudioFailed) return AudioPhase.Failed;
            if (!_audioEverMoved) return AudioPhase.Starting;
            return realtimeSec - _lastAudioMoveReal <= AudioStallSec ? AudioPhase.Ok : AudioPhase.Stalled;
        }

        void CountFrame(double realtimeSec, float deltaSec, double audioNowSec, double audioStep, bool handsPresent, bool trackerFrameArrived, double trackerAgeMs)
        {
            _accumulator.AddFrame(deltaSec);
            _accumulator.ObserveAudioClock(audioNowSec);
            _accumulator.SetHandsPresent(handsPresent);
            if (trackerFrameArrived) _accumulator.AddTrackerPacket(trackerAgeMs);
            if (deltaSec > StallFrameSec) _writer.AppendStall(_source.UtcNow, realtimeSec - _startReal, deltaSec * MillisecondsPerSecond, audioStep);
        }

        void WriteRow(double realtimeSec)
        {
            var now = _source.UtcNow;
            var row = _accumulator.Snapshot(now, realtimeSec - _startReal, _source.MonoBytes, _source.AllocatedBytes, _source.Audio, _source.Errors)
                .WithRun(_source.ExperienceState, _source.EnergyLevel, _source.Sessions, _source.NatureDropped, _source.WorkingSetBytes, Phase.Word(), _source.RenderScale, _source.QualityLevel, _source.StarveIgnored, _source.TakeQualityChanges());
            _writer.AppendRow(now, row.ToCsv());
            _lastRowAt = realtimeSec;
            Rows++;
        }

        /// <summary>The next due time after <paramref name="due"/>; if the app was stalled for longer than an interval, from now on instead of a burst of catch-up rows.</summary>
        static double NextDue(double due, double interval, double now)
        {
            var next = due + interval;
            return next > now ? next : now + interval;
        }
    }
}
