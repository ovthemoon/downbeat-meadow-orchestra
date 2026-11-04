using System.Collections.Generic;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The <c>pose</c> block of content.json: its ranges, the rule between the two thresholds, and that a change waits for a restart.</summary>
    public class PoseContentTests
    {
        static List<string> Check(PoseDto dto)
        {
            var errors = new List<string>();
            PoseContentValidator.Check(dto, errors);
            return errors;
        }

        [Test]
        public void TheDefaults_NoBlock_AndTheShippedContentPass()
        {
            Assert.IsEmpty(Check(new PoseDto()));
            Assert.IsEmpty(Check(null), "no block at all: the defaults apply");
            Assert.IsEmpty(ContentValidator.Validate(TestContent.LoadShipped()));
        }

        [TestCase(1.05f)]
        [TestCase(3.5f)]
        [TestCase(float.NaN)]
        public void OpenRatio_HasARange(float value)
        {
            Assert.That(Check(new PoseDto { openRatio = value }), Has.Some.Contains("pose.openRatio must be 1.1..3"));
        }

        [TestCase(0.4f)]
        [TestCase(1.6f)]
        [TestCase(float.NaN)]
        public void FistRatio_HasARange(float value)
        {
            Assert.That(Check(new PoseDto { fistRatio = value }), Has.Some.Contains("pose.fistRatio must be 0.5..1.5"));
        }

        [TestCase(0)]
        [TestCase(11)]
        public void ConfirmFrames_HasARange(int value)
        {
            Assert.That(Check(new PoseDto { confirmFrames = value }), Has.Some.Contains("pose.confirmFrames must be 1..10"));
        }

        [Test]
        public void FistRatio_MustBeBelowOpenRatio()
        {
            Assert.That(Check(new PoseDto { openRatio = 1.2f, fistRatio = 1.2f }), Has.Some.Contains("pose.fistRatio must be less than pose.openRatio"));
            Assert.IsEmpty(Check(new PoseDto { openRatio = 1.2f, fistRatio = 1.19f }));
        }

        [Test]
        public void TheRangesAreReportedThroughTheWholeContentCheck()
        {
            var content = TestContent.LoadShipped();
            content.pose.confirmFrames = 0;

            Assert.That(ContentValidator.Validate(content), Has.Some.Contains("pose.confirmFrames"));
        }

        [Test]
        public void APoseChange_IsNamed_AndWaitsForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.pose.openRatio += 0.1f;

            CollectionAssert.AreEqual(new[] { "pose" }, ContentSections.Changed(before, after));
            CollectionAssert.AreEqual(new[] { "pose" }, ContentSections.NeedingRestart(before, after), "F5 says so: the thresholds are read when the app starts");
        }
    }
}
