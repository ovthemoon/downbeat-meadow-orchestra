using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Downbeat.App;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// <c>--log-pose</c>: the line it writes (the id, the four finger ratios, the pose, whether the hand plays, the cell), the ratios behind it, and a logger that writes one line per hand about once a second.
    /// </summary>
    public class PoseLogTests
    {
        const double Fps30Ms = 1000.0 / 30.0;
        const float RatioTolerance = 0.01f;

        sealed class Rig
        {
            public readonly HandMotionTracker Hands = new HandMotionTracker(new HandSettingsHolder());
            public readonly List<string> Lines = new List<string>();
            public readonly PoseLogger Logger;
            long _seq;

            public Rig()
            {
                Logger = new PoseLogger(HandCellTracker.From(TestContent.LoadShipped()), Hands, PoseSettings.Default, Lines.Add);
            }

            /// <summary>Pictures of the hands at 30 a second: enough of them for the tracker to take a pose over.</summary>
            public void Show(int pictures, params PosedHand[] hands)
            {
                for (var i = 0; i < pictures; i++)
                {
                    _seq++;
                    Hands.Update(PosedFrame(_seq, _seq * Fps30Ms, PoseSettings.Default, hands));
                }
            }
        }

        // ---- the line ----------------------------------------------------------------------------------------------------------------

        [Test]
        public void Format_WritesTheIdTheFourRatiosThePoseCanPlayAndTheCell()
        {
            var line = PoseLogLine.Format(1, new FingerRatios(1.824f, 1.9f, 1.75f, 1.6049f), HandPose.Open, HandPose.Open, true, 2);

            Assert.AreEqual("[pose] hand=1 index=1.82 middle=1.90 ring=1.75 pinky=1.60 raw=Open pose=Open canPlay=1 cell=2 part=bell", line);
        }

        [Test]
        public void Format_ShowsWhatIsNotKnown_AsNotAvailable_AndNoCellAsMinusOne()
        {
            var line = PoseLogLine.Format(0, default, HandPose.Unknown, HandPose.Fist, false, HandCellTracker.NoCell);

            Assert.AreEqual("[pose] hand=0 index=n/a middle=n/a ring=n/a pinky=n/a raw=Unknown pose=Fist canPlay=0 cell=-1 part=none", line);
            StringAssert.Contains("pinky=n/a", PoseLogLine.Format(0, new FingerRatios(1f, 1f, 1f, float.NaN), HandPose.Unknown, HandPose.Unknown, true, 0), "one tip that cannot be read");
        }

        [Test]
        public void Format_IsTheSameInEveryCulture()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                var line = PoseLogLine.Format(0, new FingerRatios(1.5f, 1.5f, 1.5f, 1.5f), HandPose.Open, HandPose.Open, true, 0);

                StringAssert.Contains("index=1.50 ", line, "a point, never a comma");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        [Test]
        public void Measure_GivesTheRatiosThatTheClassifierDecidesBy()
        {
            var buffer = HandBuffer(0.5f, 0.5f, PalmSize, new[] { 1.8f, 1.4f, 1.1f, 0.9f });

            var ratios = HandPoseClassifier.Measure(buffer, HandBufferLayout.HandBase(0), 1f);

            Assert.IsTrue(ratios.IsKnown);
            Assert.AreEqual(1.8f, ratios.Index, RatioTolerance);
            Assert.AreEqual(1.4f, ratios.Middle, RatioTolerance);
            Assert.AreEqual(1.1f, ratios.Ring, RatioTolerance);
            Assert.AreEqual(0.9f, ratios.Pinky, RatioTolerance);
        }

        [Test]
        public void Measure_OfAHandThatCannotBeRead_IsNotKnown()
        {
            Assert.IsFalse(HandPoseClassifier.Measure(null, 0, 1f).IsKnown);
            Assert.IsFalse(HandPoseClassifier.Measure(new double[HandBufferLayout.Length], HandBufferLayout.HandBase(0), 1f).IsKnown, "every point at the same place: no palm to measure against");
        }

        [Test]
        public void TheReaderKeepsTheRatiosOfEachHand()
        {
            var buffer = HandBuffer(0.5f, 0.5f, PalmSize, new[] { 1.7f, 1.7f, 1.7f, 1.7f });

            Assert.IsTrue(HandFrameReader.TryRead(buffer, 0.0, out var frame));

            Assert.IsTrue(frame.Hand(0).Fingers.IsKnown);
            Assert.AreEqual(1.7f, frame.Hand(0).Fingers.Middle, RatioTolerance);
        }

        // ---- the logger --------------------------------------------------------------------------------------------------------------

        [Test]
        public void Logger_SaysWhatItIsOnceAtTheStart_AndWritesNothingWhileNoHandIsThere()
        {
            var rig = new Rig();

            rig.Logger.Tick(10.0);
            rig.Logger.Tick(11.5);

            Assert.AreEqual(1, rig.Lines.Count);
            StringAssert.StartsWith("[pose] --log-pose is on", rig.Lines[0]);
            StringAssert.Contains("1.35", rig.Lines[0], "the thresholds the pose is read with");
        }

        [Test]
        public void Logger_WritesOneLinePerHand_AboutOncePerSecond()
        {
            var rig = new Rig();
            var hands = new[] { PoseHand(0, 0.17f, 0.25f, PalmSize, HandPose.Open), PoseHand(1, 0.83f, 0.75f, PalmSize, HandPose.Fist) };
            rig.Show(3, hands);

            rig.Logger.Tick(10.0);
            foreach (var seconds in new[] { 10.4, 10.9, 11.0 })
            {
                rig.Show(1, hands);   // the tracker goes on sending pictures
                rig.Logger.Tick(seconds);
            }

            Assert.AreEqual(1 + 2 + 2, rig.Lines.Count, "the opening line, two hands at 10.0 and two at 11.0; nothing in between");
            StringAssert.StartsWith("[pose] hand=0 index=1.80 middle=1.80 ring=1.80 pinky=1.80 raw=Open pose=Open canPlay=1 cell=0 part=harp", rig.Lines[1]);
            StringAssert.StartsWith("[pose] hand=1 index=0.90 middle=0.90 ring=0.90 pinky=0.90 raw=Fist pose=Fist canPlay=0 cell=-1 part=none", rig.Lines[2]);
            Assert.AreEqual(rig.Lines[1], rig.Lines[3], "the same hand, the same line");
        }

        [Test]
        public void Logger_ShowsThePictureAndThePoseTheGameTakes_WhenTheyDiffer()
        {
            var rig = new Rig();
            rig.Show(3, PoseHand(0, 0.5f, 0.25f, PalmSize, HandPose.Open));
            rig.Show(1, PoseHand(0, 0.5f, 0.25f, PalmSize, HandPose.Fist));   // one picture of a fist is not yet a fist for the game

            rig.Logger.Tick(5.0);

            StringAssert.Contains("raw=Fist pose=Open canPlay=1", rig.Lines[1]);
        }

        [Test]
        public void LogPose_IsOffUnlessTheFlagIsGiven()
        {
            Assert.IsFalse(LaunchArgs.Parse(new[] { "Downbeat" }).Has("log-pose"));
            Assert.IsTrue(LaunchArgs.Parse(new[] { "Downbeat", "--log-pose" }).Has("log-pose"));
        }
    }
}
