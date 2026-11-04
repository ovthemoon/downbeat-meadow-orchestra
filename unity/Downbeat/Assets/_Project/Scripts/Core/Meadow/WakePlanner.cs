using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>How the meadow wakes when a hand comes into an idle one (content <c>meadow.wake</c>): the buds within <see cref="Radius"/> glint in a wave that spreads at <see cref="Speed"/>.</summary>
    public sealed class WakeSettings
    {
        public WakeSettings(float radius, float speed, int maxFlowers, float jitterSec)
        {
            if (!(radius > 0f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be positive");
            if (!(speed > 0f)) throw new ArgumentOutOfRangeException(nameof(speed), speed, "must be positive");
            if (maxFlowers < 0) throw new ArgumentOutOfRangeException(nameof(maxFlowers), maxFlowers, "must not be negative");
            if (!(jitterSec >= 0f)) throw new ArgumentOutOfRangeException(nameof(jitterSec), jitterSec, "must not be negative");
            Radius = radius; Speed = speed; MaxFlowers = maxFlowers; JitterSec = jitterSec;
        }

        /// <summary>Metres around the hand.</summary>
        public float Radius { get; }
        /// <summary>Metres a second the wave front spreads at.</summary>
        public float Speed { get; }
        /// <summary>The most flowers that glint (a thick meadow is thinned evenly to this).</summary>
        public int MaxFlowers { get; }
        /// <summary>Each flower is a little late by up to this long, so the ring is not a perfect circle.</summary>
        public float JitterSec { get; }

        public static WakeSettings From(MeadowWakeDto dto) => new WakeSettings(dto.radius, dto.speed, dto.maxFlowers, dto.jitterSec);
    }

    /// <summary>One flower of the wake and how long after the wake starts it glints.</summary>
    public readonly struct WakeBloom
    {
        public int Flower { get; }
        public float DelaySec { get; }

        public WakeBloom(int flower, float delaySec)
        {
            Flower = flower; DelaySec = delaySec;
        }
    }

    /// <summary>Which flowers glint when the meadow wakes, and when: nearest first, delayed by their distance from the hand. Pure, deterministic, any species.</summary>
    public static class WakePlanner
    {
        const float HashMultiplier = 0.7919f;
        const float HashScale = 43758.547f;

        /// <summary>Fills <paramref name="output"/> (cleared first) with up to <see cref="WakeSettings.MaxFlowers"/> flowers within the radius of <paramref name="origin"/>, evenly thinned by distance, nearest first.</summary>
        public static void Plan(FlowerInstance[] flowers, FieldPoint origin, WakeSettings settings, List<WakeBloom> output)
        {
            output.Clear();
            if (settings.MaxFlowers == 0) return;
            var near = new List<(float distance, int flower)>();
            var radiusSquared = settings.Radius * settings.Radius;
            for (var i = 0; i < flowers.Length; i++)
            {
                var dx = flowers[i].X - origin.X;
                var dz = flowers[i].Z - origin.Z;
                var d2 = dx * dx + dz * dz;
                if (d2 <= radiusSquared) near.Add(((float)Math.Sqrt(d2), i));
            }
            near.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.flower.CompareTo(b.flower));

            var picked = Math.Min(near.Count, settings.MaxFlowers);
            for (var k = 0; k < picked; k++)
            {
                var at = (int)((long)k * near.Count / picked);   // evenly spread through the ranks: the same density near the hand and at the rim
                var (distance, flower) = near[at];
                output.Add(new WakeBloom(flower, distance / settings.Speed + settings.JitterSec * Hash01(flower)));
            }
        }

        /// <summary>A repeatable number between 0 and 1 for a flower.</summary>
        static float Hash01(int flower)
        {
            var s = (float)Math.Sin((flower + 1) * HashMultiplier * 12.9898f) * HashScale;
            return s - (float)Math.Floor(s);
        }
    }

    /// <summary>
    /// Where the wake starts: at the ground under the hand that came in (between two hands; <paramref name="groundOfHand"/> finds it from where the hand is on the screen, else it is where the hand is on the field),
    /// else the middle of the field (a mouse or keyboard visit, a hand that is gone again).
    /// </summary>
    public static class WakeOrigin
    {
        const float Middle = 0.5f;

        public static FieldPoint Of(IHandMotionView motion, MeadowLayout layout, GroundOfHand groundOfHand = null)
        {
            float sumX = 0f, sumY = 0f;
            var tracked = 0;
            for (var i = 0; motion != null && i < motion.Count; i++)
            {
                var hand = motion.Hand(i);
                if (!hand.IsTracked) continue;
                sumX += hand.X;
                sumY += hand.Y;
                tracked++;
            }
            if (tracked == 0) return layout.HandToField(Middle, Middle);
            var x = sumX / tracked;
            var y = sumY / tracked;
            return groundOfHand != null && groundOfHand(x, y, out var ground) ? ground : layout.HandToField(x, y);
        }
    }
}
