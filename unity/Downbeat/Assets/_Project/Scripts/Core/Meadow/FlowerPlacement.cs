using System;

namespace Downbeat.Core
{
    /// <summary>One flower of the meadow: its species, where it stands on the ground (metres) and a size factor around 1.</summary>
    public readonly struct FlowerInstance : IEquatable<FlowerInstance>
    {
        public FlowerSpecies Species { get; }
        public float X { get; }
        public float Z { get; }
        public float Scale { get; }
        /// <summary>How tall the stem stands relative to its species (about 0.5 .. 1.15): many flowers stand low, partly hidden in the grass.</summary>
        public float Height { get; }

        public FlowerInstance(FlowerSpecies species, float x, float z, float scale, float height = 1f)
        {
            Species = species; X = x; Z = z; Scale = scale; Height = height;
        }

        public bool Equals(FlowerInstance other) => Species == other.Species && X == other.X && Z == other.Z && Scale == other.Scale && Height == other.Height;
        public override bool Equals(object obj) => obj is FlowerInstance other && Equals(other);
        public override int GetHashCode() => (((int)Species * 397 ^ X.GetHashCode()) * 397 ^ Z.GetHashCode()) * 397 ^ Scale.GetHashCode();
        public override string ToString() => $"{Species} ({X:0.00}, {Z:0.00}) x{Scale:0.00}";
    }

    /// <summary>Where the flowers stand: sown evenly over the picture (so that every part of the screen has some), each species by the mix of its zone.</summary>
    public static class FlowerPlacement
    {
        public const float MinHeight = 0.5f;
        public const float MaxHeight = 1.15f;
        public const float MinScale = 0.75f;
        public const float MaxScale = 1.3f;
        public const float DefaultBlend = 0.1f;
        const float SpeciesJitter = 0.045f;      // the border between two species is ragged, not a line
        const int MaxAttempts = 400;
        const float DriftWavelength = 3.4f;
        const float DriftFineWavelength = 1.3f;
        const int DriftSeedStride = 4099;

        /// <summary>
        /// <paramref name="count"/> flowers of the playing field of <paramref name="layout"/>, deterministic for a seed. Each flower is one of the three species in turn by chance
        /// (a third each); a position is drawn evenly over the picture (so far flowers, small on the screen, are as many per screen area as near ones) and kept with the
        /// probability that <see cref="SpeciesMap.Weight"/> gives its species at that place across the picture (moved a little by chance, so that two zones meet in a ragged border,
        /// not a line) and with a probability that follows a slowly changing noise of that species (<see cref="Drift"/>): the flowers stand in drifts with bare meadow between
        /// them. Heights vary, many low.
        /// </summary>
        public static FlowerInstance[] Generate(int seed, int count, MeadowLayout layout, float blend = DefaultBlend)
        {
            if (count <= 0) return new FlowerInstance[0];
            var flowers = new FlowerInstance[count];
            var state = Mix((uint)seed * 2654435761u + 0x9E3779B9u);
            for (var i = 0; i < count; i++)
            {
                var species = (FlowerSpecies)Math.Min(SpeciesMap.Count - 1, (int)(Next(ref state) * SpeciesMap.Count));
                float x = 0f, depth = 0f;
                for (var attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    var across = Next(ref state);      // 0 left .. 1 right of the picture
                    var row = Next(ref state);         // 0 far end of the field .. 1 near end, evenly over the screen rows
                    var wobble = (Next(ref state) - 0.5f) * 2f * SpeciesJitter;
                    var inZone = Next(ref state);
                    var inDrift = Next(ref state);
                    depth = layout.HandToField(0.5f, row).Z;
                    x = (across - 0.5f) * layout.FieldWidth * depth / layout.FarZ;
                    if (inZone <= SpeciesMap.Weight(species, Clamp01(across + wobble), blend) && inDrift <= DriftProbability(species, x, depth, seed)) break;
                }
                var size = Next(ref state);
                var height = Next(ref state);
                flowers[i] = new FlowerInstance(species, x, depth, MinScale + (MaxScale - MinScale) * size, MinHeight + (MaxHeight - MinHeight) * height * height);
            }
            return flowers;
        }

        /// <summary>0 in the bare meadow between two drifts, 1 in the middle of one; each species has its own drifts.</summary>
        public static float Drift(FlowerSpecies species, float x, float z, int seed)
        {
            var s = seed + (int)species * DriftSeedStride;
            var n = 0.5f + 0.5f * (0.65f * ValueNoise.Sample(x / DriftWavelength, z / DriftWavelength, s) + 0.35f * ValueNoise.Sample(x / DriftFineWavelength + 11.3f, z / DriftFineWavelength - 5.7f, s + 17));
            var u = (n - 0.42f) / 0.4f;
            u = u < 0f ? 0f : (u > 1f ? 1f : u);
            return u * u * (3f - 2f * u);
        }

        static float DriftProbability(FlowerSpecies species, float x, float z, int seed) => 0.03f + 0.97f * Drift(species, x, z, seed);

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float Next(ref uint state)
        {
            state = Mix(state + 0x6D2B79F5u);
            return (state & 0xFFFFFFu) / (float)0x1000000;
        }

        static uint Mix(uint v)
        {
            unchecked
            {
                v = (v ^ (v >> 16)) * 0x85EBCA6Bu;
                v = (v ^ (v >> 13)) * 0xC2B2AE35u;
                return v ^ (v >> 16);
            }
        }
    }
}
