namespace Downbeat.Core
{
    /// <summary>
    /// A picture of the cloud noise over one whole period of it, for the weak quality tier. The strong tier works the noise out for every pixel of the sky (two octaves, eight hashes each, twice
    /// for the lit side, twice for the two layers); the weak tier reads this tile instead: one texture read a layer, the same shapes, the same drift (only the position moves, never the picture).
    /// The red channel is the noise at the middle of each texel; the green channel is the same noise a little towards the sun, which is how the shader finds the lit side of a cloud
    /// (BackdropBlend.shader). The tile wraps exactly where the noise repeats, so it is read with wrap mode "repeat" and has no seam.
    /// </summary>
    public static class CloudTile
    {
        public const int TexelsPerCell = 4;
        public const int Size = CloudNoise.Period * TexelsPerCell;
        public const int Channels = 2;

        /// <summary>How far, in cells of the noise, the green channel looks towards the sun. The same number is in BackdropBlend.shader (<c>CLOUD_SUN_OFFSET</c>).</summary>
        public const float SunOffsetCells = 0.3f;

        /// <summary>The texels, row after row from the bottom row (the first <see cref="Channels"/> bytes are the red and green of the first texel).</summary>
        public static byte[] Bake()
        {
            var tile = new byte[Size * Size * Channels];
            const float TexelCells = 1f / TexelsPerCell;
            var index = 0;
            for (var z = 0; z < Size; z++)
            {
                var cellZ = (z + 0.5f) * TexelCells;
                for (var x = 0; x < Size; x++)
                {
                    var cellX = (x + 0.5f) * TexelCells;
                    tile[index++] = ToByte(CloudNoise.Fbm(cellX, cellZ));
                    tile[index++] = ToByte(CloudNoise.Fbm(cellX, cellZ + SunOffsetCells));
                }
            }
            return tile;
        }

        static byte ToByte(float value01) => (byte)(System.Math.Min(1f, System.Math.Max(0f, value01)) * 255f + 0.5f);
    }
}
