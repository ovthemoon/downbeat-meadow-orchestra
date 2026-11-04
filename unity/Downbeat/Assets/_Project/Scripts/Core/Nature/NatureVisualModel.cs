using System;

namespace Downbeat.Core
{
    /// <summary>
    /// What the picture does with the performance energy, as numbers: where the sky is on its arc, how many fireflies there are, how bright the far hills' haze is. The sky
    /// and the fireflies glide towards the energy (a note adds a few hundredths; the picture should not jump with every note), the haze pulse is a flash on a drum beat
    /// that fades in a fraction of a second. Pure logic: <c>NatureVisuals</c> in the meadow assembly hands the numbers to the shaders and particle systems.
    /// </summary>
    public sealed class NatureVisualModel
    {
        readonly float _smoothSec;
        readonly float _pulseStrength;
        readonly float _pulseSec;
        readonly float _skyLow;
        readonly float _skyHigh;

        /// <param name="skyLow">The energy up to which the sky stays at the blue hour.</param>
        /// <param name="skyHigh">The energy from which it is at the sunset (see <see cref="NatureDto.skyEnergyLow"/>); the defaults make the sky follow the energy one to one.</param>
        public NatureVisualModel(float smoothSec, float pulseStrength, float pulseSec, float skyLow = 0f, float skyHigh = 1f)
        {
            if (!(skyLow >= 0f && skyHigh <= 1f && skyLow < skyHigh)) throw new ArgumentOutOfRangeException(nameof(skyLow), skyLow, "0 <= low < high <= 1");
            if (!(smoothSec >= 0f)) throw new ArgumentOutOfRangeException(nameof(smoothSec), smoothSec, "must not be negative");
            if (!(pulseStrength >= 0f && pulseStrength <= 1f)) throw new ArgumentOutOfRangeException(nameof(pulseStrength), pulseStrength, "0..1");
            if (!(pulseSec > 0f)) throw new ArgumentOutOfRangeException(nameof(pulseSec), pulseSec, "must be positive");
            _smoothSec = smoothSec;
            _pulseStrength = pulseStrength;
            _pulseSec = pulseSec;
            _skyLow = skyLow;
            _skyHigh = skyHigh;
        }

        /// <summary>Where the sky wants to be for an energy: the blue hour (0) up to the low energy, the sunset (1) from the high one, straight between.</summary>
        public float SkyFor(float energy) => Clamp01((Clamp01(energy) - _skyLow) / (_skyHigh - _skyLow));

        /// <summary>Where the sky is on its arc (0 blue hour, 0.5 playing dusk, 1 sunset).</summary>
        public float SkyT { get; private set; }
        /// <summary>0..1: scales the number of fireflies.</summary>
        public float FireflyLevel { get; private set; }
        /// <summary>0..<c>pulseStrength</c>: extra haze brightness on the far hills.</summary>
        public float HillPulse { get; private set; }

        /// <param name="energy">The performance energy, 0..1.</param>
        /// <param name="level">Its level, 0..3: the fireflies come with level 1.</param>
        /// <param name="awake">False while nobody is there (Idle): the sky and the fireflies go back to rest whatever the number says.</param>
        public void Update(float dt, float energy, int level, bool awake)
        {
            if (!(dt > 0f)) return;
            var e = awake ? Clamp01(energy) : 0f;
            var follow = _smoothSec <= 0f ? 1f : 1f - (float)Math.Exp(-dt / _smoothSec);
            SkyT += (SkyFor(e) - SkyT) * follow;
            FireflyLevel += ((level >= 1 ? e : 0f) - FireflyLevel) * follow;
            HillPulse = Math.Max(0f, HillPulse - _pulseStrength / _pulseSec * dt);
        }

        /// <summary>A drum was heard: the haze flashes to full strength and fades from there.</summary>
        public void OnHillBeat() => HillPulse = _pulseStrength;

        static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }
}
