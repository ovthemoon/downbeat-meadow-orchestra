using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class HandLinkSettingsTests
    {
        [Test]
        public void Defaults_MatchTheTrackerDefaults()
        {
            var link = new HandSettingsHolder().Link;

            Assert.AreEqual(47800, link.UdpPort);
            Assert.AreEqual(2.0, link.TrackerTimeoutSec, 1e-12);
            Assert.IsTrue(link.MirrorX);
        }

        [Test]
        public void Set_CarriesTheLinkSettingsOfTheContent()
        {
            var holder = new HandSettingsHolder();

            holder.Set(new HandSettingsDto { udpPort = 51000, trackerTimeoutSec = 3.5f, mirrorX = false });

            Assert.AreEqual(51000, holder.Link.UdpPort);
            Assert.AreEqual(3.5, holder.Link.TrackerTimeoutSec, 1e-6);
            Assert.IsFalse(holder.Link.MirrorX);
        }
    }
}
