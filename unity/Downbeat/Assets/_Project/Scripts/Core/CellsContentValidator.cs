using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>cells</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional: a file without it gets the defaults of <see cref="CellsDto"/>.</summary>
    public static class CellsContentValidator
    {
        public static void Check(CellsDto cells, List<string> errors)
        {
            if (cells == null) return;
            if (!(cells.rowSplit >= 0.25f && cells.rowSplit <= 0.75f)) errors.Add("cells.rowSplit must be 0.25..0.75");
            if (!(cells.rowHysteresis >= 0f && cells.rowHysteresis <= 0.1f)) errors.Add("cells.rowHysteresis must be 0..0.1");
            if (cells.labels == null || cells.labels.Length != Parts.Count) errors.Add($"cells.labels must list {Parts.Count} names");
            else
            {
                for (var i = 0; i < cells.labels.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(cells.labels[i])) errors.Add($"cells.labels[{i}] is empty");
                }
            }
            if (cells.colors == null || cells.colors.Length != Parts.Count) errors.Add($"cells.colors must list {Parts.Count} colours");
            else
            {
                for (var i = 0; i < cells.colors.Length; i++)
                {
                    if (!ColorHex.TryParse(cells.colors[i], out _)) errors.Add($"cells.colors[{i}] must be #RRGGBB");
                }
            }
        }
    }
}
