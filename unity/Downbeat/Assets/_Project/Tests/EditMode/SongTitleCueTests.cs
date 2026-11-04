using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SongTitleCueTests
    {
        const double BarOne = 12.5;

        [Test]
        public void TheTitle_IsNotDueBeforeBarOne_AndIsGivenOnceFromThenOn()
        {
            var cue = new SongTitleCue();
            cue.Arm("잔잔한 들판", BarOne);

            Assert.IsFalse(cue.TryTake(BarOne - 0.001, out _), "the clock has not reached the bar yet");
            Assert.IsTrue(cue.IsArmed);
            Assert.IsTrue(cue.TryTake(BarOne, out var title), "on the bar line");
            Assert.AreEqual("잔잔한 들판", title);
            Assert.IsFalse(cue.IsArmed);
            Assert.IsFalse(cue.TryTake(BarOne + 1.0, out _), "and not again");
        }

        [Test]
        public void ALongFrame_StillGivesTheTitle_WhenTheClockIsPastTheBar()
        {
            var cue = new SongTitleCue();
            cue.Arm("밝은 아침", BarOne);

            Assert.IsTrue(cue.TryTake(BarOne + 0.4, out var title));
            Assert.AreEqual("밝은 아침", title);
        }

        [Test]
        public void ACancelledCue_GivesNothing_ForTheVisitThatEndedBeforeTheSong()
        {
            var cue = new SongTitleCue();
            cue.Arm("잔잔한 들판", BarOne);

            cue.Cancel();

            Assert.IsFalse(cue.IsArmed);
            Assert.IsFalse(cue.TryTake(BarOne + 5.0, out _));
        }

        [Test]
        public void ANewSong_ReplacesTheCueThatWasWaiting()
        {
            var cue = new SongTitleCue();
            cue.Arm("잔잔한 들판", BarOne);

            cue.Arm("밝은 아침", BarOne + 60.0);

            Assert.IsFalse(cue.TryTake(BarOne + 1.0, out _));
            Assert.IsTrue(cue.TryTake(BarOne + 60.0, out var title));
            Assert.AreEqual("밝은 아침", title);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ASongWithoutATitle_HasNothingToShow(string title)
        {
            var cue = new SongTitleCue();

            cue.Arm(title, BarOne);

            Assert.IsFalse(cue.IsArmed);
            Assert.IsFalse(cue.TryTake(BarOne + 1.0, out _));
        }

        [TestCase(3f, 3.0)]
        [TestCase(0.5f, 0.5)]
        [TestCase(10f, 10.0)]
        [TestCase(0f, SongTitleCue.MinDurationSec)]
        [TestCase(-4f, SongTitleCue.MinDurationSec)]
        [TestCase(0.01f, SongTitleCue.MinDurationSec)]
        [TestCase(600f, SongTitleCue.MaxDurationSec)]
        [TestCase(float.PositiveInfinity, SongTitleCue.MaxDurationSec)]
        [TestCase(float.NaN, SongTitleCue.DefaultDurationSec)]
        public void TheTime_FromTheContent_IsKeptInASaneRange(float configured, double expected)
        {
            Assert.AreEqual(expected, SongTitleCue.DurationSec(configured), 1e-9);
        }
    }
}
