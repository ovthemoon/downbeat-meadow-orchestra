using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class NatureVisualModelTests
    {
        const float Dt = 1f / 60f;

        static void Run(NatureVisualModel model, float seconds, float energy, int level, bool awake = true)
        {
            for (var t = 0f; t < seconds - 1e-4f; t += Dt) model.Update(Dt, energy, level, awake);
        }

        [Test]
        public void Sky_GlidesTowardsTheEnergy_AboutSixtyThreePercentAfterOneSmoothingTime()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f);

            Run(model, 2.5f, 0.8f, 3);

            Assert.AreEqual(0.8f * (1f - (float)Math.Exp(-1.0)), model.SkyT, 0.01f);
            Run(model, 20f, 0.8f, 3);
            Assert.AreEqual(0.8f, model.SkyT, 0.005f);
        }

        [Test]
        public void SmoothingTimeZero_FollowsAtOnce()
        {
            var model = new NatureVisualModel(0f, 0.35f, 0.6f);

            model.Update(Dt, 0.6f, 2, true);

            Assert.AreEqual(0.6f, model.SkyT, 1e-6f);
            Assert.AreEqual(0.6f, model.FireflyLevel, 1e-6f);
        }

        [Test]
        public void Fireflies_ComeWithLevel1_AndLeaveGentlyWhenTheLevelFallsToZero()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f);

            Run(model, 10f, 0.15f, 0);
            Assert.AreEqual(0f, model.FireflyLevel, 1e-6f, "level 0: no fireflies, whatever the energy");

            Run(model, 10f, 0.3f, 1);
            Assert.AreEqual(0.3f, model.FireflyLevel, 0.01f);

            model.Update(Dt, 0.15f, 0, true);
            Assert.Greater(model.FireflyLevel, 0.25f, "they fade out over seconds, not in one frame");
        }

        [Test]
        public void WhenNobodyIsThere_TheSkyAndTheFirefliesGoBackToRest()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f);
            Run(model, 10f, 0.9f, 3);
            Assert.Greater(model.SkyT, 0.8f);

            Run(model, 20f, 0.9f, 3, awake: false);

            Assert.Less(model.SkyT, 0.01f);
            Assert.Less(model.FireflyLevel, 0.01f);
        }

        [Test]
        public void HillPulse_FlashesToFullOnADrum_FadesLinearly_AndRestartsOnTheNextDrum()
        {
            var model = new NatureVisualModel(2.5f, 0.4f, 0.5f);
            Assert.AreEqual(0f, model.HillPulse);

            model.OnHillBeat();
            Assert.AreEqual(0.4f, model.HillPulse, 1e-6f);
            Run(model, 0.25f, 0f, 0);
            Assert.AreEqual(0.2f, model.HillPulse, 0.01f);
            Run(model, 0.5f, 0f, 0);
            Assert.AreEqual(0f, model.HillPulse, 1e-6f);

            model.OnHillBeat();
            Run(model, 0.1f, 0f, 0);
            model.OnHillBeat();
            Assert.AreEqual(0.4f, model.HillPulse, 1e-6f);
        }

        [Test]
        public void NonsenseInput_IsHandled()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f);

            model.Update(0f, 0.5f, 3, true);
            model.Update(-1f, 0.5f, 3, true);
            Assert.AreEqual(0f, model.SkyT);
            model.Update(Dt, float.NaN, 3, true);
            model.Update(Dt, 5f, 3, true);

            Assert.IsTrue(model.SkyT >= 0f && model.SkyT <= 1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(-1f, 0.35f, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(1f, 1.5f, 0.6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(1f, 0.3f, 0f));
        }

        // ---- the sky and the energy of conducting play (Part D, #8) ---------------------------------------------------------------------------

        const float SkyLow = 0.15f;
        const float SkyHigh = 0.9f;

        [Test]
        public void ASustainedStrongPerformance_ReachesTheSunset()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f, SkyLow, SkyHigh);

            Run(model, 40f, 0.975f, 3);   // a hand that conducts, 30 s in (conduct-energy-sim.json)

            Assert.GreaterOrEqual(model.SkyT, 0.995f, "the sky is at the sunset");
        }

        [Test]
        public void AHeldStillHand_NeverReachesTheSunset_NorTheDuskOfAPlayingHand()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f, SkyLow, SkyHigh);

            Run(model, 120f, 0.391f, 1);   // the highest a held still hand gets (0.26 to 0.39)

            Assert.Less(model.SkyT, 0.35f, "a dusk at most, still far from the sunset");
            Assert.Greater(model.SkyT, 0.1f, "but not the blue hour of nobody being there: somebody is");
        }

        [Test]
        public void ASlowHand_IsInTheMiddleOfTheArc()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f, SkyLow, SkyHigh);

            Run(model, 60f, 0.58f, 2);

            Assert.That(model.SkyT, Is.InRange(0.45f, 0.65f));
        }

        [Test]
        public void TheSkyWantedForAnEnergy_NeverGoesBackWhenTheEnergyGrows_AndIsPinnedAtBothEnds()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f, SkyLow, SkyHigh);
            var last = -1f;

            for (var energy = 0f; energy <= 1.0001f; energy += 0.01f)
            {
                var wanted = model.SkyFor(energy);
                Assert.GreaterOrEqual(wanted, last - 1e-6f);
                Assert.That(wanted, Is.InRange(0f, 1f));
                last = wanted;
            }
            Assert.AreEqual(0f, model.SkyFor(0.1f));
            Assert.AreEqual(1f, model.SkyFor(0.95f));
        }

        [Test]
        public void WhenNobodyIsThere_TheSkyGoesBackToTheBlueHour_WhateverTheEnergySays()
        {
            var model = new NatureVisualModel(2.5f, 0.35f, 0.6f, SkyLow, SkyHigh);
            Run(model, 30f, 0.975f, 3);

            Run(model, 40f, 0.975f, 3, awake: false);

            Assert.Less(model.SkyT, 0.01f);
        }

        [Test]
        public void TheSkyRange_IsChecked()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(2.5f, 0.35f, 0.6f, 0.9f, 0.9f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(2.5f, 0.35f, 0.6f, 0.5f, 1.2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NatureVisualModel(2.5f, 0.35f, 0.6f, -0.1f, 0.9f));
        }
    }

    public class BeatTickerTests
    {
        const double Origin = 0.5;
        static BeatGrid Grid() => new BeatGrid(Origin, 96.0, 4);
        const double BeatSec = 60.0 / 96.0;

        [Test]
        public void EachBeatComesOnce_InOrder_WithDownbeatsEveryFourth()
        {
            var ticker = new BeatTicker(Grid(), 4, 0.1);
            var ticks = new List<BeatTick>();

            for (var t = 10.0; t < 30.0; t += 0.016) ticker.Collect(t, 0.0, ticks);

            Assert.Greater(ticks.Count, 30);
            for (var i = 1; i < ticks.Count; i++) Assert.AreEqual(ticks[i - 1].Beat + 1, ticks[i].Beat, "no beat twice, none missed");
            Assert.IsTrue(ticks.All(b => Math.Abs(b.AudioTime - (Origin + b.Beat * BeatSec)) < 1e-9));
            Assert.IsTrue(ticks.All(b => b.IsDownbeat == (b.Beat % 4 == 0)));
        }

        [Test]
        public void ABeatIsHandedOutWhenItIsDueMinusTheLead()
        {
            var ticker = new BeatTicker(Grid(), 4, 0.1);
            var ticks = new List<BeatTick>();
            ticker.Collect(10.0, 0.0, ticks);
            ticks.Clear();
            var next = Origin + Math.Ceiling((10.0 - Origin) / BeatSec + 1e-9) * BeatSec;

            ticker.Collect(next - 0.02, 0.007, ticks);
            Assert.IsEmpty(ticks, "13 ms early with a 7 ms lead: not yet");
            ticker.Collect(next - 0.005, 0.007, ticks);
            Assert.AreEqual(1, ticks.Count);
        }

        [Test]
        public void AStall_SkipsTheOldBeats_InsteadOfFiringABunch()
        {
            var ticker = new BeatTicker(Grid(), 4, 0.1);
            var ticks = new List<BeatTick>();
            ticker.Collect(10.0, 0.0, ticks);
            ticks.Clear();

            ticker.Collect(15.0, 0.0, ticks);

            Assert.LessOrEqual(ticks.Count, 1, "five seconds of beats are gone; at most the one that is within the grace period comes");
        }

        [Test]
        public void StartingMidSong_DoesNotReplayThePast()
        {
            var ticker = new BeatTicker(Grid(), 4, 0.1);
            var ticks = new List<BeatTick>();

            ticker.Collect(100.0, 0.0, ticks);

            Assert.LessOrEqual(ticks.Count, 1);
        }

        [Test]
        public void Constructor_RejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeatTicker(Grid(), 0, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BeatTicker(Grid(), 4, -0.1));
        }
    }
}
