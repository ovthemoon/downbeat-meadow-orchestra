namespace Downbeat.Core
{
    /// <summary>Which of the six cells (<see cref="Part"/>) a hand is in. Shared by conducting play and by strikes.</summary>
    public static class CellPicker
    {
        /// <summary>The dead band around a column edge for a <c>hand.partHysteresis</c> (a fraction of the picture's width), in columns: the unit <see cref="Pick"/> takes.</summary>
        public static float ColumnMarginFor(float partHysteresis) => partHysteresis * Parts.Columns;

        /// <summary>
        /// The cell at (<paramref name="x01"/>, <paramref name="y01"/>): the columns are the thirds of the picture's width, the rows are above and below <see cref="CellSettings.RowSplit"/> (y counted from the top).
        /// A hand that was in a cell (<paramref name="hasPrevious"/>, <paramref name="previous"/>) stays in its column until it is <paramref name="columnMargin"/> columns (one column is a third of the width; for a
        /// <c>hand.partHysteresis</c> that is <see cref="ColumnMarginFor"/>) past the edge, and in its row until it is <see cref="CellSettings.RowHysteresis"/> past the split.
        /// </summary>
        public static Part Pick(float x01, float y01, Part previous, bool hasPrevious, float columnMargin, CellSettings cells)
        {
            var settings = cells ?? CellSettings.Default;
            var column = HandInterpreter.PickBand(Number(x01) * Parts.Columns, Parts.Columns, hasPrevious ? Parts.Column(previous) : -1, columnMargin);
            var row = PickRow(Number(y01), hasPrevious ? Parts.Row(previous) : -1, settings);
            return Parts.At(row, column);
        }

        static int PickRow(float y01, int previousRow, CellSettings cells)
        {
            var split = cells.RowSplit;
            var margin = cells.RowHysteresis;
            if (previousRow == 0 && y01 < split + margin) return 0;
            if (previousRow == 1 && y01 >= split - margin) return 1;
            return y01 < split ? 0 : 1;
        }

        static float Number(float v) => float.IsNaN(v) ? 0f : v;
    }
}
