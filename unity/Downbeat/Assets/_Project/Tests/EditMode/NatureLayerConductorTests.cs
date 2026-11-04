using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NatureLayerConductorTests
    {
        const int SlotsPerBar = 16;
        const int PitchSteps = 10;

        static NatureRules Rules() => new NatureRules(
            offbeatSlot: 2, slotsPerBeat: 4, fireflyChance: 0.35f, fireflyTopSteps: 5, fireflyGain: 1f,
            chords: new[] { new[] { 0, 1, 3 }, new[] { 1, 3, 4 } }, barsPerChord: 2, skyGain: 0.8f,
            hillsAccentSlot: 8, hillsAccentGain: 0.6f, hillsGain: 1f);

        static NatureLayerConductor Conductor(int seed = 5) => new NatureLayerConductor(Rules(), SlotsPerBar, PitchSteps, seed);

        static List<NatureNote> Plan(NatureLayerConductor c, long from, long to, int level)
        {
            var output = new List<NatureNote>();
            c.Plan(from, to, level, output);
            return output;
        }

        [Test]
        public void Conductor_NoNotesAtLevel0()
        {
            Assert.IsEmpty(Plan(Conductor(), 0, 16 * 40, 0));
        }

        [Test]
        public void Conductor_FirefliesOnlyOnOffbeats_AndOnlyInTheTopFiveSteps()
        {
            var notes = Plan(Conductor(), 0, 16 * 60, 1);

            Assert.IsNotEmpty(notes);
            Assert.IsTrue(notes.All(n => n.Layer == NatureLayer.Fireflies), "level 1 has only the fireflies");
            Assert.IsTrue(notes.All(n => n.Slot % 4 == 2), "between the beats: slot % 4 == 2");
            Assert.IsTrue(notes.All(n => n.PitchStep >= PitchSteps - 5 && n.PitchStep < PitchSteps), "the upper five notes of the scale");
            Assert.IsTrue(notes.All(n => n.Gain > 0f));
        }

        [Test]
        public void Conductor_FireflyChanceIsAboutThirtyFivePercent()
        {
            var notes = Plan(Conductor(), 0, SlotsPerBar * 4000L, 1); // 4000 bars of 4 beats: 16000 offbeat slots

            var offbeats = 4000 * 4;
            var ratio = notes.Count / (double)offbeats;
            Assert.That(ratio, Is.InRange(0.31, 0.39), $"{notes.Count} of {offbeats}");
        }

        [Test]
        public void Conductor_SkyChordEveryTwoBars_AlternatingBetweenTheTwoChords()
        {
            var notes = Plan(Conductor(), 0, 16 * 8, 2).Where(n => n.Layer == NatureLayer.Sky).ToList();

            var slots = notes.Select(n => n.Slot).Distinct().OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(new long[] { 0, 16, 32, 48, 64, 80, 96, 112 }, slots, "a chord on the first slot of every bar");
            Assert.AreEqual(3, notes.Count(n => n.Slot == 0), "three notes in a chord");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, notes.Where(n => n.Slot == 0).Select(n => n.PitchStep).OrderBy(x => x).ToArray(), "bars 0-1: A C E");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, notes.Where(n => n.Slot == 16).Select(n => n.PitchStep).OrderBy(x => x).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, notes.Where(n => n.Slot == 32).Select(n => n.PitchStep).OrderBy(x => x).ToArray(), "bars 2-3: C E G");
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, notes.Where(n => n.Slot == 48).Select(n => n.PitchStep).OrderBy(x => x).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, notes.Where(n => n.Slot == 64).Select(n => n.PitchStep).OrderBy(x => x).ToArray(), "and back to A C E");
        }

        [Test]
        public void Conductor_DrumsOnlyAtLevel3_OnTheBarAndTheThirdBeat()
        {
            Assert.IsFalse(Plan(Conductor(), 0, 16 * 8, 2).Any(n => n.Layer == NatureLayer.Hills), "no drum at level 2");

            var drums = Plan(Conductor(), 0, 16 * 8, 3).Where(n => n.Layer == NatureLayer.Hills).OrderBy(n => n.Slot).ToList();

            CollectionAssert.AreEqual(Enumerable.Range(0, 8).SelectMany(bar => new long[] { bar * 16, bar * 16 + 8 }).ToArray(), drums.Select(n => n.Slot).ToArray());
            Assert.IsTrue(drums.Where(n => n.Slot % 16 == 0).All(n => n.Gain == 1f), "full strength on the bar line");
            Assert.IsTrue(drums.Where(n => n.Slot % 16 == 8).All(n => System.Math.Abs(n.Gain - 0.6f) < 1e-6f), "0.6 on the third beat");
        }

        [Test]
        public void Conductor_LevelsAddLayers_InOrder()
        {
            Assert.IsTrue(Plan(Conductor(), 0, 16 * 8, 2).Select(n => n.Layer).Distinct().OrderBy(l => l).SequenceEqual(new[] { NatureLayer.Fireflies, NatureLayer.Sky }));
            Assert.IsTrue(Plan(Conductor(), 0, 16 * 8, 3).Select(n => n.Layer).Distinct().OrderBy(l => l).SequenceEqual(new[] { NatureLayer.Fireflies, NatureLayer.Sky, NatureLayer.Hills }));
        }

        [Test]
        public void Conductor_IsDeterministicForSeed_AndDifferentForAnother()
        {
            var a = Plan(Conductor(5), 0, 16 * 50, 3);
            var b = Plan(Conductor(5), 0, 16 * 50, 3);
            var other = Plan(Conductor(6), 0, 16 * 50, 3);

            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a.Where(n => n.Layer == NatureLayer.Fireflies).ToList(), other.Where(n => n.Layer == NatureLayer.Fireflies).ToList());
        }

        [Test]
        public void Conductor_ThePlanOfASlotDoesNotDependOnHowTheRangeWasCut()
        {
            var whole = Plan(Conductor(), 0, 16 * 20, 3);
            var pieces = new List<NatureNote>();
            var c = Conductor();
            for (long from = 0; from < 16 * 20; from += 7) c.Plan(from, System.Math.Min(from + 7, 16 * 20), 3, pieces);

            CollectionAssert.AreEqual(whole.OrderBy(n => n.Slot).ThenBy(n => n.Layer).ThenBy(n => n.PitchStep).ToList(), pieces.OrderBy(n => n.Slot).ThenBy(n => n.Layer).ThenBy(n => n.PitchStep).ToList());
        }

        [Test]
        public void Conductor_RangeEndIsExclusive_AndAnEmptyRangeGivesNothing()
        {
            Assert.IsEmpty(Plan(Conductor(), 10, 10, 3));
            Assert.IsEmpty(Plan(Conductor(), 10, 5, 3));
            Assert.IsFalse(Plan(Conductor(), 0, 16, 3).Any(n => n.Slot >= 16));
            Assert.IsTrue(Plan(Conductor(), 16, 17, 3).Any(n => n.Slot == 16), "the first slot of the range is included");
        }

        static long[] SkySlots(List<NatureNote> notes) => notes.Where(n => n.Layer == NatureLayer.Sky).Select(n => n.Slot).Distinct().OrderBy(s => s).ToArray();

        static int[] SkySteps(List<NatureNote> notes, long slot) => notes.Where(n => n.Layer == NatureLayer.Sky && n.Slot == slot).Select(n => n.PitchStep).OrderBy(x => x).ToArray();

        [Test]
        public void NatureLayerConductor_UsesTheChordSource_ThenFallsBack()
        {
            var asked = new List<long>();
            var c = Conductor();
            c.SetChordSource(bar =>
            {
                asked.Add(bar);
                if (bar == 3) return new[] { 0, 1 };
                if (bar == 5) return new[] { PitchSteps + 2 };   // outside the scale
                return null;                                      // no word from the song for this bar
            });

            var notes = Plan(c, 0, 16 * 6, 2);

            CollectionAssert.AreEqual(new[] { 0, 1 }, SkySteps(notes, 3 * 16), "bar 3: the chord the source gave");
            Assert.AreEqual(2, notes.Count(n => n.Layer == NatureLayer.Sky && n.Slot == 3 * 16), "and nothing else on that bar line");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkySteps(notes, 4 * 16), "bar 4: null, so the chord of the rules (bars 4-5: A C E)");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkySteps(notes, 5 * 16), "bar 5: a step outside the scale, so the chord of the rules, not part of the song's");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkySteps(notes, 0), "bar 0: null, the rules");
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, SkySteps(notes, 2 * 16), "bar 2: null, the rules (bars 2-3: C E G)");
            CollectionAssert.AreEqual(new long[] { 0, 1, 2, 3, 4, 5 }, asked.Distinct().OrderBy(b => b).ToArray(), "asked once per bar line, with the absolute bar");
            Assert.IsTrue(notes.Where(n => n.Layer == NatureLayer.Sky).All(n => System.Math.Abs(n.Gain - 0.8f) < 1e-6f), "a chord from the source is as loud as the sky always is");
        }

        [Test]
        public void NatureLayerConductor_ChordSource_EmptyIsTheRules_AndOneBadStepSpoilsTheWholeBar()
        {
            var c = Conductor();
            c.SetChordSource(bar => bar == 0 ? new int[0] : bar == 1 ? new[] { 2, -1 } : bar == 2 ? new[] { 2, PitchSteps } : new[] { PitchSteps - 1 });

            var notes = Plan(c, 0, 16 * 4, 2);

            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkySteps(notes, 0), "empty: the rules");
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, SkySteps(notes, 16), "a negative step: the rules for the whole bar, the good step 2 is not played");
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, SkySteps(notes, 32), "a step just past the scale: the rules");
            CollectionAssert.AreEqual(new[] { PitchSteps - 1 }, SkySteps(notes, 48), "the top step of the scale is fine");
        }

        [Test]
        public void NatureLayerConductor_ChordSource_ChangesOnlyTheSky_AndCanBeTakenAwayAgain()
        {
            var without = Plan(Conductor(), 0, 16 * 8, 3);
            var c = Conductor();
            c.SetChordSource(bar => new[] { 4 });

            var with = Plan(c, 0, 16 * 8, 3);
            c.SetChordSource(null);
            var cleared = Plan(c, 0, 16 * 8, 3);

            CollectionAssert.AreEqual(without.Where(n => n.Layer != NatureLayer.Sky).ToList(), with.Where(n => n.Layer != NatureLayer.Sky).ToList(), "fireflies and drum are the same");
            Assert.IsTrue(with.Where(n => n.Layer == NatureLayer.Sky).All(n => n.PitchStep == 4));
            CollectionAssert.AreEqual(SkySlots(without), SkySlots(with), "the sky stays on the bar lines");
            CollectionAssert.AreEqual(without, cleared, "null puts the rules back, note for note");
        }

        [Test]
        public void NatureLayerConductor_ChordSource_IsAskedOnlyWhenTheSkyPlays_WithTheBarNumberCountedFromSlotZero()
        {
            var asked = 0;
            var c = Conductor();
            c.SetChordSource(bar => { asked++; return new[] { 0 }; });

            Plan(c, 0, 16 * 8, 1);
            Assert.AreEqual(0, asked, "level 1 has no sky");

            var before = Plan(c, -32, 0, 2);
            CollectionAssert.AreEqual(new long[] { -2, -1 }, SkySlots(before).Select(s => s / 16).ToArray(), "the bars before the start have numbers too");
            Assert.AreEqual(2, asked);
        }

        [Test]
        public void Rules_RejectNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new NatureRules(2, 4, 1.5f, 5, 1f, new[] { new[] { 0 } }, 2, 1f, 8, 0.6f, 1f));
            Assert.Throws<System.ArgumentException>(() => new NatureRules(2, 4, 0.35f, 5, 1f, new int[0][], 2, 1f, 8, 0.6f, 1f));
        }
    }
}
