namespace Downbeat.Core
{
    /// <summary>
    /// What the sky behind a cloud is multiplied by on one side of the cloud. The sky arc has a colour for the lit side of the clouds and one for the shaded side; they are used for their hue only
    /// (the plates are finished pictures: the clouds must not lift or darken their mood on average), so the colour is divided by its own brightness, mixed with white by the hue share, and
    /// multiplied by the brightness of that side. It changes once a frame at most (the sky arc moves slowly), so it is worked out here and not for every pixel of the sky (BackdropBlend.shader).
    /// </summary>
    public static class CloudTint
    {
        const float LumaRed = 0.2126f;
        const float LumaGreen = 0.7152f;
        const float LumaBlue = 0.0722f;
        const float LeastLuma = 1e-3f;

        public static (float R, float G, float B) Of(float r, float g, float b, float hueShare, float brightness)
        {
            var luma = System.Math.Max(LumaRed * r + LumaGreen * g + LumaBlue * b, LeastLuma);
            return (brightness * Mix(r / luma, hueShare), brightness * Mix(g / luma, hueShare), brightness * Mix(b / luma, hueShare));
        }

        static float Mix(float hue, float share) => 1f + (hue - 1f) * share;
    }
}
