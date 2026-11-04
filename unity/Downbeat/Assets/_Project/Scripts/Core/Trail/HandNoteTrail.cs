using System;

namespace Downbeat.Core
{
    /// <summary>What one hand is doing in the frame the trail is stepped to. Places are viewport units (x to the right, y up, 0..1 across the picture).</summary>
    public readonly struct TrailHandFrame
    {
        public TrailHandFrame(bool visible, bool isFist, float viewportX, float viewportY, double captureMs, int handId, int epoch, int tag)
        {
            Visible = visible;
            IsFist = isFist;
            ViewportX = viewportX;
            ViewportY = viewportY;
            CaptureMs = captureMs;
            HandId = handId;
            Epoch = epoch;
            Tag = tag;
        }

        /// <summary>The tracker has the hand in its latest picture.</summary>
        public bool Visible { get; }

        /// <summary>A fist plays nothing and leaves no trail.</summary>
        public bool IsFist { get; }

        public float ViewportX { get; }
        public float ViewportY { get; }

        /// <summary>The capture time of the tracker frame the place is from (the same value again means no new tracker frame).</summary>
        public double CaptureMs { get; }

        /// <summary>A hand that is another one, or has come back after being lost (<c>HandMotion.Id</c> and <c>Epoch</c>), starts its trail anew instead of gliding over from where the last one was.</summary>
        public int HandId { get; }
        public int Epoch { get; }

        /// <summary>The caller's number that the notes born in this frame keep (the view puts the colour of the hand's cell in it).</summary>
        public int Tag { get; }
    }

    /// <summary>
    /// The trail of one hand: its place on the screen smoothed between tracker frames (<see cref="ScreenFollow"/>) and the notes born at that place (<see cref="NoteTrailEmitter"/>). An open hand that is in the
    /// picture leaves notes in proportion to how fast that place moves; a fist, a hand that is gone, or one that is held still leaves none, and the notes it has left fade out on their own. How much the hand moves
    /// is read from the smoothed place and not from the tracker's own speed of the hand, which is measured over the last 70 ms and so is 0 when the tracker sees the hand only 6 times a second.
    /// </summary>
    public sealed class HandNoteTrail
    {
        // The One Euro numbers for a hand the tracker may see only 6 times a second. Slower than those of the ribbon on purpose: with a few pictures a second the quicker the point follows a new picture the more
        // each of them shows as a jump (the beta that suits 20 pictures a second makes a hand at 6 a second jerk); at these a hand crossing the picture is drawn moving in every frame, the fastest frame
        // about twice as fast as the slowest, about 0.15 s behind.
        public const float FollowMinHz = 0.8f;
        public const float FollowBeta = 2f;
        public const float FollowSpeedHz = 1.5f;
        /// <summary>A hand that moves less than this (picture widths and heights a second, as the smoothed place of the hand moves) leaves no notes: that is a hand held still, its picture jitter included (about a palm length in 3 seconds).</summary>
        public const float StillSpeed = 0.05f;
        /// <summary>A hand that moves this fast or faster leaves the most notes a second (about 4 palm lengths a second, as conducting play calls a hand that conducts).</summary>
        public const float FullSpeed = 0.8f;

        readonly ScreenFollow _follow = new ScreenFollow();
        readonly NoteTrailEmitter _emitter;
        bool _wasVisible;
        int _handId;
        int _epoch;
        bool _hasStepped;
        double _lastSec;

        public HandNoteTrail(TrailSettings settings, int seed = 1)
        {
            _emitter = new NoteTrailEmitter(settings, seed);
        }

        /// <summary>The notes alive.</summary>
        public NoteTrailEmitter Notes => _emitter;

        /// <summary>How much the hand moves, 0 (still) .. 1 (as much as the trail shows), as of the last <see cref="Step"/>.</summary>
        public float Movement01 { get; private set; }

        /// <summary>Where the hand is drawn, viewport units; meaningful while the hand has been visible.</summary>
        public ScreenPoint Place => _follow.Place;

        /// <summary>Takes new numbers (a change of tier); the notes alive stay.</summary>
        public void Configure(TrailSettings settings) => _emitter.Configure(settings);

        /// <summary>Forgets the hand and every note.</summary>
        public void Clear()
        {
            _emitter.Clear();
            _follow.Reset();
            _wasVisible = false;
            _hasStepped = false;
            Movement01 = 0f;
        }

        /// <summary>How much a hand that moves at <paramref name="speed"/> earns of the trail: 0 up to <paramref name="still"/>, 1 from <paramref name="full"/> on, straight between.</summary>
        public static float MovementAmount(float speed, float still, float full)
        {
            if (!(full > still)) return speed >= full ? 1f : 0f;
            return Math.Max(0f, Math.Min(1f, (speed - still) / (full - still)));
        }

        /// <summary>One drawn frame at <paramref name="nowSec"/>. Returns how many notes were born in it.</summary>
        public int Step(double nowSec, in TrailHandFrame hand)
        {
            var dt = _hasStepped ? (float)Math.Min(NoteTrailEmitter.LongestStepSec, Math.Max(0.0, nowSec - _lastSec)) : 0f;
            _hasStepped = true;
            _lastSec = nowSec;
            if (!hand.Visible)
            {
                _wasVisible = false;
                Movement01 = 0f;
                var place = _follow.Place;
                return _emitter.Update(nowSec, place.X, place.Y, emit: false, movement01: 0f, tag: hand.Tag);
            }
            if (!_wasVisible || hand.HandId != _handId || hand.Epoch != _epoch)
            {
                _follow.Reset();
                _handId = hand.HandId;
                _epoch = hand.Epoch;
            }
            _wasVisible = true;
            var at = _follow.Step(hand.ViewportX, hand.ViewportY, hand.CaptureMs, dt, FollowMinHz, FollowBeta, FollowSpeedHz);
            Movement01 = MovementAmount(_follow.Speed, StillSpeed, FullSpeed);
            return _emitter.Update(nowSec, at.X, at.Y, emit: !hand.IsFist, movement01: Movement01, tag: hand.Tag);
        }
    }
}
