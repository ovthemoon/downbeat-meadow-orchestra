using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Whether somebody is there, from what the hand source and the inputs say each frame: a running tracker that keeps delivering frames with a hand in them, or a
    /// recent note from the mouse or keyboard (they have no hand of their own). A tracker that stops delivering frames (the camera was unplugged, a recording ended)
    /// counts as "no hands" even though its last frame still has a hand in it, which is the meaning of "the loss of the tracker is the same as no hands".
    /// Only a hand that plays counts as somebody being there (<see cref="HandPlayRule"/>, see <see cref="CountPlayers"/>): two fists held up alone, or a hand that has only just come in, never wake the meadow or start a visit.
    /// </summary>
    public sealed class HandPresenceMonitor
    {
        readonly double _frameStaleSec;
        readonly double _inputPresenceSec;
        long _lastFrameSeq = -1;
        double _lastFrameChangeAt = double.NegativeInfinity;
        double _lastInputNoteAt = double.NegativeInfinity;

        public HandPresenceMonitor(double frameStaleSec, double inputPresenceSec)
        {
            if (!(frameStaleSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(frameStaleSec), frameStaleSec, "must be positive");
            if (!(inputPresenceSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(inputPresenceSec), inputPresenceSec, "must not be negative");
            _frameStaleSec = frameStaleSec;
            _inputPresenceSec = inputPresenceSec;
        }

        /// <summary>
        /// How many of the hands in the picture count as somebody being there (the hands that play, <see cref="HandMotion.CanPlay"/>: an open hand, and one whose pose stays unsure once it has been tracked for the confirming
        /// pictures; not a fist, not a hand that has just come in) and how many are open.
        /// </summary>
        public static void CountPlayers(IHandMotionView hands, out int playing, out int open)
        {
            playing = 0;
            open = 0;
            for (var i = 0; i < hands.Count; i++)
            {
                var hand = hands.Hand(i);
                if (hand.CanPlay) playing++;
                if (hand.Pose == HandPose.Open) open++;
            }
        }

        /// <param name="frameSeq">Sequence number of the newest hand frame; any change counts as a new frame (a looping recording starts over).</param>
        /// <param name="inputNote">A note from the mouse or keyboard was played in this update.</param>
        public bool Update(double now, TrackerState trackerState, long frameSeq, int handCount, bool inputNote) =>
            Update(now, trackerState, frameSeq, handCount, handCount, inputNote, out _);

        /// <param name="handCount">The hands that count as somebody being there (<see cref="CountPlayers"/>: the hands that play).</param>
        /// <param name="openHandCount">The hands that are open.</param>
        /// <param name="openHandPresent">An open hand is there (or the mouse or keyboard was used lately: they have no hand to open): what a visit that was ended with the hands waits for.</param>
        public bool Update(double now, TrackerState trackerState, long frameSeq, int handCount, int openHandCount, bool inputNote, out bool openHandPresent)
        {
            if (frameSeq != _lastFrameSeq)
            {
                _lastFrameSeq = frameSeq;
                _lastFrameChangeAt = now;
            }
            if (inputNote) _lastInputNoteAt = now;

            var framesArriving = now - _lastFrameChangeAt <= _frameStaleSec;
            var trackerRunning = trackerState == TrackerState.Running && framesArriving;
            var inputRecent = now - _lastInputNoteAt <= _inputPresenceSec;
            openHandPresent = ExperiencePresence.HasHands(trackerRunning, openHandCount, inputRecent);
            return ExperiencePresence.HasHands(trackerRunning, handCount, inputRecent);
        }
    }
}
