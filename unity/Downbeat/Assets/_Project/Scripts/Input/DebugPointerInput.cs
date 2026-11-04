using System;
using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Controls
{
    /// <summary>
    /// Mouse and keyboard play. Click in the stage view: the thirds across and the sky above the horizon and the ground below it are the six cells (see <see cref="PointerMapping"/>), height = pitch.
    /// Strength comes from click speed (fast clicks are louder); Shift = loud, Ctrl or Alt = soft.
    /// Keyboard: A S D F G H J K L ; play the ten pitch steps for the cell under the pointer; Space plays at the pointer.
    /// </summary>
    public sealed class DebugPointerInput : IPlayInput
    {
        const int MouseButton = 0;
        static readonly KeyCode[] PitchKeys =
        {
            KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.Semicolon,
        };

        readonly IViewLayout _layout;
        readonly PlayableRange _range;
        readonly Queue<NoteIntent> _queue = new Queue<NoteIntent>(16);
        double _lastPlayAt = double.NaN;

        public DebugPointerInput(IViewLayout layout, PlayableRange range)
        {
            _layout = layout;
            _range = range;
        }

        /// <summary>Where the rows of the picture are (the meadow); null: the upper half of the view is the sky and the strip is not there.</summary>
        public IHandScreen Screen { get; set; }

        /// <summary>
        /// Says whether a click at this mouse position (as <c>Input.mousePosition</c> gives it) belongs to something drawn over the meadow, which takes it instead (the Tab panel): such a click plays no note.
        /// Null: no click is taken.
        /// </summary>
        public Func<Vector2, bool> IsBlockedAt { get; set; }

        public void Tick(double unscaledTimeSec)
        {
            var mouse = (Vector2)UnityEngine.Input.mousePosition;
            var clicked = UnityEngine.Input.GetMouseButtonDown(MouseButton) && Click(mouse, unscaledTimeSec);
            if (!clicked && UnityEngine.Input.GetKeyDown(KeyCode.Space)) PlayAt(mouse, unscaledTimeSec);

            var stage = _layout.StagePixels;
            var (strip, rowSplit, horizon) = Rows();
            var x01 = PointerMapping.X01(stage, mouse.x);
            var y01 = PointerMapping.HandY01(stage, mouse.x, mouse.y, strip, rowSplit, horizon);   // the place a hand would be at to be drawn under the pointer
            for (var i = 0; i < PitchKeys.Length && i < _range.PitchSteps; i++)
            {
                if (UnityEngine.Input.GetKeyDown(PitchKeys[i])) Enqueue(PointerMapping.PartAt(stage, mouse.x, mouse.y, horizon), i, unscaledTimeSec, x01, y01);
            }
        }

        /// <summary>A press of the left mouse button at <paramref name="mouse"/>: plays the cell under it, unless <see cref="IsBlockedAt"/> says that something drawn there takes the click. True when a note was queued.</summary>
        public bool Click(Vector2 mouse, double unscaledTimeSec) => !(IsBlockedAt?.Invoke(mouse) ?? false) && PlayAt(mouse, unscaledTimeSec);

        bool PlayAt(Vector2 mouse, double unscaledTimeSec)
        {
            var stage = _layout.StagePixels;
            var (strip, rowSplit, horizon) = Rows();
            if (!PointerMapping.TryMap(stage, mouse.x, mouse.y, _range.PitchSteps, horizon, out var part, out var step)) return false;
            var x01 = PointerMapping.X01(stage, mouse.x);
            var y01 = PointerMapping.HandY01(stage, mouse.x, mouse.y, strip, rowSplit, horizon);
            Enqueue(part, step, unscaledTimeSec, x01, y01);
            return true;
        }

        (float strip, float rowSplit, float horizon) Rows() => Screen == null
            ? (0f, PointerMapping.DefaultHorizon, PointerMapping.DefaultHorizon)
            : (Screen.OverlayFraction, Screen.RowSplit, HandScreenMap.HorizonOf(Screen.HorizonViewport, Screen.OverlayFraction));

        /// <summary>Puts an intent in the queue as if it had been clicked (tests, and later replay input).</summary>
        public void Inject(NoteIntent intent) => _queue.Enqueue(intent);

        /// <summary>Drops queued notes (used when another input takes over).</summary>
        public void Clear() => _queue.Clear();

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

        void Enqueue(Part part, int step, double now, float x01, float y01)
        {
            var interval = double.IsNaN(_lastPlayAt) ? double.NaN : now - _lastPlayAt;
            _lastPlayAt = now;
            _queue.Enqueue(new NoteIntent(part, step, ChooseVelocity(interval), default, double.NaN, x01, 1f, y01));
        }

        static float ChooseVelocity(double clickIntervalSec)
        {
            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift)) return VelocityMapper.LoudVelocity;
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.LeftAlt)) return VelocityMapper.SoftVelocity;
            return VelocityMapper.FromClickInterval(clickIntervalSec);
        }
    }
}
