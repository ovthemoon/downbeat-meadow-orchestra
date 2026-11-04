using System;
using System.Collections.Generic;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>
    /// <c>--soak</c>: a night run that needs no camera and no tracker and makes no sound. It stands for the looping demo recording as the hand input, the muted output and gaps
    /// between the plays that are long enough for the meadow to settle and go idle, so the whole flow of a visit (Idle, Waking, Playing, Settling) happens again and again.
    /// Whatever the command line says explicitly wins over these defaults.
    /// </summary>
    public static class SoakMode
    {
        public const string Flag = "soak";
        /// <summary>Seconds between the end of one play of the recording and the start of the next, repeated in this order: a short pause (the same visit goes on), one that makes it
        /// settle and wake again, and one that is long enough for the energy to fade to idle (about 23 s after the last note).</summary>
        public const string DefaultGaps = "1,5,35";

        public static void ApplyDefaults(IDictionary<string, string> options)
        {
            if (options == null || !options.ContainsKey(Flag)) return;
            AddIfMissing(options, "input", "replay");
            AddIfMissing(options, "file", ReplayPath.DefaultFile);
            AddIfMissing(options, "mute", string.Empty);
            AddIfMissing(options, "replay-gaps", DefaultGaps);
        }

        static void AddIfMissing(IDictionary<string, string> options, string name, string value)
        {
            if (!options.ContainsKey(name)) options[name] = value;
        }
    }

    /// <summary>How long a looping recording rests after each play (<c>--replay-gaps 1,5,35</c>): the list is used in order and starts again at its end.</summary>
    public sealed class ReplayGapSchedule
    {
        public const double DefaultPauseSec = 1.0;

        readonly double[] _gapsSec;

        ReplayGapSchedule(double[] gapsSec)
        {
            _gapsSec = gapsSec;
        }

        /// <summary>Comma-separated seconds. Entries that are not a number of 0 or more are skipped; with none left the pause is <see cref="DefaultPauseSec"/>.</summary>
        public static ReplayGapSchedule Parse(string text)
        {
            var gaps = new List<double>();
            foreach (var part in (text ?? string.Empty).Split(','))
            {
                if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0) gaps.Add(value);
            }
            if (gaps.Count == 0) gaps.Add(DefaultPauseSec);
            return new ReplayGapSchedule(gaps.ToArray());
        }

        /// <summary>The rest after the <paramref name="playNumber"/>-th play (1 for the first).</summary>
        public double GapAfterPlay(int playNumber)
        {
            if (playNumber < 1) throw new ArgumentOutOfRangeException(nameof(playNumber), playNumber, "plays are counted from 1");
            return _gapsSec[(playNumber - 1) % _gapsSec.Length];
        }
    }
}
