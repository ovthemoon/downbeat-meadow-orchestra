namespace Downbeat.Core
{
    /// <summary>
    /// A size floor in screen pixels for far particles: a seed 0.03 m across is under 2 pixels at 12 m and a mere flicker (and a pixel or two at a render scale under 1). The same arithmetic is in
    /// MeadowParticle.shader (the vertex stage moves the corners of the particle's quad out from its middle by this factor), so what the particle looks like does not depend on a CPU pass.
    /// </summary>
    public static class ParticleSizeFloor
    {
        /// <summary>
        /// The factor to multiply the size of a particle by: 1 when it already covers <paramref name="minPixels"/> or more, else what brings it to exactly that many pixels across. Continuous (no pop when a
        /// particle drifts past the distance where it begins) and only ever 1 or more.
        /// </summary>
        /// <param name="projectionM11">The vertical scale of the camera's projection: 1 / tan(half the vertical field of view).</param>
        /// <param name="screenHeightPixels">The height in pixels of the picture that is drawn (the render target, so a render scale below 1 counts).</param>
        public static float Scale(float sizeMetres, float viewDepthMetres, float projectionM11, float screenHeightPixels, float minPixels)
        {
            if (!(minPixels > 0f) || !(sizeMetres > 0f) || !(viewDepthMetres > 0f) || !(projectionM11 > 0f) || !(screenHeightPixels > 0f)) return 1f;
            var pixels = sizeMetres * projectionM11 * 0.5f * screenHeightPixels / viewDepthMetres;
            return pixels >= minPixels ? 1f : minPixels / pixels;
        }
    }
}
