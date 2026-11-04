using System;
using Downbeat.Core;

namespace Downbeat.Controls
{
    /// <summary>
    /// The one <see cref="IPlayInput"/> the game sees. It follows <see cref="InputModeResolver"/>: mouse, camera hands, or a replay,
    /// and falls back to the mouse when the camera cannot be used. Only the active input produces notes; the hand source is always
    /// polled so the camera state stays current, and the page is told in words which mode is active.
    /// </summary>
    public sealed class SwitchingPlayInput : IPlayInput, IGridAwareInput
    {
        readonly DebugPointerInput _mouse;
        readonly HandPlayInput _hand;
        readonly InputMode _requested;
        readonly string _replayName;
        readonly Action<string> _announce;
        ModeDecision _decision;
        TrackerState _lastState = (TrackerState)(-1);
        bool _decided;

        public SwitchingPlayInput(DebugPointerInput mouse, HandPlayInput hand, InputMode requested, string replayName, Action<string> announce)
        {
            _mouse = mouse;
            _hand = hand;
            _requested = requested;
            _replayName = replayName;
            _announce = announce;
        }

        public InputMode Active => _decision.Active;
        public string Label => _decision.Label;

        public void OnGridReady(in BeatGrid grid, int slotsPerBar) => _hand.OnGridReady(grid, slotsPerBar);

        public void Tick(double unscaledTimeSec)
        {
            _hand.Tick(unscaledTimeSec);
            if (!_decided || _hand.State != _lastState) Decide();
            if (_decision.Active == InputMode.Mouse)
            {
                _hand.Discard();
                _mouse.Tick(unscaledTimeSec);
            }
        }

        public bool TryDequeue(out NoteIntent intent) => _decision.Active == InputMode.Mouse ? _mouse.TryDequeue(out intent) : _hand.TryDequeue(out intent);

        void Decide()
        {
            _lastState = _hand.State;
            var next = InputModeResolver.Resolve(_requested, _lastState, _replayName);
            var wasDecided = _decided;
            var changed = !wasDecided || next.Active != _decision.Active || next.Label != _decision.Label;
            _decided = true;
            if (!changed) return;
            _decision = next;
            if (wasDecided)
            {
                _hand.Discard(); // notes queued for the previous mode are stale now
                _mouse.Clear();
            }
            _announce?.Invoke(next.Label);
        }
    }
}
