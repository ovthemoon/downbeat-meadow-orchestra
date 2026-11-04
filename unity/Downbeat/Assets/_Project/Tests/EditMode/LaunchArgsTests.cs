using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class LaunchArgsTests
    {
        [Test]
        public void Get_ReturnsValue_WhenGivenAsNextToken()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "--input", "replay" });

            Assert.AreEqual("replay", args.Get("input"));
        }

        [Test]
        public void Get_ReturnsValue_WhenGivenWithEqualsSign()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "--file=replay/a.json" });

            Assert.AreEqual("replay/a.json", args.Get("file"));
        }

        [Test]
        public void Get_ReturnsNull_WhenOptionIsAbsent()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "--input", "mouse" });

            Assert.IsNull(args.Get("view"));
            Assert.IsFalse(args.Has("view"));
        }

        [Test]
        public void Has_IsTrueAndValueIsEmpty_WhenOptionIsAFlag()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "--measure-clock" });

            Assert.IsTrue(args.Has("measure-clock"));
            Assert.AreEqual(string.Empty, args.Get("measure-clock"));
        }

        [Test]
        public void Flag_DoesNotSwallowTheFollowingOption()
        {
            var args = LaunchArgs.Parse(new[] { "--measure-clock", "--input", "mouse" });

            Assert.AreEqual(string.Empty, args.Get("measure-clock"));
            Assert.AreEqual("mouse", args.Get("input"));
        }

        [Test]
        public void Flag_DoesNotSwallowAUnitySingleDashOption()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "--mute", "-logFile", "player.log", "--input", "hand" });

            Assert.AreEqual(string.Empty, args.Get("mute"), "-logFile belongs to Unity, it is not the value of --mute");
            Assert.AreEqual("hand", args.Get("input"));
            Assert.IsFalse(args.Has("logFile"));
        }

        [TestCase("-1", -1.0)]
        [TestCase("-0.5", -0.5)]
        [TestCase("-.25", -0.25)]
        public void ANegativeNumber_IsStillTheValueOfTheOptionBeforeIt(string token, double expected)
        {
            var args = LaunchArgs.Parse(new[] { "--sky", token });

            Assert.IsTrue(args.TryGetDouble("sky", out var value));
            Assert.AreEqual(expected, value, 1e-9);
        }

        [Test]
        public void SingleDashArguments_BelongToUnityAndAreIgnored()
        {
            var args = LaunchArgs.Parse(new[] { "Downbeat", "-screen-width", "1280", "-batchmode", "--input", "hand" });

            Assert.IsFalse(args.Has("screen-width"));
            Assert.IsFalse(args.Has("batchmode"));
            Assert.AreEqual("hand", args.Get("input"));
        }

        [Test]
        public void Get_IsCaseInsensitiveOnTheName()
        {
            var args = LaunchArgs.Parse(new[] { "--Input", "mouse" });

            Assert.AreEqual("mouse", args.Get("input"));
        }

        [Test]
        public void Get_UsesTheLastOccurrence_WhenAnOptionRepeats()
        {
            var args = LaunchArgs.Parse(new[] { "--input", "mouse", "--input", "replay" });

            Assert.AreEqual("replay", args.Get("input"));
        }

        [TestCase("1.5", true, 1.5)]
        [TestCase("30", true, 30.0)]
        [TestCase("abc", false, 0.0)]
        [TestCase("", false, 0.0)]
        public void TryGetDouble_ParsesWithInvariantCulture(string text, bool expectedOk, double expectedValue)
        {
            var args = LaunchArgs.Parse(new[] { "--quit-after", text });

            var ok = args.TryGetDouble("quit-after", out var value);

            Assert.AreEqual(expectedOk, ok);
            if (expectedOk) Assert.AreEqual(expectedValue, value, 1e-12);
        }

        [Test]
        public void Parse_ToleratesNullAndEmptyInput()
        {
            Assert.IsFalse(LaunchArgs.Parse(null).Has("input"));
            Assert.IsFalse(LaunchArgs.Parse(new string[0]).Has("input"));
        }

        [Test]
        public void DefaultsForTests_FillsOnlyWhatIsMissing()
        {
            using (LaunchArgs.OverrideForTests(new[] { "Downbeat", "--songs", "on", "--input", "replay" }))
            using (LaunchArgs.DefaultsForTests(new[] { "--songs", "off", "--mute" }))
            {
                var current = LaunchArgs.Current;

                Assert.AreEqual("on", current.Get("songs"), "what the test asked for is not replaced");
                Assert.AreEqual("replay", current.Get("input"), "and what only the test asked for stays");
                Assert.IsTrue(current.Has("mute"), "an option the test did not give comes from the defaults");
                Assert.AreEqual(string.Empty, current.Get("mute"));
            }
        }

        [Test]
        public void DefaultsForTests_GiveTheirValueWhenTheTestSaysNothing_AndGoWhenDisposed()
        {
            using (LaunchArgs.OverrideForTests(new[] { "Downbeat", "--input", "mouse" }))
            {
                using (LaunchArgs.DefaultsForTests(new[] { "--songs", "off" }))
                {
                    Assert.AreEqual("off", LaunchArgs.Current.Get("songs"));
                    Assert.AreEqual("mouse", LaunchArgs.Current.Get("input"));
                }

                Assert.IsNull(LaunchArgs.Current.Get("songs"), "disposed: the defaults are gone");
                Assert.AreEqual("mouse", LaunchArgs.Current.Get("input"), "and the override of the test is as it was");
            }
        }

        [Test]
        public void DefaultsForTests_AreSeenWithoutAnOverride_AndAnOverrideOpenedLaterStillWins()
        {
            using (LaunchArgs.DefaultsForTests(new[] { "--songs", "off" }))
            {
                Assert.AreEqual("off", LaunchArgs.Current.Get("songs"), "no override at all: the defaults still apply");

                using (LaunchArgs.OverrideForTests(new[] { "Downbeat", "--songs", "on" })) Assert.AreEqual("on", LaunchArgs.Current.Get("songs"));
                using (LaunchArgs.OverrideForTests(new[] { "Downbeat" })) Assert.AreEqual("off", LaunchArgs.Current.Get("songs"), "an override that does not mention the option leaves the default");
            }
        }

        [Test]
        public void DefaultsForTests_NestedScopes_RestoreInTheOrderTheyWereOpened()
        {
            using (LaunchArgs.DefaultsForTests(new[] { "--songs", "off" }))
            {
                using (LaunchArgs.DefaultsForTests(new[] { "--songs", "on", "--mute" }))
                {
                    Assert.AreEqual("off", LaunchArgs.Current.Get("songs"), "the earlier defaults are the older answer: they were there first and the new ones only fill what is missing");
                    Assert.IsTrue(LaunchArgs.Current.Has("mute"));
                }

                Assert.IsFalse(LaunchArgs.Current.Has("mute"));
                Assert.AreEqual("off", LaunchArgs.Current.Get("songs"));
            }
            Assert.IsNull(LaunchArgs.Current.Get("songs"));
        }

        [Test]
        public void DefaultsForTests_IgnoreNullAndEmptyInput()
        {
            using (LaunchArgs.DefaultsForTests(null)) Assert.IsFalse(LaunchArgs.Current.Has("songs"));
            using (LaunchArgs.DefaultsForTests(new string[0])) Assert.IsFalse(LaunchArgs.Current.Has("songs"));
        }
    }
}
