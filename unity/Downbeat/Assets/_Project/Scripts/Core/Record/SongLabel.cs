namespace Downbeat.Core
{
    /// <summary>
    /// Which song a visit was played to: its id and the title that is shown. <c>default</c> is no song (free play). The recorder asks for one when a visit ends and the card keeps the two texts.
    /// </summary>
    public readonly struct SongLabel
    {
        readonly string _id;
        readonly string _title;

        public SongLabel(string id, string title)
        {
            _id = id;
            _title = title;
        }

        /// <summary>The id of the song; never null (empty for no song).</summary>
        public string Id => _id ?? string.Empty;
        /// <summary>The title that is shown; never null (empty when there is none).</summary>
        public string Title => _title ?? string.Empty;
        /// <summary>True for <c>default</c> and for a label whose id and title are both blank: there is no song to name.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(_id) && string.IsNullOrWhiteSpace(_title);
    }
}
