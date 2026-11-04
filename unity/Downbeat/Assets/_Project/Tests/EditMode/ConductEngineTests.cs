using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// Conducting play (docs/conducting-play.md §5 and §8): a raised hand keeps the music going on the beat grid; the cell it is in (three columns, two rows) is the instrument, movement is
    /// loudness and density (its height is neither), and all of it holds when the tracker delivers 6 pictures a second (167 ms apart) or an uneven 5 to 30. The middle of a cell is at x 0.17, 0.5
    /// or 0.83 and y 0.25 (the sky row: harp, recorder, bell) or 0.75 (the ground row: low string, marimba, hand drum); the recorder, the bell and the low string need 2 slots between notes.
    /// </summary>
    public class ConductEngineTests
    {
        const float MinVelocity = 0.25f;
        const double SettleSec = 2.0;
        const double Fps30 = 1.0 / 30.0;
        const double Fps6 = 1.0 / 6.0;
        static readonly Part[] AllParts = { Part.Harp, Part.Recorder, Part.Bell, Part.LowString, Part.Marimba, Part.HandDrum };

        // ---- a hand held still ---------------------------------------------------------------------------------------------------------

        [TestCase(Fps30)]
        [TestCase(Fps6)]
        public void AStillHand_PlaysOneNotePerBeat_AtMinVelocity(double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(12.0, ConductRig.StillIn(Part.Harp), pictureEverySec);

            var gaps = rig.Gaps(Part.Harp, SettleSec);
            Assert.GreaterOrEqual(gaps.Count, 10);
            Assert.That(gaps, Has.All.EqualTo(ConductRig.SlotsPerBeat), "one note a beat, and jitter does not make it more");
            foreach (var note in rig.Of(Part.Harp))
            {
                var onBarLine = rig.SlotOf(note) % ConductRig.SlotsPerBar == 0;
                Assert.AreEqual(Math.Min(1f, MinVelocity + (onBarLine ? 0.1f : 0f)), note.Velocity, 0.02f, "the strength of a hand held still, and a little more on the first beat of a bar");
            }
        }

        [Test]
        public void Parts_TheSixCellsAreSixDifferentInstruments_OneForEveryCell([ValueSource(nameof(AllParts))] Part part, [Values(Fps30, Fps6)] double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(6.0, ConductRig.StillIn(part), pictureEverySec);

            Assert.IsNotEmpty(rig.Notes);
            Assert.That(rig.Notes.Select(n => n.Part), Has.All.EqualTo(part), "a hand in the middle of a cell plays that cell's instrument and no other");
        }

        [Test]
        public void Conduct_StillHand_PlaysAtMinVelocity_InEveryCell([ValueSource(nameof(AllParts))] Part part, [Values(Fps30, Fps6)] double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(8.0, ConductRig.StillIn(part), pictureEverySec);

            var plain = rig.Unaccented(part, SettleSec);
            Assert.GreaterOrEqual(plain.Count, 6);
            Assert.That(plain.Select(n => n.Velocity), Has.All.EqualTo(rig.Settings.MinVelocity).Within(0.02f), $"{part}: a hand that does not move plays softly, wherever it is");
        }

        // ---- the amount of movement is the density ---------------------------------------------------------------------------------------

        [TestCase(2f, 2L, Fps30)]
        [TestCase(5f, 1L, Fps30)]
        [TestCase(2f, 2L, Fps6)]
        [TestCase(5f, 1L, Fps6)]
        public void Movement_SetsTheDistanceBetweenNotes_AtAnyTrackerSpeed(float palmsPerSecond, long expectedGap, double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(12.0, ConductRig.CircleIn(Part.Harp, palmsPerSecond), pictureEverySec);

            var gaps = rig.Gaps(Part.Harp, SettleSec);
            Assert.GreaterOrEqual(gaps.Count, 20);
            Assert.That(gaps, Has.All.EqualTo(expectedGap), $"{palmsPerSecond} palms a second at {1.0 / pictureEverySec:0} pictures a second");
        }

        [TestCase(Fps30)]
        [TestCase(Fps6)]
        public void TheRecorder_NeverPlaysCloserThanItsMinimumGap(double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.CircleIn(Part.Recorder, 5f), pictureEverySec);

            var gaps = rig.Gaps(Part.Recorder, SettleSec);
            Assert.GreaterOrEqual(gaps.Count, 10);
            Assert.That(gaps, Has.All.EqualTo(2L), "5 palms a second wants a note every slot, the recorder needs 2");
        }

        [Test]
        public void EveryPart_NeverPlaysCloserThanItsMinimumGap([ValueSource(nameof(AllParts))] Part part)
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.CircleIn(part, 5f), Fps6);

            var gaps = rig.Gaps(part, SettleSec);
            Assert.GreaterOrEqual(gaps.Count, 10);
            Assert.That(gaps, Has.All.EqualTo((long)ConductRig.DefaultGaps[(int)part]), $"{part}: a note every slot where it may, else as close as its gap lets it");
        }

        [Test]
        public void ADensityDoesNotChangeInTheMiddleOfABeat_AndNeverFlipsAtAThreshold()
        {
            var rig = new ConductRig();
            var slow = ConductRig.CircleIn(Part.Harp, 1.0f);
            var fast = ConductRig.CircleIn(Part.Harp, 3.0f);

            rig.Run(4.0, slow, Fps30);
            rig.Run(4.0, fast, Fps30);

            foreach (var gap in rig.Gaps(Part.Harp, 0.0)) Assert.That(new long[] { 1, 2, 4 }, Contains.Item(gap));
            var slots = rig.Of(Part.Harp).Select(rig.SlotOf).ToList();
            for (var i = 1; i < slots.Count; i++)
            {
                var before = slots[i] - slots[i - 1];
                if (before < ConductRig.SlotsPerBeat) Assert.AreEqual(0, slots[i] % before, "a note of a faster density stays on the slots of that density");
            }
        }

        [Test]
        public void ANoteIsNeverPlayedTwiceInTheSameSlot_OfAPart()
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.Both(ConductRig.CircleIn(Part.Harp, 6f, 0.1f, 0), ConductRig.CircleIn(Part.Marimba, 3f, 0.1f, 1)), Fps6);

            foreach (var part in AllParts)
            {
                var slots = rig.Of(part).Select(rig.SlotOf).ToList();
                CollectionAssert.AreEqual(slots.Distinct().OrderBy(s => s).ToList(), slots, "every slot once, in order");
            }
        }

        [Test]
        public void WithAnUnevenTracker_OfFiveToThirtyPicturesASecond_TheRulesHoldForTheMostPart()
        {
            var still = new ConductRig();
            var slow = new ConductRig();
            var fast = new ConductRig();

            still.Run(20.0, ConductRig.StillIn(Part.Harp), ConductRig.UnevenPictures(3));
            slow.Run(20.0, ConductRig.CircleIn(Part.Harp, 2f), ConductRig.UnevenPictures(5));
            fast.Run(20.0, ConductRig.CircleIn(Part.Harp, 6f), ConductRig.UnevenPictures(9));

            Assert.That(still.Gaps(Part.Harp, SettleSec), Has.All.EqualTo(4L), "a hand held still is the same at any speed of the tracker");
            AssertMostly(slow.Gaps(Part.Harp, SettleSec), 2L, 0.85);
            AssertMostly(fast.Gaps(Part.Harp, SettleSec), 1L, 0.85);
        }

        static void AssertMostly(List<long> gaps, long expected, double share)
        {
            Assert.GreaterOrEqual(gaps.Count, 15);
            var hits = gaps.Count(g => g == expected);
            Assert.GreaterOrEqual(hits / (double)gaps.Count, share, $"{hits} of {gaps.Count} gaps were {expected}: {string.Join(",", gaps.Take(40))}");
        }

        // ---- movement is loudness, height is not --------------------------------------------------------------------------------------------

        [TestCase(Fps30)]
        [TestCase(Fps6)]
        public void Conduct_FasterHand_IsLouder_At6And30Fps(double pictureEverySec)
        {
            var strengths = new List<float>();
            foreach (var palms in new[] { 0f, 1f, 2f, 3f, 6f })
            {
                var rig = new ConductRig();
                rig.Run(10.0, palms <= 0f ? ConductRig.StillIn(Part.Harp) : ConductRig.CircleIn(Part.Harp, palms), pictureEverySec);
                var plain = rig.Unaccented(Part.Harp, 3.0);
                Assert.GreaterOrEqual(plain.Count, 6);
                strengths.Add(plain.Average(n => n.Velocity));
                TestContext.Out.WriteLine($"CONDUCT-LOUDNESS palms={palms} tracker={1.0 / pictureEverySec:0}fps velocity={strengths.Last():0.000} expected={rig.Settings.VelocityForMotion(palms):0.000}");
            }

            for (var i = 1; i < strengths.Count; i++) Assert.Greater(strengths[i], strengths[i - 1] + 0.05f, $"{i}: the more the hand moves the louder it plays");
            Assert.AreEqual(MinVelocity, strengths[0], 0.02f, "a hand held still is the softest");
            Assert.AreEqual(1f, strengths[4], 0.03f, "and one that is waved hard (more than loudPalms) is at full strength");
        }

        [TestCase(Fps30)]
        [TestCase(Fps6)]
        public void Conduct_HeightNoLongerChangesLoudness(double pictureEverySec)
        {
            var strengths = new List<float>();
            foreach (var y in new[] { 0.12f, 0.25f, 0.38f })   // high, middle and low, all in the sky row
            {
                var still = new ConductRig();
                still.Run(6.0, ConductRig.Still(0.17f, y), pictureEverySec);
                strengths.Add(still.Unaccented(Part.Harp, SettleSec).Average(n => n.Velocity));
                var waved = new ConductRig();
                waved.Run(8.0, ConductRig.Circle(0.17f, y, 2.5f, 0.1f), pictureEverySec);
                strengths.Add(waved.Unaccented(Part.Harp, 3.0).Average(n => n.Velocity));
            }

            Assert.AreEqual(strengths[0], strengths[2], 0.01f, "a still hand: high or low, the same");
            Assert.AreEqual(strengths[2], strengths[4], 0.01f);
            Assert.AreEqual(strengths[1], strengths[3], 0.03f, "a hand waved at the same speed: high or low, the same");
            Assert.AreEqual(strengths[3], strengths[5], 0.03f);
            Assert.Greater(strengths[1], strengths[0] + 0.1f, "but movement does change it");
        }

        [Test]
        public void TheRowOfTheHand_DoesNotChangeLoudnessEither()
        {
            var sky = new ConductRig();
            var ground = new ConductRig();

            sky.Run(8.0, ConductRig.CircleIn(Part.Harp, 2.5f), Fps30);
            ground.Run(8.0, ConductRig.CircleIn(Part.LowString, 2.5f), Fps30);

            Assert.AreEqual(sky.Unaccented(Part.Harp, 3.0).Average(n => n.Velocity), ground.Unaccented(Part.LowString, 3.0).Average(n => n.Velocity), 0.03f);
        }

        [Test]
        public void VelocityForMotion_RisesStraightFromMinVelocityAtStillPalmsToOneAtLoudPalms()
        {
            var settings = ConductSettings.Default;

            Assert.AreEqual(settings.MinVelocity, settings.VelocityForMotion(0f), 1e-6f);
            Assert.AreEqual(settings.MinVelocity, settings.VelocityForMotion(settings.StillPalms), 1e-6f, "the jitter of a still hand is not movement");
            Assert.AreEqual(1f, settings.VelocityForMotion(settings.LoudPalms), 1e-6f);
            Assert.AreEqual(1f, settings.VelocityForMotion(settings.LoudPalms * 3f), 1e-6f, "and no louder than that");
            var middle = (settings.StillPalms + settings.LoudPalms) / 2f;
            Assert.AreEqual((settings.MinVelocity + 1f) / 2f, settings.VelocityForMotion(middle), 1e-6f);
            Assert.AreEqual(settings.MinVelocity, settings.VelocityForMotion(-1f), 1e-6f);
        }

        // ---- a hand that goes ---------------------------------------------------------------------------------------------------------------

        [TestCase(Fps30)]
        [TestCase(Fps6)]
        public void AHandThatGoes_FadesToNothingInReleaseSec_ThenNothingPlays_AndWhenItComesBackItGrowsAgain(double pictureEverySec)
        {
            var rig = new ConductRig();
            var goesAt = 6.0;
            var backAt = 14.0;

            rig.Run(20.0, ConductRig.Gone(ConductRig.StillIn(Part.Harp), goesAt, backAt), pictureEverySec);

            var start = ConductRig.GridOrigin - 1.0;
            double At(NoteIntent n) => rig.Grid.SlotTime(rig.SlotOf(n)) - start;
            var before = rig.Notes.Where(n => At(n) > 3.0 && At(n) < goesAt).ToList();
            var fading = rig.Notes.Where(n => At(n) >= goesAt && At(n) < backAt).ToList();
            var after = rig.Notes.Where(n => At(n) >= backAt).ToList();
            Assert.IsNotEmpty(before);
            Assert.IsNotEmpty(fading, "the music does not stop at once");
            for (var i = 1; i < fading.Count; i++) Assert.LessOrEqual(fading[i].Velocity, fading[i - 1].Velocity + 1e-4f, "only quieter");
            Assert.Less(fading.Last().Velocity, before.Average(n => n.Velocity) * 0.5f, "it has become much quieter by the end");
            var lastFadingAt = At(fading.Last());
            Assert.Less(lastFadingAt, goesAt + 0.8 + 1.5 + 0.7, "hold (0.8 s) and release (1.5 s) after the hand has gone, and no more notes after that");
            Assert.Greater(lastFadingAt, goesAt + 0.8, "it fades, it does not stop at the moment the hand is missed");
            Assert.IsNotEmpty(after, "the hand is back: the music is back");
            Assert.Greater(after.Max(n => n.Velocity), 0.8f * before.Max(n => n.Velocity), "and as loud as before");
        }

        [Test]
        public void AHandThatIsMissedForAMoment_GoesOnPlayingWhereItWas()
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.Gone(ConductRig.StillIn(Part.Harp), 5.0, 5.6), Fps6);

            var gaps = rig.Gaps(Part.Harp, SettleSec);
            Assert.That(gaps, Has.All.EqualTo(4L), "0.6 s without a picture of the hand is not a gap in the music");
            Assert.That(rig.Notes.Select(n => n.Velocity), Has.All.GreaterThanOrEqualTo(MinVelocity - 1e-4f), "and it did not fade");
        }

        [Test]
        public void ATrackerThatStopsSendingPictures_LetsTheMusicFade_EvenThoughTheLastPictureStillShowsTheHand()
        {
            var rig = new ConductRig();
            rig.Run(6.0, ConductRig.StillIn(Part.Harp), Fps30);
            var playedBefore = rig.Notes.Count;

            rig.Run(6.0, ConductRig.StillIn(Part.Harp), 1000.0);   // no picture arrives for the next six seconds: the motion tracker keeps showing the last one

            var late = rig.Notes.Skip(playedBefore).Where(n => rig.Grid.SlotTime(rig.SlotOf(n)) - (ConductRig.GridOrigin - 1.0) > 6.0 + 0.8 + 1.5 + 0.7).ToList();
            Assert.IsEmpty(late, "a hand that is not seen any more is not played for ever");
            Assert.Less(rig.Notes.Last().Velocity, rig.Notes[playedBefore - 1].Velocity, "it faded");
        }

        // ---- voices -------------------------------------------------------------------------------------------------------------------------

        [Test]
        public void TwoHandsOnTheSamePart_AreOneVoice()
        {
            var rig = new ConductRig();
            var hands = ConductRig.Both(ConductRig.Still(0.15f, 0.2f, 0), ConductRig.Still(0.25f, 0.3f, 1));

            rig.Run(8.0, hands, Fps30);

            Assert.That(rig.Notes.Select(n => n.Part), Has.All.EqualTo(Part.Harp));
            Assert.That(rig.Gaps(Part.Harp, SettleSec), Has.All.EqualTo(4L), "one note a beat, not two");
        }

        [Test]
        public void TwoHandsOnTheSamePart_TheOneThatMovesMoreSetsTheStrength()
        {
            var rig = new ConductRig();
            var still = ConductRig.Still(0.1f, 0.2f, 0);
            var waving = ConductRig.Circle(0.22f, 0.25f, 3f, 0.08f, 1);

            rig.Run(10.0, ConductRig.Both(still, waving), Fps30);

            var plain = rig.Unaccented(Part.Harp, 3.0);
            Assert.AreEqual(rig.Settings.VelocityForMotion(3f), plain.Average(n => n.Velocity), 0.05f, "the louder hand counts, not the average of the two");
            Assert.Greater(plain.Average(n => n.Velocity), MinVelocity + 0.3f);
        }

        [Test]
        public void TwoHandsOnTwoParts_AreTwoVoices()
        {
            var rig = new ConductRig();

            rig.Run(8.0, ConductRig.Both(ConductRig.StillIn(Part.Harp, 0), ConductRig.StillIn(Part.HandDrum, 1)), Fps30);

            Assert.That(rig.Of(Part.Harp).Count(), Is.GreaterThan(5));
            Assert.That(rig.Of(Part.HandDrum).Count(), Is.GreaterThan(5));
            Assert.AreEqual(0, rig.Of(Part.Recorder).Count());
            Assert.AreEqual(0, rig.Of(Part.Marimba).Count());
        }

        [Test]
        public void TwoHandsInTheSameColumnButOtherRows_AreTwoVoices()
        {
            var rig = new ConductRig();

            rig.Run(8.0, ConductRig.Both(ConductRig.StillIn(Part.Recorder, 0), ConductRig.StillIn(Part.Marimba, 1)), Fps30);

            Assert.That(rig.Of(Part.Recorder).Count(), Is.GreaterThan(3), "the sky above");
            Assert.That(rig.Of(Part.Marimba).Count(), Is.GreaterThan(5), "and the ground below");
        }

        [Test]
        public void AHandOnTheEdgeOfTwoParts_DoesNotJumpBetweenThem()
        {
            var rig = new ConductRig();
            var edge = 1f / 3f;

            rig.Run(10.0, ConductRig.Still(edge + 0.004f, 0.25f), Fps30);

            Assert.AreEqual(1, rig.Notes.Select(n => n.Part).Distinct().Count(), "the dead band around the edge holds the part");
        }

        [Test]
        public void AHandOnTheLineBetweenTheRows_DoesNotJumpBetweenThem()
        {
            var rig = new ConductRig();
            var line = CellSettings.Default.RowSplit;
            var start = ConductRig.Still(0.17f, line - 0.02f);
            var wobble = ConductRig.Still(0.17f, line + 0.012f);

            rig.Run(2.0, start, Fps30);
            rig.Run(8.0, wobble, Fps30);   // the hand sinks a little past the line: the dead band keeps it in the row it was in

            Assert.AreEqual(1, rig.Notes.Select(n => n.Part).Distinct().Count());
            Assert.AreEqual(Part.Harp, rig.Notes[0].Part);
        }

        // ---- the song ----------------------------------------------------------------------------------------------------------------------

        [Test]
        public void ThePhraseAdvancesOneStepPerNote_AndStartsAgainAtEveryBarLine()
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.StillIn(Part.Harp), Fps30);

            var phrase = new[] { 0, 2, 4, 7, 4, 2 };
            var bars = rig.Of(Part.Harp).GroupBy(n => rig.SlotOf(n) / ConductRig.SlotsPerBar).Where(g => g.Count() == 4).ToList();
            Assert.GreaterOrEqual(bars.Count, 2);
            foreach (var bar in bars) CollectionAssert.AreEqual(phrase.Take(4).ToArray(), bar.Select(n => n.PitchStep).ToArray(), "a bar holds four notes and starts the phrase at its first");
        }

        [Test]
        public void ThePhraseWrapsAround_WhenABarHoldsMoreNotesThanThePhrase()
        {
            var rig = new ConductRig();

            rig.Run(8.0, ConductRig.CircleIn(Part.Harp, 6f), Fps30);

            var bar = rig.Of(Part.Harp).GroupBy(n => rig.SlotOf(n) / ConductRig.SlotsPerBar).First(g => g.Count() == ConductRig.SlotsPerBar);
            var phrase = new[] { 0, 2, 4, 7, 4, 2 };
            CollectionAssert.AreEqual(Enumerable.Range(0, ConductRig.SlotsPerBar).Select(i => phrase[i % phrase.Length]).ToArray(), bar.Select(n => n.PitchStep).ToArray());
        }

        [Test]
        public void EveryPart_PlaysItsOwnPhrase([ValueSource(nameof(AllParts))] Part part)
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.StillIn(part), Fps30);

            var phrase = rig.Settings.Phrase(part);
            var bars = rig.Of(part).GroupBy(n => rig.SlotOf(n) / ConductRig.SlotsPerBar).Where(g => g.Count() == 4).ToList();
            Assert.GreaterOrEqual(bars.Count, 2);
            foreach (var bar in bars) CollectionAssert.AreEqual(Enumerable.Range(0, 4).Select(i => phrase[i % phrase.Count]).ToArray(), bar.Select(n => n.PitchStep).ToArray(), $"{part}");
        }

        [Test]
        public void TheFirstBeatOfABarIsAccented()
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.StillIn(Part.Harp), Fps30);

            var downbeats = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) % ConductRig.SlotsPerBar == 0).Select(n => n.Velocity).ToList();
            var others = rig.Of(Part.Harp).Where(n => rig.SlotOf(n) % ConductRig.SlotsPerBar != 0).Select(n => n.Velocity).ToList();
            Assert.IsNotEmpty(downbeats);
            Assert.AreEqual(others.Average() + 0.1f, downbeats.Average(), 0.01f);
        }

        // ---- the beat grid -----------------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryNote_IsPutOnItsOwnSlotByTheScheduler_WithoutBeingPushedOrDropped()
        {
            var rig = new ConductRig();
            var scheduler = new QuantizedNoteScheduler(rig.Grid, new SchedulerSettings(ConductRig.LeadSec, 2, ConductRig.DefaultGaps));

            rig.Run(12.0, ConductRig.Both(ConductRig.CircleIn(Part.Harp, 5f, 0.1f, 0), ConductRig.CircleIn(Part.Bell, 2f, 0.05f, 1)), Fps6);

            Assert.GreaterOrEqual(rig.Notes.Count, 50);
            foreach (var note in rig.Notes)
            {
                Assert.IsTrue(scheduler.TrySchedule(note.RequestedAt, note, out var scheduled), "the scheduler takes it");
                Assert.AreEqual(rig.SlotOf(note), scheduled.Slot);
                Assert.IsFalse(scheduled.WasPushed, "and puts it where the engine meant it");
                Assert.GreaterOrEqual(scheduled.AudioTime, note.RequestedAt + ConductRig.LeadSec - 1e-6, "no closer than the lead");
            }
        }

        [Test]
        public void EveryNote_IsForTheNextSlotTheSchedulerCanReachAtTheMomentItIsMade()
        {
            var rig = new ConductRig();

            rig.Run(10.0, ConductRig.CircleIn(Part.Harp, 3f), Fps6);

            Assert.GreaterOrEqual(rig.Notes.Count, 20);
            for (var i = 0; i < rig.Notes.Count; i++)
            {
                var next = rig.Grid.SlotForTrigger(rig.MadeAt[i], ConductRig.LeadSec);
                Assert.AreEqual(next, rig.SlotOf(rig.Notes[i]), "made at " + rig.MadeAt[i] + ": it names the slot that is up next, not the one after");
            }
        }

        [Test]
        public void ANoteIsRequestedOnlyOnceTheSlotIsTheNextOneTheSchedulerCanReach_NeverInThePast()
        {
            var rig = new ConductRig();

            rig.Run(8.0, ConductRig.CircleIn(Part.Harp, 5f), Fps30);

            foreach (var note in rig.Notes) Assert.GreaterOrEqual(rig.Grid.SlotTime(rig.SlotOf(note)), rig.Grid.Origin, "never before the first bar line");
        }

        [Test]
        public void TheSameHandsAtTheSameTimes_GiveTheSameNotes()
        {
            List<(long slot, int step, float velocity, Part part)> Play()
            {
                var rig = new ConductRig();
                rig.Run(10.0, ConductRig.Both(ConductRig.CircleIn(Part.Harp, 3f, 0.1f, 0), ConductRig.StillIn(Part.HandDrum, 1)), ConductRig.UnevenPictures(11));
                return rig.Notes.Select(n => (rig.SlotOf(n), n.PitchStep, n.Velocity, n.Part)).ToList();
            }

            CollectionAssert.AreEqual(Play(), Play());
        }

        [Test]
        public void ANoteCarriesTheValidHandTiming_AndTheCellOfTheHand()
        {
            var rig = new ConductRig();

            rig.Run(4.0, ConductRig.StillIn(Part.HandDrum), Fps30);

            Assert.IsNotEmpty(rig.Notes);
            Assert.That(rig.Notes.Select(n => n.Timing.IsValid), Has.All.True, "a note with hand timing counts as the hand's: the meadow knows somebody is there");
            Assert.That(rig.Notes.Select(n => n.X01), Has.All.EqualTo(0.83f).Within(0.02f));
            Assert.That(rig.Notes.Select(n => n.Y01), Has.All.EqualTo(0.75f).Within(0.02f), "and how far down the picture it is: the effects of the sky are drawn there");
        }

        [Test]
        public void WithoutGridOrSettings_NothingPlays()
        {
            var unconfigured = new ConductEngine();
            var rig = new ConductRig();
            var notes = new List<NoteIntent>();
            rig.Run(2.0, ConductRig.StillIn(Part.Harp), Fps30);

            unconfigured.Update(11.0, rig.Motion, 1f, notes);

            Assert.IsEmpty(notes);
        }

        // ---- review of A-C: a long frame, a short loss, a crossing -----------------------------------------------------------------------------

        [Test]
        public void AFrameLongerThanASlot_StillStartsThePhraseAgainAtTheBarLineItJumpedOver()
        {
            var rig = new ConductRig();
            var seq = 0L;
            void Feed(double audioNow)
            {
                var ms = (audioNow - (ConductRig.GridOrigin - 1.0)) * 1000.0;
                rig.Motion.Update(HandTestData.Frame(++seq, ms, new HandTestData.Pose(0, 0.17f, 0.25f)));
                rig.Engine.Update(audioNow, rig.Motion, 1f, rig.Notes);
            }

            var barLine = ConductRig.GridOrigin + 2 * ConductRig.SlotsPerBar * 60.0 / ConductRig.Bpm / ConductRig.SlotsPerBeat;   // the third bar line: 15.0 s
            for (var frame = 0; frame <= 342; frame++) Feed(ConductRig.GridOrigin - 1.0 + frame / 60.0);   // 60 frames a second up to 0.3 s before the bar line
            Feed(barLine);                                                                                      // one frame of 0.3 s: the slot of the bar line is never scanned
            for (var frame = 1; frame <= 180; frame++) Feed(barLine + frame / 60.0);

            var barSlot = rig.Grid.FirstSlotAtOrAfter(barLine);
            Assert.AreEqual(0, barSlot % ConductRig.SlotsPerBar, "the test jumps over a bar line");
            Assert.IsFalse(rig.Notes.Any(n => rig.SlotOf(n) == barSlot), "no note was played on the slot that was jumped over");
            var first = rig.Of(Part.Harp).First(n => rig.SlotOf(n) > barSlot);
            Assert.AreEqual(ConductSettings.Default.Phrase(Part.Harp)[0], first.PitchStep, "the first note after the bar line is the first step of the phrase, as at every bar line");
        }

        [Test]
        public void AHandThatTheTrackerLosesForAMoment_KeepsItsMovement_SoTheDensityDoesNotFall()
        {
            var rig = new ConductRig();
            var waving = ConductRig.CircleIn(Part.Harp, 7f);
            Func<double, Pose[]> withLosses = t =>
            {
                var phase = (t - 3.0) % 3.0;
                return t > 3.0 && phase < 0.5 ? new Pose[0] : waving(t);   // three pictures at 6 a second without the hand every 3 s: more than the 250 ms after which the tracker starts the hand's history again
            };

            rig.Run(40.0, withLosses, Fps6);

            var gaps = rig.Gaps(Part.Harp, 3.0);
            Assert.GreaterOrEqual(gaps.Count, 100);
            Assert.That(gaps, Has.All.EqualTo(1L), "a hand that was waving goes on playing a note every slot: after a short loss it does not read as still for a beat");
        }

        [TestCase(Part.Harp, Part.Recorder)]
        [TestCase(Part.Recorder, Part.Bell)]
        [TestCase(Part.Harp, Part.LowString)]
        [TestCase(Part.Marimba, Part.Recorder)]
        public void ACrossingIntoTheNextCell_FadesTheOldPartQuickly_ButAHandThatGoesTakesTheReleaseSec(Part from, Part to)
        {
            var crossing = new ConductRig();
            var leaving = new ConductRig();
            var crossAt = 6.0;
            var settings = crossing.Settings;

            crossing.Run(12.0, t => t < crossAt ? ConductRig.StillIn(from)(t) : ConductRig.StillIn(to)(t), Fps30);
            leaving.Run(12.0, ConductRig.Gone(ConductRig.StillIn(from), crossAt), Fps30);

            var crossAbs = ConductRig.GridOrigin - 1.0 + crossAt;
            IEnumerable<double> MadeAfter(ConductRig rig, Part part) =>
                Enumerable.Range(0, rig.Notes.Count).Where(i => rig.Notes[i].Part == part && rig.MadeAt[i] > crossAbs).Select(i => rig.MadeAt[i]);
            var lastAfterCrossing = MadeAfter(crossing, from).DefaultIfEmpty(crossAbs).Max();
            var lastAfterLeaving = MadeAfter(leaving, from).DefaultIfEmpty(crossAbs).Max();

            Assert.Less(lastAfterCrossing - crossAbs, settings.CrossfadeSec + 0.2, $"{from} is gone about as soon as the crossfade is over: two instruments do not play on together");
            Assert.IsNotEmpty(MadeAfter(crossing, to), $"and {to} took over");
            Assert.Greater(lastAfterLeaving - crossAbs, 0.6, "a hand that goes still fades over releaseSec");
        }
    }
}
