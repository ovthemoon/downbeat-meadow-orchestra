using System;

namespace Downbeat.Core
{
    /// <summary>One hand as the two-fists ending sees it: where it is (x, y as in <see cref="HandMotion"/>, 0..1, y down), how big its palm is (video heights) and whether it is a fist.</summary>
    public readonly struct CutoffHand
    {
        public float X { get; }
        public float Y { get; }
        /// <summary>Palm length in video heights (<see cref="HandMotion.Size"/>).</summary>
        public float Size { get; }
        /// <summary>True only for a hand that is surely a fist; an open or an unsure hand is false.</summary>
        public bool IsFist { get; }

        public CutoffHand(float x, float y, float size, bool isFist)
        {
            X = x; Y = y; Size = size; IsFist = isFist;
        }
    }

    /// <summary>
    /// Both hands are fists, the distance between them (sideways, in palm lengths) grows by <see cref="CutoffSettings.SpreadPalms"/> within <see cref="CutoffSettings.WindowSec"/> and ends at
    /// least <see cref="CutoffSettings.MinApartPalms"/> apart, and they stay so for <see cref="CutoffSettings.HoldSec"/>: <see cref="Update"/> then says true, once. After that it says false until
    /// neither hand is a fist any more (a hand that is gone is not a fist), so fists that stay up after the end do not end the next piece.
    /// <para>It looks at the growth of the distance and not at the distance itself, so fists held wide apart, fists that drift apart slowly and a hand that closes while it is far from the other
    /// never count. Anything but two fists clears what it has seen: open hands that wave wildly, one fist and one open hand, a hand that is gone. It works on the time it is
    /// given (the pictures of the tracker), so 6 pictures a second give the same answer as 30, only later by a picture or two.</para>
    /// <para>Fed with the hands of the tracker (<see cref="Update(double, IHandMotionView, float)"/>) a hand that is missing from a picture or two (a tracker at 6 pictures a second drops one now and then in the
    /// middle of a spread) stays where it was last seen, as a fist if it was one, for the hold time; when the tracker finds it again its pose is not known for the pictures that confirm it, and it
    /// is taken for the same fist meanwhile. A hand that is missing longer is gone, and the window with it.</para>
    /// </summary>
    public sealed class CutoffGesture
    {
        const double TimeEpsilon = 1e-9;
        const int StartCapacity = 64;
        const float MinPalmSize = 1e-4f;
        /// <summary>How long a hand that is missing from the pictures goes on being where it was last seen (the same as the cell guide's and the conducting play's: <see cref="HandCellTracker.HoldSecOf"/>).</summary>
        public const double DefaultHandHoldSec = 0.8;

        /// <summary>What is remembered of one hand of the tracker, by its id.</summary>
        struct Remembered
        {
            public bool Seen;
            public int Epoch;
            public double LastSeenSec;
            public float X;
            public float Y;
            public float Size;
            public bool IsFist;
            /// <summary>The tracker found the hand again within the hold time and has not confirmed its pose yet: it counts as the fist it was.</summary>
            public bool ResumedFist;
        }

        readonly CutoffSettings _settings;
        readonly double _handHoldSec;
        readonly Remembered[] _remembered = new Remembered[HandBufferLayout.MaxHands];
        double _lastHandsSec = double.NegativeInfinity;
        double[] _times = new double[StartCapacity];
        float[] _apart = new float[StartCapacity];
        int _head;
        int _count;
        bool _armed = true;
        bool _holding;
        double _holdingSince;
        double _lastNow = double.NegativeInfinity;

        /// <param name="handHoldSec">How long a hand of the tracker that is missing from the pictures goes on counting, where it was last seen (<see cref="DefaultHandHoldSec"/>).</param>
        public CutoffGesture(CutoffSettings settings, double handHoldSec = DefaultHandHoldSec)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _handHoldSec = Math.Max(0.0, handHoldSec);
        }

        /// <summary>Forget everything (the stream of pictures restarted, a new run): the gesture is armed again.</summary>
        public void Reset()
        {
            ClearWindow();
            ForgetHands();
            _armed = true;
            _lastNow = double.NegativeInfinity;
        }

        void ForgetHands()
        {
            for (var i = 0; i < _remembered.Length; i++) _remembered[i] = default;
            _lastHandsSec = double.NegativeInfinity;
        }

        /// <summary>
        /// The same for the hands the tracker sees (the first two of them, in id order; a hand is a fist when its <see cref="HandMotion.Pose"/> says so). A hand that is missing from a picture or two
        /// stays where it was last seen for the hold time (see the class).
        /// </summary>
        public bool Update(double nowSec, IHandMotionView hands, float aspect)
        {
            if (nowSec < _lastHandsSec) ForgetHands(); // time went back: a replay began again
            _lastHandsSec = nowSec;

            var count = 0;
            CutoffHand first = default;
            CutoffHand second = default;
            for (var id = 0; id < _remembered.Length; id++)
            {
                if (hands.TryGetById(id, out var hand) && hand.IsTracked) Observe(ref _remembered[id], hand, nowSec);
                ref var r = ref _remembered[id];
                if (!r.Seen || nowSec - r.LastSeenSec > _handHoldSec)
                {
                    r.Seen = false;
                    continue;
                }
                var current = new CutoffHand(r.X, r.Y, r.Size, r.IsFist);
                if (count == 0) first = current; else second = current;
                count++;
            }
            return Update(nowSec, count, first, second, aspect);
        }

        void Observe(ref Remembered r, in HandMotion hand, double nowSec)
        {
            if (!r.Seen || hand.Epoch != r.Epoch)
            {
                // a hand the tracker found again within the hold time is the same hand: it is a fist until its new pose is confirmed (a hand that is new to the tracker has no pose for a picture or two)
                r.ResumedFist = r.Seen && nowSec - r.LastSeenSec <= _handHoldSec && r.IsFist;
                r.Epoch = hand.Epoch;
            }
            var fist = hand.Pose == HandPose.Fist;
            if (r.ResumedFist && hand.Pose == HandPose.Unknown && !hand.CanPlay) fist = true;
            else r.ResumedFist = false;
            r.Seen = true;
            r.LastSeenSec = nowSec;
            r.X = hand.X;
            r.Y = hand.Y;
            r.Size = hand.Size;
            r.IsFist = fist;
        }

        /// <param name="nowSec">The time of the picture (any steady clock in seconds).</param>
        /// <param name="handCount">How many hands are in the picture (0..2); <paramref name="first"/> and <paramref name="second"/> are the first <paramref name="handCount"/> of them.</param>
        /// <param name="aspect">Width / height of the picture: x and y are fractions of different lengths.</param>
        /// <returns>True once, in the picture in which the held spread is complete.</returns>
        public bool Update(double nowSec, int handCount, CutoffHand first, CutoffHand second, float aspect)
        {
            if (nowSec < _lastNow) ClearWindow(); // time went back: a replay began again
            _lastNow = nowSec;

            var firstFist = handCount >= 1 && first.IsFist;
            var secondFist = handCount >= 2 && second.IsFist;
            if (!firstFist && !secondFist) _armed = true;
            if (!firstFist || !secondFist || !_armed || !TryMeasure(first, second, aspect, out var apart))
            {
                ClearWindow();
                return false;
            }

            Push(nowSec, apart);
            if (_holding) return KeepHolding(nowSec, apart);

            if (apart < _settings.MinApartPalms || apart - Closest() < _settings.SpreadPalms) return false;
            _holding = true;
            _holdingSince = nowSec;
            return KeepHolding(nowSec, apart);
        }

        bool KeepHolding(double nowSec, float apart)
        {
            if (apart < _settings.MinApartPalms)
            {
                _holding = false; // they came back together before the hold was over
                return false;
            }
            if (nowSec - _holdingSince < _settings.HoldSec - TimeEpsilon) return false;

            ClearWindow();
            _armed = false;
            return true;
        }

        static bool TryMeasure(CutoffHand first, CutoffHand second, float aspect, out float apart)
        {
            apart = 0f;
            var palm = (first.Size + second.Size) * 0.5f;
            var sideways = Math.Abs(first.X - second.X) * aspect;
            if (!(palm > MinPalmSize) || !(aspect > 0f) || float.IsInfinity(sideways) || float.IsNaN(sideways)) return false;
            apart = sideways / palm;
            return !float.IsInfinity(apart);
        }

        void Push(double nowSec, float apart)
        {
            while (_count > 0 && nowSec - _times[_head] > _settings.WindowSec + TimeEpsilon)
            {
                _head = (_head + 1) % _times.Length;
                _count--;
            }
            if (_count == _times.Length) Grow();
            var tail = (_head + _count) % _times.Length;
            _times[tail] = nowSec;
            _apart[tail] = apart;
            _count++;
        }

        float Closest()
        {
            var closest = float.PositiveInfinity;
            for (var i = 0; i < _count; i++) closest = Math.Min(closest, _apart[(_head + i) % _apart.Length]);
            return closest;
        }

        void Grow()
        {
            var times = new double[_times.Length * 2];
            var apart = new float[_apart.Length * 2];
            for (var i = 0; i < _count; i++)
            {
                times[i] = _times[(_head + i) % _times.Length];
                apart[i] = _apart[(_head + i) % _apart.Length];
            }
            _times = times;
            _apart = apart;
            _head = 0;
        }

        void ClearWindow()
        {
            _head = 0;
            _count = 0;
            _holding = false;
        }
    }
}
