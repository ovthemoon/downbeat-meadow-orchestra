namespace Downbeat.Core
{
    /// <summary>
    /// The shape of the ground: a gentle, deterministic rolling surface made of three octaves of smooth value noise. Pure function of
    /// (x, z, seed), so the mesh, the grass and the flowers all stand on the same ground. Heights are in [0, maxHeight]; the slope stays
    /// well under 0.25 (a quarter of a metre of rise over a metre of ground) for maxHeight up to about 1.5.
    /// </summary>
    /// <summary>
    /// The gentle undulation of the near ground (the hummocks you look across): one more octave of <see cref="ValueNoise"/>, <see cref="Height"/> metres up and down over <see cref="Wavelength"/> metres, at full
    /// strength up to <see cref="FromZ"/> and fading smoothly to nothing at <see cref="ToZ"/>, before the haze where the 3D ground hands over to the painted plate (so the hand-over line stays as it was).
    /// </summary>
    public readonly struct TerrainRelief
    {
        public float Height { get; }
        public float Wavelength { get; }
        public float FromZ { get; }
        public float ToZ { get; }

        public TerrainRelief(float height, float wavelength, float fromZ, float toZ)
        {
            Height = height; Wavelength = wavelength; FromZ = fromZ; ToZ = toZ;
        }

        public static TerrainRelief None => default;
    }

    public static class MeadowTerrain
    {
        const int ReliefSeedOffset = 7;

        const int Octaves = 3;
        static readonly float[] Wavelength = { 24f, 9f, 5f };
        static readonly float[] Amplitude = { 0.42f, 0.3f, 0.28f };
        const int SeedStride = 1013;

        /// <summary>The ground with its near undulation: the rolling ground plus <see cref="Relief"/>. The surface of the mesh, the grass, the flowers and the particles all ask this.</summary>
        public static float Height(float x, float z, int seed, float maxHeight, in TerrainRelief relief) => Height(x, z, seed, maxHeight) + Relief(x, z, seed, relief);

        /// <summary>The undulation alone at (x, z): 0 beyond <see cref="TerrainRelief.ToZ"/> and where there is none.</summary>
        public static float Relief(float x, float z, int seed, in TerrainRelief relief)
        {
            if (!(relief.Height > 0f) || !(relief.Wavelength > 0f) || z >= relief.ToZ) return 0f;
            var fade = 1f;
            if (z > relief.FromZ)
            {
                var t = (z - relief.FromZ) / (relief.ToZ - relief.FromZ);
                fade = 1f - t * t * (3f - 2f * t);
            }
            return relief.Height * fade * ValueNoise.Sample(x / relief.Wavelength, z / relief.Wavelength, seed + ReliefSeedOffset * SeedStride);
        }

        public static float Height(float x, float z, int seed, float maxHeight)
        {
            var sum = 0f;
            var norm = 0f;
            for (var i = 0; i < Octaves; i++)
            {
                sum += Amplitude[i] * ValueNoise.Sample(x / Wavelength[i], z / Wavelength[i], seed + i * SeedStride);
                norm += Amplitude[i];
            }

            var t = 0.5f + 0.5f * sum / norm;
            return (t < 0f ? 0f : (t > 1f ? 1f : t)) * maxHeight;
        }
    }
}
