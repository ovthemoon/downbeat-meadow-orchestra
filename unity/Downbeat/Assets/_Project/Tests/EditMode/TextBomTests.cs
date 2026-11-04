using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class TextBomTests
    {
        [Test]
        public void Strip_RemovesTheByteOrderMarkThatNotepadAndPowerShellWrite()
        {
            Assert.AreEqual("{ \"a\": 1 }", TextBom.Strip("\uFEFF{ \"a\": 1 }"));
        }

        [Test]
        public void Strip_LeavesTextWithoutOneAlone()
        {
            Assert.AreEqual("{ }", TextBom.Strip("{ }"));
            Assert.AreEqual(string.Empty, TextBom.Strip(string.Empty));
            Assert.IsNull(TextBom.Strip(null));
        }

        [Test]
        public void Strip_RemovesOnlyTheOneAtTheStart()
        {
            Assert.AreEqual("a\uFEFFb", TextBom.Strip("\uFEFFa\uFEFFb"));
        }

        [Test]
        public void AContentJsonWithAByteOrderMark_ParsesOnceStripped()
        {
            var json = "\uFEFF" + UnityEngine.JsonUtility.ToJson(TestContent.LoadShipped());

            Assert.Throws<System.ArgumentException>(() => UnityEngine.JsonUtility.FromJson<ContentDto>(json), "the mark is what broke the loader");
            Assert.DoesNotThrow(() => UnityEngine.JsonUtility.FromJson<ContentDto>(TextBom.Strip(json)));
        }
    }
}
