using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Downbeat.Core
{
    /// <summary>
    /// The one switch of song mode: which value a run starts with, and the small JSON (<c>{"songMode":true}</c>) that keeps the operator's choice between runs.
    /// The command line beats the saved choice, which beats the content (<c>experience.songMode</c>).
    /// </summary>
    public static class SongModeSetting
    {
        const string On = "on";
        const string Off = "off";
        const string Key = "songMode";
        const string LogTimeFormat = "yyyy-MM-dd HH:mm:ss";

        static readonly Regex Saved = new Regex("^\\s*\\{\\s*\"" + Key + "\"\\s*:\\s*(true|false)\\s*\\}\\s*$", RegexOptions.CultureInvariant);

        /// <param name="launchValue">The value of <c>--songs</c>, or null when it was not given. Only <c>on</c> and <c>off</c> (any case) answer; anything else is ignored (and named in <paramref name="problem"/>).</param>
        /// <param name="saved">The choice saved by the Tab panel, or null when there is none.</param>
        /// <param name="contentDefault">The content value <c>experience.songMode</c>.</param>
        public static bool Resolve(string launchValue, bool? saved, bool contentDefault) => Resolve(launchValue, saved, contentDefault, out _);

        /// <summary>
        /// Like <see cref="Resolve(string, bool?, bool)"/>, and when <c>--songs</c> was given with a value that is neither <c>on</c> nor <c>off</c> (<c>true</c>, <c>1</c>, a bare <c>--songs</c>), the
        /// line to log in <paramref name="problem"/>: the value, and the choice that is used instead. Null otherwise.
        /// </summary>
        public static bool Resolve(string launchValue, bool? saved, bool contentDefault, out string problem)
        {
            problem = null;
            var launch = launchValue?.Trim();
            if (string.Equals(launch, On, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(launch, Off, StringComparison.OrdinalIgnoreCase)) return false;
            var used = saved ?? contentDefault;
            if (launchValue != null) problem = $"--songs '{launchValue}' is not {On} or {Off}; {(saved.HasValue ? "the saved choice" : "the content's choice")} ({(used ? On : Off)}) is used";
            return used;
        }

        /// <summary>The line for the log when the operator switches the song mode (spec 4.10: the time and the value), the time being the local time like the one on the cards: <c>[songs] switch on (tab) at 2026-11-01 10:42:05</c>.</summary>
        public static string SwitchLogLine(bool on, DateTime localTime) =>
            "[songs] switch " + (on ? On : Off) + " (tab) at " + localTime.ToString(LogTimeFormat, CultureInfo.InvariantCulture);

        /// <summary>The text of the settings file for this choice.</summary>
        public static string Encode(bool on) => "{\"" + Key + "\":" + (on ? "true" : "false") + "}";

        /// <summary>The choice in a settings file; null for anything that is not one (an empty or a damaged file).</summary>
        public static bool? Decode(string json)
        {
            if (json == null) return null;
            var match = Saved.Match(json);
            return match.Success ? match.Groups[1].Value == "true" : (bool?)null;
        }
    }
}
