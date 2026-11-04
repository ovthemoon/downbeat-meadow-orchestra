using System;
using System.Runtime.InteropServices;

namespace Downbeat.Core
{
    public enum WindWaveKind
    {
        /// <summary>A front that runs along x: the same on every row of depth. With a positive speed it runs towards +x and pushes that way; with a negative speed towards -x, pushing that way.</summary>
        Sweep = 0,
        /// <summary>A ring that spreads from a point on the ground.</summary>
        Radial = 1,
    }

    /// <summary>
    /// One gust: where it started, when, how strong, how fast it travels and for how long it lives. <paramref name="glow"/> makes the grass tips it passes sparkle. <paramref name="widthScale"/> makes its front
    /// that many times as wide as the field's wave width (a long wave of the low string, a wide ring of the hand drum); the strength on the front itself stays the amplitude.
    /// </summary>
    public readonly struct WindWave
    {
        public float OriginX { get; }
        public float OriginZ { get; }
        /// <summary>Audio or scene time in seconds at which the wave started.</summary>
        public double StartTime { get; }
        public float Amplitude { get; }
        public float Speed { get; }
        public float LifeSec { get; }
        public WindWaveKind Kind { get; }
        public float Glow { get; }
        /// <summary>How many times as wide as <see cref="WindField.WaveWidth"/> the front of this wave is (1 = the usual width).</summary>
        public float WidthScale { get; }

        public WindWave(float originX, float originZ, double startTime, float amplitude, float speed, float lifeSec, WindWaveKind kind, float glow, float widthScale = 1f)
        {
            if (!(widthScale > 0f)) throw new ArgumentOutOfRangeException(nameof(widthScale), "must be > 0");
            OriginX = originX; OriginZ = originZ; StartTime = startTime; Amplitude = amplitude; Speed = speed; LifeSec = lifeSec; Kind = kind; Glow = glow; WidthScale = widthScale;
        }
    }

    /// <summary>
    /// A wave as the shader reads it: three float4s, with the age instead of the start time so that the shader never has to deal with a double.
    /// Memory layout: (originX, originZ, speed, kind) (amplitude, lifeSec, glow, age) (widthScale, 0, 0, 0) = 48 bytes, the same order as the HLSL struct MeadowWave in MeadowWind.hlsl.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WindWaveGpu
    {
        public float OriginX;
        public float OriginZ;
        public float Speed;
        public float Kind;
        public float Amplitude;
        public float LifeSec;
        public float Glow;
        public float Age;
        public float WidthScale;
        /// <summary>Rounds the third float4 up (the shader reads (widthScale, 0, 0, 0)); always 0.</summary>
        public float Spare0;
        public float Spare1;
        public float Spare2;

        public const int SizeBytes = 48;
    }

    /// <summary>
    /// The gusts that cross the meadow. A fixed number of waves (the oldest is replaced when it is full). The strength of a wave at a point is
    /// <c>amplitude * exp(-d^2 / width^2) * max(0, 1 - age / life)</c> where d is the distance from the front: for a sweep <c>x - (originX + speed * age)</c>, for a ring
    /// <c>|p - origin| - speed * age</c>, and width is the field's wave width times the wave's own <see cref="WindWave.WidthScale"/>. <see cref="SampleBend"/> and <see cref="SampleGlow"/> are the same formulas the grass shader evaluates (they are the CPU's
    /// way of asking what the grass is doing). Not thread safe.
    /// </summary>
    public sealed class WindField
    {
        readonly WindWave[] _waves;
        readonly float _widthSquared;
        int _count;

        public WindField(int capacity, float waveWidth)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (!(waveWidth > 0f)) throw new ArgumentOutOfRangeException(nameof(waveWidth));
            _waves = new WindWave[capacity];
            WaveWidth = waveWidth;
            _widthSquared = waveWidth * waveWidth;
        }

        public int Capacity => _waves.Length;
        public int Count => _count;
        public float WaveWidth { get; }

        public void Add(in WindWave wave)
        {
            if (_count < _waves.Length)
            {
                _waves[_count++] = wave;
                return;
            }

            var oldest = 0;
            for (var i = 1; i < _waves.Length; i++)
            {
                if (_waves[i].StartTime < _waves[oldest].StartTime) oldest = i;
            }
            _waves[oldest] = wave;
        }

        /// <summary>Takes back the waves that start after <paramref name="startTime"/> (a note that was asked for and then cancelled: the piece was ended before it was heard); returns how many.</summary>
        public int RemoveStartingAfter(double startTime)
        {
            var kept = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_waves[i].StartTime > startTime) continue;
                _waves[kept++] = _waves[i];
            }
            var removed = _count - kept;
            _count = kept;
            return removed;
        }

        /// <summary>How many waves start after <paramref name="startTime"/> (they are asked for, not started yet).</summary>
        public int CountStartingAfter(double startTime)
        {
            var count = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_waves[i].StartTime > startTime) count++;
            }
            return count;
        }

        /// <summary>Copies the waves that are alive at <paramref name="now"/> (started, not yet at the end of their life) into <paramref name="destination"/>; returns how many.</summary>
        public int CopyActive(double now, WindWaveGpu[] destination)
        {
            var copied = 0;
            for (var i = 0; i < _count && copied < destination.Length; i++)
            {
                var wave = _waves[i];
                var age = now - wave.StartTime;
                if (age < 0.0 || age >= wave.LifeSec) continue;
                destination[copied++] = new WindWaveGpu
                {
                    OriginX = wave.OriginX, OriginZ = wave.OriginZ, Speed = wave.Speed, Kind = (float)wave.Kind,
                    Amplitude = wave.Amplitude, LifeSec = wave.LifeSec, Glow = wave.Glow, Age = (float)age, WidthScale = wave.WidthScale,
                };
            }
            return copied;
        }

        /// <summary>How strongly the wind bends the grass at (x, z): the sum of the strengths of all live waves (always >= 0).</summary>
        public float SampleBend(float x, float z, double now)
        {
            var sum = 0f;
            for (var i = 0; i < _count; i++)
            {
                if (TryProfile(_waves[i], x, z, now, out var profile)) sum += _waves[i].Amplitude * profile;
            }
            return sum;
        }

        /// <summary>
        /// Which way, and how hard, the wind pushes at (x, z): the sum over the live waves of their strength along their direction (a sweep along +x, a ring away from its
        /// centre). The length of the vector for one wave is <see cref="SampleBend"/>. Particles drift with it.
        /// </summary>
        public FieldPoint SampleVector(float x, float z, double now)
        {
            float sumX = 0f, sumZ = 0f;
            for (var i = 0; i < _count; i++)
            {
                var wave = _waves[i];
                if (!TryProfile(wave, x, z, now, out var profile)) continue;
                var strength = wave.Amplitude * profile;
                if (wave.Kind == WindWaveKind.Sweep)
                {
                    sumX += wave.Speed < 0f ? -strength : strength;
                    continue;
                }
                var dx = x - wave.OriginX;
                var dz = z - wave.OriginZ;
                var length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length < 1e-4f) continue;
                sumX += strength * dx / length;
                sumZ += strength * dz / length;
            }
            return new FieldPoint(sumX, sumZ);
        }

        /// <summary>How much the tips sparkle at (x, z): the same envelope as the bend, weighted by each wave's glow instead of its amplitude.</summary>
        public float SampleGlow(float x, float z, double now)
        {
            var sum = 0f;
            for (var i = 0; i < _count; i++)
            {
                if (_waves[i].Glow > 0f && TryProfile(_waves[i], x, z, now, out var profile)) sum += _waves[i].Glow * profile;
            }
            return sum;
        }

        /// <summary>exp(-d^2 / (width x the wave's width scale)^2) * (1 - age / life), or false when the wave is not alive.</summary>
        bool TryProfile(in WindWave wave, float x, float z, double now, out float profile)
        {
            profile = 0f;
            var age = now - wave.StartTime;
            if (age < 0.0 || age >= wave.LifeSec) return false;
            var travelled = wave.Speed * (float)age;
            float distance;
            if (wave.Kind == WindWaveKind.Sweep)
            {
                distance = x - (wave.OriginX + travelled);
            }
            else
            {
                var dx = x - wave.OriginX;
                var dz = z - wave.OriginZ;
                distance = (float)Math.Sqrt(dx * dx + dz * dz) - travelled;
            }
            var fade = 1f - (float)(age / wave.LifeSec);
            profile = (float)Math.Exp(-distance * distance / (_widthSquared * wave.WidthScale * wave.WidthScale)) * fade;
            return true;
        }
    }
}
