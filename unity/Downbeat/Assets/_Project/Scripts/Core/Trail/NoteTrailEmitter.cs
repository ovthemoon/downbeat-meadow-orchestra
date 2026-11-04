using System;

namespace Downbeat.Core
{
    /// <summary>
    /// One note of a trail: where it was born (viewport units, y up), when, the small drift it floats away with, and how it is tilted. <see cref="Tag"/> is the caller's own number (the view keeps the colour
    /// of the cell the hand was in there). A note lives <c>trail.lifeSec</c> seconds: it is there in full at its birth, stays so for the first part of its life and fades out on its way to the end, growing a little smaller as it goes.
    /// </summary>
    public readonly struct TrailNote
    {
        /// <summary>The share of its life that a note stays fully visible before it starts to fade.</summary>
        const float HoldShare = 0.35f;
        /// <summary>The size of a note at the end of its life, as a share of its size at birth.</summary>
        public const float EndScale = 0.65f;

        public double BirthSec { get; }
        public float X { get; }
        public float Y { get; }
        /// <summary>Viewport units a second.</summary>
        public float DriftX { get; }
        public float DriftY { get; }
        /// <summary>Radians; the note is drawn turned by this much.</summary>
        public float Tilt { get; }
        public int Tag { get; }

        public TrailNote(double birthSec, float x, float y, float driftX, float driftY, float tilt, int tag)
        {
            BirthSec = birthSec;
            X = x;
            Y = y;
            DriftX = driftX;
            DriftY = driftY;
            Tilt = tilt;
            Tag = tag;
        }

        public float XAt(double nowSec) => X + DriftX * (float)(nowSec - BirthSec);

        public float YAt(double nowSec) => Y + DriftY * (float)(nowSec - BirthSec);

        /// <summary>0 at birth, 1 at the end of its <paramref name="lifeSec"/> (more than 1 after that).</summary>
        public float Age01(double nowSec, float lifeSec) => lifeSec <= 0f ? 1f : (float)((nowSec - BirthSec) / lifeSec);

        /// <summary>How visible a note of age <paramref name="age01"/> is, 0..1: fully at its birth and for the first 35% of its life, then it fades out smoothly to its end. 0 before its birth and from its end on.</summary>
        public static float Alpha(float age01)
        {
            if (age01 < 0f || age01 >= 1f) return 0f;
            var t = Math.Min(1f, Math.Max(0f, (age01 - HoldShare) / (1f - HoldShare)));
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>The size of a note of age <paramref name="age01"/> as a share of its size at birth: 1 at its birth, <see cref="EndScale"/> at its end.</summary>
        public static float Scale(float age01) => 1f + (EndScale - 1f) * Math.Min(1f, Math.Max(0f, age01));
    }

    /// <summary>
    /// Decides how many notes a hand leaves and where. Each <see cref="Update"/> gets the (smoothed) place of the hand and how much it moves (0..1): the more it moves the more notes a second, from none up to
    /// <c>trail.notesPerSec</c>; no more than <c>trail.maxNotes</c> are alive at once (the oldest goes first) and a note is gone after <c>trail.lifeSec</c>. A note is born at the place of the hand (spread along the way
    /// the hand went since the last update), scattered a little around it, and floats up and sideways slowly. A seeded random generator makes a run repeatable. Not thread safe.
    /// </summary>
    public sealed class NoteTrailEmitter
    {
        /// <summary>A pause of the frames longer than this counts as this long: a hitch must not give a burst of notes.</summary>
        public const double LongestStepSec = 0.25;
        const float ScatterInSizes = 0.8f;
        const float MinDriftSpeed = 0.015f;
        const float MaxDriftSpeed = 0.05f;
        const float MaxDriftAngle = 1.1f;
        const float MaxTilt = 0.35f;

        readonly Random _random;
        TrailNote[] _ring = new TrailNote[1];
        int _oldest;
        int _count;
        TrailSettings _settings;
        double _carry;
        bool _hasLast;
        double _lastSec;
        float _lastX;
        float _lastY;

        public NoteTrailEmitter(TrailSettings settings, int seed = 1)
        {
            _random = new Random(seed);
            Configure(settings);
        }

        /// <summary>The notes alive after the last <see cref="Update"/>.</summary>
        public int Count => _count;

        /// <summary>The notes alive, oldest first.</summary>
        public TrailNote this[int index]
        {
            get
            {
                if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
                return _ring[(_oldest + index) % _ring.Length];
            }
        }

        /// <summary>Takes new numbers; the notes alive stay (the newest ones, when there is room for fewer now).</summary>
        public void Configure(TrailSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            var capacity = Math.Max(1, settings.MaxNotes);
            if (capacity == _ring.Length) return;
            var ring = new TrailNote[capacity];
            var keep = settings.MaxNotes <= 0 ? 0 : Math.Min(_count, capacity);
            for (var i = 0; i < keep; i++) ring[i] = this[_count - keep + i];
            _ring = ring;
            _oldest = 0;
            _count = keep;
        }

        /// <summary>Forgets every note and the last place.</summary>
        public void Clear()
        {
            _oldest = 0;
            _count = 0;
            _carry = 0.0;
            _hasLast = false;
        }

        /// <summary>
        /// One drawn frame at <paramref name="nowSec"/>: forgets the notes that have lived out their life and, when <paramref name="emit"/>, gives the hand at (<paramref name="x"/>, <paramref name="y"/>)
        /// (viewport units) the notes that its <paramref name="movement01"/> (0 = still, 1 = as much as can be) earns since the last call. Returns how many were born.
        /// </summary>
        public int Update(double nowSec, float x, float y, bool emit, float movement01, int tag)
        {
            var dt = _hasLast ? Math.Min(LongestStepSec, Math.Max(0.0, nowSec - _lastSec)) : 0.0;
            var fromX = _hasLast ? _lastX : x;
            var fromY = _hasLast ? _lastY : y;
            _hasLast = true;
            _lastSec = nowSec;
            _lastX = x;
            _lastY = y;
            Prune(nowSec);

            var rate = _settings.NotesPerSec * Math.Max(0f, Math.Min(1f, movement01));
            if (!emit || _settings.MaxNotes <= 0 || !(rate > 0f))
            {
                _carry = 0.0;
                return 0;
            }
            _carry += rate * dt;
            var born = Math.Min((int)_carry, _settings.MaxNotes);
            _carry -= (int)_carry;
            for (var k = 1; k <= born; k++)
            {
                var along = k / (double)born;
                Push(nowSec - dt * (1.0 - along), Lerp(fromX, x, (float)along), Lerp(fromY, y, (float)along), tag);
            }
            return born;
        }

        void Prune(double nowSec)
        {
            while (_count > 0 && nowSec - _ring[_oldest].BirthSec >= _settings.LifeSec)
            {
                _oldest = (_oldest + 1) % _ring.Length;
                _count--;
            }
        }

        // The time, the place and the colour are given, the scatter, the drift and the tilt are made here.
        void Push(double birthSec, float x, float y, int tag)
        {
            var angle = _random.NextDouble() * 2.0 * Math.PI;
            var radius = Math.Sqrt(_random.NextDouble()) * _settings.Size * ScatterInSizes;
            var driftAngle = (_random.NextDouble() * 2.0 - 1.0) * MaxDriftAngle;
            var driftSpeed = MinDriftSpeed + _random.NextDouble() * (MaxDriftSpeed - MinDriftSpeed);
            var tilt = (_random.NextDouble() * 2.0 - 1.0) * MaxTilt;
            var note = new TrailNote(
                birthSec,
                x + (float)(Math.Cos(angle) * radius),
                y + (float)(Math.Sin(angle) * radius),
                (float)(Math.Sin(driftAngle) * driftSpeed),
                (float)(Math.Cos(driftAngle) * driftSpeed),
                (float)tilt,
                tag);
            if (_count == _ring.Length)
            {
                _ring[_oldest] = note;
                _oldest = (_oldest + 1) % _ring.Length;
                return;
            }
            _ring[(_oldest + _count) % _ring.Length] = note;
            _count++;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
