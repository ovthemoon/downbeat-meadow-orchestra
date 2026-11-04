using System;
using System.IO;
using System.Security;
using System.Text;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// The operator's choice of the song mode switch, kept in a small file in the data folder of the app (next to the cards), so that it stays when the app is started again, also by the supervisor.
    /// A file that is missing, damaged or cannot be read or written is only a warning in the log: the app starts and plays all the same.
    /// </summary>
    public sealed class SongModeStore
    {
        public const string FileName = "settings.json";
        const string OptionName = "settings-file";

        readonly string _path;

        public SongModeStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string FilePath => _path;

        /// <summary>
        /// The file for this run: <c>--settings-file PATH</c> when given, else <c>settings.json</c> in <paramref name="dataFolder"/>. A path that cannot be a path (a character that no path has) is a warning and
        /// the data folder is used: the app must start whatever the operator typed. Never throws.
        /// </summary>
        public static string PathFor(LaunchArgs launch, string dataFolder)
        {
            var fallback = System.IO.Path.Combine(dataFolder, FileName);
            var given = launch.Get(OptionName);
            if (string.IsNullOrEmpty(given)) return fallback;
            try
            {
                return System.IO.Path.GetFullPath(given);
            }
            catch (Exception e) when (IsFileProblem(e))
            {
                Debug.LogWarning($"[songs] --{OptionName} {Printable(given)} is not a usable path, using {fallback} ({Printable(e.Message)})");
                return fallback;
            }
        }

        /// <summary>The store of this run. The editor (and so the tests) keep it in the temporary cache, never among the real files of the app, like the cards.</summary>
        public static SongModeStore Open(LaunchArgs launch) =>
            new SongModeStore(PathFor(launch, Application.isEditor ? Application.temporaryCachePath : Application.persistentDataPath));

        /// <summary>The saved choice; null when there is none (no file, or one that is not understood).</summary>
        public bool? Load()
        {
            string text;
            try
            {
                if (!File.Exists(_path)) return null;
                text = File.ReadAllText(_path);
            }
            catch (Exception e) when (IsFileProblem(e))
            {
                Debug.LogWarning($"[songs] could not read the settings file {_path}: {e.Message}");
                return null;
            }
            var saved = SongModeSetting.Decode(text);
            if (saved == null) Debug.LogWarning($"[songs] the settings file {_path} is not understood: it is ignored");
            return saved;
        }

        /// <summary>Keeps the choice. Never throws.</summary>
        public void Save(bool on)
        {
            try
            {
                var folder = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_path, SongModeSetting.Encode(on));
            }
            catch (Exception e) when (IsFileProblem(e))
            {
                Debug.LogWarning($"[songs] could not save the settings file {_path}: {e.Message}");
            }
        }

        /// <summary>The text with every control character shown as a question mark: a character that ends a line (or the text, for the Unity log) must not cut the warning that names it.</summary>
        static string Printable(string text)
        {
            var printable = new StringBuilder(text.Length);
            foreach (var c in text) printable.Append(char.IsControl(c) ? '?' : c);
            return printable.ToString();
        }

        static bool IsFileProblem(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is SecurityException;
    }
}
