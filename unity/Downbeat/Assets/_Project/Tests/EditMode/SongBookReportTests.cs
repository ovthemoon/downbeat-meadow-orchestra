using System;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SongBookReportTests
    {
        static SongScore Song(string id) => new SongScore(id, id, "", new[] { new SongBar(57, new[] { 57, 60, 64 }, Array.Empty<SongNote>()) });

        static SongBookResult Book(int songs, params string[] rejections) =>
            new SongBookResult(Enumerable.Range(0, songs).Select(i => Song("song-" + i)).ToList(), rejections);

        [Test]
        public void ABookWithAllItsSongs_IsOneLineThatSaysHowManyAndNoWarning()
        {
            var lines = SongBookReport.Lines(Book(3));

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("[songs] book: 3 songs, 0 rejected", lines[0].Text);
            Assert.IsFalse(lines[0].IsWarning);
        }

        [Test]
        public void ASongThatWasLeftOut_IsAWarningWithItsReason_AndCountsInTheBookLine()
        {
            var lines = SongBookReport.Lines(Book(2, "song 'old': bell cannot play midi 77"));

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("[songs] left out song 'old': bell cannot play midi 77", lines[0].Text);
            Assert.IsTrue(lines[0].IsWarning);
            Assert.AreEqual("[songs] book: 2 songs, 1 rejected", lines[1].Text);
            Assert.IsFalse(lines[1].IsWarning);
        }

        [Test]
        public void WhenNoSongIsLeft_OneMoreWarningSaysThatTheVisitsPlayFreely()
        {
            var lines = SongBookReport.Lines(Book(0, "song 'a': wrong tempo", "song 'b': no bars"));

            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual(2, lines.Count(l => l.Text.StartsWith("[songs] left out")));
            Assert.AreEqual("[songs] book: 0 songs, 2 rejected", lines[2].Text);
            Assert.IsTrue(lines[3].IsWarning);
            StringAssert.Contains("free play", lines[3].Text);
            Assert.AreEqual(1, lines.Count(l => l.Text.Contains("free play")), "said once");
        }

        [Test]
        public void ContentWithoutSongs_IsNotAProblem_OneQuietLine()
        {
            var lines = SongBookReport.Lines(Book(0));

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("[songs] book: 0 songs, 0 rejected", lines[0].Text);
            Assert.IsFalse(lines[0].IsWarning, "a content file from before songs plays as it always did");
        }

        [Test]
        public void ANullBook_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => SongBookReport.Lines(null));
        }
    }
}
