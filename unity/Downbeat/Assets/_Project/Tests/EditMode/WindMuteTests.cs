using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class WindMuteTests
    {
        [Test]
        public void Parse_EmptyOrNull_MutesNothing()
        {
            Assert.AreEqual(WindTerm.None, WindMute.Parse(null, out var unknownNull));
            Assert.AreEqual(string.Empty, unknownNull);
            Assert.AreEqual(WindTerm.None, WindMute.Parse("  ", out var unknownBlank));
            Assert.AreEqual(string.Empty, unknownBlank);
        }

        [Test]
        public void Parse_NamesAreCaseInsensitive_AndTrimmed()
        {
            var terms = WindMute.Parse(" Breeze , HANDS,waves", out var unknown);

            Assert.AreEqual(WindTerm.Breeze | WindTerm.Hands | WindTerm.Waves, terms);
            Assert.AreEqual(string.Empty, unknown);
        }

        [Test]
        public void Parse_UnknownNamesAreReportedNotIgnored()
        {
            var terms = WindMute.Parse("breeze,tornado,sparkle,gale", out var unknown);

            Assert.AreEqual(WindTerm.Breeze | WindTerm.Sparkle, terms, "the known names still count");
            Assert.AreEqual("tornado,gale", unknown);
        }

        [Test]
        public void Parse_All_MutesEveryTerm()
        {
            var terms = WindMute.Parse("all", out var unknown);

            Assert.AreEqual(string.Empty, unknown);
            foreach (WindTerm term in System.Enum.GetValues(typeof(WindTerm)))
            {
                if (term == WindTerm.None) continue;
                Assert.IsTrue((terms & term) == term, term + " is muted by 'all'");
            }
        }

        [Test]
        public void ToShaderFlags_OneForKeptTermZeroForMuted()
        {
            var flags = WindMute.ToShaderFlags(WindTerm.Waves | WindTerm.Sparkle);

            Assert.AreEqual(1f, flags.breeze);
            Assert.AreEqual(0f, flags.waves);
            Assert.AreEqual(1f, flags.hands);
            Assert.AreEqual(0f, flags.sparkle);
            Assert.AreEqual(1f, flags.gusts);
            Assert.AreEqual(1f, flags.sway);
            Assert.AreEqual(1f, flags.flutter);
        }

        [Test]
        public void ToShaderFlags_TheSteadyWindCanBeTakenApartOneTermAtATime()
        {
            var flags = WindMute.ToShaderFlags(WindTerm.Gusts | WindTerm.Flutter);

            Assert.AreEqual(1f, flags.breeze, "the breeze as a whole still works");
            Assert.AreEqual(0f, flags.gusts);
            Assert.AreEqual(1f, flags.sway);
            Assert.AreEqual(0f, flags.flutter);
        }
    }
}
