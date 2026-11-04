using System.IO;

namespace Downbeat.Content
{
    /// <summary>What the content code needs from a file system: reading, and replacing a folder by a copy of another. A fake in memory stands in for it in tests.</summary>
    public interface IFileProbe
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        long FileLength(string path);
        /// <summary>The text of a file; throws <see cref="IOException"/> or <see cref="System.UnauthorizedAccessException"/> when it cannot be read.</summary>
        string ReadAllText(string path);
        /// <summary>Up to <paramref name="count"/> bytes from <paramref name="offset"/> (fewer at the end of the file).</summary>
        byte[] ReadBytes(string path, long offset, int count);
        /// <summary>
        /// Makes <paramref name="destination"/> a copy of <paramref name="source"/>. The copy is first made beside it (<c>destination.new</c>); then the old copy is set aside (<c>destination.old</c>),
        /// the new one is moved into place and the old one is deleted, as well as it can be. A failure before the new copy is in place puts the old copy back; a crash between the two moves leaves
        /// <c>destination.old</c>, which <see cref="RecoverInterruptedReplace"/> puts back (every replace and the loader do that first).
        /// </summary>
        void ReplaceDirectory(string source, string destination);
        /// <summary>
        /// Cleans up after a <see cref="ReplaceDirectory"/> that did not finish: a half-made <c>destination.new</c> is deleted, a <c>destination.old</c> is moved back when <paramref name="destination"/> is
        /// missing (the power went between the two moves: the old copy is the copy) and deleted when it is not.
        /// </summary>
        void RecoverInterruptedReplace(string destination);
    }

    /// <summary>The real disk.</summary>
    public sealed class DiskFileProbe : IFileProbe
    {
        const string StagingSuffix = ".new";
        const string PreviousSuffix = ".old";
        /// <summary>Reported to the step hook of the constructor once the old copy has been set aside and the new one is not yet in place (the moment where a lock or a crash does the harm).</summary>
        public const string StepOldSetAside = "old-set-aside";

        readonly System.Action<string> _step;

        /// <param name="step">For tests: called at the named steps of <see cref="ReplaceDirectory"/>; an exception it throws is a failure at that step.</param>
        public DiskFileProbe(System.Action<string> step = null)
        {
            _step = step;
        }

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public long FileLength(string path) => new FileInfo(path).Length;

        public string ReadAllText(string path) => File.ReadAllText(path);

        public byte[] ReadBytes(string path, long offset, int count)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[count];
                var read = 0;
                while (read < count)
                {
                    var n = stream.Read(buffer, read, count - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read == count) return buffer;
                var shorter = new byte[read];
                System.Array.Copy(buffer, shorter, read);
                return shorter;
            }
        }

        public void ReplaceDirectory(string source, string destination)
        {
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            destination = Trim(destination);
            RecoverInterruptedReplace(destination);
            var staging = destination + StagingSuffix;
            var previous = destination + PreviousSuffix;
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            try
            {
                CopyTree(source, staging);
            }
            catch
            {
                DeleteBestEffort(staging);
                throw;
            }

            var hadPrevious = Directory.Exists(destination);
            try
            {
                if (hadPrevious) Directory.Move(destination, previous);   // a rename: it does not need every file of the old copy to be deletable
                _step?.Invoke(StepOldSetAside);
                Directory.Move(staging, destination);
            }
            catch
            {
                if (hadPrevious && !Directory.Exists(destination) && Directory.Exists(previous)) TryMoveBack(previous, destination);
                DeleteBestEffort(staging);
                throw;
            }
            DeleteBestEffort(previous);   // a file that a virus scanner holds leaves the folder for the next replace or start to remove
        }

        public void RecoverInterruptedReplace(string destination)
        {
            destination = Trim(destination);
            DeleteBestEffort(destination + StagingSuffix);   // an unfinished copy is never used
            var previous = destination + PreviousSuffix;
            if (!Directory.Exists(previous)) return;
            if (Directory.Exists(destination)) DeleteBestEffort(previous);
            else Directory.Move(previous, destination);
        }

        static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        static void TryMoveBack(string previous, string destination)
        {
            try { Directory.Move(previous, destination); }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) { /* the next replace or start moves it back (RecoverInterruptedReplace) */ }
        }

        /// <summary>Deletes a folder and ignores a failure: the files may be read-only (the mark is cleared first) or held by another program for a moment.</summary>
        static void DeleteBestEffort(string folder)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(folder, true);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"[content] could not remove {folder} yet: {e.Message}");
            }
        }

        static void CopyTree(string source, string destination)
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                File.SetAttributes(target, FileAttributes.Normal);   // File.Copy keeps a read-only mark; a copy that cannot be deleted would break the fallback for ever
            }
        }
    }
}
