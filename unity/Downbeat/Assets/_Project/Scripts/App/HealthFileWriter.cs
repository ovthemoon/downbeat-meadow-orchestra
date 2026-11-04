using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>
    /// The files of the health log: <c>health-yyyyMMdd.csv</c> (one row per interval, a new file when the local date changes), <c>stalls-yyyyMMdd.csv</c> (one line per frame over 100 ms,
    /// capped), <c>heartbeat.txt</c> (the UTC time of the last beat) and <c>exit-reasons.txt</c> (one line each time the player ended itself). If the folder cannot be created the fallback folder is used; if neither can be, nothing is written and the app
    /// goes on: a log that cannot be written must never stop the show.
    /// </summary>
    public sealed class HealthFileWriter
    {
        public const int MaxStallLinesPerFile = 2000;
        public const string StallHeader = "timestamp_utc,uptime_s,frame_ms,audio_step_s";
        /// <summary>One line for every time the player ended itself (<see cref="SelfExit"/>), kept across restarts: the reason that the supervisor log only shows as an exit code.</summary>
        public const string ExitRecordFile = "exit-reasons.txt";

        readonly Action<string> _warn;
        readonly Dictionary<string, int> _stallLines = new Dictionary<string, int>();
        bool _writeFailureReported;
        readonly HashSet<string> _stallCapReported = new HashSet<string>();

        public HealthFileWriter(string primaryDir, string fallbackDir, Action<string> warn)
        {
            _warn = warn ?? (_ => { });
            PrimaryDirectory = primaryDir;
            if (TryCreate(primaryDir)) Directory = primaryDir;
            else if (TryCreate(fallbackDir))
            {
                Directory = fallbackDir;
                UsedFallback = true;
                _warn($"health log: could not use {primaryDir}; using the fallback folder {fallbackDir}");
            }
            else _warn($"health log: no folder can be written ({primaryDir}, {fallbackDir}); nothing will be logged");
        }

        public bool IsActive => Directory != null;
        /// <summary>The folder that was asked for (the one the supervisor reads).</summary>
        public string PrimaryDirectory { get; }
        /// <summary>The folder asked for could not be made and the fallback is used: whatever watches the asked-for folder (the supervisor) sees no heartbeat.</summary>
        public bool UsedFallback { get; }
        /// <summary>The folder in use, or null when none could be created.</summary>
        public string Directory { get; }

        public string RowFileFor(DateTime utcNow) => Path.Combine(Directory ?? string.Empty, $"health-{LocalDate(utcNow)}.csv");

        public void AppendRow(DateTime utcNow, string csvLine)
        {
            if (!IsActive) return;
            var path = RowFileFor(utcNow);
            Write(() =>
            {
                var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
                File.AppendAllText(path, (isNew ? HealthRow.CsvHeader + "\n" : string.Empty) + csvLine + "\n");
            });
        }

        public void AppendStall(DateTime utcNow, double uptimeSec, double frameMs, double audioStepSec)
        {
            if (!IsActive) return;
            var path = Path.Combine(Directory, $"stalls-{LocalDate(utcNow)}.csv");
            if (!_stallLines.TryGetValue(path, out var lines)) lines = File.Exists(path) ? File.ReadAllLines(path).Length - 1 : 0;
            if (lines >= MaxStallLinesPerFile)
            {
                if (_stallCapReported.Add(path)) _warn($"health log: {path} has {MaxStallLinesPerFile} stall lines; no more are written to it"); // once per file: the next day's file says it too
                return;
            }
            var c = CultureInfo.InvariantCulture;
            var line = string.Join(",", utcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", c), uptimeSec.ToString("0.0", c), frameMs.ToString("0.0", c), audioStepSec.ToString("0.000", c));
            Write(() => File.AppendAllText(path, (File.Exists(path) ? string.Empty : StallHeader + "\n") + line + "\n"));
            _stallLines[path] = Math.Max(0, lines) + 1;
        }

        /// <summary>
        /// Overwrites <c>heartbeat.txt</c>: line 1 is the UTC time (ISO 8601); line 2 is <c>audio=starting|ok|stalled|failed</c> (<see cref="AudioPhase"/>); line 3 is <c>app=loading|running|failed</c>
        /// (<see cref="AppPhase"/>). It is written by the main thread from the first frame on, whether the sound runs or not, so a frozen main thread stops it and a missing sound device does not
        /// look like a dead app. The audio clock can run while the app is not up, so only <c>app=running</c> says the app is: something watching the app (the supervisor) reads all three.
        /// </summary>
        public void WriteHeartbeat(DateTime utcNow, AudioPhase audio = AudioPhase.Ok, AppPhase app = AppPhase.Running)
        {
            if (!IsActive) return;
            var stamp = utcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var text = stamp + "\naudio=" + audio.Word() + "\napp=" + app.Word() + "\n";
            Write(() => File.WriteAllText(Path.Combine(Directory, "heartbeat.txt"), text));
        }

        /// <summary>Adds a line to <c>exit-reasons.txt</c> next to the heartbeat (the player is about to end itself and says why). May be called from any thread; nothing is written when no folder can be written.</summary>
        public void AppendExitRecord(string line)
        {
            if (!IsActive) return;
            Write(() => File.AppendAllText(Path.Combine(Directory, ExitRecordFile), line + "\n"));
        }

        void Write(Action action)
        {
            try
            {
                action();
                _writeFailureReported = false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (_writeFailureReported) return;
                _writeFailureReported = true;
                _warn($"health log: could not write in {Directory}: {e.Message}");
            }
        }

        bool TryCreate(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return false;
            }
        }

        static string LocalDate(DateTime utcNow) => utcNow.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }
}
