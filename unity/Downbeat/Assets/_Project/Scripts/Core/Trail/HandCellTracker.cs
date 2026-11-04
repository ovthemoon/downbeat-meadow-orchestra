using System;

namespace Downbeat.Core
{
    /// <summary>
    /// Which of the six cells each hand is in, as the conducting play has it: the same <see cref="CellPicker"/> with the same dead bands (<c>hand.partHysteresis</c>, <c>cells.rowHysteresis</c>), fed
    /// with the same pictures of the hand, a hand that does not play (<see cref="HandPlayRule"/>: a fist, or a hand that has only just come in) or has been missing longer than the hold time is in no cell (it plays nothing), and a hand keeps its cell through a missing picture. So the
    /// cell that the guide lights is the cell whose instrument plays. Hands are those of the tracker, by id (0..<see cref="MaxHands"/>-1). Not thread safe.
    /// </summary>
    public sealed class HandCellTracker
    {
        public const int MaxHands = HandBufferLayout.MaxHands;
        public const int NoCell = -1;

        sealed class Track
        {
            public int Epoch = int.MinValue;
            public double LastSeenSec = double.NegativeInfinity;
            public double LatestCaptureMs = double.NegativeInfinity;
            public float X;
            public float Y;
            /// <summary>The hand plays (<see cref="HandPlayRule"/>); a fist, and a hand that has only just come in, is in no cell.</summary>
            public bool CanPlay;
            public int Cell = NoCell;
            /// <summary>The cell the hand was last in, kept while it is in none: the next place is picked with it as the cell the hand comes from.</summary>
            public int LastCell = NoCell;
        }

        readonly Track[] _tracks = new Track[MaxHands];
        readonly CellSettings _cells;
        readonly float _columnMarginBands;
        readonly double _holdSec;

        /// <param name="cells">The rows and their dead band (null: the defaults).</param>
        /// <param name="partHysteresis"><c>hand.partHysteresis</c>, as a fraction of the picture's width.</param>
        /// <param name="holdSec">How long a hand that is missing goes on being in its cell (<c>conduct.holdMs</c> or <c>hand.maxGapMs</c>, the longer).</param>
        public HandCellTracker(CellSettings cells, float partHysteresis, double holdSec)
        {
            _cells = cells ?? CellSettings.Default;
            _columnMarginBands = CellPicker.ColumnMarginFor(partHysteresis);
            _holdSec = Math.Max(0.0, holdSec);
            for (var i = 0; i < _tracks.Length; i++) _tracks[i] = new Track();
        }

        /// <summary>The tracker made from the numbers that the conducting play is configured with (<c>cells</c>, <c>hand</c>, <c>conduct</c>): one place for them, so that the two cannot drift apart.</summary>
        public static HandCellTracker From(ContentDto content)
        {
            var hand = new HandInterpreterSettings(content.hand);
            var conduct = ConductSettings.From(content.conduct, null);
            return new HandCellTracker(CellSettings.From(content.cells), hand.PartHysteresis, HoldSecOf(conduct, hand));
        }

        /// <summary>How long a hand that is missing from the pictures goes on counting where it was last seen (the longer of <c>conduct.holdMs</c> and <c>hand.maxGapMs</c>): the cell guide, the conducting play and the two-fists ending all use it.</summary>
        public static double HoldSecOf(ConductSettings conduct, HandInterpreterSettings hand) => Math.Max(conduct.HoldMs, hand.MaxGapMs) / 1000.0;

        /// <summary>A hand is now in another cell than the one it was in at the last <see cref="Update"/> (it moved on from one cell to the next; a hand that appears or goes is not that).</summary>
        public bool ChangedThisFrame { get; private set; }

        /// <summary>The cell (0..5, the number of its <see cref="Part"/>) that the hand of that id is in, or <see cref="NoCell"/> when it is in none (not there, a fist, or an id that does not exist).</summary>
        public int CellOf(int handId) => handId < 0 || handId >= _tracks.Length ? NoCell : _tracks[handId].Cell;

        /// <summary>
        /// The row of the cell the hand of that id is in: <paramref name="isSky"/> is true for the upper row (the sky), false for the lower (the ground). It is the row the instrument plays in, dead band of
        /// <c>cells.rowHysteresis</c> included, so what is drawn for a hand (the ribbon, the grass it blows) can change row exactly when the sound does. False, and <paramref name="isSky"/> false, when the hand is in no cell (<see cref="CellOf"/>).
        /// </summary>
        public bool TryGetRow(int handId, out bool isSky)
        {
            var cell = CellOf(handId);
            isSky = cell != NoCell && Parts.IsSky((Part)cell);
            return cell != NoCell;
        }

        /// <summary>Forgets every hand.</summary>
        public void Reset()
        {
            for (var i = 0; i < _tracks.Length; i++) _tracks[i] = new Track();
            ChangedThisFrame = false;
        }

        /// <summary>Once per frame: <paramref name="nowSec"/> is a clock that runs on (any one), <paramref name="hands"/> the tracker's hands.</summary>
        public void Update(double nowSec, IHandMotionView hands)
        {
            ChangedThisFrame = false;
            for (var id = 0; id < _tracks.Length; id++)
            {
                var track = _tracks[id];
                var before = track.Cell;
                if (hands != null && hands.TryGetById(id, out var hand) && hand.IsTracked) Observe(track, hand, nowSec);
                var active = nowSec - track.LastSeenSec <= _holdSec && track.CanPlay;
                track.Cell = active ? Pick(track) : NoCell;
                if (track.Cell != NoCell) track.LastCell = track.Cell;
                if (before != NoCell && track.Cell != NoCell && before != track.Cell) ChangedThisFrame = true;
            }
        }

        // a picture that has not been seen before counts (the view goes on reporting the last one until the next arrives, and that is not a sighting); a hand that is back after more than the hold time starts over
        void Observe(Track track, in HandMotion hand, double nowSec)
        {
            if (hand.Epoch != track.Epoch)
            {
                if (nowSec - track.LastSeenSec > _holdSec) track.LatestCaptureMs = double.NegativeInfinity;
                track.Epoch = hand.Epoch;
            }
            if (!(hand.CaptureMs > track.LatestCaptureMs)) return;
            track.X = hand.X;
            track.Y = hand.Y;
            track.CanPlay = HandPlayRule.Resolve(hand, nowSec - track.LastSeenSec <= _holdSec, track.CanPlay);
            track.LastSeenSec = nowSec;
            track.LatestCaptureMs = hand.CaptureMs;
        }

        int Pick(Track track) =>
            (int)CellPicker.Pick(track.X, track.Y, (Part)Math.Max(track.LastCell, 0), track.LastCell != NoCell, _columnMarginBands, _cells);
    }
}
