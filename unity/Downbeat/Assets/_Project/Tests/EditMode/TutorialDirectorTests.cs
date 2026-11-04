using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>The follow-along tutorial: when it appears, which of its three steps shows, when a step is ticked, and what makes it go away. Every test runs at 30 and at 6 pictures a second.</summary>
    public class TutorialDirectorTests
    {
        const double IdleDelay = 10.0;
        const double Step = 5.0;
        const double FadeIn = 0.6;
        const double FadeOut = 0.8;
        static readonly double[] Rates = { 30.0, 6.0 };

        /// <summary>A visitor's evening, fed to the director one picture at a time.</summary>
        sealed class Scene
        {
            readonly double _dt;
            public readonly TutorialDirector Director = new TutorialDirector(new TutorialSettings(IdleDelay, Step, FadeIn, FadeOut, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            public ExperienceState State = ExperienceState.Idle;
            public double IdleSince;
            public bool OpenHand, CellChanged, BothFists;
            public double Now;
            public TutorialFrame Frame;

            public Scene(double fps, double start = 0.0)
            {
                _dt = 1.0 / fps;
                Now = start;
                IdleSince = start;
                Frame = Director.Update(Now, State, IdleSince, false, false, false);
            }

            /// <summary>Runs until the clock reads <paramref name="until"/> (the last picture is exactly at that time).</summary>
            public TutorialFrame RunTo(double until)
            {
                while (Now + _dt < until + 1e-9)
                {
                    Now += _dt;
                    Frame = Director.Update(Now, State, IdleSince, OpenHand, CellChanged, BothFists);
                    CellChanged = false; // a change of cell is an event of one picture
                }
                if (until - Now > 1e-9)
                {
                    Now = until;
                    Frame = Director.Update(Now, State, IdleSince, OpenHand, CellChanged, BothFists);
                    CellChanged = false;
                }
                return Frame;
            }

            public void Play(double at)
            {
                RunTo(at);
                State = ExperienceState.Playing;
                OpenHand = true;
            }

            public void VisitEnds(double at)
            {
                RunTo(at);
                State = ExperienceState.Idle;
                IdleSince = at;
                OpenHand = false;
            }
        }

        [TestCaseSource(nameof(Rates))]
        public void Appears_TenSecondsAfterTheVisitEnds(double fps)
        {
            var scene = new Scene(fps);
            scene.Play(5.0);
            scene.RunTo(100.0);
            Assert.IsFalse(scene.Frame.IsVisible, "somebody is playing");

            scene.VisitEnds(100.0);

            Assert.IsFalse(scene.RunTo(105.0).IsVisible, "five quiet seconds");
            Assert.IsFalse(scene.RunTo(109.8).IsVisible, "not yet");
            Assert.IsTrue(scene.RunTo(110.3).IsVisible, "ten quiet seconds: it begins to show");
            Assert.AreEqual(1f, scene.RunTo(111.0).Alpha, 1e-4f, "and is fully there once it has faded in");
        }

        [TestCaseSource(nameof(Rates))]
        public void Appears_TenSecondsAfterStart(double fps)
        {
            var scene = new Scene(fps, 0.0);

            Assert.IsFalse(scene.RunTo(9.8).IsVisible);
            Assert.IsTrue(scene.RunTo(10.3).IsVisible);
        }

        [Test]
        public void Appears_TenSecondsAfterStart_WhateverTheClockReadsAtStart()
        {
            // the audio clock does not begin at 0, and the state machine's "since" of a fresh start is 0: the first picture is the start
            var scene = new Scene(30.0, 4000.0) { IdleSince = 0.0 };

            Assert.IsFalse(scene.RunTo(4009.5).IsVisible);
            Assert.IsTrue(scene.RunTo(4010.4).IsVisible);
        }

        [Test]
        public void FadesIn_OverTheFadeInTime()
        {
            var scene = new Scene(30.0);

            scene.RunTo(10.0);
            Assert.AreEqual(0f, scene.Frame.Alpha, 0.04f);
            scene.RunTo(10.3);
            Assert.AreEqual(0.5f, scene.Frame.Alpha, 0.06f);
            scene.RunTo(10.7);
            Assert.AreEqual(1f, scene.Frame.Alpha, 1e-4f);
        }

        [TestCaseSource(nameof(Rates))]
        public void Steps_LoopEveryFiveSeconds(double fps)
        {
            var scene = new Scene(fps);

            AssertStep(scene.RunTo(10.2), 0);
            AssertStep(scene.RunTo(14.8), 0);
            AssertStep(scene.RunTo(15.3), 1);
            AssertStep(scene.RunTo(19.8), 1);
            AssertStep(scene.RunTo(20.3), 2);
            AssertStep(scene.RunTo(24.8), 2);
            AssertStep(scene.RunTo(25.3), 0, "round again");
            AssertStep(scene.RunTo(30.3), 1);
            AssertStep(scene.RunTo(100.5), 0, "and so on while nobody is there");
        }

        static void AssertStep(TutorialFrame frame, int step, string why = null)
        {
            Assert.IsTrue(frame.IsVisible, why);
            Assert.AreEqual(step, frame.Step, why);
        }

        [Test]
        public void StepProgress_RunsFromZeroToOneInEachStep()
        {
            var scene = new Scene(30.0);

            Assert.AreEqual(0.04f, scene.RunTo(10.2).StepProgress, 0.02f);
            Assert.AreEqual(0.5f, scene.RunTo(12.5).StepProgress, 0.02f);
            Assert.AreEqual(0.96f, scene.RunTo(14.8).StepProgress, 0.02f);
            Assert.AreEqual(0.04f, scene.RunTo(15.2).StepProgress, 0.02f);
        }

        [TestCaseSource(nameof(Rates))]
        public void Check_ShowsWhenTheStepIsDone_ButDoesNotSkipTime(double fps)
        {
            var scene = new Scene(fps);
            scene.RunTo(21.0);
            Assert.IsFalse(scene.Frame.IsChecked(2), "nothing done yet");

            scene.BothFists = true;
            scene.RunTo(21.5);

            Assert.IsTrue(scene.Frame.IsChecked(2), "two fists in the step that asks for them");
            Assert.AreEqual(2, scene.Frame.Step, "the check does not hurry the next step");
            Assert.AreEqual(2, scene.RunTo(24.8).Step);
            Assert.IsTrue(scene.Frame.IsChecked(2), "and it stays for the rest of the step");
            Assert.IsFalse(scene.Frame.IsChecked(0) || scene.Frame.IsChecked(1));
            Assert.AreEqual(0, scene.RunTo(25.3).Step, "the step comes round only when its time is up");
        }

        [Test]
        public void Check_OfStepTwo_IsSetByAChangeOfCell()
        {
            var scene = new Scene(30.0);
            scene.RunTo(16.0);

            scene.CellChanged = true;
            scene.RunTo(16.05);

            Assert.IsTrue(scene.Frame.IsChecked(1));
            Assert.AreEqual(1, scene.RunTo(19.8).Step);
            Assert.IsTrue(scene.Frame.IsChecked(1));
        }

        [Test]
        public void Check_OfStepOne_ShowsWhileTheOpenHandFadesTheTutorialOut()
        {
            var scene = new Scene(30.0);
            scene.RunTo(11.0);

            scene.OpenHand = true;
            scene.RunTo(11.2);

            Assert.IsTrue(scene.Frame.IsVisible, "still fading");
            Assert.IsTrue(scene.Frame.IsChecked(0));
            Assert.AreEqual(0, scene.Frame.Step);
        }

        [Test]
        public void Check_IsOnlyForTheStepThatAsksForIt()
        {
            var scene = new Scene(30.0);
            scene.BothFists = true;

            scene.RunTo(12.0);   // step one asks for an open hand, not for fists
            scene.CellChanged = true;
            scene.RunTo(12.1);

            Assert.IsFalse(scene.Frame.IsChecked(0) || scene.Frame.IsChecked(1) || scene.Frame.IsChecked(2));
        }

        [Test]
        public void Checks_AreClearedWhenTheStepsComeRoundAgain()
        {
            var scene = new Scene(30.0);
            scene.RunTo(21.0);
            scene.BothFists = true;
            scene.RunTo(22.0);
            Assert.IsTrue(scene.Frame.IsChecked(2));

            scene.BothFists = false;
            scene.RunTo(26.0);

            Assert.AreEqual(0, scene.Frame.Step);
            Assert.IsFalse(scene.Frame.IsChecked(2), "a new round starts without ticks");
        }

        [TestCaseSource(nameof(Rates))]
        public void OpenHand_FadesItOut(double fps)
        {
            var scene = new Scene(fps);
            scene.RunTo(12.0);
            Assert.AreEqual(1f, scene.Frame.Alpha, 1e-4f);

            scene.OpenHand = true;
            var halfway = scene.RunTo(12.4);
            Assert.That(halfway.Alpha, Is.InRange(0.3f, 0.7f), "the fade takes 0.8 s");
            Assert.IsFalse(scene.RunTo(13.0).IsVisible, "gone after the fade time");
            Assert.IsFalse(scene.RunTo(20.0).IsVisible, "and it stays gone while the hand is there");
        }

        [Test]
        public void OpenHand_FadesItOut_AlsoWhenTheVisitHasAlreadyBegun()
        {
            // the state machine is one step ahead: it says Waking before the director is told about the open hand
            var scene = new Scene(30.0);
            scene.RunTo(12.0);

            scene.State = ExperienceState.Waking;
            scene.RunTo(12.4);

            Assert.That(scene.Frame.Alpha, Is.InRange(0.3f, 0.7f));
            Assert.IsFalse(scene.RunTo(13.0).IsVisible);
        }

        [Test]
        public void OpenHand_ThatLeavesAtOnce_DoesNotBringTheTutorialBackAtOnce()
        {
            var scene = new Scene(30.0);
            scene.RunTo(12.0);
            scene.OpenHand = true;
            scene.RunTo(12.2);
            scene.OpenHand = false;   // still Idle for the state machine

            Assert.IsFalse(scene.RunTo(15.0).IsVisible);
            Assert.IsFalse(scene.RunTo(21.5).IsVisible, "ten quiet seconds are counted from the last open hand");
            var back = scene.RunTo(22.8);
            Assert.IsTrue(back.IsVisible);
            Assert.AreEqual(0, back.Step, "and it starts from the first step again");
        }

        [Test]
        public void AFade_WithNoTime_HidesAtOnce()
        {
            var director = new TutorialDirector(new TutorialSettings(IdleDelay, Step, 0.0, 0.0, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            director.Update(0.0, ExperienceState.Idle, 0.0, false, false, false);
            var shown = director.Update(10.0, ExperienceState.Idle, 0.0, false, false, false);
            Assert.AreEqual(1f, shown.Alpha, 1e-4f);

            var hidden = director.Update(10.1, ExperienceState.Idle, 0.0, true, false, false);

            Assert.IsFalse(hidden.IsVisible);
        }

        [TestCaseSource(nameof(Rates))]
        public void Fists_DoNotFadeIt(double fps)
        {
            var scene = new Scene(fps);
            scene.RunTo(12.0);

            scene.BothFists = true;

            for (var t = 13.0; t <= 40.0; t += 1.0) Assert.AreEqual(1f, scene.RunTo(t).Alpha, 1e-4f, "two fists at " + t);
        }

        [TestCaseSource(nameof(Rates))]
        public void Fists_AlreadyUpWhenTheTimeIsUp_DoNotHoldItBack(double fps)
        {
            var scene = new Scene(fps) { BothFists = true };

            Assert.IsTrue(scene.RunTo(10.5).IsVisible);
        }

        [TestCase(ExperienceState.Waking)]
        [TestCase(ExperienceState.Playing)]
        [TestCase(ExperienceState.Settling)]
        public void NeverShows_WhilePlaying(ExperienceState state)
        {
            foreach (var fps in Rates)
            {
                var scene = new Scene(fps) { State = state, IdleSince = 0.0 };
                for (var t = 1.0; t < 200.0; t += 7.3) Assert.IsFalse(scene.RunTo(t).IsVisible, $"{state} at {t} ({fps} fps)");
            }
        }

        [Test]
        public void ASettlingMeadow_NeverShowsIt_EvenWhenTheVisitEndedLongAgo()
        {
            var scene = new Scene(30.0);
            scene.Play(2.0);
            scene.RunTo(50.0);
            scene.State = ExperienceState.Settling;
            scene.OpenHand = false;

            Assert.IsFalse(scene.RunTo(80.0).IsVisible);
        }

        [Test]
        public void ANewVisit_StartsTheStepsFromTheFirstOne()
        {
            var scene = new Scene(30.0);
            scene.RunTo(22.0);
            Assert.AreEqual(2, scene.Frame.Step);
            scene.Play(23.0);
            scene.RunTo(30.0);
            Assert.IsFalse(scene.Frame.IsVisible);

            scene.VisitEnds(60.0);
            var first = scene.RunTo(70.5);

            Assert.AreEqual(0, first.Step);
            Assert.That(first.StepProgress, Is.LessThan(0.15f));
        }

        [Test]
        public void CallsFarApart_StillGiveTheRightFrame()
        {
            // the sparse calls of a test or a stalled frame: the step follows the clock, not the number of calls
            var director = new TutorialDirector(new TutorialSettings(IdleDelay, Step, FadeIn, FadeOut, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            director.Update(0.0, ExperienceState.Idle, 0.0, false, false, false);

            var frame = director.Update(23.0, ExperienceState.Idle, 0.0, false, false, false);

            Assert.IsTrue(frame.IsVisible);
            Assert.AreEqual(2, frame.Step);
            Assert.AreEqual(1f, frame.Alpha, 1e-4f);
        }

        [Test]
        public void TheClockGoingBackwards_IsHarmless()
        {
            var scene = new Scene(30.0);
            scene.RunTo(12.0);

            var frame = scene.Director.Update(11.0, ExperienceState.Idle, 0.0, false, false, false);

            Assert.IsTrue(frame.IsVisible);
            Assert.That(frame.Alpha, Is.InRange(0f, 1f));
        }

        [Test]
        public void ASettings_RefusesNumbersThatCannotWork()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TutorialSettings(-1.0, Step, FadeIn, FadeOut, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TutorialSettings(IdleDelay, 0.0, FadeIn, FadeOut, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TutorialSettings(IdleDelay, Step, -0.1, FadeOut, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TutorialSettings(IdleDelay, Step, FadeIn, -0.1, 0.45f, TutorialSettings.DefaultTexts, "끝"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TutorialSettings(IdleDelay, Step, FadeIn, FadeOut, 1.5f, TutorialSettings.DefaultTexts, "끝"));
            Assert.Throws<System.ArgumentException>(() => new TutorialSettings(IdleDelay, Step, FadeIn, FadeOut, 0.45f, new[] { "a", "b" }, "끝"));
        }
    }
}
