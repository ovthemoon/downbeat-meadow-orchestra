using System;

namespace Downbeat.Core
{
    public enum ParticleKind
    {
        None = 0,
        /// <summary>Poppy petals: the marimba's pop.</summary>
        Petals = 1,
    }

    /// <summary>A burst of particles: what kind, how many, where on the ground and when it starts (audio time, the moment the note is heard).</summary>
    public readonly struct ParticleCue
    {
        public ParticleKind Kind { get; }
        public int Count { get; }
        public FieldPoint FieldPos { get; }
        public double StartTime { get; }

        public ParticleCue(ParticleKind kind, int count, FieldPoint fieldPos, double startTime)
        {
            Kind = kind; Count = count; FieldPos = fieldPos; StartTime = startTime;
        }
    }

    /// <summary>What a played note throws into the air.</summary>
    public static class ParticleCues
    {
        const int PetalBase = 3;
        const float PetalPerVelocity = 6f;

        /// <summary>
        /// Marimba: <c>3 + round(6 x velocity)</c> poppy petals thrown at the spot on the ground. Every other part: nothing on the ground: the upper row (harp, recorder, bell) plays in the air with the effects
        /// of <see cref="SkyEffectKind"/> (the recorder's dandelion seeds rise there), the low string and the hand drum move the grass.
        /// </summary>
        public static ParticleCue ForNote(in MeadowNoteEvent e)
        {
            var velocity = e.Velocity < 0f ? 0f : (e.Velocity > 1f ? 1f : e.Velocity);
            if (e.Part == Part.Marimba) return new ParticleCue(ParticleKind.Petals, PetalBase + (int)Math.Round(PetalPerVelocity * velocity, MidpointRounding.AwayFromZero), e.FieldPos, e.AudibleTime);
            return new ParticleCue(ParticleKind.None, 0, e.FieldPos, e.AudibleTime);
        }

        /// <summary>How many fireflies are alive at a firefly level of 0..1 (<paramref name="max"/> at 1).</summary>
        public static int FireflyTarget(float level, int max)
        {
            var l = level < 0f ? 0f : (level > 1f ? 1f : level);
            return (int)Math.Round(l * max, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>The glow of the ribbon that follows a hand: it appears quickly when a hand comes in and fades away over about half a second when it is gone.</summary>
    public static class HandRibbonFade
    {
        public static float Step(float alpha, bool present, float dtSec, float appearSec, float fadeSec)
        {
            var next = present ? alpha + dtSec / Math.Max(appearSec, 1e-4f) : alpha - dtSec / Math.Max(fadeSec, 1e-4f);
            return next < 0f ? 0f : (next > 1f ? 1f : next);
        }
    }
}
