namespace Downbeat.Core
{
    /// <summary>
    /// Where the two rows of the hand are on the picture of the meadow now (see <see cref="HandScreenMap"/>): the strip of the record screen over the bottom, the hand height where the rows meet, and the horizon
    /// they meet at. The meadow knows them (its camera sets the horizon); the mouse needs them to pick the cell under the pointer the way the lit cell is drawn.
    /// </summary>
    public interface IHandScreen
    {
        /// <summary>The share of the picture, from the bottom, that the record strip covers (0 with two screens).</summary>
        float OverlayFraction { get; }

        /// <summary>The hand height (0 top .. 1 bottom) where the rows meet (<c>cells.rowSplit</c>).</summary>
        float RowSplit { get; }

        /// <summary>The viewport y of the horizon, 0 at the bottom .. 1 at the top; NaN while the meadow is not built (half way up the free picture is then used).</summary>
        float HorizonViewport { get; }
    }
}
