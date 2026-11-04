using System;
using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>
    /// The two-fists ending on scripted hands, at 30 and at 6 pictures a second (the weak laptop's tracker): a spread triggers once, and nothing that happens in ordinary play does.
    /// Distances are in palm lengths; the hands sit around the middle of the picture.
    /// </summary>
    public class CutoffGestureTests
    {
        const float Aspect = 4f / 3f;
        const float Palm = 0.1f;
        const double StartSec = 100.0; // not 0: a clock that has run for a while
        const double SpreadAt = 1.0;
        const double SpreadDoneAt = 1.5;

        readonly struct Pictures
        {
            public int Count { get; }
            public CutoffHand First { get; }
            public CutoffHand Second { get; }

            public Pictures(int count, CutoffHand first, CutoffHand second)
            {
                Count = count; First = first; Second = second;
            }
        }

        /// <summary>Two hands <paramref name="apartPalms"/> apart (sideways), centred on x 0.5.</summary>
        static Pictures Pair(double apartPalms, bool firstFist = true, bool secondFist = true, int count = 2, float palm = Palm, float aspect = Aspect)
        {
            var half = (float)apartPalms * palm / aspect * 0.5f;
            return new Pictures(count, new CutoffHand(0.5f - half, 0.5f, palm, firstFist), new CutoffHand(0.5f + half, 0.5f, palm, secondFist));
        }

        /// <summary>Two fists <paramref name="width"/> apart as a fraction of the picture width, whatever the picture is (the palm size is given in video heights).</summary>
        static Pictures PairByWidth(double width, float palm) =>
            new Pictures(2, new CutoffHand(0.5f - (float)width * 0.5f, 0.5f, palm, true), new CutoffHand(0.5f + (float)width * 0.5f, 0.5f, palm, true));

        static double Ramp(double t, double from, double to, double fromSec, double toSec) =>
            t <= fromSec ? from : t >= toSec ? to : from + (to - from) * (t - fromSec) / (toSec - fromSec);

        /// <summary>Fists one palm apart that are pulled to six palms apart between <see cref="SpreadAt"/> and <see cref="SpreadDoneAt"/> and stay there.</summary>
        static double SpreadTrajectory(double t) => Ramp(t, 1.0, 6.0, SpreadAt, SpreadDoneAt);

        static List<double> TriggerTimes(int fps, double seconds, Func<double, Pictures> at, CutoffSettings settings = null, float aspect = Aspect) =>
            Run(new CutoffGesture(settings ?? CutoffSettings.Default), fps, 0.0, seconds, at, aspect);

        /// <summary>Feeds the pictures of [fromSec, toSec] (seconds after the start) and returns the times (seconds after the start) at which the gesture said true.</summary>
        static List<double> Run(CutoffGesture gesture, int fps, double fromSec, double toSec, Func<double, Pictures> at, float aspect = Aspect)
        {
            var times = new List<double>();
            var first = (int)Math.Round(fromSec * fps);
            var last = (int)Math.Round(toSec * fps);
            for (var i = first; i <= last; i++)
            {
                var t = (double)i / fps;
                var p = at(t);
                if (gesture.Update(StartSec + t, p.Count, p.First, p.Second, aspect)) times.Add(t);
            }
            return times;
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TwoFistsSpread_Triggers(int fps)
        {
            var times = TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t)));

            Assert.AreEqual(1, times.Count, "once");
            Assert.GreaterOrEqual(times[0], SpreadAt + 0.25 + 0.2 - 1e-9, "not before the fists are 3.5 palms apart and have stayed so for the hold");
            Assert.LessOrEqual(times[0], SpreadDoneAt + 0.2 + 2.0 / fps + 1e-9, "and soon after: a picture or two late at most");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OneFistOneOpen_NeverTriggers(int fps)
        {
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), firstFist: true, secondFist: false)), "right hand open");
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), firstFist: false, secondFist: true)), "left hand open");
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), firstFist: false, secondFist: false)), "both open");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OneFistAndAHandThatIsNotThere_NeverTriggers(int fps)
        {
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), count: 1)));
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), count: 0)));
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OpenHandsWavingWildly_ForThirtySeconds_NeverTriggers(int fps)
        {
            var random = new Random(11);
            var times = TriggerTimes(fps, 30.0, t =>
            {
                // three waves a second across the whole picture, some jitter, and a hand or both hidden in a picture now and then
                var apart = 5.5 + 4.5 * Math.Sin(2.0 * Math.PI * 3.0 * t) + (random.NextDouble() - 0.5);
                var hidden = random.NextDouble();
                var count = hidden < 0.1 ? 1 : hidden < 0.15 ? 0 : 2;
                return Pair(Math.Max(0.2, apart), firstFist: false, secondFist: false, count: count);
            });

            Assert.IsEmpty(times);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void OneFistWavingWithAnOpenHand_ForThirtySeconds_NeverTriggers(int fps)
        {
            var times = TriggerTimes(fps, 30.0, t => Pair(5.5 + 4.5 * Math.Sin(2.0 * Math.PI * 2.0 * t), firstFist: true, secondFist: false));

            Assert.IsEmpty(times);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void FistsTogetherWithoutSpreading_NeverTriggers(int fps)
        {
            var random = new Random(5);
            Assert.IsEmpty(TriggerTimes(fps, 10.0, t => Pair(1.0 + 0.6 * (random.NextDouble() - 0.5))), "fists together, trembling");
            Assert.IsEmpty(TriggerTimes(fps, 10.0, t => Pair(6.0)), "fists held wide apart from the start");
            Assert.IsEmpty(TriggerTimes(fps, 30.0, t => Pair(1.0 + 0.2 * t)), "fists that drift apart slowly (0.2 palm a second)");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void FistsMovingApartAFewPalmsButNotFarEnough_NeverTrigger(int fps)
        {
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(Ramp(t, 0.5, 3.0, SpreadAt, SpreadDoneAt))), "2.5 palms of spread, but they end 3 palms apart (3.5 needed)");
            Assert.IsEmpty(TriggerTimes(fps, 4.0, t => Pair(Ramp(t, 3.0, 4.5, SpreadAt, SpreadDoneAt))), "they end far apart but the spread was only 1.5 palms");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void ASpreadThatIsNotHeld_NeverTriggers(int fps)
        {
            // the fists fly apart and are back together within 0.15 s: the hold (0.2 s) is not met
            var times = TriggerTimes(fps, 4.0, t => Pair(t >= 1.5 && t < 1.65 ? 6.0 : 1.0));

            Assert.IsEmpty(times);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void TheSpreadMustFitInTheWindow(int fps)
        {
            // 5 palms of spread, but in 3 s while the window is 1 s: nowhere inside any second is the growth 2 palms (it is 1.7)
            var times = TriggerTimes(fps, 8.0, t => Pair(Ramp(t, 1.0, 6.0, 1.0, 4.0)));

            Assert.IsEmpty(times);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void Retrigger_OnlyAfterHandsOpen(int fps)
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);

            var first = Run(gesture, fps, 0.0, 3.0, t => Pair(SpreadTrajectory(t)));
            // the fists stay up: they come together again and fly apart again, twice
            var again = Run(gesture, fps, 3.0 + 1.0 / fps, 8.0, t => Pair(t % 2.0 < 1.0 ? Ramp(t % 1.0, 6.0, 1.0, 0.0, 0.3) : Ramp(t % 1.0, 1.0, 6.0, 0.4, 0.8)));
            // both hands open (a second), close into fists together and spread
            var open = Run(gesture, fps, 8.0 + 1.0 / fps, 9.0, t => Pair(1.0, firstFist: false, secondFist: false));
            var afterOpen = Run(gesture, fps, 9.0 + 1.0 / fps, 12.0, t => Pair(Ramp(t, 1.0, 6.0, 10.0, 10.5)));

            Assert.AreEqual(1, first.Count, "the first end");
            Assert.IsEmpty(again, "fists that stay up do not end anything again, however often they spread");
            Assert.IsEmpty(open);
            Assert.AreEqual(1, afterOpen.Count, "after the hands were open the gesture works again");
        }

        [TestCase(30)]
        [TestCase(6)]
        public void Retrigger_NeedsBothHandsToStopBeingFists_NotJustOne(int fps)
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);
            Run(gesture, fps, 0.0, 3.0, t => Pair(SpreadTrajectory(t)));

            // the right hand opens for a second while the left stays a fist; then it closes again and the fists fly apart again
            Run(gesture, fps, 3.0 + 1.0 / fps, 4.0, t => Pair(1.0, firstFist: true, secondFist: false));
            var afterwards = Run(gesture, fps, 4.0 + 1.0 / fps, 8.0, t => Pair(Ramp(t, 1.0, 6.0, 5.0, 5.5)));

            Assert.IsEmpty(afterwards);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void Retrigger_AlsoAfterTheHandsLeftTheCamera(int fps)
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);
            Run(gesture, fps, 0.0, 3.0, t => Pair(SpreadTrajectory(t)));
            Run(gesture, fps, 3.0 + 1.0 / fps, 4.0, t => Pair(1.0, count: 0));

            var afterwards = Run(gesture, fps, 4.0 + 1.0 / fps, 8.0, t => Pair(Ramp(t, 1.0, 6.0, 5.0, 5.5)));

            Assert.AreEqual(1, afterwards.Count);
        }

        [TestCase(30)]
        [TestCase(6)]
        public void AHandThatIsNotSureYet_CountsAsNotAFist(int fps)
        {
            // the second hand is "unknown" (IsFist false) for the whole spread, a fist only in the last second
            var times = TriggerTimes(fps, 4.0, t => Pair(SpreadTrajectory(t), secondFist: t >= 3.0));

            Assert.IsEmpty(times, "the spread happened before both were fists, and 6 palms held apart is no spread");
        }

        [Test]
        public void ADistanceIsCountedInPalmsOfThePicture_NotInFractionsOfTheWidth()
        {
            // the same sideways movement on the screen: 0.05 to 0.25 of the width
            bool Triggers(float aspect, float palm) =>
                TriggerTimes(30, 4.0, t => PairByWidth(Ramp(t, 0.05, 0.25, SpreadAt, SpreadDoneAt), palm), null, aspect).Count > 0;

            Assert.IsFalse(Triggers(1f, 0.1f), "square picture, palm 0.1: 0.5 to 2.5 palms");
            Assert.IsTrue(Triggers(2f, 0.1f), "a picture twice as wide: the same fraction of the width is twice the length, 1 to 5 palms");
            Assert.IsFalse(Triggers(2f, 0.2f), "hands twice as big: half as many palms again, 0.5 to 2.5");
        }

        [Test]
        public void TheDistanceIsDividedByTheMeanPalmOfTheTwoHands()
        {
            // one hand twice as big as the other (mean palm 0.15)
            var gesture = new CutoffGesture(CutoffSettings.Default);
            var times = new List<double>();
            for (var i = 0; i <= 90; i++)
            {
                var t = i / 30.0;
                var apart = (float)Ramp(t, 0.1, 0.6, SpreadAt, SpreadDoneAt);
                var small = new CutoffHand(0.5f - apart * 0.5f, 0.5f, 0.1f, true);
                var big = new CutoffHand(0.5f + apart * 0.5f, 0.5f, 0.2f, true);
                if (gesture.Update(StartSec + t, 2, small, big, Aspect)) times.Add(t);
            }

            Assert.AreEqual(1, times.Count, "0.1 to 0.6 of the width at 4/3 over 0.15 is 0.9 to 5.3 palms");
        }

        [Test]
        public void ANoSizedHandOrABadPictureNeverTriggers_AndNeverThrows()
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);
            for (var i = 0; i < 90; i++)
            {
                var t = StartSec + i / 30.0;
                var spread = (float)Ramp(i / 30.0, 0.1, 0.7, SpreadAt, SpreadDoneAt);
                var noSize = new CutoffHand(0.5f - spread * 0.5f, 0.5f, 0f, true);
                var left = new CutoffHand(0.5f - spread * 0.5f, 0.5f, 0.1f, true);
                var right = new CutoffHand(0.5f + spread * 0.5f, 0.5f, 0.1f, true);
                Assert.IsFalse(gesture.Update(t, 2, noSize, new CutoffHand(0.5f + spread * 0.5f, 0.5f, 0f, true), Aspect), "palm size 0");
                Assert.IsFalse(gesture.Update(t, 2, new CutoffHand(float.NaN, 0.5f, 0.1f, true), right, Aspect), "x NaN");
                Assert.IsFalse(gesture.Update(t, 2, left, right, 0f), "no aspect");
                Assert.IsFalse(gesture.Update(t, 2, left, right, float.NaN), "aspect NaN");
            }
        }

        [Test]
        public void Reset_ArmsItAgainAtOnce()
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);
            var first = Run(gesture, 30, 0.0, 3.0, t => Pair(SpreadTrajectory(t)));
            gesture.Reset();

            var second = Run(gesture, 30, 3.0 + 1.0 / 30.0, 6.0, t => Pair(Ramp(t, 1.0, 6.0, 4.0, 4.5)));

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(1, second.Count, "a restarted stream (Reset) does not wait for open hands");
        }

        [Test]
        public void TimeGoingBack_ForgetsWhatWasSeenBefore()
        {
            var gesture = new CutoffGesture(CutoffSettings.Default);
            // fists one palm apart at the end of a recording (clock 105 s) ...
            for (var i = 0; i < 4; i++) gesture.Update(StartSec + 5.0 + i / 30.0, 2, Pair(1.0).First, Pair(1.0).Second, Aspect);

            // ... then the recording begins again (clock 100 s) with the fists 3.6 palms apart and not moving
            var triggered = false;
            for (var i = 0; i <= 60; i++) triggered |= gesture.Update(StartSec + i / 30.0, 2, Pair(3.6).First, Pair(3.6).Second, Aspect);

            Assert.IsFalse(triggered, "the closer fists of the old stream are no start for a spread in the new one");
        }

        [Test]
        public void ASettingsObjectIsRequired()
        {
            Assert.Throws<ArgumentNullException>(() => new CutoffGesture(null));
        }

        [Test]
        public void AZeroHold_TriggersInThePictureThatCompletesTheSpread()
        {
            var settings = CutoffSettings.From(new CutoffDto { holdSec = 0f });

            var times = TriggerTimes(30, 4.0, t => Pair(SpreadTrajectory(t)), settings);

            Assert.AreEqual(1, times.Count);
            Assert.LessOrEqual(times[0], SpreadDoneAt + 1.0 / 30.0 + 1e-9);
        }

        [Test]
        public void TheContentNumbersAreWhatDecides()
        {
            // a spread of 5 palms that does trigger with the defaults does not when 6 palms of spread are asked for
            var strict = CutoffSettings.From(new CutoffDto { spreadPalms = 6f });

            Assert.AreEqual(1, TriggerTimes(30, 4.0, t => Pair(SpreadTrajectory(t))).Count);
            Assert.IsEmpty(TriggerTimes(30, 4.0, t => Pair(SpreadTrajectory(t)), strict));
        }
    }
}
