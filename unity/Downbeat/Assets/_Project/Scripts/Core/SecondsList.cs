using System;
using System.Collections.Generic;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>A list of times in seconds from a command line option (<c>--reload-content-at 6,14</c>): the positive numbers in it, in ascending order; anything else is skipped.</summary>
    public static class SecondsList
    {
        public static IReadOnlyList<double> Parse(string text)
        {
            var seconds = new List<double>();
            foreach (var part in (text ?? string.Empty).Split(','))
            {
                if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value)) seconds.Add(value);
            }
            seconds.Sort();
            return seconds;
        }
    }
}
