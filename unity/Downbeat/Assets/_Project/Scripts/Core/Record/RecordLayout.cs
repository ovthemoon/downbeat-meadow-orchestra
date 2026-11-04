using System;

namespace Downbeat.Core
{
    /// <summary>A rectangle in logical pixels, counted from the bottom left corner (y goes up).</summary>
    public readonly struct LayoutRect
    {
        public LayoutRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float Right => X + Width;
        public float Top => Y + Height;
        public bool IsEmpty => !(Width > 0f && Height > 0f);

        public bool Overlaps(in LayoutRect other) => !IsEmpty && !other.IsEmpty && X < other.Right - 1e-3f && other.X < Right - 1e-3f && Y < other.Top - 1e-3f && other.Y < Top - 1e-3f;
    }

    /// <summary>
    /// Where the parts of the record screen go. On its own screen (<see cref="DisplayMode.Dual"/>) the title is on top, then the live score, the lines of the nature layers, and
    /// today's cards in a row along the bottom. As a strip over the meadow (<see cref="DisplayMode.SingleOverlay"/>) the score and the nature lines are on the left and the cards on the right.
    /// Sizes are fractions of the height, so the same layout reads the same at any resolution.
    /// </summary>
    public sealed class RecordLayout
    {
        const float DualMargin = 0.052f;
        const float DualGap = 0.018f;
        const float DualTitle = 0.085f;
        const float DualScore = 0.34f;
        const float DualNature = 0.2f;
        const int DualCardColumns = 6;

        const float StripMargin = 0.07f;
        const float StripGap = 0.05f;
        const float StripLeftShare = 0.6f;
        const float StripScoreShare = 0.6f;
        const int StripCardColumns = 3;

        RecordLayout(DisplayMode mode, LayoutRect title, LayoutRect score, LayoutRect nature, LayoutRect cards, int columns, int rows, float cardWidth, float cardHeight, float gap, float margin)
        {
            Mode = mode;
            Title = title;
            Score = score;
            Nature = nature;
            Cards = cards;
            CardColumns = columns;
            CardRows = rows;
            CardWidth = cardWidth;
            CardHeight = cardHeight;
            Gap = gap;
            Margin = margin;
        }

        public DisplayMode Mode { get; }
        /// <summary>Empty in the strip.</summary>
        public LayoutRect Title { get; }
        public LayoutRect Score { get; }
        public LayoutRect Nature { get; }
        public LayoutRect Cards { get; }
        public int CardColumns { get; }
        public int CardRows { get; }
        public float CardWidth { get; }
        public float CardHeight { get; }
        public float Gap { get; }
        public float Margin { get; }

        /// <summary>The place of the <paramref name="index"/>-th card (0 = the newest, top left, then to the right and down).</summary>
        public LayoutRect CardRect(int index)
        {
            if (index < 0 || index >= CardColumns * CardRows) throw new ArgumentOutOfRangeException(nameof(index), index, "no such card slot");
            var column = index % CardColumns;
            var row = index / CardColumns;
            return new LayoutRect(Cards.X + column * (CardWidth + Gap), Cards.Top - (row + 1) * CardHeight - row * Gap, CardWidth, CardHeight);
        }

        public static RecordLayout Compute(DisplayMode mode, float width, float height, int cardsShown)
        {
            if (!(width > 0f) || !(height > 0f)) throw new ArgumentOutOfRangeException(nameof(width), "the screen needs a size");
            if (cardsShown < 1 || cardsShown > 12) throw new ArgumentOutOfRangeException(nameof(cardsShown), cardsShown, "1..12");
            return mode == DisplayMode.Dual ? Dual(width, height, cardsShown) : Strip(width, height, cardsShown);
        }

        static RecordLayout Dual(float width, float height, int cardsShown)
        {
            var margin = DualMargin * height;
            var gap = DualGap * height;
            var inner = width - 2f * margin;
            var title = new LayoutRect(margin, height - margin - DualTitle * height, inner, DualTitle * height);
            var score = new LayoutRect(margin, title.Y - gap - DualScore * height, inner, DualScore * height);
            var nature = new LayoutRect(margin, score.Y - gap - DualNature * height, inner, DualNature * height);
            var cards = new LayoutRect(margin, margin, inner, nature.Y - gap - margin);
            return WithCards(DisplayMode.Dual, title, score, nature, cards, DualCardColumns, cardsShown, gap, margin);
        }

        static RecordLayout Strip(float width, float height, int cardsShown)
        {
            var margin = StripMargin * height;
            var gap = StripGap * height;
            var leftWidth = (width - 2f * margin - gap) * StripLeftShare;
            var left = height - 2f * margin - gap;
            var nature = new LayoutRect(margin, margin, leftWidth, left * (1f - StripScoreShare));
            var score = new LayoutRect(margin, nature.Top + gap, leftWidth, left * StripScoreShare);
            var cards = new LayoutRect(margin + leftWidth + gap, margin, width - 2f * margin - leftWidth - gap, height - 2f * margin);
            return WithCards(DisplayMode.SingleOverlay, default, score, nature, cards, StripCardColumns, Math.Min(cardsShown, StripCardColumns), gap, margin);
        }

        static RecordLayout WithCards(DisplayMode mode, LayoutRect title, LayoutRect score, LayoutRect nature, LayoutRect cards, int maxColumns, int shown, float gap, float margin)
        {
            var columns = Math.Min(shown, maxColumns);
            var rows = (shown + columns - 1) / columns;
            var cardWidth = (cards.Width - gap * (columns - 1)) / columns;
            var cardHeight = (cards.Height - gap * (rows - 1)) / rows;
            return new RecordLayout(mode, title, score, nature, cards, columns, rows, cardWidth, cardHeight, gap, margin);
        }
    }

    /// <summary>The words that carry a number: "1분 12초", "자연 3", "10월 29일". The formats come from content.json.</summary>
    public static class CardText
    {
        public static string Duration(double seconds, string format)
        {
            var total = (int)Math.Max(0.0, Math.Round(seconds));
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, total / 60, total % 60);
        }

        public static string Nature(int joined, string format) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Math.Max(0, joined));

        public static string Date(DateTime local, string format) => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, local.Month, local.Day);

        /// <summary>The time of day on a twelve-hour clock: {0} the word before or after noon (<paramref name="meridiem"/>[0] and [1]), {1} the hour 1..12, {2} the minute. Noon is 12 after noon, midnight 12 before.</summary>
        public static string TimeOfDay(DateTime local, string format, System.Collections.Generic.IReadOnlyList<string> meridiem)
        {
            var hour = local.Hour % 12;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, meridiem[local.Hour < 12 ? 0 : 1], hour == 0 ? 12 : hour, local.Minute);
        }
    }
}
