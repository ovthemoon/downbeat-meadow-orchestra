using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.Controls
{
    /// <summary>
    /// Hand play: pulls tracker frames from an <see cref="IHandSource"/> (live camera or replay), feeds the continuous hand motion
    /// (<see cref="HandMotionTracker"/>, what views read) and queues note intents. In strike mode the <see cref="IHandInterpreter"/> turns strikes into notes and
    /// frame timestamps drive everything; in conduct mode (the default) the <see cref="ConductPlayer"/> plays what the hands are doing on the beat grid, by the audio clock,
    /// and no strike is looked for.
    /// </summary>
    public sealed class HandPlayInput : IPlayInput, IGridAwareInput
    {
        readonly IHandSource _source;
        readonly PlayableRange _range;
        readonly HandSettingsHolder _settings;
        readonly HandMotionTracker _motion;
        readonly IHandInterpreter _interpreter;
        readonly Queue<NoteIntent> _queue = new Queue<NoteIntent>(16);
        readonly List<HandFrame> _frames = new List<HandFrame>(8);
        readonly List<HandStrike> _strikes = new List<HandStrike>(4);
        readonly ConductPlayer _conduct;
        readonly List<NoteIntent> _conducted = new List<NoteIntent>(8);
        bool _configured;
        int _generation;

        public HandPlayInput(IHandSource source, PlayableRange range, HandSettingsHolder settings, HandMotionTracker motion, IHandInterpreter interpreter, ConductPlayer conduct = null)
        {
            _conduct = conduct;
            _source = source;
            _range = range;
            _settings = settings;
            _motion = motion;
            _interpreter = interpreter;
        }

        public TrackerState State => _source.State;
        public int StrikeCount { get; private set; }

        public void OnGridReady(in BeatGrid grid, int slotsPerBar)
        {
            _conduct?.OnGridReady(grid);
            (_source as IGridAwareInput)?.OnGridReady(grid, slotsPerBar);
        }

        public void Tick(double unscaledTimeSec)
        {
            if (!_configured)
            {
                _interpreter.Configure(_settings.Settings, _range.PitchSteps);
                _configured = true;
            }
            _frames.Clear();
            _source.Poll(_frames);
            if (_source.Generation != _generation)
            {
                _generation = _source.Generation;
                _motion.Reset();
                _interpreter.Reset();
                _conduct?.Reset();
            }

            var conducting = _conduct != null && _conduct.IsConducting;
            for (var i = 0; i < _frames.Count; i++)
            {
                _strikes.Clear();
                _motion.Update(_frames[i]);
                if (conducting) continue; // no strike is looked for: the hands are read as they are
                if (_conduct != null && !_conduct.MayPlay)
                {
                    _interpreter.Reset();   // the piece was ended with the hands: no strike until the next visit starts
                    continue;
                }
                _interpreter.Process(_frames[i], _motion, _strikes);
                for (var s = 0; s < _strikes.Count; s++)
                {
                    var strike = _strikes[s];
                    StrikeCount++;
                    _queue.Enqueue(new NoteIntent(strike.Part, strike.PitchStep, strike.Velocity, strike.Timing, _source.RequestTimeFor(strike.Timing.CaptureMs), strike.X01, y01: strike.Y01));
                }
            }

            if (!conducting) return;
            _conducted.Clear();
            _conduct.Update(_motion, _motion.Aspect, _conducted);
            for (var i = 0; i < _conducted.Count; i++) _queue.Enqueue(_conducted[i]);
        }

        public bool TryDequeue(out NoteIntent intent)
        {
            if (_queue.Count == 0)
            {
                intent = default;
                return false;
            }
            intent = _queue.Dequeue();
            return true;
        }

        /// <summary>Drops queued notes (they would be stale if this input was not the active one).</summary>
        public void Discard() => _queue.Clear();
    }
}
