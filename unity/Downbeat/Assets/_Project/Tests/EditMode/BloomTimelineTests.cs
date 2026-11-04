using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class BloomTimelineTests
    {
        const double Audible = 20.0;
        static readonly BloomSettings Settings = new BloomSettings(0.05f, 0.35f, 3f, 4f, 0.15f);

        static float V(double now) => BloomTimeline.Value(now, Audible, Settings);

        [Test]
        public void Bloom_OpensAtAudibleTime()
        {
            Assert.AreEqual(0f, V(Audible - 0.06), 1e-6f, "closed before it starts");
            Assert.AreEqual(0f, V(Audible - 0.05), 1e-6f, "starts 50 ms before the sound is heard");
            Assert.Greater(V(Audible - 0.03), 0f);
            Assert.GreaterOrEqual(V(Audible + 0.03), 0.5f, "half open within about two frames after the sound: a pop, not a drift");
            Assert.AreEqual(1f, V(Audible - 0.05 + 0.35), 1e-6f, "fully open after 0.35 s");
        }

        [Test]
        public void Bloom_OpensMonotonically_ThenHolds()
        {
            var previous = 0f;
            for (var t = Audible - 0.05; t <= Audible - 0.05 + 0.35; t += 0.005)
            {
                var v = V(t);
                Assert.GreaterOrEqual(v, previous - 1e-6f);
                previous = v;
            }
            Assert.AreEqual(1f, V(Audible - 0.05 + 0.35 + 1.5), 1e-6f, "held open");
            Assert.AreEqual(1f, V(Audible - 0.05 + 0.35 + 3.0), 1e-6f);
        }

        [Test]
        public void Bloom_SettlesToBudLevel()
        {
            var endOfClose = Audible - 0.05 + 0.35 + 3.0 + 4.0;

            Assert.AreEqual(0.15f, V(endOfClose), 1e-6f);
            Assert.AreEqual(0.15f, V(endOfClose + 100.0), 1e-6f, "and stays there");
            Assert.AreEqual(0.575f, V(endOfClose - 2.0), 1e-3f, "halfway through the close it is halfway between open and bud");
        }

        [Test]
        public void Bloom_ClosesMonotonically()
        {
            var start = Audible - 0.05 + 0.35 + 3.0;
            var previous = 1f;
            for (var t = start; t <= start + 4.0; t += 0.05)
            {
                var v = V(t);
                Assert.LessOrEqual(v, previous + 1e-6f);
                previous = v;
            }
        }

        [Test]
        public void Bloom_ARetriggerRisesFromWhereTheFlowerIs_NotFromZero()
        {
            var now = Audible + 6.0; // part way through the close of the first bloom
            var current = V(now);

            var secondAudible = now + 0.05;
            Assert.AreEqual(current, BloomTimeline.Value(now, secondAudible, Settings, current), 1e-6f, "at its start the new bloom is where the flower was");
            Assert.AreEqual(1f, BloomTimeline.Value(secondAudible + 0.3, secondAudible, Settings, current), 1e-6f);
            Assert.Greater(BloomTimeline.Value(secondAudible + 0.01, secondAudible, Settings, current), current);
        }

        [Test]
        public void ARetrigger_KeepsTheOldBloomRunningUntilTheNewOneStarts_NoFreeze()
        {
            // the first bloom is heard at 20 and starts closing at 20 - 0.05 + 0.35 + 3 = 23.3: at 24.0 it is half way through the close
            var first = BloomRun.First(Audible, 0f);
            var now = 24.0;
            var newAudible = now + 0.1; // struck again; the new bloom starts at newAudible - 0.05 = 24.05

            var second = first.Retrigger(newAudible, Settings);

            var previousValue = first.Value(now, Settings);
            Assert.Less(previousValue, 1f);
            Assert.AreEqual(previousValue, second.Value(now, Settings), 1e-6f, "until the new bloom starts, the flower goes on doing what it was doing");
            var later = second.Value(now + 0.03, Settings);
            Assert.Greater(System.Math.Abs(later - previousValue), 1e-4f, "it does not stand still for the 50 ms before the new bloom starts: the old close goes on");
            Assert.Less(later, previousValue, "closing: it keeps getting smaller");
        }

        [Test]
        public void ARetrigger_RisesFromWhereTheFlowerWillBeWhenItStarts_SoThereIsNoJump()
        {
            var first = BloomRun.First(Audible, 0f);
            var now = 24.0;
            var second = first.Retrigger(now + 0.1, Settings);
            var start = now + 0.1 - Settings.LeadSec;

            var justBefore = second.Value(start - 1e-6, Settings);
            var justAfter = second.Value(start + 1e-6, Settings);

            Assert.AreEqual(justBefore, justAfter, 1e-4f, "continuous at the moment the new bloom takes over");
            Assert.Greater(second.Value(start + 0.1, Settings), justAfter, "and then it opens");
            Assert.AreEqual(1f, second.Value(start + Settings.OpenSec + 0.01, Settings), 1e-6f);
        }

        [Test]
        public void ARunThatIsNotRetriggered_BehavesLikeTheTimeline()
        {
            var run = BloomRun.First(Audible, 0.15f);

            for (var t = Audible - 0.2; t < Audible + 9.0; t += 0.07) Assert.AreEqual(BloomTimeline.Value(t, Audible, Settings, 0.15f), run.Value(t, Settings), 1e-6f, $"t {t}");
            Assert.IsTrue(run.IsSettled(Audible + 8.0, Settings));
            Assert.IsFalse(run.IsSettled(Audible + 3.0, Settings));
        }

        [Test]
        public void IsSettled_TellsWhenNothingMoreCanChange()
        {
            Assert.IsFalse(BloomTimeline.IsSettled(Audible, Audible, Settings));
            Assert.IsFalse(BloomTimeline.IsSettled(Audible + 5.0, Audible, Settings));
            Assert.IsTrue(BloomTimeline.IsSettled(Audible + 8.0, Audible, Settings));
        }

        [Test]
        public void Settings_RejectNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BloomSettings(0.05f, 0f, 3f, 4f, 0.15f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BloomSettings(0.05f, 0.35f, -1f, 4f, 0.15f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new BloomSettings(0.05f, 0.35f, 3f, 4f, 1.5f));
        }
    }
}
