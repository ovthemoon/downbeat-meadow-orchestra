using System;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--log-pose</c> (off by default): about once a second it writes one line to Player.log for every hand the tracker sees, with the four finger ratios, the pose, whether the hand plays and the cell it is in
    /// (<see cref="PoseLogLine"/>). It is how the pose thresholds (<c>pose.openRatio</c>, <c>pose.fistRatio</c>) are tuned on the machine the camera is on, with a relaxed raised hand, a fist and a half open hand
    /// held up in turn. It only watches: it keeps a cell tracker of its own (the cell that the conducting play, the cell guide and the tutorial would give), and nothing it does reaches the notes.
    /// </summary>
    public sealed class PoseLogger
    {
        public const double PeriodSec = 1.0;

        readonly HandCellTracker _cells;
        readonly IHandMotionView _hands;
        readonly Action<string> _sink;
        double _nextAtSec = double.NegativeInfinity;

        /// <param name="cells">The cell tracker made from the content (<see cref="HandCellTracker.From"/>); the logger is the only one to update it.</param>
        /// <param name="hands">The hands of the tracker; the finger ratios are only there when this is the <see cref="HandMotionTracker"/> of the game.</param>
        /// <param name="thresholds">The numbers the pose is read with, named once in the first line so that a log is read against them.</param>
        /// <param name="sink">Where a line goes; null writes to the Unity log (Player.log).</param>
        public PoseLogger(HandCellTracker cells, IHandMotionView hands, PoseSettings thresholds, Action<string> sink = null)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
            _hands = hands ?? throw new ArgumentNullException(nameof(hands));
            _sink = sink ?? (message => Debug.Log(message));
            var read = thresholds ?? PoseSettings.Default;
            _sink(
                FormattableString.Invariant($"{PoseLogLine.Tag} --log-pose is on: a line per hand about every {PeriodSec:0} s. index..pinky = fingertip distance from the wrist in wrist-to-middle-knuckle lengths; ") +
                FormattableString.Invariant($"a finger is stretched at >= {read.OpenRatio:0.00} and curled at <= {read.FistRatio:0.00} (pose.openRatio, pose.fistRatio); three of four make Open or Fist; raw = this picture, pose = taken over after {read.ConfirmFrames} pictures in a row"));
        }

        /// <param name="nowSec">A clock that runs on (the real time of the app).</param>
        public void Tick(double nowSec)
        {
            _cells.Update(nowSec, _hands);
            if (nowSec < _nextAtSec) return;
            var wrote = false;
            for (var i = 0; i < _hands.Count; i++)
            {
                var hand = _hands.Hand(i);
                if (!hand.IsTracked) continue;
                FingerRatios fingers = default;
                var raw = hand.Pose;
                (_hands as HandMotionTracker)?.TryGetPicture(hand.Id, out fingers, out raw);
                _sink(PoseLogLine.Format(hand.Id, fingers, raw, hand.Pose, hand.CanPlay, _cells.CellOf(hand.Id)));
                wrote = true;
            }
            if (wrote) _nextAtSec = nowSec + PeriodSec;
        }
    }
}
