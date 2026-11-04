using System;

namespace Downbeat.Core
{
    /// <summary>A rectangle of the picture in viewport units: x to the right, y up, 0..1 across the picture.</summary>
    public readonly struct CellRect
    {
        public CellRect(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float XMin { get; }
        public float YMin { get; }
        public float XMax { get; }
        public float YMax { get; }
        public float CenterX => (XMin + XMax) * 0.5f;
        public float CenterY => (YMin + YMax) * 0.5f;
    }

    /// <summary>
    /// Where the six cells are on the picture: three columns and two rows (cell number = row * 3 + column, row 0 on top, as the parts are numbered) between the strip at the bottom of the picture
    /// (<c>display.overlayFraction</c> of its height; 0 with two screens) and the top. The rows meet at the horizon (<see cref="HandScreenMap"/>): the top row is the sky above it, the bottom row the ground
    /// below it, so a hand drawn at <see cref="HandScreenMap.Viewport"/> is inside the cell it plays.
    /// </summary>
    public static class CellGuideLayout
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int CellCount = Columns * Rows;

        /// <param name="horizonVy">The viewport y of the horizon, where the rows meet (NaN: half way between the strip and the top).</param>
        public static CellRect Cell(int cell, float overlayFraction, float horizonVy)
        {
            if (cell < 0 || cell >= CellCount) throw new ArgumentOutOfRangeException(nameof(cell), cell, "a cell is 0..5");
            var row = cell / Columns;
            var column = cell % Columns;
            var rowLine = HandScreenMap.HorizonOf(horizonVy, overlayFraction);
            return new CellRect(
                column / (float)Columns,
                row == 0 ? rowLine : overlayFraction,
                (column + 1) / (float)Columns,
                row == 0 ? 1f : rowLine);
        }
    }

    /// <summary>
    /// How bright the picture of a cell frame is, from its edge inwards: a thin bright line at the edge, a soft glow that falls off inside it, and a faint tint over the whole cell.
    /// <paramref name="distance01"/> is the distance from the nearest edge as a share of the thickness of the frame (0 = on the edge, 1 = the thickness of the frame in, or deeper).
    /// </summary>
    public static class CellFrameShape
    {
        const float LineFrom = 0.03f, LineTo = 0.09f;
        const float GlowStrength = 0.5f;
        const float Tint = 0.012f;

        public static float Alpha(float distance01)
        {
            var d = distance01 < 0f ? 0f : distance01 > 1f ? 1f : distance01;
            var t = Math.Max(0f, Math.Min(1f, (d - LineFrom) / (LineTo - LineFrom)));
            var line = 1f - t * t * (3f - 2f * t);
            var glow = GlowStrength * (1f - d) * (1f - d);
            var value = Math.Max(line, glow);
            return value + Tint * (1f - value);
        }
    }
}
