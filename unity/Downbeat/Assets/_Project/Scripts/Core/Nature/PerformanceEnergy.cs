using System;

namespace Downbeat.Core
{
    /// <summary>How the energy of a performance builds up and fades, and where its levels are (all from <c>content.json</c>).</summary>
    public sealed class EnergySettings
    {
        public EnergySettings(float riseBase, float risePerVelocity, float halfLifeSec, float level1, float level2, float level3, float hysteresis)
        {
            if (riseBase < 0f) throw new ArgumentOutOfRangeException(nameof(riseBase), riseBase, "must not be negative");
            if (risePerVelocity < 0f) throw new ArgumentOutOfRangeException(nameof(risePerVelocity), risePerVelocity, "must not be negative");
            if (riseBase + risePerVelocity <= 0f) throw new ArgumentOutOfRangeException(nameof(riseBase), "a note has to add some energy");
            if (!(halfLifeSec > 0f)) throw new ArgumentOutOfRangeException(nameof(halfLifeSec), halfLifeSec, "must be positive");
            if (!(level1 > 0f && level1 < level2 && level2 < level3 && level3 <= 1f))
                throw new ArgumentOutOfRangeException(nameof(level1), "the levels must ascend inside 0..1");
            if (hysteresis < 0f) throw new ArgumentOutOfRangeException(nameof(hysteresis), hysteresis, "must not be negative");
            if (hysteresis >= Math.Min(level1, Math.Min(level2 - level1, level3 - level2)))
                throw new ArgumentOutOfRangeException(nameof(hysteresis), hysteresis, "must be smaller than the gap between two levels");

            RiseBase = riseBase;
            RisePerVelocity = risePerVelocity;
            HalfLifeSec = halfLifeSec;
            Level1 = level1;
            Level2 = level2;
            Level3 = level3;
            Hysteresis = hysteresis;
        }

        public float RiseBase { get; }
        public float RisePerVelocity { get; }
        public float HalfLifeSec { get; }
        public float Level1 { get; }
        public float Level2 { get; }
        public float Level3 { get; }
        public float Hysteresis { get; }

        public const int MaxLevel = 3;

        /// <summary>The energy at which <paramref name="level"/> (1..3) starts.</summary>
        public float ThresholdOf(int level)
        {
            switch (level)
            {
                case 1: return Level1;
                case 2: return Level2;
                case 3: return Level3;
                default: throw new ArgumentOutOfRangeException(nameof(level), level, "1..3");
            }
        }
    }

    /// <summary>
    /// The "how alive is the meadow" value, 0..1: every note adds a little, it fades by half every <see cref="EnergySettings.HalfLifeSec"/>.
    /// Its <see cref="Level"/> (0..3) says which nature layers play; a level is left only when the value is a hysteresis below the value at which it
    /// started, so a note right at a boundary does not make the layers flicker.
    /// </summary>
    public sealed class PerformanceEnergy
    {
        readonly EnergySettings _settings;
        float _value;
        int _level;

        public PerformanceEnergy(EnergySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public float Value => _value;
        public int Level => _level;

        /// <param name="weight">1 for a normal note; less for a note that should count for less (a conducted note of a hand that is only held up).</param>
        public void AddNote(float velocity, float weight = 1f)
        {
            var v = float.IsNaN(velocity) ? 0f : Math.Max(0f, Math.Min(1f, velocity));
            var w = float.IsNaN(weight) ? 0f : Math.Max(0f, weight);
            _value = Math.Min(1f, _value + w * (_settings.RiseBase + _settings.RisePerVelocity * v));
            UpdateLevel();
        }

        public void Advance(double seconds)
        {
            if (!(seconds > 0.0)) return;
            _value = (float)(_value * Math.Pow(0.5, seconds / _settings.HalfLifeSec));
            if (_value < 1e-6f) _value = 0f;
            UpdateLevel();
        }

        /// <summary>Sets the value directly (clamped to 0..1): for tests and for the player's <c>--hold-energy</c> option (captures at a chosen level).</summary>
        public void Set(float value)
        {
            _value = Math.Max(0f, Math.Min(1f, value));
            UpdateLevel();
        }

        void UpdateLevel()
        {
            while (_level < EnergySettings.MaxLevel && _value >= _settings.ThresholdOf(_level + 1)) _level++;
            while (_level > 0 && _value < _settings.ThresholdOf(_level) - _settings.Hysteresis) _level--;
        }
    }
}
