using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>One line the app writes to its log about the songs, and whether it is a warning.</summary>
    public readonly struct SongLogLine
    {
        public SongLogLine(string text, bool isWarning)
        {
            Text = text;
            IsWarning = isWarning;
        }

        public string Text { get; }
        public bool IsWarning { get; }
    }

    /// <summary>What the log says about the songs of the content when the app starts: each song that was left out and why, how many songs there are, and, when none is left, that the visits play freely.</summary>
    public static class SongBookReport
    {
        public static IReadOnlyList<SongLogLine> Lines(SongBookResult book)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));

            var lines = new List<SongLogLine>(book.Rejections.Count + 2);
            foreach (var rejection in book.Rejections) lines.Add(new SongLogLine("[songs] left out " + rejection, true));
            lines.Add(new SongLogLine($"[songs] book: {book.Songs.Count} songs, {book.Rejections.Count} rejected", false));
            if (book.Songs.Count == 0 && book.Rejections.Count > 0) lines.Add(new SongLogLine("[songs] no song can be played: every visit is free play", true));
            return lines;
        }
    }
}
