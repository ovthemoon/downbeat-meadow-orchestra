namespace Downbeat.Core
{
    /// <summary>
    /// Large-scale patterns of the grass that are the same for the CPU (tests, flowers) and the GPU (the grass shader reads them from a baked texture):
    /// where the field rises into hummocks and sinks into hollows, and how the grass thins out into the backdrop plate.
    /// </summary>
    public static class MeadowFields
    {
        const float LargeWeight = 0.62f;
        const float SmallWeight = 0.38f;
        const float SmallScale = 0.4f;
        const float SmallOffset = 31.7f;
        const float ContrastLow = 0.22f;
        const float ContrastHigh = 0.78f;
        const int SeedStride = 7919;

        /// <summary>
        /// 0 in the middle of a hollow, 1 on top of a hummock. Hummocks are about <paramref name="scaleMeters"/> across (a few metres), with a finer
        /// second layer; the result is pushed apart so that most ground is clearly one or the other. Smooth: blades next to each other get nearly the same value.
        /// </summary>
        public static float Hummock(float x, float z, int seed, float scaleMeters)
        {
            var large = ValueNoise.Sample(x / scaleMeters, z / scaleMeters, seed + SeedStride);
            var small = ValueNoise.Sample(x / (scaleMeters * SmallScale) + SmallOffset, z / (scaleMeters * SmallScale) - SmallOffset, seed + 2 * SeedStride);
            var t = 0.5f + 0.5f * (LargeWeight * large + SmallWeight * small);
            var u = (t - ContrastLow) / (ContrastHigh - ContrastLow);
            u = u < 0f ? 0f : (u > 1f ? 1f : u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>1 up to <paramref name="fromZ"/>, 0 from <paramref name="toZ"/>, a smooth fall in between: how much of the 3D grass is left at depth z.</summary>
        public static float FadeWeight(float z, float fromZ, float toZ)
        {
            if (z <= fromZ) return 1f;
            if (z >= toZ) return 0f;
            var u = (z - fromZ) / (toZ - fromZ);
            return 1f - u * u * (3f - 2f * u);
        }
    }
}
