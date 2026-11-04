using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The songs that ship in content/content.json: the song book accepts every one of them, and the rest of the content is still valid with them in it.</summary>
    public class ShippedSongsTests
    {
        static readonly string[] ShippedIds = { "quiet-meadow", "bright-morning", "auld-lang-syne" };
        const int OctaveDownSemitones = -12;

        [Test]
        public void ShippedSongs_AreAllAccepted()
        {
            var result = SongBook.Build(TestContent.LoadShipped());

            Assert.IsEmpty(result.Rejections, string.Join("; ", result.Rejections));
            CollectionAssert.AreEqual(ShippedIds, result.Songs.Select(s => s.Id).ToArray(), "the songs play in the order of the content");
        }

        [Test]
        public void TheHarpPlaysAuldLangSyneAnOctaveDown_AndNoOtherShippedSongMovesAnInstrument()
        {
            var songs = SongBook.Build(TestContent.LoadShipped()).Songs;

            Assert.AreEqual(OctaveDownSemitones, songs.Single(s => s.Id == "auld-lang-syne").OctaveFor(Part.Harp));
            foreach (var song in songs.Where(s => s.Id != "auld-lang-syne"))
            {
                Assert.AreEqual(0, song.OctaveFor(Part.Harp), song.Id);
            }
            foreach (var song in songs)
            {
                Assert.AreEqual(0, song.OctaveFor(Part.Recorder), song.Id);
                Assert.AreEqual(0, song.OctaveFor(Part.Bell), song.Id);
            }
        }

        [Test]
        public void ShippedContent_StillValidates()
        {
            var errors = ContentValidator.Validate(TestContent.LoadShipped());

            Assert.IsEmpty(errors, string.Join("; ", errors));
        }
    }
}
