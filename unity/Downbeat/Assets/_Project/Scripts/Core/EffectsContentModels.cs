using System;

namespace Downbeat.Core
{
    // The content blocks of the effects of the six cells (docs/superpowers/specs/2026-10-31-conduct-six-cells-design.md, 4-5). All three are optional: a content.json from before them
    // gets the values written here, which are the same as in tools/lib/content-build.mjs and in content/content.json (a test holds the three together).

    /// <summary>
    /// <c>meadow.skyEffects</c>: what a note of the top row (harp, recorder, bell) makes in the air where the hand is: a ring of light that spreads (harp), seeds that rise (recorder), star dust that falls
    /// slowly and twinkles (bell). Counts are for the High tier at full strength; the Low tier makes <see cref="lowCountScale"/> of them.
    /// </summary>
    [Serializable]
    public sealed class SkyEffectsDto
    {
        /// <summary>Points of light in one ring of the harp.</summary>
        public int ringCount = 28;
        /// <summary>Seeds one note of the recorder lifts at full strength (a softer note lifts fewer).</summary>
        public int seedCount = 10;
        /// <summary>Specks of one fall of star dust of the bell at full strength (a softer note lets fewer fall).</summary>
        public int starCount = 14;
        /// <summary>How long the seeds live (seconds); the ring lives 0.4 of it and the star dust 0.75 of it (SkyEffectSettings.LifeFor).</summary>
        public float lifeSec = 3.6f;
        /// <summary>How far in front of the camera the effects are made (metres, along the way the camera looks): in the air over the meadow, not on the ground.</summary>
        public float distance = 7f;
        /// <summary>The radius (metres) a ring of light reaches at full strength.</summary>
        public float ringRadius = 1.1f;
        /// <summary>HDR brightness of the light: 1 keeps the colour of the cell as it is, above 1 it washes out towards white (the camera's bloom catches it).</summary>
        public float glow = 1.0f;
        /// <summary>Most particles alive at once in each of the three systems (the buffer of the system).</summary>
        public int maxParticles = 400;
        /// <summary>The Low tier makes this share of the particles (0.5 = half): a weak graphics chip is held back by the overdraw of added light.</summary>
        public float lowCountScale = 0.5f;
    }

    /// <summary>
    /// <c>meadow.wind.longWave</c>: the wave of the low string. A sweep along the field that starts at the hand and runs towards the side of the field the hand is on (it lays the grass that way); long
    /// because its front is <see cref="widthScale"/> times as wide as <c>meadow.wind.waveWidth</c>, low because it is soft. Strength = <c>amplitude * (1 + velocityGain * velocity)</c>.
    /// </summary>
    [Serializable]
    public sealed class WindLongWaveDto
    {
        public float amplitude = 0.22f;
        /// <summary>The amplitude grows by this fraction at full velocity.</summary>
        public float velocityGain = 0.5f;
        /// <summary>How fast the front runs along the field (m/s).</summary>
        public float speed = 5f;
        /// <summary>How long it lives. Short enough that the low string at its quickest (one note every two slots) holds only 5 of the 16 waves of the field (WindFieldGpu.Capacity).</summary>
        public float lifeSec = 1.3f;
        /// <summary>The width of the front as a multiple of <c>meadow.wind.waveWidth</c>.</summary>
        public float widthScale = 2.4f;
    }

    /// <summary>
    /// <c>meadow.wind.drumRing</c>: the ring of the hand drum. Like a note's ring (<c>meadow.wind.noteRadial</c>) but strong, wide (<see cref="widthScale"/>) and quick, so the ground seems to thump.
    /// </summary>
    [Serializable]
    public sealed class WindDrumRingDto
    {
        public float amplitude = 0.5f;
        /// <summary>The amplitude grows by this fraction at full velocity.</summary>
        public float velocityGain = 0.5f;
        /// <summary>How fast the ring spreads (m/s).</summary>
        public float speed = 7f;
        /// <summary>How long it lives. Short enough that the hand drum at its quickest (one note a slot) holds only 6 of the 16 waves of the field (WindFieldGpu.Capacity).</summary>
        public float lifeSec = 0.9f;
        /// <summary>The width of the ring as a multiple of <c>meadow.wind.waveWidth</c>.</summary>
        public float widthScale = 1.5f;
    }
}
