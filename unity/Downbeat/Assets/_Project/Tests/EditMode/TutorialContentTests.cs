using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.Content;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>The <c>tutorial</c> block of content.json: shipped, defaulted when missing, checked, and named when it changes.</summary>
    public class TutorialContentTests
    {
        static bool Mentions(ContentDto content, string text) => ContentValidator.Validate(content).Any(e => e.Contains(text));

        [Test]
        public void TheShippedContent_HasTheTutorialBlock_WithTheValuesOfTheDataClass()
        {
            var shipped = TestContent.LoadShipped().tutorial;
            var defaults = new TutorialDto();

            Assert.IsNotNull(shipped, "content/content.json has the tutorial block");
            Assert.AreEqual(defaults.idleDelaySec, shipped.idleDelaySec, 1e-6f);
            Assert.AreEqual(defaults.stepSec, shipped.stepSec, 1e-6f);
            Assert.AreEqual(defaults.fadeInSec, shipped.fadeInSec, 1e-6f);
            Assert.AreEqual(defaults.fadeOutSec, shipped.fadeOutSec, 1e-6f);
            Assert.AreEqual(defaults.dim, shipped.dim, 1e-6f);
            CollectionAssert.AreEqual(defaults.texts, shipped.texts, "texts (tools/lib/content-build.mjs has the same defaults)");
            Assert.AreEqual(defaults.endWord, shipped.endWord);
            Assert.AreEqual(defaults.openHandImage, shipped.openHandImage, "openHandImage (tools/lib/content-build.mjs has the same default)");
            Assert.AreEqual(defaults.fistImage, shipped.fistImage, "fistImage");
        }

        [Test]
        public void ThePicturesOfTheHands_AreNamedByDefault_AndTheSettingsCarryTheNames()
        {
            var d = new TutorialDto();

            Assert.AreEqual("tutorial/hand-open.png", d.openHandImage);
            Assert.AreEqual("tutorial/hand-fist.png", d.fistImage);
            var settings = TutorialSettings.From(d);
            Assert.AreEqual("tutorial/hand-open.png", settings.OpenHandImage);
            Assert.AreEqual("tutorial/hand-fist.png", settings.FistImage);
            Assert.AreEqual(string.Empty, TutorialSettings.From(new TutorialDto { openHandImage = null }).OpenHandImage, "no name: the hand is drawn in code");
        }

        [Test]
        public void APicturePath_IsAPlainRelativePngOrJpg_OrEmpty()
        {
            foreach (var good in new[] { "tutorial/hand-open.png", "hands/a.PNG", "x/y/z.jpg", "a.jpeg", string.Empty, null })
            {
                var content = TestContent.LoadShipped();
                content.tutorial.openHandImage = good;
                Assert.IsFalse(Mentions(content, "tutorial.openHandImage"), good ?? "null");
            }
            foreach (var bad in new[] { "/etc/hand.png", "../hand.png", "tutorial/../../hand.png", "C:/hand.png", "tutorial\\hand.png", "tutorial/hand.gif", "tutorial/hand", " tutorial/hand.png", "tutorial//hand.png", "   " })
            {
                var content = TestContent.LoadShipped();
                content.tutorial.fistImage = bad;
                Assert.IsTrue(Mentions(content, "tutorial.fistImage"), "'" + bad + "'");
            }
        }

        [Test]
        public void AContentFileWithoutThePictureKeys_GetsTheDefaultPictures()
        {
            var content = JsonUtility.FromJson<ContentDto>("{\"tutorial\":{\"idleDelaySec\":12}}");

            Assert.AreEqual("tutorial/hand-open.png", content.tutorial.openHandImage);
            Assert.AreEqual("tutorial/hand-fist.png", content.tutorial.fistImage);
        }

        [Test]
        public void ThePicturesToLoad_AreTheNamedOnes_EachOnce_AndNoneForAnEmptyPath()
        {
            CollectionAssert.AreEqual(new[] { "tutorial/hand-open.png", "tutorial/hand-fist.png" }, ContentRepository.TutorialImagePaths(new TutorialDto()).ToArray());
            CollectionAssert.AreEqual(new[] { "tutorial/hand-open.png", "tutorial/hand-fist.png" }, ContentRepository.TutorialImagePaths(null).ToArray(), "a file without the block has the defaults");
            CollectionAssert.AreEqual(new[] { "a.png" }, ContentRepository.TutorialImagePaths(new TutorialDto { openHandImage = "a.png", fistImage = "a.png" }).ToArray());
            CollectionAssert.AreEqual(new[] { "b.png" }, ContentRepository.TutorialImagePaths(new TutorialDto { openHandImage = string.Empty, fistImage = "b.png" }).ToArray());
        }

        [Test]
        public void TheDefaults_AreTheUsersDecisions()
        {
            var d = new TutorialDto();

            Assert.AreEqual(10f, d.idleDelaySec, "it comes after ten quiet seconds");
            Assert.AreEqual(5f, d.stepSec, "three steps of five seconds");
            Assert.AreEqual(0.6f, d.fadeInSec);
            Assert.AreEqual(0.8f, d.fadeOutSec);
            Assert.AreEqual(0.45f, d.dim);
            CollectionAssert.AreEqual(new[] { "손을 펴서 들어 보세요", "손을 옮기면 악기가 바뀌어요", "두 주먹을 쥐고 벌리면 끝" }, d.texts);
            Assert.IsEmpty(ContentValidator.Validate(TestContent.LoadShipped()), "the shipped content passes every check");
        }

        [Test]
        public void AContentFileWithoutTheBlock_StillLoads_AndGetsTheDefaults()
        {
            var json = JsonUtility.ToJson(TestContent.LoadShipped());
            json = Regex.Replace(json, ",\"tutorial\":\\{[^{}]*\"texts\":\\[[^\\]]*\\][^{}]*\\}", string.Empty);
            Assert.IsFalse(json.Contains("\"tutorial\""), "the test content has no tutorial block");

            var content = JsonUtility.FromJson<ContentDto>(json);
            var settings = TutorialSettings.From(content.tutorial);

            Assert.IsEmpty(ContentValidator.Validate(content), "an older content.json still passes the checks");
            Assert.AreEqual(10.0, settings.IdleDelaySec);
            Assert.AreEqual(5.0, settings.StepSec);
            Assert.AreEqual("손을 펴서 들어 보세요", settings.Texts[0]);
        }

        [Test]
        public void ABlockWithOnlySomeKeys_GetsTheOthersFromTheDefaults()
        {
            var content = JsonUtility.FromJson<ContentDto>("{\"tutorial\":{\"idleDelaySec\":20}}");

            Assert.AreEqual(20f, content.tutorial.idleDelaySec);
            Assert.AreEqual(5f, content.tutorial.stepSec);
            Assert.AreEqual(3, content.tutorial.texts.Length);
        }

        [Test]
        public void NumbersOutsideTheirRange_AreReportedWithTheirNames()
        {
            AssertRange(c => c.tutorial.idleDelaySec, (c, v) => c.tutorial.idleDelaySec = v, "tutorial.idleDelaySec", 2.9f, 120.5f);
            AssertRange(c => c.tutorial.stepSec, (c, v) => c.tutorial.stepSec = v, "tutorial.stepSec", 1.9f, 20.5f);
            AssertRange(c => c.tutorial.fadeInSec, (c, v) => c.tutorial.fadeInSec = v, "tutorial.fadeInSec", -0.1f, 3.5f);
            AssertRange(c => c.tutorial.fadeOutSec, (c, v) => c.tutorial.fadeOutSec = v, "tutorial.fadeOutSec", -0.1f, 3.5f);
            AssertRange(c => c.tutorial.dim, (c, v) => c.tutorial.dim = v, "tutorial.dim", -0.1f, 0.95f);
        }

        static void AssertRange(System.Func<ContentDto, float> read, System.Action<ContentDto, float> write, string name, float below, float above)
        {
            foreach (var bad in new[] { below, above, float.NaN })
            {
                var content = TestContent.LoadShipped();
                write(content, bad);
                Assert.IsTrue(Mentions(content, name), $"{name} = {bad}");
            }
            foreach (var good in new[] { read(TestContent.LoadShipped()) })
            {
                var content = TestContent.LoadShipped();
                write(content, good);
                Assert.IsFalse(Mentions(content, name), $"{name} = {good} is the shipped value");
            }
        }

        [Test]
        public void TheEdgesOfTheRanges_Pass()
        {
            var content = TestContent.LoadShipped();
            content.tutorial.idleDelaySec = 3f;
            content.tutorial.stepSec = 2f;
            content.tutorial.fadeInSec = 0f;
            content.tutorial.fadeOutSec = 3f;
            content.tutorial.dim = 0.9f;

            Assert.IsEmpty(ContentValidator.Validate(content));
        }

        [Test]
        public void TheTexts_NeedThreeNonEmptyLines()
        {
            foreach (var texts in new[] { new string[0], new[] { "a", "b" }, new[] { "a", "b", "c", "d" }, new[] { "a", " ", "c" }, new[] { "a", "b", null }, null })
            {
                var content = TestContent.LoadShipped();
                content.tutorial.texts = texts;
                Assert.IsTrue(Mentions(content, "tutorial.texts"), texts == null ? "null" : string.Join("|", texts));
            }
        }

        [Test]
        public void AVeryLongLine_IsReported()
        {
            var content = TestContent.LoadShipped();
            content.tutorial.texts = new[] { "a", new string('가', 61), "c" };

            Assert.IsTrue(Mentions(content, "tutorial.texts"));
        }

        [Test]
        public void TheEndWord_NeedsAFewCharacters()
        {
            foreach (var word in new[] { string.Empty, " ", null, "아주아주긴단어입니다" })
            {
                var content = TestContent.LoadShipped();
                content.tutorial.endWord = word;
                Assert.IsTrue(Mentions(content, "tutorial.endWord"), word ?? "null");
            }
        }

        [Test]
        public void ABlockThatIsMissing_IsNotAnError()
        {
            var content = TestContent.LoadShipped();
            content.tutorial = null;

            Assert.IsEmpty(ContentValidator.Validate(content), "the block is optional: the defaults apply");
            Assert.AreEqual(10.0, TutorialSettings.From(null).IdleDelaySec);
        }

        [Test]
        public void TheSettings_FollowTheBlock()
        {
            var dto = new TutorialDto { idleDelaySec = 20f, stepSec = 4f, fadeInSec = 1f, fadeOutSec = 0.5f, dim = 0.3f, texts = new[] { "하나", "둘", "셋" }, endWord = "그만" };

            var settings = TutorialSettings.From(dto);

            Assert.AreEqual(20.0, settings.IdleDelaySec);
            Assert.AreEqual(4.0, settings.StepSec);
            Assert.AreEqual(1.0, settings.FadeInSec);
            Assert.AreEqual(0.5, settings.FadeOutSec);
            Assert.AreEqual(0.3f, settings.Dim);
            CollectionAssert.AreEqual(new[] { "하나", "둘", "셋" }, settings.Texts);
            Assert.AreEqual("그만", settings.EndWord);
        }

        [Test]
        public void TheSettings_DoNotChange_WhenTheDtoDoes()
        {
            var dto = new TutorialDto();
            var settings = TutorialSettings.From(dto);

            dto.texts[0] = "changed";
            dto.idleDelaySec = 99f;

            Assert.AreEqual("손을 펴서 들어 보세요", settings.Texts[0], "the content file can be replaced while the tutorial goes on");
            Assert.AreEqual(10.0, settings.IdleDelaySec);
        }

        [Test]
        public void ATutorialChange_IsNamed_AndWaitsForARestart()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.tutorial.idleDelaySec += 5f;

            CollectionAssert.AreEqual(new[] { "tutorial" }, ContentSections.Changed(before, after));
            CollectionAssert.AreEqual(new[] { "tutorial" }, ContentSections.NeedingRestart(before, after), "F5 says so instead of silently ignoring it");
        }

        [Test]
        public void ATutorialTextChange_IsNamedToo()
        {
            var before = TestContent.LoadShipped();
            var after = TestContent.LoadShipped();
            after.tutorial.texts[1] = "다른 글";

            CollectionAssert.AreEqual(new[] { "tutorial" }, ContentSections.Changed(before, after));
        }
    }
}
