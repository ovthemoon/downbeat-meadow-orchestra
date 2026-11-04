using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>Which flowers a note opens: the ones of the instrument's species nearest to the spot that was played, as many as the note is strong.</summary>
    public static class FlowerSelector
    {
        /// <summary>1 + round(velocity x maxPerNote); a velocity outside 0..1 is clamped.</summary>
        public static int CountFor(float velocity, int maxPerNote)
        {
            var v = velocity < 0f ? 0f : (velocity > 1f ? 1f : velocity);
            return 1 + (int)System.Math.Round(v * maxPerNote, System.MidpointRounding.AwayFromZero);
        }

        /// <summary>Fills <paramref name="output"/> (cleared first) with the indexes of up to <paramref name="count"/> flowers of <paramref name="species"/>, nearest to <paramref name="target"/> first.</summary>
        public static void Select(FlowerInstance[] flowers, FlowerSpecies species, FieldPoint target, int count, List<int> output)
        {
            output.Clear();
            if (count <= 0) return;
            for (var i = 0; i < flowers.Length; i++)
            {
                if (flowers[i].Species != species) continue;
                var d = DistanceSquared(flowers[i], target);
                if (output.Count == count && d >= DistanceSquared(flowers[output[count - 1]], target)) continue;
                // insertion into the short sorted list
                var at = output.Count < count ? output.Count : count - 1;
                if (output.Count < count) output.Add(i); else output[at] = i;
                while (at > 0 && DistanceSquared(flowers[output[at - 1]], target) > d)
                {
                    var swap = output[at - 1];
                    output[at - 1] = output[at];
                    output[at] = swap;
                    at--;
                }
            }
        }

        static float DistanceSquared(in FlowerInstance flower, in FieldPoint target)
        {
            var dx = flower.X - target.X;
            var dz = flower.Z - target.Z;
            return dx * dx + dz * dz;
        }
    }
}
