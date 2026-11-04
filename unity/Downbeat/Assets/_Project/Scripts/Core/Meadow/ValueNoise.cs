namespace Downbeat.Core
{
    /// <summary>Smooth 2D value noise in [-1, 1] from integer lattice hashes: the same numbers on every platform. Shared by the ground and the grass fields.</summary>
    public static class ValueNoise
    {
        public static float Sample(float x, float z, int seed)
        {
            var x0 = (int)System.Math.Floor(x);
            var z0 = (int)System.Math.Floor(z);
            var fx = Ease(x - x0);
            var fz = Ease(z - z0);
            var a = Lattice(x0, z0, seed);
            var b = Lattice(x0 + 1, z0, seed);
            var c = Lattice(x0, z0 + 1, seed);
            var d = Lattice(x0 + 1, z0 + 1, seed);
            var top = a + (b - a) * fx;
            var bottom = c + (d - c) * fx;
            return top + (bottom - top) * fz;
        }

        static float Ease(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Lattice(int x, int z, int seed)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)z * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0xFFFFFF * 2f - 1f;
            }
        }
    }
}
