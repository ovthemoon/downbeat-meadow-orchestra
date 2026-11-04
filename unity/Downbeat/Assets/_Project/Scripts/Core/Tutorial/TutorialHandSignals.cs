using System;

namespace Downbeat.Core
{
    /// <summary>
    /// What the tutorial needs to know about the hands in one picture: is an open hand in view (or did the mouse or keyboard play), did a hand that plays move into another cell in this picture, and are both hands
    /// fists. The cells come from <see cref="HandCellTracker"/>, which picks them as the conducting play does (the same <see cref="CellPicker"/> with the same dead bands, a hand that does not play - a fist, or a hand that
    /// has only just come in - in no cell), so a hand trembling on the line between two cells is not a change and the cell that counts is the cell whose instrument plays. Pure logic on the hands it is given.
    /// </summary>
    public sealed class TutorialHandSignals
    {
        readonly HandCellTracker _cells;

        /// <param name="cells">The tracker of the cells of the hands; this reads it and feeds it the hands (it is not shared with anything that updates it as well).</param>
        public TutorialHandSignals(HandCellTracker cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        /// <summary>An open hand is in view, or a note was played with the mouse or keyboard (they have no hand of their own to show).</summary>
        public bool OpenHandVisible { get; private set; }
        /// <summary>A hand that plays moved into another cell in this picture (true for one picture only).</summary>
        public bool CellChanged { get; private set; }
        /// <summary>Two hands are in view and both are fists.</summary>
        public bool BothFists { get; private set; }

        /// <param name="nowSec">A clock that runs on (the audio clock): how long a hand that is missing for a moment keeps its cell is counted on it.</param>
        /// <param name="hands">The hands of the latest picture; null reads as no hands.</param>
        /// <param name="inputNote">A note from the mouse or keyboard was played in this picture.</param>
        public void Read(double nowSec, IHandMotionView hands, bool inputNote)
        {
            _cells.Update(nowSec, hands);
            CellChanged = _cells.ChangedThisFrame;
            OpenHandVisible = inputNote;
            var tracked = 0;
            var fists = 0;
            var count = hands == null ? 0 : hands.Count;
            for (var i = 0; i < count; i++)
            {
                var hand = hands.Hand(i);
                if (!hand.IsTracked) continue;
                tracked++;
                if (hand.Pose == HandPose.Fist) fists++;
                else if (hand.Pose == HandPose.Open) OpenHandVisible = true;
            }
            BothFists = tracked == HandBufferLayout.MaxHands && fists == tracked;
        }
    }
}
