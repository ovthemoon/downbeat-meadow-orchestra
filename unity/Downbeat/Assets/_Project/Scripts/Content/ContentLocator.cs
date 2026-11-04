using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Downbeat.Content
{
    /// <summary>
    /// Finds the content folder. A player reads, in this order, (1) the <c>content</c> folder next to the executable (next to the .app on a Mac): what the operator edits; (2) when that is missing or
    /// not usable, the <c>last-known-good</c> copy in the user's data folder, which is a copy of the last external content that loaded; (3) when there is none either (a new PC), the
    /// <c>StreamingAssets/default-content</c> folder that was built into the program. A folder is usable when <see cref="ContentFolderValidator"/> finds nothing wrong with it. Why a better copy was
    /// passed over is kept in <see cref="ContentSource.Reason"/> and goes to the log. The Editor reads the repository's content folder and nothing else.
    /// </summary>
    public static class ContentLocator
    {
        public const string ContentFolder = "content";
        public const string ContentFile = "content.json";
        public const string LastKnownGoodFolder = "last-known-good";
        public const string BuiltInFolder = "default-content";
        const string ReasonSeparator = " | ";
        const int ErrorsInReason = 4;

        static ContentSource _active;

        /// <summary>The copy of the content the app loaded (null until it has, and always null in the Editor).</summary>
        public static ContentSource Active => _active;

        /// <summary>Folder that holds the executable: next to Downbeat.exe, or next to Downbeat.app.</summary>
        public static string ExecutableDirectory()
        {
            var data = Path.GetFullPath(Application.dataPath);
            var up = Application.platform == RuntimePlatform.OSXPlayer ? Path.Combine("..", "..") : "..";
            return Path.GetFullPath(Path.Combine(data, up));
        }

        /// <summary>Where every file of the content is read from: the folder of <see cref="Active"/>, or the one the Editor and a player that has not chosen yet use.</summary>
        public static string BaseUrl()
        {
            var folder = Application.isEditor ? RepositoryContentFolder() : _active != null ? _active.RootDir : Path.Combine(ExecutableDirectory(), ContentFolder);
            return UrlOf(folder);
        }

        public static string UrlOf(string folder) => new Uri(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri;

        /// <summary>Makes <paramref name="source"/> the copy that every file is read from from now on.</summary>
        public static void SetActive(ContentSource source) => _active = source;

        public static string ExternalFolder(string exeDir) => Path.Combine(exeDir, ContentFolder);

        public static string LastKnownGoodDirectory(string persistentDir) => Path.Combine(persistentDir, LastKnownGoodFolder);

        public static string BuiltInDirectory(string streamingAssetsDir) => Path.Combine(streamingAssetsDir, BuiltInFolder);

        /// <summary>The three places for the running player.</summary>
        public static ContentSource Resolve(IFileProbe probe, params ContentOrigin[] rejected) =>
            Resolve(ExecutableDirectory(), Application.streamingAssetsPath, Application.persistentDataPath, probe, rejected);

        /// <summary>
        /// The first usable copy, in the order above. <paramref name="rejected"/> are origins that are not to be tried again (one that validated but then failed to load). Never throws: when nothing is
        /// usable the built-in folder comes back with a reason that says so, and the loader reports it.
        /// </summary>
        public static ContentSource Resolve(string exeDir, string streamingAssetsDir, string persistentDir, IFileProbe probe, params ContentOrigin[] rejected)
        {
            var skip = new HashSet<ContentOrigin>(rejected ?? new ContentOrigin[0]);
            RecoverLastKnownGood(LastKnownGoodDirectory(persistentDir), probe);
            var candidates = new[]
            {
                (ContentOrigin.External, ExternalFolder(exeDir), "external content"),
                (ContentOrigin.LastKnownGood, LastKnownGoodDirectory(persistentDir), "last known good"),
                (ContentOrigin.BuiltIn, BuiltInDirectory(streamingAssetsDir), "built-in content"),
            };
            var reasons = new List<string>();
            foreach (var (origin, folder, label) in candidates)
            {
                if (skip.Contains(origin)) { reasons.Add($"{label}: could not be loaded"); continue; }
                if (!probe.DirectoryExists(folder)) { reasons.Add($"{label}: folder not found ({folder})"); continue; }
                var errors = ContentFolderValidator.Validate(folder, probe);
                if (errors.Count == 0) return new ContentSource(folder, origin, string.Join(ReasonSeparator, reasons));
                reasons.Add($"{label} rejected ({folder}): {Summary(errors)}");
            }
            reasons.Add("built-in content is also unusable");
            return new ContentSource(candidates[2].Item2, ContentOrigin.BuiltIn, string.Join(ReasonSeparator, reasons));
        }

        /// <summary>
        /// After external content has loaded for real: keeps a copy of it as the last known good. Content from elsewhere is not copied. A copy that fails (a full disk) is reported in the log and
        /// leaves the old copy as it was. Returns whether a copy was made.
        /// </summary>
        public static bool Promote(ContentSource source, string persistentDir, IFileProbe probe)
        {
            if (source.Origin != ContentOrigin.External) return false;
            try
            {
                probe.ReplaceDirectory(source.RootDir, LastKnownGoodDirectory(persistentDir));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[content] could not keep a last-known-good copy: {e.Message}");
                return false;
            }
        }

        /// <summary>A replace of the last-known-good copy that was cut short (power loss) leaves it as <c>.old</c>: it is put back before anyone looks for it.</summary>
        static void RecoverLastKnownGood(string folder, IFileProbe probe)
        {
            try { probe.RecoverInterruptedReplace(folder); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"[content] could not tidy up the last known good copy: {e.Message}"); }
        }

        static string Summary(IReadOnlyList<string> errors)
        {
            var shown = string.Join("; ", errors.Take(ErrorsInReason));
            return errors.Count > ErrorsInReason ? $"{shown}; and {errors.Count - ErrorsInReason} more" : shown;
        }

        static string RepositoryContentFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ContentFolder));
    }
}
