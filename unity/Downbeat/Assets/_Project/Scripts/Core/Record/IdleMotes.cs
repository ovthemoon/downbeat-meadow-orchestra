using System;

namespace Downbeat.Core
{
    public enum MoteKind { Firefly = 0, Petal = 1 }

    /// <summary>
    /// One mote at one moment. Places and sizes are in screen heights: <see cref="X"/> runs from 0 to the aspect ratio of the screen, <see cref="Y"/> from 0 (bottom) to 1 (top), so the same
    /// numbers fit any resolution. <see cref="Alpha"/> already has the peak brightness, the slow breathing and the dimming of a performance in it.
    /// </summary>
    public struct MoteState
    {
        public float X;
        public float Y;
        /// <summary>Across the quad it is drawn with.</summary>
        public float Size;
        public float Alpha;
        /// <summary>Radians; only a petal turns (slowly). The value jumps by a whole turn when it wraps, which looks the same.</summary>
        public float Angle;
        public MoteKind Kind;
        /// <summary>Petals: which flower colour (0 bellflower, 1 dandelion, 2 poppy). Fireflies: 0.</summary>
        public int Variant;
    }

    /// <summary>
    /// The slow life of the record screen while nobody plays: fireflies that rise and petals that fall behind the panels, a few percent of the screen height per second, swaying a little,
    /// fading in and out at the ends of their lives. Pure and seeded: the same seed gives the same motes, and a mote is a function of the clock alone (no state per mote, no random numbers at
    /// run time), so a test can pin any moment. The clock is a double and is wrapped to a fraction of a period in double precision before it becomes a float, so after days of uptime the motes
    /// still move in even steps. While a performance is on the motes step back to a fraction of their brightness (<see cref="Advance"/>) and come back when the meadow is idle again.
    /// </summary>
    public sealed class IdleMoteField
    {
        public const int MaxCount = 64;
        /// <summary>The share of a life over which a mote fades in, and again over which it fades out.</summary>
        public const float FadeFraction = 0.25f;
        /// <summary>Seconds a hand needs to take the motes down to their performing level, and the meadow to bring them back.</summary>
        public const float FadeDownSec = 1.5f;
        public const float FadeUpSec = 4f;
        /// <summary>A frame that took longer than this (a stall, a suspended process) fades the motes only this far.</summary>
        public const float MaxFadeStepSec = 0.25f;
        public const float MaxSpeed = 0.2f;

        const int PetalEvery = 4;                          // every fourth mote is a petal
        const int FlowerColours = Parts.Columns;           // a petal is the colour of one of the three flowers, which are the three columns of the ground row
        const float SideMargin = 0.12f;                    // a mote is born at least this far (screen heights) from the left and right edges: the sway and the drift keep it on the screen
        const float EdgeMargin = 0.04f;                    // and from the top and bottom
        const float MinTravel = 0.25f, MaxTravel = 0.5f;   // the glide of one life, in screen heights
        const float MinSpeedShare = 0.7f, MaxSpeedShare = 1.3f;
        const float MinSway = 0.012f, MaxSway = 0.03f;
        const float MinSwayPeriod = 9f, MaxSwayPeriod = 17f;
        const float MaxDrift = 0.04f;
        const float MinPulsePeriod = 6f, MaxPulsePeriod = 10f;
        const float PulseDepth = 0.15f;                    // the slow breathing of the brightness
        const float MinFireflySize = 0.024f, MaxFireflySize = 0.036f;
        const float MinPetalSize = 0.022f, MaxPetalSize = 0.034f;
        const float MinTurnRate = 0.08f, MaxTurnRate = 0.2f;   // radians per second
        const float TwoPi = (float)(2.0 * Math.PI);

        readonly Mote[] _motes;
        readonly float _peakAlpha;
        readonly float _performingLevel;
        double _lastClock;
        bool _hasClock;
        float _progress;   // 0 = idle .. 1 = performing

        readonly struct Mote
        {
            public Mote(MoteKind kind, int variant, float size, float travel, double lifeSec, double phaseSec, float swayAmount, float swayPeriod, float swayPhase, float drift,
                float pulsePeriod, float pulsePhase, float turnRate, float turnPhase, uint salt)
            {
                Kind = kind; Variant = variant; Size = size; Travel = travel; LifeSec = lifeSec; PhaseSec = phaseSec; SwayAmount = swayAmount; SwayPeriod = swayPeriod; SwayPhase = swayPhase;
                Drift = drift; PulsePeriod = pulsePeriod; PulsePhase = pulsePhase; TurnRate = turnRate; TurnPhase = turnPhase; Salt = salt;
            }

            public readonly MoteKind Kind;
            public readonly int Variant;
            public readonly float Size;
            public readonly float Travel;
            public readonly double LifeSec;
            public readonly double PhaseSec;
            public readonly float SwayAmount;
            public readonly float SwayPeriod;
            public readonly float SwayPhase;
            public readonly float Drift;
            public readonly float PulsePeriod;
            public readonly float PulsePhase;
            public readonly float TurnRate;
            public readonly float TurnPhase;
            public readonly uint Salt;
        }

        /// <param name="count">0 for none, at most <see cref="MaxCount"/>.</param>
        /// <param name="speed">The glide of a mote in screen heights per second (each one has its own 0.7 to 1.3 times that).</param>
        /// <param name="peakAlpha">The brightest a mote gets (0..1).</param>
        /// <param name="performingLevel">The share of their brightness the motes keep while a performance is on (0..1).</param>
        public IdleMoteField(int count, uint seed, float speed, float peakAlpha, float performingLevel)
        {
            if (count < 0 || count > MaxCount) throw new ArgumentOutOfRangeException(nameof(count), count, $"0..{MaxCount}");
            if (!(speed > 0f && speed <= MaxSpeed)) throw new ArgumentOutOfRangeException(nameof(speed), speed, $"0..{MaxSpeed} screen heights per second");
            if (!(peakAlpha >= 0f && peakAlpha <= 1f)) throw new ArgumentOutOfRangeException(nameof(peakAlpha), peakAlpha, "0..1");
            if (!(performingLevel >= 0f && performingLevel <= 1f)) throw new ArgumentOutOfRangeException(nameof(performingLevel), performingLevel, "0..1");
            _peakAlpha = peakAlpha;
            _performingLevel = performingLevel;
            _motes = new Mote[count];
            for (var i = 0; i < count; i++) _motes[i] = MakeMote(i, seed, speed);
            Level = 1f;
        }

        public int Count => _motes.Length;

        /// <summary>How much of their brightness the motes have now: 1 when the meadow is idle, the performing level while a performance is on, and smoothly in between.</summary>
        public float Level { get; private set; }

        /// <summary>The brightness over one life (0 at both ends, 1 in the middle): a smooth step up over <see cref="FadeFraction"/> and the same step down.</summary>
        public static float LifeEnvelope(float lifeShare)
        {
            var edge = Math.Min(lifeShare, 1f - lifeShare) / FadeFraction;
            return SmoothStep(edge < 0f ? 0f : (edge > 1f ? 1f : edge));
        }

        /// <summary>Once per frame: takes the motes down while <paramref name="performing"/> and brings them back when it is not. The clock is the same seconds <see cref="Evaluate"/> gets.</summary>
        public void Advance(double clockSeconds, bool performing)
        {
            if (!_hasClock) { _lastClock = clockSeconds; _hasClock = true; }
            var step = clockSeconds - _lastClock;
            _lastClock = clockSeconds;
            step = step < 0.0 ? 0.0 : (step > MaxFadeStepSec ? MaxFadeStepSec : step);
            var progress = _progress + (float)(performing ? step / FadeDownSec : -step / FadeUpSec);
            _progress = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            Level = 1f + (_performingLevel - 1f) * SmoothStep(_progress);
        }

        /// <summary>Fills <paramref name="into"/> (at least <see cref="Count"/> long) with the motes at <paramref name="clockSeconds"/>, for a screen <paramref name="aspect"/> times as wide as it is tall. Allocates nothing.</summary>
        public void Evaluate(double clockSeconds, float aspect, MoteState[] into)
        {
            if (into == null || into.Length < _motes.Length) throw new ArgumentException("the array must hold every mote", nameof(into));
            var width = Math.Max(0f, aspect - 2f * SideMargin);
            for (var i = 0; i < _motes.Length; i++)
            {
                ref readonly var m = ref _motes[i];
                var local = clockSeconds / m.LifeSec + m.PhaseSec / m.LifeSec;
                var cycle = Math.Floor(local);
                var share = (float)(local - cycle);
                var cycleSalt = unchecked(m.Salt + (uint)(long)cycle * 0x9E3779B9u);

                var baseX = SideMargin + Random(cycleSalt, 1u) * width;
                var along = Random(cycleSalt, 2u) * (1f - 2f * EdgeMargin - m.Travel);
                var rises = m.Kind == MoteKind.Firefly;
                var y = rises ? EdgeMargin + along + m.Travel * share : 1f - EdgeMargin - along - m.Travel * share;
                var sway = m.SwayAmount * (float)Math.Sin(TwoPi * Wrapped(clockSeconds / m.SwayPeriod + m.SwayPhase));
                var pulse = 1f - PulseDepth * (0.5f + 0.5f * (float)Math.Sin(TwoPi * Wrapped(clockSeconds / m.PulsePeriod + m.PulsePhase)));

                ref var s = ref into[i];
                s.X = baseX + sway + m.Drift * share;
                s.Y = y;
                s.Size = m.Size;
                s.Alpha = _peakAlpha * LifeEnvelope(share) * pulse * Level;
                s.Angle = m.Kind == MoteKind.Petal ? TwoPi * Wrapped(clockSeconds * m.TurnRate / TwoPi + m.TurnPhase) : 0f;
                s.Kind = m.Kind;
                s.Variant = m.Variant;
            }
        }

        static Mote MakeMote(int index, uint seed, float speed)
        {
            var salt = BreezeHash.Hash(seed * 0x85EBCA6Bu + (uint)index * 0x9E3779B1u + 0x27D4EB2Fu);
            var petal = index % PetalEvery == PetalEvery - 1;
            var travel = Between(salt, 10u, MinTravel, MaxTravel);
            var glide = speed * Between(salt, 11u, MinSpeedShare, MaxSpeedShare);
            var life = travel / glide;
            return new Mote(
                petal ? MoteKind.Petal : MoteKind.Firefly,
                petal ? index / PetalEvery % FlowerColours : 0,
                petal ? Between(salt, 12u, MinPetalSize, MaxPetalSize) : Between(salt, 12u, MinFireflySize, MaxFireflySize),
                travel,
                life,
                Random(salt, 13u) * life,
                Between(salt, 14u, MinSway, MaxSway),
                Between(salt, 15u, MinSwayPeriod, MaxSwayPeriod),
                Random(salt, 16u),
                Between(salt, 17u, -MaxDrift, MaxDrift),
                Between(salt, 18u, MinPulsePeriod, MaxPulsePeriod),
                Random(salt, 19u),
                Between(salt, 20u, MinTurnRate, MaxTurnRate) * (Random(salt, 21u) < 0.5f ? -1f : 1f),
                Random(salt, 22u),
                salt);
        }

        static float Between(uint salt, uint stream, float min, float max) => min + (max - min) * Random(salt, stream);

        static float Random(uint salt, uint stream) => BreezeHash.Unit(BreezeHash.Hash(salt ^ (stream * 0x9E3779B1u)));

        /// <summary>The fraction of a turn, in [0, 1), worked out in double precision.</summary>
        static float Wrapped(double turns) => (float)(turns - Math.Floor(turns));

        static float SmoothStep(float t) => t * t * (3f - 2f * t);
    }
}
