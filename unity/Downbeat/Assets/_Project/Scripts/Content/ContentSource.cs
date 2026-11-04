namespace Downbeat.Content
{
    /// <summary>Which copy of the content the app runs on.</summary>
    public enum ContentOrigin
    {
        /// <summary>The <c>content</c> folder next to the program: what the exhibition's operator edits.</summary>
        External,
        /// <summary>The last external content that loaded (kept in the user's data folder), used when the external folder is broken or gone.</summary>
        LastKnownGood,
        /// <summary>The content that was built into the program (<c>StreamingAssets/default-content</c>): what a fresh PC starts with.</summary>
        BuiltIn,
    }

    /// <summary>The folder the content is read from, which copy it is, and why a better one was not used (empty when the first choice worked).</summary>
    public sealed class ContentSource
    {
        public ContentSource(string rootDir, ContentOrigin origin, string reason)
        {
            RootDir = rootDir;
            Origin = origin;
            Reason = reason ?? string.Empty;
        }

        public string RootDir { get; }
        public ContentOrigin Origin { get; }
        public string Reason { get; }

        public override string ToString() => Reason.Length == 0 ? $"{Origin} ({RootDir})" : $"{Origin} ({RootDir}); {Reason}";
    }
}
