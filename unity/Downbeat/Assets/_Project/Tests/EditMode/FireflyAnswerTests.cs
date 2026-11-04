using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The fireflies answer a bell of the nature score: which of them glow, where a bell is heard, and how the glow comes and goes.</summary>
    public class FireflyAnswerTests
    {
        const float Radius = 3.5f;
        const int Count = 2;

        static MeadowLayout Layout() => new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);

        static List<FieldPoint> Swarm(params (float x, float z)[] at) => at.Select(p => new FieldPoint(p.x, p.z)).ToList();

        // ---- which fireflies ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheNearestFirefliesAnswer_NearestFirst()
        {
            var swarm = Swarm((0f, 4f), (2f, 4f), (-1f, 4.5f), (0.5f, 5f), (6f, 7f));
            var output = new List<int>();

            FireflyAnswer.Pick(swarm, new FieldPoint(0.4f, 4.6f), 3, Radius, output);

            CollectionAssert.AreEqual(new[] { 3, 0, 2 }, output, "by distance from the spot of the bell");
        }

        [Test]
        public void NoMoreThanTheCountAnswer_AndNoneBeyondTheRadius()
        {
            var swarm = Swarm((0f, 4f), (0.2f, 4f), (0.4f, 4f), (7f, 4f));
            var output = new List<int>();

            FireflyAnswer.Pick(swarm, new FieldPoint(0f, 4f), Count, Radius, output);
            Assert.AreEqual(Count, output.Count);

            FireflyAnswer.Pick(swarm, new FieldPoint(7f, 7.9f), 5, 1f, output);
            Assert.IsEmpty(output, "a bell far from every firefly is answered by none");
        }

        [Test]
        public void TheHeightOfAFirefly_DoesNotMatter_OnlyWhereOnTheGroundItIs()
        {
            // positions are on the ground plane (x, z): a firefly that hovers high above the spot is as near as one that hovers low
            var swarm = Swarm((0f, 4f));
            var output = new List<int>();

            FireflyAnswer.Pick(swarm, new FieldPoint(0.1f, 4f), 1, Radius, output);

            CollectionAssert.AreEqual(new[] { 0 }, output);
        }

        [Test]
        public void TheSameSwarmAndSpot_GiveTheSameAnswer_AndTiesGoToTheLowerIndex()
        {
            var swarm = Swarm((1f, 4f), (-1f, 4f), (0f, 5f));
            var first = new List<int>();
            var second = new List<int>();

            FireflyAnswer.Pick(swarm, new FieldPoint(0f, 4f), 2, Radius, first);
            FireflyAnswer.Pick(swarm, new FieldPoint(0f, 4f), 2, Radius, second);

            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { 0, 1 }, first, "equally near: the lower index first");
        }

        [Test]
        public void NoFireflies_NoAnswer_AndTheOutputIsCleared()
        {
            var output = new List<int> { 9, 9 };

            FireflyAnswer.Pick(new List<FieldPoint>(), new FieldPoint(0f, 4f), Count, Radius, output);
            Assert.IsEmpty(output);

            output.Add(3);
            FireflyAnswer.Pick(Swarm((0f, 4f)), new FieldPoint(0f, 4f), 0, Radius, output);
            Assert.IsEmpty(output, "a count of 0 answers with none");
        }

        // ---- where a bell is heard ---------------------------------------------------------------------------------------------------------

        [Test]
        public void ABellIsHeardAtTheDepthOfItsPitch_AndSomewhereAcrossTheField()
        {
            var layout = Layout();

            var spot = FireflyAnswer.BellSpot(6, 8, 40, layout);

            Assert.AreEqual(layout.DepthForPitchStep(6, 8), spot.Z, 1e-4f, "a higher bell is farther, like a higher note");
            Assert.LessOrEqual(System.Math.Abs(spot.X), layout.HalfWidthAt(spot.Z), "inside the field at that depth");
        }

        [Test]
        public void TheBellsOfDifferentSlots_AreHeardAtDifferentPlaces_ButTheSameBellAlwaysAtTheSameOne()
        {
            var layout = Layout();

            var xs = Enumerable.Range(0, 30).Select(i => FireflyAnswer.BellSpot(6, 8, i * 8, layout).X).ToList();

            Assert.Greater(xs.Distinct().Count(), 20, "they do not all come from one spot");
            Assert.Greater(xs.Max() - xs.Min(), layout.HalfWidthAt(layout.DepthForPitchStep(6, 8)), "spread over a good part of the width");
            Assert.AreEqual(FireflyAnswer.BellSpot(6, 8, 40, layout).X, FireflyAnswer.BellSpot(6, 8, 40, layout).X);
        }

        // ---- how the glow comes and goes ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheGlowRisesQuickly_ThenFadesToNothing_AndNeverFlickers()
        {
            const float Attack = 0.05f;
            const float Decay = 0.8f;

            Assert.AreEqual(0f, FireflyAnswer.Envelope(-0.1f, Attack, Decay));
            Assert.AreEqual(0f, FireflyAnswer.Envelope(0f, Attack, Decay), 1e-6f);
            Assert.AreEqual(1f, FireflyAnswer.Envelope(Attack, Attack, Decay), 1e-5f, "the peak is at the end of the attack");
            Assert.AreEqual(0f, FireflyAnswer.Envelope(Attack + Decay, Attack, Decay), 1e-5f);
            Assert.AreEqual(0f, FireflyAnswer.Envelope(Attack + Decay + 5f, Attack, Decay));

            var last = 0f;
            var rising = true;
            for (var age = 0f; age <= Attack + Decay; age += 0.005f)
            {
                var value = FireflyAnswer.Envelope(age, Attack, Decay);
                Assert.That(value, Is.InRange(0f, 1f));
                if (age <= Attack) Assert.GreaterOrEqual(value, last - 1e-6f, "up to the peak it only rises");
                else
                {
                    if (rising) rising = false;
                    Assert.LessOrEqual(value, last + 1e-6f, "after the peak it only falls: one glow, no second");
                }
                last = value;
            }
        }

        [Test]
        public void TheSettingsComeFromContent_AndRefuseNonsense()
        {
            var dto = TestContent.LoadShipped().meadow.particles;

            var settings = FireflyAnswerSettings.From(dto);

            Assert.Greater(settings.Count, 0);
            Assert.Greater(settings.Radius, 0f);
            Assert.Greater(settings.DecaySec, settings.AttackSec);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FireflyAnswerSettings(-1, 3f, 0.05f, 0.8f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FireflyAnswerSettings(2, 0f, 0.05f, 0.8f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FireflyAnswerSettings(2, 3f, 0f, 0.8f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FireflyAnswerSettings(2, 3f, 0.05f, 0.04f, 1f));
        }
    }
}
