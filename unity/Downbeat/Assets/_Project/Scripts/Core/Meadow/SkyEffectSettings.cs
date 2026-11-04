using System;

namespace Downbeat.Core
{
    /// <summary>The effects a note of the top row makes in the air (see <see cref="SkyEffectsDto"/>).</summary>
    public enum SkyEffectKind
    {
        /// <summary>The harp: a ring of light that spreads from the hand.</summary>
        LightRing = 0,
        /// <summary>The recorder: dandelion seeds that rise from the hand.</summary>
        Seeds = 1,
        /// <summary>The bell: star dust that falls slowly and twinkles.</summary>
        StarDust = 2,
    }

    /// <summary>
    /// <c>meadow.skyEffects</c> as numbers: how many particles an effect makes (fewer on the Low tier and for a softer note), how long they live and how far a ring reaches. Built from content (a block that is
    /// missing gives the defaults).
    /// </summary>
    public sealed class SkyEffectSettings
    {
        /// <summary>The name of the tier that makes fewer particles (the weak graphics chip; the first tier is the full one).</summary>
        public const string LowTierName = "Low";

        const float RingLifeShare = 0.4f;   // of lifeSec: a ring of light is quick
        const float StarLifeShare = 0.75f;  // of lifeSec: star dust falls for a while, the seeds (a full lifeSec) float the longest
        const float SoftestShare = 0.5f;    // the softest note makes this share of the seeds and specks of the loudest

        SkyEffectSettings(SkyEffectsDto dto)
        {
            RingCount = dto.ringCount;
            SeedCount = dto.seedCount;
            StarCount = dto.starCount;
            LifeSec = dto.lifeSec;
            Distance = dto.distance;
            RingRadius = dto.ringRadius;
            Glow = dto.glow;
            MaxParticles = dto.maxParticles;
            LowCountScale = dto.lowCountScale;
        }

        public int RingCount { get; }
        public int SeedCount { get; }
        public int StarCount { get; }
        public float LifeSec { get; }
        public float Distance { get; }
        public float RingRadius { get; }
        public float Glow { get; }
        public int MaxParticles { get; }
        public float LowCountScale { get; }

        public static SkyEffectSettings From(SkyEffectsDto dto) => new SkyEffectSettings(dto ?? new SkyEffectsDto());

        /// <summary>The effect in the air that a note of this part makes: the harp a ring of light, the recorder seeds, the bell star dust; null for the parts of the lower row (they play on the grass).</summary>
        public static SkyEffectKind? KindFor(Part part)
        {
            switch (part)
            {
                case Part.Harp: return SkyEffectKind.LightRing;
                case Part.Recorder: return SkyEffectKind.Seeds;
                case Part.Bell: return SkyEffectKind.StarDust;
                default: return null;
            }
        }

        public static bool IsLowTier(MeadowQualityDto tier) => tier != null && string.Equals(tier.name, LowTierName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// How many particles one note makes. A ring always has all its points (a softer note makes a smaller ring, see <see cref="RadiusFor"/>); seeds and specks go from half of the count (the softest note)
        /// to the whole count (the loudest). The Low tier makes <see cref="LowCountScale"/> of that, at least one when the effect is on at all.
        /// </summary>
        public int CountFor(SkyEffectKind kind, float strength01, bool lowTier)
        {
            var strength = Clamp01(strength01);
            int configured;
            float count;
            switch (kind)
            {
                case SkyEffectKind.LightRing: configured = RingCount; count = RingCount; break;
                case SkyEffectKind.Seeds: configured = SeedCount; count = SeedCount * Lerp(SoftestShare, 1f, strength); break;
                default: configured = StarCount; count = StarCount * Lerp(SoftestShare, 1f, strength); break;
            }
            if (lowTier) count *= LowCountScale;
            var whole = (int)Math.Round(count, MidpointRounding.AwayFromZero);
            return configured > 0 && whole < 1 ? 1 : whole;
        }

        /// <summary>How long the particles of an effect live (seconds).</summary>
        public float LifeFor(SkyEffectKind kind)
        {
            switch (kind)
            {
                case SkyEffectKind.LightRing: return LifeSec * RingLifeShare;
                case SkyEffectKind.StarDust: return LifeSec * StarLifeShare;
                default: return LifeSec;
            }
        }

        /// <summary>The radius a ring of light reaches (metres): all of <see cref="RingRadius"/> for the loudest note, 0.6 of it for the softest.</summary>
        public float RadiusFor(float strength01) => RingRadius * Lerp(0.6f, 1f, Clamp01(strength01));

        static float Clamp01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;   // NaN is 0

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
