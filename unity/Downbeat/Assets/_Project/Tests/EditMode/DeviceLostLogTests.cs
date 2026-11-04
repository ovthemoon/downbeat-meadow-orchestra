using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class DeviceLostLogTests
    {
        /// <summary>The last line of the player log of the Windows soak before the player froze (docs/windows-soak-2026-11-02.md).</summary>
        const string SoakLine = "d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x887A0005]";

        [Test]
        public void TheLineThatEndedTheWindowsSoak_IsADeviceLost()
        {
            Assert.IsTrue(DeviceLostLog.IsDeviceLost(SoakLine));
        }

        [TestCase("d3d11: failed to create buffer (target 0x2 mode 0 size 12) [0x887A0005]", TestName = "DXGI_ERROR_DEVICE_REMOVED as a number")]
        [TestCase("d3d11: swap chain present failed [0x887A0006]", TestName = "DXGI_ERROR_DEVICE_HUNG as a number")]
        [TestCase("d3d11: swap chain present failed [0x887A0007]", TestName = "DXGI_ERROR_DEVICE_RESET as a number")]
        [TestCase("Present failed: DXGI_ERROR_DEVICE_REMOVED", TestName = "DEVICE_REMOVED as a word")]
        [TestCase("Present failed: DXGI_ERROR_DEVICE_HUNG", TestName = "DEVICE_HUNG as a word")]
        [TestCase("Present failed: DXGI_ERROR_DEVICE_RESET", TestName = "DEVICE_RESET as a word")]
        [TestCase("d3d11: failed to create buffer [0x887a0005]", TestName = "the number in lower case")]
        [TestCase("present failed: dxgi_error_device_removed", TestName = "the word in lower case")]
        public void TheDeviceLostSigns_AreRecognised(string line)
        {
            Assert.IsTrue(DeviceLostLog.IsDeviceLost(line));
        }

        [TestCase("[Downbeat] ready")]
        [TestCase("[health] writing to logs every 60 s")]
        [TestCase("d3d11: failed to create buffer (target 0x5 mode 0 size 96) [0x80070057]", TestName = "another failed call (E_INVALIDARG) is not a lost device")]
        [TestCase("d3d11: failed to create buffer [0x887A0001]", TestName = "DXGI_ERROR_INVALID_CALL is not a lost device")]
        [TestCase("")]
        [TestCase(null)]
        public void OrdinaryLines_AreNot(string line)
        {
            Assert.IsFalse(DeviceLostLog.IsDeviceLost(line));
        }

        [Test]
        public void ThePatternList_HasTheThreeDxgiCodesAndTheirWords_WithoutDuplicates()
        {
            CollectionAssert.IsSupersetOf(DeviceLostLog.Patterns, new[] { "0x887A0005", "0x887A0006", "0x887A0007", "DEVICE_REMOVED" });
            Assert.AreEqual(DeviceLostLog.Patterns.Count, DeviceLostLog.Patterns.Select(p => p.ToUpperInvariant()).Distinct().Count());
            Assert.IsTrue(DeviceLostLog.Patterns.All(p => p.Length >= 10), "a short pattern would match ordinary text");
        }
    }
}
