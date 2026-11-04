using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using Downbeat.Meadow;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// What a played note of each of the six cells does to the meadow, through the note router: the sky row (harp, recorder, bell) opens no flower and pushes no grass, the low string lays the grass in a long wave,
    /// the hand drum thumps a wide ring, the marimba pushes the ring of a note; and what happens to the field of 16 waves when all of them play as fast as they can.
    /// </summary>
    public class SixCellsEffectsRoutingTests
    {
        const double SceneNow = 100.0;
        const double HeardNow = 50.0;
        const double Audible = 50.4;
        const float Tolerance = 1e-4f;
        const double SimulatedSec = 30.0;
        const float LifeLeftOfANewWaveWhenItIsDropped = 0.35f;

        sealed class RecordingBloom : IBloomTarget
        {
            public int Calls;
            public void Bloom(IReadOnlyList<int> flowerIndices, double audibleTime) => Calls++;
        }

        sealed class Rig
        {
            public MeadowNoteRouter Router;
            public RecordingBloom Bloom = new RecordingBloom();
            public WindField Wind;
            public MeadowLayout Layout;
            public MeadowDto Meadow;
            public ContentDto Content;
        }

        static Rig Create(Func<double, double, double> toWindTime = null)
        {
            var content = TestContent.LoadShipped();
            var meadow = content.meadow;
            var rig = new Rig { Content = content, Meadow = meadow };
            rig.Layout = new MeadowLayout(meadow.layout.fieldWidth, meadow.layout.nearZ, meadow.layout.farZ, meadow.layout.horizonScreenY);
            var flowers = FlowerPlacement.Generate(meadow.flowers.seed, 600, rig.Layout, meadow.flowers.blend);
            rig.Wind = new WindField(WindFieldGpu.Capacity, meadow.wind.waveWidth);
            rig.Router = new MeadowNoteRouter(flowers, rig.Layout, rig.Wind, rig.Bloom, meadow.flowers, meadow.wind, content.song.pitchSteps, toWindTime ?? ((audible, heard) => SceneNow + (audible - heard)));
            return rig;
        }

        static ScheduledNote Note(Part part, float velocity, float x01, float y01, double audible = Audible) =>
            new ScheduledNote(10, audible, audible - 0.3, part, 4, velocity, false, x01, y01);

        static WindWaveGpu OnlyWave(Rig rig, double atSceneTime)
        {
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            Assert.AreEqual(1, rig.Wind.CopyActive(atSceneTime, scratch), "one wave is alive");
            return scratch[0];
        }

        static readonly double HeardAtScene = SceneNow + (Audible - HeardNow);

        // ---- the effect each part has ----------------------------------------------------------------------------------

        [Test]
        public void EachPartOfTheSkyRow_HasItsEffect_TheGroundHasNone()
        {
            Assert.AreEqual(SkyEffectKind.LightRing, SkyEffectSettings.KindFor(Part.Harp));
            Assert.AreEqual(SkyEffectKind.Seeds, SkyEffectSettings.KindFor(Part.Recorder));
            Assert.AreEqual(SkyEffectKind.StarDust, SkyEffectSettings.KindFor(Part.Bell));
            foreach (var part in new[] { Part.LowString, Part.Marimba, Part.HandDrum }) Assert.IsNull(SkyEffectSettings.KindFor(part), part + " plays on the grass");
            foreach (Part part in Enum.GetValues(typeof(Part))) Assert.AreEqual(Parts.IsSky(part), SkyEffectSettings.KindFor(part).HasValue, part + ": the effects are exactly those of the upper row");
        }

        [TestCase(Part.Harp, 0.17f, 0.2f)]
        [TestCase(Part.Recorder, 0.5f, 0.3f)]
        [TestCase(Part.Bell, 0.83f, 0.1f)]
        public void SkyNote_NeverBlooms_AndLeavesTheGrassAlone(Part sky, float x01, float y01)
        {
            var rig = Create();
            MeadowNoteEvent? seen = null;
            rig.Router.NoteRouted += e => seen = e;

            foreach (var velocity in new[] { 0.05f, 0.5f, 1f }) rig.Router.OnNote(Note(sky, velocity, x01, y01), HeardNow);

            Assert.AreEqual(0, rig.Bloom.Calls, "no flower opens");
            Assert.AreEqual(0, rig.Wind.Count, "no wave, no ring, no glow in the grass");
            Assert.IsTrue(seen.HasValue, "the effect in the air hears of it");
            Assert.AreEqual(x01, seen.Value.X01, Tolerance, "and where the hand was on the picture");
            Assert.AreEqual(y01, seen.Value.Y01, Tolerance);
        }

        // ---- the grass of the bottom row ----------------------------------------------------------------------------------

        [TestCase(0.17f, -1f)]
        [TestCase(0.83f, 1f)]
        public void LowString_LaysTheGrassInALongWave_TowardsTheSideOfTheHand(float x01, float side)
        {
            var rig = Create();
            var settings = rig.Meadow.wind.longWave;

            rig.Router.OnNote(Note(Part.LowString, 1f, x01, 0.75f), HeardNow);

            Assert.AreEqual(0, rig.Wind.CopyActive(SceneNow + 0.1, new WindWaveGpu[4]), "nothing is heard yet");
            var wave = OnlyWave(rig, HeardAtScene + 0.05);
            Assert.AreEqual((float)WindWaveKind.Sweep, wave.Kind, "a sweep, the same at every depth");
            Assert.AreEqual(side * settings.speed, wave.Speed, Tolerance, "towards the side of the hand");
            Assert.AreEqual(settings.widthScale, wave.WidthScale, Tolerance, "long: wider than any other wave");
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), wave.Amplitude, Tolerance);
            Assert.AreEqual(settings.lifeSec, wave.LifeSec, Tolerance);
            var push = rig.Wind.SampleVector(wave.OriginX, wave.OriginZ, HeardAtScene + 0.05);
            Assert.AreEqual(side, Math.Sign(push.X), "the grass at the hand lies that way");
            Assert.AreEqual(1, rig.Bloom.Calls, "and the bellflowers of its column open, as before");
        }

        [Test]
        public void HandDrum_PushesAWideRing()
        {
            var rig = Create();
            var settings = rig.Meadow.wind.drumRing;
            var note = rig.Meadow.wind.noteRadial;

            rig.Router.OnNote(Note(Part.HandDrum, 1f, 0.83f, 0.75f), HeardNow);

            var wave = OnlyWave(rig, HeardAtScene + 0.05);
            Assert.AreEqual((float)WindWaveKind.Radial, wave.Kind);
            Assert.AreEqual(settings.widthScale, wave.WidthScale, Tolerance, "wide");
            Assert.AreEqual(settings.amplitude * (1f + settings.velocityGain), wave.Amplitude, Tolerance);
            Assert.Greater(wave.Amplitude, 1.5f * note.amplitude * (1f + note.velocityGain), "and much stronger than the ring of a note");
            Assert.AreEqual(settings.speed, wave.Speed, Tolerance);
            Assert.AreEqual(settings.lifeSec, wave.LifeSec, Tolerance);
            Assert.AreEqual(1, rig.Bloom.Calls, "the poppies of its column open, as before");
        }

        [Test]
        public void Marimba_PushesThePlainRingOfANote_AsItDid()
        {
            var rig = Create();
            var note = rig.Meadow.wind.noteRadial;

            rig.Router.OnNote(Note(Part.Marimba, 1f, 0.5f, 0.75f), HeardNow);

            var wave = OnlyWave(rig, HeardAtScene + 0.05);
            Assert.AreEqual((float)WindWaveKind.Radial, wave.Kind);
            Assert.AreEqual(1f, wave.WidthScale, Tolerance, "the usual width");
            Assert.AreEqual(note.amplitude * (1f + note.velocityGain), wave.Amplitude, Tolerance);
            Assert.AreEqual(note.speed, wave.Speed, Tolerance);
            Assert.AreEqual(note.lifeSec, wave.LifeSec, Tolerance);
            Assert.AreEqual(0f, wave.Glow, Tolerance, "no glow: the harp, which had it, plays in the air now");
        }

        [Test]
        public void ALouderLowStringNote_LaysTheGrassHarder_AndASofterOneLessSo()
        {
            var soft = Create();
            var loud = Create();

            soft.Router.OnNote(Note(Part.LowString, 0.1f, 0.17f, 0.75f), HeardNow);
            loud.Router.OnNote(Note(Part.LowString, 1f, 0.17f, 0.75f), HeardNow);

            Assert.Less(OnlyWave(soft, HeardAtScene + 0.05).Amplitude, OnlyWave(loud, HeardAtScene + 0.05).Amplitude);
        }

        // ---- all six parts, as fast as they go ----------------------------------------------------------------------------

        sealed class Seen
        {
            public double Start;
            public WindWaveKind Kind;
            public float Speed;
            public float Life;
            public float Age;
            public string Name;
            public string Key => $"{Start:0.0000}|{Kind}|{Speed:0.0}|{Life:0.0}";
        }

        static List<Seen> Alive(WindField wind, double now)
        {
            var scratch = new WindWaveGpu[WindFieldGpu.Capacity];
            var count = wind.CopyActive(now, scratch);
            var list = new List<Seen>(count);
            for (var i = 0; i < count; i++)
            {
                var kind = (WindWaveKind)(int)scratch[i].Kind;
                var name = kind == WindWaveKind.Sweep ? "low string" : (scratch[i].WidthScale > 1f ? "hand drum" : "marimba");
                list.Add(new Seen { Start = now - scratch[i].Age, Kind = kind, Speed = scratch[i].Speed, Life = scratch[i].LifeSec, Age = scratch[i].Age, Name = name });
            }
            return list;
        }

        [Test]
        public void AllSixPartsAtQuarterBeatDensity_TheFieldHoldsItsSixteen_AndOnlyTheOldestWaveGoes()
        {
            // every part plays on every gap-th slot (song.partMinGapSlots: the quickest the scheduler lets it go) with the strongest note; the sky row makes no wave, so it is the lower row that fills the field
            var rig = Create((audible, heard) => audible);
            var song = rig.Content.song;
            var slotSec = 60.0 / song.bpm / song.slotsPerBeat;
            var gaps = song.partMinGapSlots.ToDictionary(g => g.part, g => g.slots);
            var mostAlive = 0;
            var groundNotes = 0;
            var droppedByName = new Dictionary<string, List<float>>();

            for (var slot = 0; slot * slotSec < SimulatedSec; slot++)
            {
                var now = slot * slotSec;
                foreach (Part part in Enum.GetValues(typeof(Part)))
                {
                    if (slot % gaps[Parts.Name(part)] != 0) continue;
                    var before = Alive(rig.Wind, now);
                    var column = Parts.Column(part);
                    var x01 = column == 0 ? SpeciesMap.LeftCentre : (column == 1 ? SpeciesMap.MiddleCentre : SpeciesMap.RightCentre);
                    var countBefore = rig.Wind.Count;
                    rig.Router.OnNote(new ScheduledNote(slot, now, now - 0.1, part, 4, 1f, false, x01, Parts.IsSky(part) ? 0.25f : 0.75f), now);
                    if (Parts.IsSky(part))
                    {
                        Assert.AreEqual(countBefore, rig.Wind.Count, part + ": a note of the sky row makes no wave");
                        continue;
                    }
                    groundNotes++;
                    var after = Alive(rig.Wind, now);
                    mostAlive = Math.Max(mostAlive, after.Count);
                    Assert.LessOrEqual(after.Count, WindFieldGpu.Capacity, "the field never holds more than its capacity");

                    // what went: the waves that were alive before and are not any more (the new wave is in `after` only)
                    var remaining = after.GroupBy(w => w.Key).ToDictionary(g => g.Key, g => g.Count());
                    var survivors = new List<Seen>();
                    var dropped = new List<Seen>();
                    foreach (var wave in before.OrderBy(w => w.Start))
                    {
                        if (remaining.TryGetValue(wave.Key, out var left) && left > 0) { remaining[wave.Key] = left - 1; survivors.Add(wave); }
                        else dropped.Add(wave);
                    }
                    foreach (var gone in dropped)
                    {
                        if (survivors.Count > 0) Assert.GreaterOrEqual(gone.Age + 1e-4, survivors.Max(s => s.Age), "only the oldest wave is dropped: it is older than every one that stays");
                        if (!droppedByName.TryGetValue(gone.Name, out var list)) droppedByName[gone.Name] = list = new List<float>();
                        list.Add(1f - gone.Age / gone.Life);
                    }
                }
            }

            Assert.Greater(groundNotes, 100, "the lower row played");
            Assert.LessOrEqual(mostAlive, WindFieldGpu.Capacity);
            foreach (var name in new[] { "low string", "hand drum" })
            {
                if (!droppedByName.TryGetValue(name, out var left)) continue;
                Assert.LessOrEqual(left.Max(), LifeLeftOfANewWaveWhenItIsDropped, $"a wave of the {name} is dropped only near its end ({left.Count} dropped, up to {left.Max():0.00} of its life left)");
            }
            TestContext.Out.WriteLine("six parts at quarter-beat density, " + SimulatedSec + " s: " + groundNotes + " waves made, at most " + mostAlive + " alive; dropped while alive: " +
                string.Join("; ", new[] { "low string", "hand drum", "marimba" }.Select(n => droppedByName.TryGetValue(n, out var l) ? $"{n} {l.Count}x, {l.Average():0.00} of the life left on average" : n + " none")));
        }
    }
}
