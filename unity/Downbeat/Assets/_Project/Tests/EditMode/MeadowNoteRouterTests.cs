using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class MeadowNoteRouterTests
    {
        const int PitchSteps = 10;
        const double SceneNow = 100.0;
        const double HeardNow = 50.0;
        const double Audible = 50.4;

        sealed class RecordingBloom : IBloomTarget
        {
            public readonly List<(int[] Flowers, double AudibleTime)> Calls = new List<(int[] Flowers, double AudibleTime)>();

            public void Bloom(IReadOnlyList<int> flowerIndices, double audibleTime)
            {
                var copy = new int[flowerIndices.Count];
                for (var i = 0; i < copy.Length; i++) copy[i] = flowerIndices[i];
                Calls.Add((copy, audibleTime));
            }
        }

        static MeadowFlowersDto FlowerSettings() => new MeadowFlowersDto { maxPerNote = 6, blend = 0.08f, harpGlow = 0.8f };
        static MeadowWindDto WindSettings() => new MeadowWindDto
        {
            waveWidth = 1.8f, noteRadial = new WindRadialDto { amplitude = 0.35f, velocityGain = 0.45f, speed = 9f, lifeSec = 2f },
        };

        static (MeadowNoteRouter router, RecordingBloom bloom, WindField wind, FlowerInstance[] flowers, MeadowLayout layout) Create()
        {
            var layout = new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);
            var flowers = FlowerPlacement.Generate(7, 1800, layout);
            var bloom = new RecordingBloom();
            var wind = new WindField(16, 1.8f);
            var router = new MeadowNoteRouter(flowers, layout, wind, bloom, FlowerSettings(), WindSettings(), PitchSteps, (audible, heard) => SceneNow + (audible - heard));
            return (router, bloom, wind, flowers, layout);
        }

        static ScheduledNote Note(Part part, int step, float velocity, float x01, float y01 = float.NaN) => new ScheduledNote(10, Audible, Audible - 0.3, part, step, velocity, false, x01, y01);

        [Test]
        public void Router_TwoHandsDifferentZones_BothBloom()
        {
            var (router, bloom, _, flowers, _) = Create();

            router.OnNote(Note(Part.LowString, 3, 0.7f, 0.15f, 0.8f), HeardNow);
            router.OnNote(Note(Part.HandDrum, 6, 0.7f, 0.85f, 0.8f), HeardNow);

            Assert.AreEqual(2, bloom.Calls.Count);
            Assert.IsTrue(bloom.Calls[0].Flowers.Length > 0 && bloom.Calls[1].Flowers.Length > 0);
            foreach (var index in bloom.Calls[0].Flowers) Assert.AreEqual(FlowerSpecies.Bellflower, flowers[index].Species, "the low string opens bellflowers");
            foreach (var index in bloom.Calls[1].Flowers) Assert.AreEqual(FlowerSpecies.Poppy, flowers[index].Species, "the hand drum opens poppies");
            Assert.AreEqual(Audible, bloom.Calls[0].AudibleTime, 1e-9);
            Assert.AreEqual(Audible, bloom.Calls[1].AudibleTime, 1e-9);
        }

        [Test]
        public void Router_BloomsTheFlowersAroundTheSpotThatWasPlayed()
        {
            var (router, bloom, _, flowers, layout) = Create();

            router.OnNote(Note(Part.Marimba, 4, 0.5f, 0.5f), HeardNow);

            var expected = layout.HandToField(0.5f, layout.HandYForDepth(layout.DepthForPitchStep(4, PitchSteps)));
            foreach (var index in bloom.Calls[0].Flowers)
            {
                Assert.LessOrEqual(System.Math.Abs(flowers[index].Z - expected.Z), 1.2f, "near the depth of that pitch");
                Assert.LessOrEqual(System.Math.Abs(flowers[index].X - expected.X), 2.5f, "and near where across the field the hand was");
            }
        }

        [Test]
        public void Router_MoreVelocity_BloomsMoreFlowers()
        {
            var (router, bloom, _, _, _) = Create();

            router.OnNote(Note(Part.Marimba, 4, 0.05f, 0.5f), HeardNow);
            router.OnNote(Note(Part.Marimba, 4, 1f, 0.5f), HeardNow);

            Assert.Less(bloom.Calls[0].Flowers.Length, bloom.Calls[1].Flowers.Length);
            Assert.AreEqual(FlowerSelector.CountFor(1f, 6), bloom.Calls[1].Flowers.Length);
        }

        [TestCase(Part.Harp)]
        [TestCase(Part.Recorder)]
        [TestCase(Part.Bell)]
        public void Router_SkyNote_DoesNotBloomOrPushGrass(Part sky)
        {
            var (router, bloom, wind, _, _) = Create();
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(Note(sky, 4, 1f, 0.5f, 0.2f), HeardNow);

            Assert.IsEmpty(bloom.Calls, "the sky plays in the air and opens no flower");
            Assert.AreEqual(0, wind.Count, "and does not push the grass in a ring");
            Assert.IsTrue(seen.HasValue, "but the effects of the sky still hear of the note");
            Assert.AreEqual(0.5f, seen.Value.X01, 1e-6f);
            Assert.AreEqual(0.2f, seen.Value.Y01, 1e-6f, "and where in the picture the hand played");
        }

        [TestCase(Part.LowString, FlowerSpecies.Bellflower, 0.15f)]
        [TestCase(Part.Marimba, FlowerSpecies.Dandelion, 0.5f)]
        [TestCase(Part.HandDrum, FlowerSpecies.Poppy, 0.85f)]
        public void Router_GroundNote_BloomsTheFlowerOfItsColumn(Part ground, FlowerSpecies expected, float x01)
        {
            var (router, bloom, wind, flowers, _) = Create();

            router.OnNote(Note(ground, 4, 0.9f, x01, 0.8f), HeardNow);

            Assert.AreEqual(1, bloom.Calls.Count);
            Assert.IsNotEmpty(bloom.Calls[0].Flowers);
            foreach (var index in bloom.Calls[0].Flowers) Assert.AreEqual(expected, flowers[index].Species, $"{ground}");
            Assert.AreEqual(1, wind.Count, "and pushes the grass in a ring");
        }

        [Test]
        public void Router_AGroundNoteMakesAWindRing_ButNoGlow()
        {
            var (router, _, wind, _, layout) = Create();
            var at = layout.HandToField(0.5f, layout.HandYForDepth(layout.DepthForPitchStep(4, PitchSteps)));

            router.OnNote(Note(Part.Marimba, 4, 0.7f, 0.5f), HeardNow);

            var wave = SceneNow + (Audible - HeardNow);
            Assert.AreEqual(1, wind.Count);
            Assert.AreEqual(0f, wind.SampleGlow(at.X, at.Z, wave + 0.01), 1e-6f);
            Assert.Greater(wind.SampleBend(at.X, at.Z, wave + 0.01), 0.1f, "but the wind ring bends the grass");
        }

        [Test]
        public void Router_TheWindStartsWhenTheNoteIsHeard_NotWhenItIsScheduled()
        {
            var (router, _, wind, _, layout) = Create();
            var at = layout.HandToField(0.5f, layout.HandYForDepth(layout.DepthForPitchStep(4, PitchSteps)));

            router.OnNote(Note(Part.Marimba, 4, 0.7f, 0.5f), HeardNow);

            Assert.AreEqual(0, wind.CopyActive(SceneNow + 0.1, new WindWaveGpu[4]), "0.1 s from now nothing is heard yet");
            Assert.AreEqual(1, wind.CopyActive(SceneNow + (Audible - HeardNow) + 0.05, new WindWaveGpu[4]));
        }

        [Test]
        public void Router_RaisesTheEventForOthersToUse()
        {
            var (router, _, _, _, layout) = Create();
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(Note(Part.HandDrum, 7, 0.9f, 0.8f, 0.7f), HeardNow);

            Assert.IsTrue(seen.HasValue);
            Assert.AreEqual(Part.HandDrum, seen.Value.Part);
            Assert.AreEqual(0.8f, seen.Value.X01, 1e-6f);
            Assert.AreEqual(0.7f, seen.Value.Y01, 1e-6f);
            Assert.AreEqual(7, seen.Value.PitchStep);
            Assert.AreEqual(0.9f, seen.Value.Velocity, 1e-6f);
            Assert.AreEqual(Audible, seen.Value.AudibleTime, 1e-9);
            Assert.AreEqual(layout.DepthForPitchStep(7, PitchSteps), seen.Value.FieldPos.Z, 1e-4f, "z is the depth of the pitch step");
            Assert.AreEqual(layout.LateralFraction(seen.Value.FieldPos.X, seen.Value.FieldPos.Z), 0.8f, 1e-3f, "x is where across the field the hand played");
        }

        [Test]
        public void Router_ANoteWithoutAnX_FallsBackToTheMiddleOfItsInstrumentsZone()
        {
            var (router, _, _, _, layout) = Create();
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(new ScheduledNote(1, Audible, 0, Part.HandDrum, 2, 0.5f, false), HeardNow);

            Assert.AreEqual(0.83f, layout.LateralFraction(seen.Value.FieldPos.X, seen.Value.FieldPos.Z), 1e-3f);
        }

        // ---- the ground under the hand ------------------------------------------------------------------------------------------------------

        static (MeadowNoteRouter router, RecordingBloom bloom, WindField wind, FlowerInstance[] flowers, MeadowLayout layout) CreateWithGround(GroundOfHand ground)
        {
            var layout = new MeadowLayout(13.2f, 2.3f, 8f, 0.3f);
            var flowers = FlowerPlacement.Generate(7, 1800, layout);
            var bloom = new RecordingBloom();
            var wind = new WindField(16, 1.8f);
            var router = new MeadowNoteRouter(flowers, layout, wind, bloom, FlowerSettings(), WindSettings(), PitchSteps, (audible, heard) => SceneNow + (audible - heard), ground);
            return (router, bloom, wind, flowers, layout);
        }

        [Test]
        public void GroundNote_StartsUnderTheHand_NotAtTheDepthOfThePitch()
        {
            var under = new FieldPoint(-3.5f, 4.2f);
            float seenX = -1f, seenY = -1f;
            var (router, bloom, wind, flowers, layout) = CreateWithGround((float x01, float y01, out FieldPoint ground) => { seenX = x01; seenY = y01; ground = under; return true; });
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(Note(Part.Marimba, 9, 0.7f, 0.3f, 0.8f), HeardNow);

            Assert.AreEqual(0.3f, seenX, 1e-6f, "the place of the hand is what the ground is looked up for");
            Assert.AreEqual(0.8f, seenY, 1e-6f);
            Assert.AreEqual(under.X, seen.Value.FieldPos.X, 1e-6f);
            Assert.AreEqual(under.Z, seen.Value.FieldPos.Z, 1e-6f, "a high note with the hand low on the screen is played low on the field: the pitch step does not move it");
            Assert.AreEqual(1, wind.Count);
            foreach (var index in bloom.Calls[0].Flowers)
            {
                Assert.LessOrEqual(System.Math.Abs(flowers[index].Z - under.Z), 1.2f, "the flowers that open are around the spot under the hand");
                Assert.AreNotEqual(layout.DepthForPitchStep(9, PitchSteps), under.Z);
            }
        }

        [Test]
        public void GroundNote_WithoutAHandPlace_FallsBackToThePitchDepth()
        {
            var asked = false;
            var (router, _, _, _, layout) = CreateWithGround((float x01, float y01, out FieldPoint ground) => { asked = true; ground = default; return true; });
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(Note(Part.HandDrum, 7, 0.7f, 0.85f), HeardNow);   // a key press: no height

            Assert.IsFalse(asked, "nobody is asked where the hand is when it is not known");
            Assert.AreEqual(layout.DepthForPitchStep(7, PitchSteps), seen.Value.FieldPos.Z, 1e-4f);
        }

        [Test]
        public void GroundNote_WhenTheGroundCannotBeFound_FallsBackToThePitchDepth()
        {
            var (router, _, _, _, layout) = CreateWithGround((float x01, float y01, out FieldPoint ground) => { ground = default; return false; });
            MeadowNoteEvent? seen = null;
            router.NoteRouted += e => seen = e;

            router.OnNote(Note(Part.LowString, 5, 0.7f, 0.2f, 0.9f), HeardNow);

            Assert.AreEqual(layout.DepthForPitchStep(5, PitchSteps), seen.Value.FieldPos.Z, 1e-4f);
        }

        [Test]
        public void SkyNote_IsNeverLookedUpOnTheGround()
        {
            var asked = false;
            var (router, bloom, wind, _, _) = CreateWithGround((float x01, float y01, out FieldPoint ground) => { asked = true; ground = default; return true; });

            router.OnNote(Note(Part.Harp, 4, 0.7f, 0.2f, 0.2f), HeardNow);

            Assert.IsFalse(asked);
            Assert.AreEqual(0, bloom.Calls.Count);
            Assert.AreEqual(0, wind.Count);
        }
    }
}
