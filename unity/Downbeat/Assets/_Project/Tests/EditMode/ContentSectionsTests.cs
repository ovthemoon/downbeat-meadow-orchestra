using System.Linq;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ContentSectionsTests
    {
        [Test]
        public void Changed_IsEmpty_ForTwoCopiesOfTheSameContent()
        {
            Assert.IsEmpty(ContentSections.Changed(TestContent.LoadShipped(), TestContent.LoadShipped()));
        }

        [Test]
        public void Changed_NamesOnlyTheSectionsThatDiffer()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.meadow.wind.beatSweep.amplitude *= 2f;
            after.song.bpm += 4f;

            CollectionAssert.AreEquivalent(new[] { "meadow", "song" }, ContentSections.Changed(before, after));
        }

        [Test]
        public void MeadowOnlyChange_CanBeAppliedWithoutARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.meadow.wind.beatSweep.amplitude *= 2f;

            CollectionAssert.AreEqual(new[] { "meadow" }, ContentSections.Changed(before, after));
            Assert.IsEmpty(ContentSections.NeedingRestart(before, after), "the meadow is the section that is rebuilt while running");
        }

        [Test]
        public void TheNoteTrail_IsAppliedByTheMeadowRebuild_WhenTheMeadowChangesToo_AndWaitsForARestartWhenItChangesAlone()
        {
            var before = TestContent.LoadShipped();
            var alone = TestContent.LoadShipped();
            alone.trail.notesPerSec += 2f;
            var withMeadow = TestContent.LoadShipped();
            withMeadow.trail.notesPerSec += 2f;
            withMeadow.meadow.wind.beatSweep.amplitude *= 2f;
            withMeadow.conduct.releaseSec += 0.5f;

            CollectionAssert.AreEqual(new[] { "trail" }, ContentSections.NeedingRestart(before, alone), "nothing is rebuilt for a trail that changed alone: the log says it waits for the next start");
            CollectionAssert.AreEquivalent(new[] { "meadow", "trail", "conduct" }, ContentSections.Changed(before, withMeadow));
            CollectionAssert.AreEqual(new[] { "conduct" }, ContentSections.NeedingRestart(before, withMeadow), "the rebuild of the meadow makes the trail again: only the conducting waits");
        }

        [Test]
        public void NeedingRestart_ListsTheSectionsThatCannotChangeWhileRunning()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.meadow.wind.beatSweep.amplitude *= 2f;
            after.audio.maxVoices += 1;
            after.instruments[0].gainDb -= 1f;

            CollectionAssert.AreEquivalent(new[] { "audio", "instruments" }, ContentSections.NeedingRestart(before, after));
        }

        [Test]
        public void ContentSections_ReportsSongs()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.songs[0].title += " (changed)";

            CollectionAssert.AreEqual(new[] { "songs" }, ContentSections.Changed(before, after));
            CollectionAssert.AreEqual(new[] { "songs" }, ContentSections.NeedingRestart(before, after), "the songs are read when the app starts: F5 says so instead of silently ignoring them");
        }

        [Test]
        public void ContentSections_ReportsSongs_WhenOneIsAdded_OrTheListIsRemoved()
        {
            var before = TestContent.LoadShipped();
            var added = TestContent.LoadShipped();
            added.songs = added.songs.Concat(new[] { added.songs[0] }).ToArray();
            var removed = TestContent.LoadShipped();
            removed.songs = null;

            CollectionAssert.AreEqual(new[] { "songs" }, ContentSections.Changed(before, added));
            CollectionAssert.AreEqual(new[] { "songs" }, ContentSections.Changed(before, removed));
        }

        [Test]
        public void ContentSections_DoesNotReportSongs_ForTwoCopiesOfTheSameSongs()
        {
            CollectionAssert.DoesNotContain(ContentSections.Changed(TestContent.LoadShipped(), TestContent.LoadShipped()), "songs");
        }

        [Test]
        public void TheConductingAndTheAutomaticQuality_AreNamedWhenTheyChange_AndWaitForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.conduct.releaseSec += 0.5f;
            after.autoQuality.upDwellSec += 10f;

            CollectionAssert.AreEquivalent(new[] { "conduct", "autoQuality" }, ContentSections.NeedingRestart(before, after), "F5 says so instead of silently ignoring them");
        }
    }
}
