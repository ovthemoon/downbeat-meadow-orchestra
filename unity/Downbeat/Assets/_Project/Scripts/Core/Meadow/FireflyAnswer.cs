using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>How the fireflies answer a bell (content <c>meadow.particles.fireflyAnswer*</c>): the nearest <see cref="Count"/> within <see cref="Radius"/> glow, <see cref="Gain"/> times brighter at the peak.</summary>
    public sealed class FireflyAnswerSettings
    {
        public FireflyAnswerSettings(int count, float radius, float attackSec, float decaySec, float gain)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "must not be negative");
            if (!(radius > 0f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be positive");
            if (!(attackSec > 0f)) throw new ArgumentOutOfRangeException(nameof(attackSec), attackSec, "must be positive");
            if (!(decaySec > attackSec)) throw new ArgumentOutOfRangeException(nameof(decaySec), decaySec, "must be longer than the attack");
            if (!(gain >= 0f)) throw new ArgumentOutOfRangeException(nameof(gain), gain, "must not be negative");
            Count = count; Radius = radius; AttackSec = attackSec; DecaySec = decaySec; Gain = gain;
        }

        public int Count { get; }
        /// <summary>Metres over the ground.</summary>
        public float Radius { get; }
        public float AttackSec { get; }
        public float DecaySec { get; }
        /// <summary>How much brighter than usual a firefly is at the peak of its answer (1 = twice).</summary>
        public float Gain { get; }

        public static FireflyAnswerSettings From(MeadowParticlesDto dto) => new FireflyAnswerSettings(dto.fireflyAnswerCount, dto.fireflyAnswerRadius, dto.fireflyAnswerAttackSec, dto.fireflyAnswerDecaySec, dto.fireflyAnswerGain);
    }

    /// <summary>The fireflies glow for a moment when a bell of the nature score sounds: which of them, where the bell is heard, and the shape of the glow. Pure and repeatable.</summary>
    public static class FireflyAnswer
    {
        const float HashScale = 43758.547f;
        const float SpreadOfTheField = 0.9f;
        const float DecayShapeTimes = 3f;

        /// <summary>Fills <paramref name="output"/> (cleared first) with the indexes of up to <paramref name="maxCount"/> fireflies within <paramref name="radius"/> of the bell, nearest first; equally near, the lower index first. Only the place over the ground counts, not the height.</summary>
        public static void Pick(IReadOnlyList<FieldPoint> positions, FieldPoint spot, int maxCount, float radius, List<int> output)
        {
            output.Clear();
            if (maxCount <= 0) return;
            var radiusSquared = radius * radius;
            for (var i = 0; i < positions.Count; i++)
            {
                var d = DistanceSquared(positions[i], spot);
                if (d > radiusSquared) continue;
                var at = output.Count;
                if (output.Count == maxCount)
                {
                    if (d >= DistanceSquared(positions[output[maxCount - 1]], spot)) continue;
                    at = maxCount - 1;
                    output[at] = i;
                }
                else output.Add(i);
                while (at > 0 && DistanceSquared(positions[output[at - 1]], spot) > d)
                {
                    (output[at - 1], output[at]) = (output[at], output[at - 1]);
                    at--;
                }
            }
        }

        /// <summary>Where on the ground a bell of the nature score is heard: at the depth of its pitch (a higher bell is farther), and somewhere across the field, which a fixed number of its slot picks.</summary>
        public static FieldPoint BellSpot(int pitchStep, int pitchSteps, long slot, MeadowLayout layout)
        {
            var z = layout.DepthForPitchStep(pitchStep, pitchSteps);
            var h = Hash01(slot * 0.6180339 + pitchStep * 1.7);
            return new FieldPoint((float)(h - 0.5) * 2f * layout.HalfWidthAt(z) * SpreadOfTheField, z);
        }

        /// <summary>
        /// The glow of a firefly <paramref name="ageSec"/> seconds after the bell: 0 before, rising in a straight line to 1 at <paramref name="attackSec"/>, then falling away smoothly (an exponential that
        /// reaches 0 at <paramref name="attackSec"/> + <paramref name="decaySec"/>): one glow, never a second.
        /// </summary>
        public static float Envelope(float ageSec, float attackSec, float decaySec)
        {
            if (ageSec <= 0f) return 0f;
            if (ageSec < attackSec) return ageSec / attackSec;
            var t = (ageSec - attackSec) / decaySec;
            if (t >= 1f) return 0f;
            var floor = (float)Math.Exp(-DecayShapeTimes);
            return Math.Max(0f, ((float)Math.Exp(-DecayShapeTimes * t) - floor) / (1f - floor));
        }

        static float DistanceSquared(FieldPoint a, FieldPoint b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }

        static double Hash01(double x)
        {
            var s = Math.Sin(x * 12.9898) * HashScale;
            return s - Math.Floor(s);
        }
    }
}
