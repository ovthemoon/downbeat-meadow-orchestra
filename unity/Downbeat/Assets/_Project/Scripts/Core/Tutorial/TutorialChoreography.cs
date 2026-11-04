using System;

namespace Downbeat.Core
{
    /// <summary>
    /// What happens on the screen during the five seconds of each step of the tutorial, as plain numbers of the step's progress (0..1) and of the clock: the open hand breathes, a hand walks round the six cells, two fists
    /// spread apart. Positions are on the stage, the part of the screen above the record strip: u from the left (0) to the right (1), v from the top (0) to the bottom (1), like the x and y of a hand. The timings follow the mock-up
    /// the owner approved, stretched to the length of a step.
    /// </summary>
    public static class TutorialChoreography
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int CellCount = Columns * Rows;
        public const int SoundBarCount = 5;

        /// <summary>A breath of the open hand of the first step takes this long, as does the ring that leaves it.</summary>
        public const double BreathPeriodSec = 1.6;
        const float BreathGrowth = 0.12f;

        // the walk of the second step: along the top row, then back along the bottom row (the numbers of the cells, row * 3 + column)
        static readonly int[] WalkOrder = { 0, 1, 2, 5, 4, 3 };
        const float RestShare = 2f / 3f;   // of the time at a cell, the rest is the way to the next one

        const float FaintGrid = 0.5f;      // the outlines of the first step, as strong as half of those of the second
        const float EdgeShare = 0.08f;     // a step fades in over this share of its time, and out over the same at its end

        const float FistsStartU = 0.415f;
        const float FistsEndU = 0.165f;

        /// <summary>The middle of a cell: the six cells share the stage evenly, as the cells of the hand's x and y do.</summary>
        public static (float U, float V) CellCenter(int cell)
        {
            var column = cell % Columns;
            var row = cell / Columns;
            return ((column + 0.5f) / Columns, (row + 0.5f) / Rows);
        }

        /// <summary>The size of the open hand of the first step: 1 to 1.12 and back, once every <see cref="BreathPeriodSec"/>.</summary>
        public static float Breath(double seconds) => 1f + BreathGrowth * 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * seconds / BreathPeriodSec));

        /// <summary>How far the ring that leaves the open hand has grown: 0 (small and bright) .. 1 (wide and gone), then again.</summary>
        public static float RingPhase(double seconds)
        {
            var phase = seconds / BreathPeriodSec;
            return (float)(phase - Math.Floor(phase));
        }

        /// <summary>Where the hand of the second step is: it rests in a cell for two thirds of its time and goes on to the next one in the rest.</summary>
        public static (float U, float V) RoamPosition(float progress)
        {
            var legs = WalkOrder.Length;
            var scaled = Clamp01(progress) * legs;
            var leg = Math.Min(legs - 1, (int)scaled);
            var within = scaled - leg;
            var (fromU, fromV) = CellCenter(WalkOrder[leg]);
            if (leg == legs - 1 || within <= RestShare) return (fromU, fromV);
            var (toU, toV) = CellCenter(WalkOrder[leg + 1]);
            var t = Smooth((within - RestShare) / (1f - RestShare));
            return (fromU + (toU - fromU) * t, fromV + (toV - fromV) * t);
        }

        /// <summary>How lit a cell is (0..1): fully while the hand is in it, not at all once the hand is half way to the next one.</summary>
        public static float CellLight(int cell, float progress)
        {
            var (u, v) = RoamPosition(progress);
            var (cu, cv) = CellCenter(cell);
            var distance = Math.Max(Math.Abs(u - cu) * Columns, Math.Abs(v - cv) * Rows);   // in cells
            return 1f - SmoothStep(0.1f, 0.5f, distance);
        }

        /// <summary>How far apart the two fists of the third step are (0 = side by side, 1 = spread): they wait a moment, spread, and stay.</summary>
        public static float FistSpread(float progress) => SmoothStep(0.2f, 0.6f, progress);

        /// <summary>The middles of the left and the right fist (u), mirror images of each other round the middle of the stage.</summary>
        public static (float Left, float Right) FistCentres(float spread)
        {
            var left = FistsStartU + (FistsEndU - FistsStartU) * Clamp01(spread);
            return (left, 1f - left);
        }

        /// <summary>The two arrows beside the spreading fists: they show while the fists move.</summary>
        public static float ArrowAlpha(float progress) => Trapezoid(progress, 0.15f, 0.30f, 0.60f, 0.75f);

        /// <summary>The word "끝" fades in as the fists come to rest.</summary>
        public static float EndWordAlpha(float progress) => SmoothStep(0.58f, 0.70f, progress);

        /// <summary>How loud the music of the third step still is (1 = playing, 0 = stopped), per bar of the little level meter: each bar dies a moment after the one before.</summary>
        public static float SoundLevel(float progress, int bar) => 1f - Clamp01((progress - 0.03f * bar - 0.2f) / 0.42f);

        /// <summary>The look of a step's own picture at its edges: it fades in over the first and out over the last eighth of the step, so that one step melts into the next.</summary>
        public static float SceneAlpha(float progress) => Math.Min(SmoothStep(0f, EdgeShare, progress), 1f - SmoothStep(1f - EdgeShare, 1f, progress));

        /// <summary>How strong the six cell outlines are: faint in the first step, strong in the second, gone in the third; they stay faint across the border between the first and the second.</summary>
        public static float GridStrength(int step, float progress)
        {
            var intro = SmoothStep(0f, EdgeShare, progress);
            var outro = 1f - SmoothStep(1f - EdgeShare, 1f, progress);
            switch (step)
            {
                case 0: return FaintGrid * intro;
                case 1: return (FaintGrid + (1f - FaintGrid) * intro) * outro;
                default: return 0f;
            }
        }

        /// <summary>How strong the names of the cells are: only in the second step.</summary>
        public static float LabelStrength(int step, float progress) => step == 1 ? SceneAlpha(progress) : 0f;

        static float Trapezoid(float x, float rise0, float rise1, float fall0, float fall1) =>
            Math.Min(SmoothStep(rise0, rise1, x), 1f - SmoothStep(fall0, fall1, x));

        static float SmoothStep(float from, float to, float x)
        {
            var t = Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        static float Smooth(float t) => SmoothStep(0f, 1f, t);

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
