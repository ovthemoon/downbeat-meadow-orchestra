using System;

namespace Downbeat.Core
{
    /// <summary>
    /// When the cell guide shows what. The name of a cell shows when a hand comes INTO the cell (from another cell, or from nowhere): it fades in in a moment, stays <c>trail.labelSec</c> seconds and fades out
    /// again; a hand that stays in the cell shows it no more. The border of a cell glows softly all the time a hand is in it (and a moment after). Two hands at most (the tracker sees no more), each with the cell
    /// it is in (0..5, or any other number for no hand). A hand that is missing for a moment (a tracker that loses it and sees it again in the same cell) is not coming in again.
    /// </summary>
    public sealed class CellLabelTimer
    {
        public const int CellCount = CellGuideLayout.CellCount;
        public const int Slots = 2;
        public const float FadeInSec = 0.12f;
        public const float FadeOutSec = 0.4f;
        public const float GlowAttackSec = 0.1f;
        public const float GlowReleaseSec = 0.4f;
        /// <summary>A hand that comes back into the cell it left less than this long ago is not coming in again.</summary>
        public const double ReentryGraceSec = 0.6;

        readonly float _labelSec;
        readonly int[] _in = { -1, -1 };
        readonly int[] _lastIn = { -1, -1 };
        readonly double[] _leftAt = new double[Slots];
        readonly double[] _shownAt = new double[CellCount];
        readonly float[] _shownFrom = new float[CellCount];
        readonly bool[] _everShown = new bool[CellCount];
        readonly float[] _glow = new float[CellCount];
        bool _hasLast;
        double _lastSec;

        public CellLabelTimer(float labelSec)
        {
            _labelSec = Math.Max(0f, labelSec);
        }

        /// <summary>One drawn frame at <paramref name="nowSec"/> with the cell each hand is in (any number outside 0..5: no hand).</summary>
        public void Update(double nowSec, int cellOfHand0, int cellOfHand1)
        {
            var dt = _hasLast ? Math.Max(0.0, nowSec - _lastSec) : 0.0;
            _hasLast = true;
            _lastSec = nowSec;
            Place(0, cellOfHand0, nowSec);
            Place(1, cellOfHand1, nowSec);
            for (var cell = 0; cell < CellCount; cell++)
            {
                var target = IsOccupied(cell) ? 1f : 0f;
                var rate = target > _glow[cell] ? GlowAttackSec : GlowReleaseSec;
                _glow[cell] += (target - _glow[cell]) * (float)(1.0 - Math.Exp(-dt / rate));
            }
        }

        /// <summary>Is a hand in the cell now?</summary>
        public bool IsOccupied(int cell) => cell >= 0 && cell < CellCount && (_in[0] == cell || _in[1] == cell);

        /// <summary>How brightly the border of the cell glows, 0..1.</summary>
        public float Glow(int cell) => cell < 0 || cell >= CellCount ? 0f : _glow[cell];

        /// <summary>How visible the name of the cell is at <paramref name="nowSec"/>, 0..1.</summary>
        public float LabelAlpha(int cell, double nowSec)
        {
            if (cell < 0 || cell >= CellCount || !_everShown[cell] || !(_labelSec > 0f)) return 0f;
            var age = Math.Max(0.0, nowSec - _shownAt[cell]);
            var rise = Math.Min(1.0, age / FadeInSec);
            var coming = _shownFrom[cell] + (1f - _shownFrom[cell]) * (float)(rise * rise * (3.0 - 2.0 * rise));
            var late = Math.Min(1.0, Math.Max(0.0, (age - _labelSec) / FadeOutSec));
            return coming * (1f - (float)(late * late * (3.0 - 2.0 * late)));
        }

        void Place(int slot, int rawCell, double nowSec)
        {
            var cell = rawCell >= 0 && rawCell < CellCount ? rawCell : -1;
            if (cell == _in[slot]) return;
            if (cell < 0)
            {
                _leftAt[slot] = nowSec;
                _in[slot] = -1;
                return;
            }
            var flickered = _in[slot] < 0 && _lastIn[slot] == cell && nowSec - _leftAt[slot] <= ReentryGraceSec;
            _in[slot] = cell;
            _lastIn[slot] = cell;
            if (!flickered) Show(cell, nowSec);
        }

        // it goes on from the brightness it has now: a name that is still there does not blink off and on again
        void Show(int cell, double nowSec)
        {
            _shownFrom[cell] = LabelAlpha(cell, nowSec);
            _shownAt[cell] = nowSec;
            _everShown[cell] = true;
        }
    }
}
