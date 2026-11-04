using System;
using System.Collections.Generic;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>
    /// Command-line options of the app: <c>--name value</c>, <c>--name=value</c> or a bare <c>--flag</c>. Arguments with one dash
    /// belong to Unity itself (<c>-batchmode</c>, <c>-screen-width</c>) and are ignored here; they are never the value of a bare flag either. Immutable.
    /// </summary>
    public sealed class LaunchArgs
    {
        const string OptionPrefix = "--";
        const char ValueSeparator = '=';

        static readonly LaunchArgs EmptyArgs = new LaunchArgs(new Dictionary<string, string>());

        readonly Dictionary<string, string> _options;

        LaunchArgs(Dictionary<string, string> options)
        {
            _options = options;
        }

        static LaunchArgs _overrideForTests;
        static LaunchArgs _defaultsForTests;

        /// <summary>The options of this process, with the defaults of the tests added (<see cref="DefaultsForTests"/>). Read once per call; cheap enough for start-up code.</summary>
        public static LaunchArgs Current
        {
            get
            {
                var given = _overrideForTests ?? Parse(Environment.GetCommandLineArgs());
                return _defaultsForTests == null ? given : given.WithMissingFrom(_defaultsForTests);
            }
        }

        /// <summary>For tests that start the real scene: until the returned scope is disposed <see cref="Current"/> is <paramref name="args"/> instead of the command line of the test runner.</summary>
        public static IDisposable OverrideForTests(IReadOnlyList<string> args)
        {
            var previous = _overrideForTests;
            _overrideForTests = Parse(args);
            return new Restore(() => _overrideForTests = previous);
        }

        /// <summary>
        /// For a whole set of tests (a fixture that runs once for an assembly): until the returned scope is disposed every option of <paramref name="args"/> is added to <see cref="Current"/> where
        /// nothing else gave it, whether by <see cref="OverrideForTests"/> or by the command line; an option that is given there is not replaced. Defaults that are already in force were there first and win over
        /// the ones of a later call.
        /// </summary>
        public static IDisposable DefaultsForTests(IReadOnlyList<string> args)
        {
            var previous = _defaultsForTests;
            var added = Parse(args);
            _defaultsForTests = previous == null ? added : previous.WithMissingFrom(added);
            return new Restore(() => _defaultsForTests = previous);
        }

        sealed class Restore : IDisposable
        {
            readonly Action _restore;

            public Restore(Action restore)
            {
                _restore = restore;
            }

            public void Dispose() => _restore();
        }

        /// <summary>These options, and those of <paramref name="other"/> that these do not have.</summary>
        LaunchArgs WithMissingFrom(LaunchArgs other)
        {
            var merged = new Dictionary<string, string>(_options, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in other._options)
            {
                if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value;
            }
            return new LaunchArgs(merged);
        }

        public static LaunchArgs Empty => EmptyArgs;

        public static LaunchArgs Parse(IReadOnlyList<string> args)
        {
            if (args == null || args.Count == 0) return EmptyArgs;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Count; i++)
            {
                var token = args[i];
                if (token == null || !token.StartsWith(OptionPrefix, StringComparison.Ordinal) || token.Length == OptionPrefix.Length) continue;

                var body = token.Substring(OptionPrefix.Length);
                var equals = body.IndexOf(ValueSeparator);
                if (equals >= 0)
                {
                    options[body.Substring(0, equals)] = body.Substring(equals + 1);
                    continue;
                }

                var hasValue = i + 1 < args.Count && IsValue(args[i + 1]);
                options[body] = hasValue ? args[i + 1] : string.Empty;
                if (hasValue) i++;
            }
            SoakMode.ApplyDefaults(options);
            return new LaunchArgs(options);
        }

        /// <summary>
        /// A token is the value of the option before it unless it is another option: <c>--name</c>, or a one-dash option of Unity (<c>-logFile</c>, a dash and a letter).
        /// A dash and a digit or a point is a negative number (<c>-1</c>, <c>-0.5</c>) and stays a value.
        /// </summary>
        static bool IsValue(string token)
        {
            if (token == null || token.StartsWith(OptionPrefix, StringComparison.Ordinal)) return false;
            return !(token.Length >= 2 && token[0] == '-' && char.IsLetter(token[1]));
        }

        public bool Has(string name) => _options.ContainsKey(name);

        /// <summary>The value, an empty string for a bare flag, or null when the option was not given.</summary>
        public string Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

        public bool TryGetDouble(string name, out double value)
        {
            value = 0.0;
            return _options.TryGetValue(name, out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
