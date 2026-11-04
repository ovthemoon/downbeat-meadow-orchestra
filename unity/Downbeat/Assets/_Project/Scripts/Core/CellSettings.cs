namespace Downbeat.Core
{
    /// <summary>
    /// The six cells of the hand's place in the picture (three columns, two rows, see <see cref="Parts"/>) as an immutable snapshot of <see cref="CellsDto"/>: where the rows meet, how far a
    /// hand has to go past that line to change row, and the name and colour of each cell. The arrays are in the order of <see cref="Part"/>; do not change them.
    /// </summary>
    public sealed class CellSettings
    {
        public const float DefaultRowSplit = 0.5f;
        public const float DefaultRowHysteresis = 0.03f;

        /// <summary>Where the upper row (the sky) ends and the lower row (the ground) begins, as a fraction of the picture's height counted from the top.</summary>
        public float RowSplit { get; }
        /// <summary>A hand that is in a row stays in it until it is this far (a fraction of the picture's height) past <see cref="RowSplit"/>, so a hand on the line does not flip between two instruments.</summary>
        public float RowHysteresis { get; }
        /// <summary>The name of each cell, in Korean, shown in the cell when a hand enters it.</summary>
        public string[] Labels { get; }
        /// <summary>The colour of each cell, <c>#RRGGBB</c>.</summary>
        public string[] Colors { get; }

        CellSettings(float rowSplit, float rowHysteresis, string[] labels, string[] colors)
        {
            RowSplit = rowSplit;
            RowHysteresis = rowHysteresis;
            Labels = labels;
            Colors = colors;
        }

        /// <summary>The settings of a content file without the block (one shared object: it is never changed).</summary>
        public static CellSettings Default { get; } = From(null);

        /// <summary>From the content block (null: the defaults). Values that the content validator would have refused are not checked again here; a list that is not six long is replaced by the default one.</summary>
        public static CellSettings From(CellsDto dto)
        {
            var d = dto ?? new CellsDto();
            return new CellSettings(d.rowSplit, d.rowHysteresis, SixOr(d.labels, CellsDto.DefaultLabels), SixOr(d.colors, CellsDto.DefaultColors));
        }

        static string[] SixOr(string[] given, string[] fallback) => (string[])(given != null && given.Length == Parts.Count ? given : fallback).Clone();
    }
}
