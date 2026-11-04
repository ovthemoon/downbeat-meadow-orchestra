using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of <c>meadow.skyEffects</c> (called by <see cref="MeadowContentValidator"/>). The block is optional.</summary>
    public static class SkyContentValidator
    {
        public static void Check(SkyEffectsDto sky, List<string> errors)
        {
            if (sky == null) return; // the defaults apply
            if (!(sky.ringCount >= 0 && sky.ringCount <= 200)) errors.Add("meadow.skyEffects.ringCount must be 0..200");
            if (!(sky.seedCount >= 0 && sky.seedCount <= 100)) errors.Add("meadow.skyEffects.seedCount must be 0..100");
            if (!(sky.starCount >= 0 && sky.starCount <= 100)) errors.Add("meadow.skyEffects.starCount must be 0..100");
            if (!(sky.lifeSec >= 0.5f && sky.lifeSec <= 20f)) errors.Add("meadow.skyEffects.lifeSec must be 0.5..20");
            if (!(sky.distance >= 2f && sky.distance <= 40f)) errors.Add("meadow.skyEffects.distance must be 2..40");
            if (!(sky.ringRadius >= 0.1f && sky.ringRadius <= 5f)) errors.Add("meadow.skyEffects.ringRadius must be 0.1..5");
            if (!(sky.glow >= 0f && sky.glow <= 30f)) errors.Add("meadow.skyEffects.glow must be 0..30");
            if (!(sky.maxParticles >= 16 && sky.maxParticles <= 5000)) errors.Add("meadow.skyEffects.maxParticles must be 16..5000");
            if (!(sky.lowCountScale >= 0f && sky.lowCountScale <= 1f)) errors.Add("meadow.skyEffects.lowCountScale must be 0..1");
        }
    }
}
