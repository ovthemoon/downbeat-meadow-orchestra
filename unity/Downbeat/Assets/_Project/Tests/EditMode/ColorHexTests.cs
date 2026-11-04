using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ColorHexTests
    {
        [Test]
        public void TryParse_ReadsHashAndBareHex()
        {
            Assert.IsTrue(ColorHex.TryParse("#FF8000", out var a));
            Assert.AreEqual(1f, a.R, 1e-6f);
            Assert.AreEqual(128f / 255f, a.G, 1e-6f);
            Assert.AreEqual(0f, a.B, 1e-6f);
            Assert.IsTrue(ColorHex.TryParse("0a141e", out var b));
            Assert.AreEqual(10f / 255f, b.R, 1e-6f);
            Assert.AreEqual(30f / 255f, b.B, 1e-6f);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("#12345")]
        [TestCase("#1234567")]
        [TestCase("#GG0000")]
        [TestCase("red")]
        public void TryParse_RejectsAnythingElse(string text)
        {
            Assert.IsFalse(ColorHex.TryParse(text, out _));
        }
    }
}
