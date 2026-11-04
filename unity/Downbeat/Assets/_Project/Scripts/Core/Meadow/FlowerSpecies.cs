using System;

namespace Downbeat.Core
{
    /// <summary>The three flowers of the meadow; each one belongs to a column of the lower row (the ground) of the six cells.</summary>
    public enum FlowerSpecies
    {
        /// <summary>Bellflower: the left column (the low string plays there).</summary>
        Bellflower = 0,
        /// <summary>Dandelion: the middle column (the marimba).</summary>
        Dandelion = 1,
        /// <summary>Poppy: the right column (the hand drum).</summary>
        Poppy = 2,
    }

    public static class SpeciesMap
    {
        public const int Count = 3;
        public const float LeftCentre = 0.17f;
        public const float MiddleCentre = 0.5f;
        public const float RightCentre = 0.83f;
        const float LeftBorder = 1f / 3f;
        const float RightBorder = 2f / 3f;

        /// <summary>The flower a note of this part opens: the flower of its column for the lower row, none for the upper row (the sky plays in the air and opens no flowers).</summary>
        public static FlowerSpecies? ForPart(Part part)
        {
            if (Parts.IsSky(part)) return null;
            return (FlowerSpecies)Parts.Column(part);
        }

        /// <summary>The part of the lower row (the ground) in the column of this flower.</summary>
        public static Part ToPart(FlowerSpecies species) => Parts.At(1, (int)species);

        /// <summary>The middle of the column a part plays in, as a fraction of the picture width.</summary>
        public static float CentreOf(Part part)
        {
            switch (Parts.Column(part))
            {
                case 0: return LeftCentre;
                case 1: return MiddleCentre;
                default: return RightCentre;
            }
        }

        /// <summary>
        /// How much of the flowers at picture position <paramref name="x01"/> (0 left .. 1 right) are of <paramref name="species"/>. The zones meet at 1/3 and 2/3
        /// and mix over <paramref name="blend"/> (a fraction of the width) around each border, so the species fade into each other instead of changing at a line.
        /// The weights of the three species sum to 1. Which instrument plays is decided by the cell of the hand (<see cref="CellPicker"/>), not by this: the mix is only how the
        /// meadow looks.
        /// </summary>
        public static float Weight(FlowerSpecies species, float x01, float blend)
        {
            var x = x01 < 0f ? 0f : (x01 > 1f ? 1f : x01);
            var right = Smooth(x, LeftBorder, blend);   // 0 in the left zone, 1 right of the first border
            var far = Smooth(x, RightBorder, blend);    // 0 left of the second border, 1 in the right zone
            switch (species)
            {
                case FlowerSpecies.Bellflower: return 1f - right;
                case FlowerSpecies.Dandelion: return right - far;
                default: return far;
            }
        }

        /// <summary>Smoothstep across a border: 0 before border - blend/2, 1 after border + blend/2.</summary>
        static float Smooth(float x, float border, float blend)
        {
            if (blend <= 0f) return x < border ? 0f : 1f;
            var u = (x - (border - blend * 0.5f)) / blend;
            u = u < 0f ? 0f : (u > 1f ? 1f : u);
            return u * u * (3f - 2f * u);
        }
    }
}
