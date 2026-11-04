using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class ParticleCuesTests
    {
        static MeadowNoteEvent Note(Part part, float velocity, double audible = 12.5) =>
            new MeadowNoteEvent(part, 3, velocity, audible, new FieldPoint(1.5f, 6f));

        [TestCase(0f, 3)]
        [TestCase(0.5f, 6)]
        [TestCase(1f, 9)]
        public void Marimba_EmitsPetals(float velocity, int expected)
        {
            var cue = ParticleCues.ForNote(Note(Part.Marimba, velocity));

            Assert.AreEqual(ParticleKind.Petals, cue.Kind);
            Assert.AreEqual(expected, cue.Count);
        }

        [TestCase(Part.Harp)]
        [TestCase(Part.Recorder)]
        [TestCase(Part.Bell)]
        [TestCase(Part.LowString)]
        [TestCase(Part.HandDrum)]
        public void TheOtherParts_ThrowNothingOnTheGround(Part part)
        {
            var cue = ParticleCues.ForNote(Note(part, 1f));

            Assert.AreEqual(ParticleKind.None, cue.Kind, "the sky row has effects of its own in the air, the low string and the hand drum move the grass");
            Assert.AreEqual(0, cue.Count);
        }

        [Test]
        public void TheCueKeepsWhereAndWhenTheNoteIsHeard()
        {
            var cue = ParticleCues.ForNote(Note(Part.Marimba, 0.5f, audible: 99.25));

            Assert.AreEqual(1.5f, cue.FieldPos.X, 1e-6f);
            Assert.AreEqual(6f, cue.FieldPos.Z, 1e-6f);
            Assert.AreEqual(99.25, cue.StartTime, 1e-9);
        }

        [Test]
        public void VelocityOutsideZeroToOne_IsClamped()
        {
            Assert.AreEqual(3, ParticleCues.ForNote(Note(Part.Marimba, -2f)).Count);
            Assert.AreEqual(9, ParticleCues.ForNote(Note(Part.Marimba, 9f)).Count);
        }

        [TestCase(0f, 40, 0)]
        [TestCase(1f, 40, 40)]
        [TestCase(0.5f, 40, 20)]
        [TestCase(0.26f, 15, 4)]
        [TestCase(2f, 40, 40)]
        [TestCase(-1f, 40, 0)]
        public void FireflyCount_FollowsTheLevel(float level, int max, int expected)
        {
            Assert.AreEqual(expected, ParticleCues.FireflyTarget(level, max));
        }
    }

    public class HandRibbonFadeTests
    {
        const float FadeSec = 0.5f;
        const float AppearSec = 0.1f;

        [Test]
        public void ARibbonOfAPresentHandRisesToFullQuickly()
        {
            var alpha = 0f;
            for (var i = 0; i < 6; i++) alpha = HandRibbonFade.Step(alpha, true, 1f / 60f, AppearSec, FadeSec);

            Assert.AreEqual(1f, alpha, 1e-6f, "after 0.1 s it is fully there");
            Assert.Greater(HandRibbonFade.Step(0f, true, 1f / 60f, AppearSec, FadeSec), 0.1f, "and it starts on the very first frame");
        }

        [Test]
        public void ARibbonOfAHandThatIsGone_FadesToNothingInHalfASecond()
        {
            var alpha = 1f;
            for (var i = 0; i < 30; i++) alpha = HandRibbonFade.Step(alpha, false, 1f / 60f, AppearSec, FadeSec);

            Assert.AreEqual(0f, alpha, 1e-5f);
            Assert.AreEqual(0.5f, HandRibbonFade.Step(1f, false, 0.25f, AppearSec, FadeSec), 1e-5f, "linear: half after a quarter second");
        }

        [Test]
        public void StaysInZeroToOne()
        {
            Assert.AreEqual(1f, HandRibbonFade.Step(0.95f, true, 5f, AppearSec, FadeSec), 1e-6f);
            Assert.AreEqual(0f, HandRibbonFade.Step(0.05f, false, 5f, AppearSec, FadeSec), 1e-6f);
        }
    }
}
