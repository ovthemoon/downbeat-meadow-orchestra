namespace Downbeat.Core
{
    /// <summary>
    /// An integer hash that gives the same numbers on every machine and in the shaders (MeadowBreeze.hlsl has the same few lines): gusts and cloud patches are picked from it, so the
    /// wind needs no state and no random numbers at run time. It is "lowbias32" (a well-mixed 32-bit finaliser).
    /// </summary>
    public static class BreezeHash
    {
        public static uint Hash(uint k)
        {
            unchecked
            {
                k ^= k >> 16;
                k *= 0x7feb352du;
                k ^= k >> 15;
                k *= 0x846ca68bu;
                k ^= k >> 16;
                return k;
            }
        }

        /// <summary>The hash as a number in [0, 1) (24 bits, exact in a float).</summary>
        public static float Unit(uint k) => (Hash(k) >> 8) * (1f / 16777216f);
    }

    /// <summary>
    /// Smooth value noise for clouds and their shadows. The lattice repeats every 256 cells (and the second octave, at twice the frequency, every 512 of its own), so the position of a
    /// pattern can be wrapped in double precision on the CPU and the GPU only ever sees small numbers: after days of uptime a cloud still moves in even steps.
    /// </summary>
    public static class CloudNoise
    {
        public const int Period = 256;
        const uint LatticeX = 73856093u;
        const uint LatticeZ = 19349663u;
        const float SecondOctaveOffsetX = 17.3f;
        const float SecondOctaveOffsetZ = 5.1f;

        /// <summary>Two octaves in [0, 1], at lattice coordinates (x, z).</summary>
        public static float Fbm(float x, float z)
        {
            var coarse = Value(x, z, Period);
            var fine = Value(x * 2f + SecondOctaveOffsetX, z * 2f + SecondOctaveOffsetZ, Period * 2);
            return 0.7f * coarse + 0.3f * fine;
        }

        static float Value(float x, float z, int period)
        {
            var fx = (float)System.Math.Floor(x);
            var fz = (float)System.Math.Floor(z);
            var tx = Smooth(x - fx);
            var tz = Smooth(z - fz);
            var ix = (int)fx;
            var iz = (int)fz;
            var a = Lattice(ix, iz, period);
            var b = Lattice(ix + 1, iz, period);
            var c = Lattice(ix, iz + 1, period);
            var d = Lattice(ix + 1, iz + 1, period);
            var top = a + (b - a) * tx;
            var bottom = c + (d - c) * tx;
            return top + (bottom - top) * tz;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static float Lattice(int ix, int iz, int period)
        {
            unchecked
            {
                var wx = (uint)(ix & (period - 1));
                var wz = (uint)(iz & (period - 1));
                return BreezeHash.Unit(wx * LatticeX ^ wz * LatticeZ);
            }
        }
    }
}
