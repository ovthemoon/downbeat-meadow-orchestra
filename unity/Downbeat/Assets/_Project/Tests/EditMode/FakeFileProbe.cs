using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Downbeat.Content;

namespace Downbeat.Tests
{
    /// <summary>A file system in memory for the content tests: paths use "/", a folder exists when a file lies below it.</summary>
    sealed class FakeFileProbe : IFileProbe
    {
        static Dictionary<string, byte[]> _shippedFiles;

        readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>();

        /// <summary>Folders that were replaced through <see cref="ReplaceDirectory"/>: "source -> destination".</summary>
        public List<string> Replacements { get; } = new List<string>();
        /// <summary>When set, <see cref="ReplaceDirectory"/> throws this (a disk that is full or locked).</summary>
        public Exception FailReplaceWith { get; set; }

        public static string Join(string folder, string relative) => folder.TrimEnd('/') + "/" + relative;

        /// <summary>The repository's real content folder, copied into memory below <paramref name="folder"/> (the files are read once per test run).</summary>
        public FakeFileProbe AddShippedContent(string folder)
        {
            if (_shippedFiles == null)
            {
                _shippedFiles = new Dictionary<string, byte[]>();
                var root = Path.GetDirectoryName(TestContent.RepoContentPath);
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    _shippedFiles[file.Substring(root.Length + 1).Replace('\\', '/')] = File.ReadAllBytes(file);
                }
            }
            foreach (var entry in _shippedFiles) _files[Join(folder, entry.Key)] = entry.Value;
            return this;
        }

        public FakeFileProbe Put(string path, byte[] bytes) { _files[path] = bytes; return this; }

        public FakeFileProbe Put(string path, string text) => Put(path, new UTF8Encoding(false).GetBytes(text));

        public FakeFileProbe Remove(string path) { _files.Remove(path); return this; }

        public bool Has(string path) => _files.ContainsKey(path);

        public byte[] Bytes(string path) => _files[path];

        public bool DirectoryExists(string path) => _files.Keys.Any(k => k.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal));

        public bool FileExists(string path) => _files.ContainsKey(path);

        public long FileLength(string path) => _files.TryGetValue(path, out var bytes) ? bytes.Length : throw new FileNotFoundException(path);

        public string ReadAllText(string path) => _files.TryGetValue(path, out var bytes) ? new UTF8Encoding(false).GetString(bytes) : throw new FileNotFoundException(path);

        public byte[] ReadBytes(string path, long offset, int count)
        {
            if (!_files.TryGetValue(path, out var bytes)) throw new FileNotFoundException(path);
            var available = (int)Math.Max(0, Math.Min(count, bytes.Length - offset));
            var slice = new byte[available];
            if (available > 0) Array.Copy(bytes, offset, slice, 0, available);
            return slice;
        }

        public void RecoverInterruptedReplace(string destination)
        {
            var folder = destination.TrimEnd('/');
            foreach (var stale in _files.Keys.Where(k => k.StartsWith(folder + ".new/", StringComparison.Ordinal)).ToList()) _files.Remove(stale);
            var oldPrefix = folder + ".old/";
            var old = _files.Where(e => e.Key.StartsWith(oldPrefix, StringComparison.Ordinal)).ToList();
            if (old.Count == 0) return;
            var destinationExists = _files.Keys.Any(k => k.StartsWith(folder + "/", StringComparison.Ordinal));
            foreach (var entry in old)
            {
                _files.Remove(entry.Key);
                if (!destinationExists) _files[folder + "/" + entry.Key.Substring(oldPrefix.Length)] = entry.Value;
            }
        }

        public void ReplaceDirectory(string source, string destination)
        {
            if (FailReplaceWith != null) throw FailReplaceWith;
            Replacements.Add(source + " -> " + destination);
            var sourcePrefix = source.TrimEnd('/') + "/";
            var destinationPrefix = destination.TrimEnd('/') + "/";
            foreach (var stale in _files.Keys.Where(k => k.StartsWith(destinationPrefix, StringComparison.Ordinal)).ToList()) _files.Remove(stale);
            foreach (var entry in _files.Where(e => e.Key.StartsWith(sourcePrefix, StringComparison.Ordinal)).ToList()) _files[destinationPrefix + entry.Key.Substring(sourcePrefix.Length)] = entry.Value;
        }
    }
}
