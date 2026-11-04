using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Downbeat.Core;
using NUnit.Framework;
using UnityEngine;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>
    /// Open hand or fist, read from the 21 points of a hand: the classifier on one picture, the tracker that takes a new pose over only after a few pictures in a row (also at 6 pictures a second),
    /// the conducting engine that lets a fist play nothing, the shipped demo recording, and the content block with its defaults.
    /// </summary>
    public class HandPoseTests
    {
        const double Fps6Ms = 1000.0 / 6.0;
        const int HandSlot = 0;
        static readonly PoseSettings Defaults = PoseSettings.Default;

        static HandPose Classify(params float[] fingerRatios) =>
            HandPoseClassifier.Classify(HandBuffer(0.5f, 0.5f, PalmSize, fingerRatios), HandBufferLayout.HandBase(HandSlot), Defaults);

        // ---- one picture -------------------------------------------------------------------------------------------------------------

        [Test]
        public void Classify_OpenHand_IsOpen()
        {
            Assert.AreEqual(HandPose.Open, Classify(OpenRatio, OpenRatio, OpenRatio, OpenRatio));
            Assert.AreEqual(HandPose.Open, Classify(1.89f, 1.89f, 1.66f, 1.55f), "the ratios measured on the recorded demo hand");
        }

        [Test]
        public void Classify_Fist_IsFist()
        {
            Assert.AreEqual(HandPose.Fist, Classify(FistRatio, FistRatio, FistRatio, FistRatio));
        }

        [Test]
        public void Classify_ThreeFingersCurled_IsFist()
        {
            Assert.AreEqual(HandPose.Fist, Classify(FistRatio, FistRatio, FistRatio, OpenRatio), "one finger sticking out does not stop a fist");
            Assert.AreEqual(HandPose.Fist, Classify(OpenRatio, FistRatio, FistRatio, FistRatio));
        }

        [Test]
        public void Classify_ThreeFingersStretched_IsOpen()
        {
            Assert.AreEqual(HandPose.Open, Classify(OpenRatio, OpenRatio, OpenRatio, FistRatio), "a curled little finger does not stop an open hand");
        }

        [Test]
        public void Classify_ARelaxedRaisedHand_IsOpen_AHalfCurledOneIsNot()
        {
            Assert.AreEqual(HandPose.Open, Classify(1.4f, 1.4f, 1.4f, 1.4f), "a relaxed raised hand measures 1.3 to 1.5");
            Assert.AreEqual(HandPose.Open, Classify(1.36f, 1.5f, 1.4f, 1.3f), "three of four are enough");
            Assert.AreEqual(HandPose.Unknown, Classify(1.3f, 1.3f, 1.3f, 1.3f), "a half curled hand is still neither");
            Assert.AreEqual(HandPose.Fist, Classify(1.15f, 1.15f, 1.15f, 1.15f), "and the fist is where it was");
        }

        [Test]
        public void FingertipsOutsideThePicture_AreUnknown()
        {
            // a hand held high: its fingertips are above the top edge (y < 0), where the tracker only guesses, and a guess lies near the wrist
            HandPose At(float x, float y, params float[] ratios) => HandPoseClassifier.Classify(HandBuffer(x, y, PalmSize, ratios), HandBufferLayout.HandBase(HandSlot), Defaults);
            var open = new[] { OpenRatio, OpenRatio, OpenRatio, OpenRatio };
            var fist = new[] { FistRatio, FistRatio, FistRatio, FistRatio };

            Assert.AreEqual(HandPose.Open, At(0.5f, 0.5f, open), "in the middle: open");
            Assert.AreEqual(HandPose.Unknown, At(0.5f, 0.05f, open), "the fingertips are above the top edge");
            Assert.AreEqual(HandPose.Fist, At(0.5f, 0.5f, fist), "in the middle: a fist");
            Assert.AreEqual(HandPose.Unknown, At(0.5f, 0.01f, fist), "a fist so high that its fingertips (0.02 above the palm's top) are cut off by the top edge");
            Assert.AreEqual(HandPose.Fist, At(0.5f, 0.1f, fist), "just inside the edge it is a fist again");
            Assert.AreEqual(HandPose.Unknown, At(0.02f, 0.5f, open), "beyond the left edge");
            Assert.AreEqual(HandPose.Unknown, At(0.98f, 0.5f, open), "beyond the right edge");
            Assert.AreEqual(HandPose.Open, At(0.5f, 0.5f, 1.89f, 1.89f, 1.66f, 1.55f), "and a hand that is inside the picture is read as before");
        }

        [Test]
        public void Classify_HalfCurled_IsUnknown()
        {
            Assert.AreEqual(HandPose.Unknown, Classify(HalfRatio, HalfRatio, HalfRatio, HalfRatio));
            Assert.AreEqual(HandPose.Unknown, Classify(OpenRatio, OpenRatio, FistRatio, FistRatio), "two and two: neither");
            Assert.AreEqual(HandPose.Unknown, Classify(OpenRatio, OpenRatio, HalfRatio, HalfRatio), "the fingers in between count for neither side");
        }

        [Test]
        public void Classify_ThresholdsComeFromTheSettings()
        {
            var buffer = HandBuffer(0.5f, 0.5f, PalmSize, new[] { 1.5f, 1.5f, 1.5f, 1.5f });
            var demanding = new PoseSettings(1.6f, 1.15f, 2);

            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(buffer, HandBufferLayout.HandBase(HandSlot), Defaults));
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(buffer, HandBufferLayout.HandBase(HandSlot), demanding), "1.5 is not stretched enough for a reader that wants 1.6");
            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(buffer, HandBufferLayout.HandBase(HandSlot), null), "no settings: the defaults");
        }

        [Test]
        public void Classify_MeasuresInVideoHeights_NotInFractionsOfTheWidth()
        {
            // a 2:1 picture: the middle knuckle is straight above the wrist (1 palm length), the tips are 1.8 palm lengths away to the side.
            // In fractions of the width that is only 0.9 (a fist) unless the aspect is applied.
            const float Aspect = 2f;
            var landmarks = Landmarks(0.5f, 0.5f, PalmSize, HandPose.Open, Aspect);
            foreach (var tip in new[] { 8, 12, 16, 20 })
            {
                landmarks[tip * 2] = landmarks[HandBufferLayout.Wrist * 2] + OpenRatio * PalmSize / Aspect;
                landmarks[tip * 2 + 1] = landmarks[HandBufferLayout.Wrist * 2 + 1];
            }
            var buffer = HandBuffer(landmarks, Aspect);

            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(buffer, HandBufferLayout.HandBase(HandSlot), Defaults));
        }

        [Test]
        public void Classify_BadNumbers_AreUnknown_NotAnException()
        {
            var open = new[] { OpenRatio, OpenRatio, OpenRatio, OpenRatio };
            var baseIndex = HandBufferLayout.HandBase(HandSlot);
            double[] WithTipBroken(params int[] tips)
            {
                var buffer = HandBuffer(0.5f, 0.5f, PalmSize, open);
                foreach (var tip in tips) buffer[baseIndex + 2 + tip * 3] = double.NaN;
                return buffer;
            }
            var flat = HandBuffer(0.5f, 0.5f, PalmSize, open);
            for (var i = 0; i < HandBufferLayout.LandmarksPerHand; i++)   // a hand squeezed into one point: no palm length to measure against
            {
                flat[baseIndex + 2 + i * 3] = 0.5;
                flat[baseIndex + 3 + i * 3] = 0.5;
            }

            Assert.AreEqual(HandPose.Open, HandPoseClassifier.Classify(WithTipBroken(12), baseIndex, Defaults), "a tip that cannot be read counts for neither side: the other three still say open");
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(WithTipBroken(12, 16), baseIndex, Defaults), "two good tips are not enough");
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(flat, baseIndex, Defaults));
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(null, baseIndex, Defaults));
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(new double[10], baseIndex, Defaults));
            Assert.AreEqual(HandPose.Unknown, HandPoseClassifier.Classify(HandBuffer(0.5f, 0.5f, PalmSize, open), -1, Defaults));
        }

        [Test]
        public void TheReader_PutsThePoseOnTheSample_UsingTheGivenThresholds()
        {
            var frame = PosedFrame(1, 0, PoseHand(0, 0.3f, 0.5f, PalmSize, HandPose.Fist), PoseHand(1, 0.7f, 0.5f, PalmSize, HandPose.Open));
            var lenient = PosedFrame(1, 0, new PoseSettings(1.1f, 0.5f, 2), PoseHand(0, 0.3f, 0.5f, PalmSize, HandPose.Unknown));

            Assert.AreEqual(HandPose.Fist, frame.Hand(0).Pose);
            Assert.AreEqual(HandPose.Open, frame.Hand(1).Pose);
            Assert.AreEqual(HandPose.Open, lenient.Hand(0).Pose, "the half curled hand is open for a reader that asks for little");
            Assert.AreEqual(0.3f, frame.Hand(0).X, 1e-4f, "the palm centre does not depend on the fingers");
        }

        // ---- over time: the tracker --------------------------------------------------------------------------------------------------

        static HandMotionTracker TrackerWith(int confirmFrames = PoseSettings.DefaultConfirmFrames)
        {
            var holder = new HandSettingsHolder();
            holder.Set(new HandSettingsDto(), new PoseDto { confirmFrames = confirmFrames });
            return new HandMotionTracker(holder);
        }

        /// <summary>Feeds one picture per pose, 6 a second, and returns the pose the tracker reports after each.</summary>
        static List<HandPose> PosesAfterEach(HandMotionTracker tracker, params HandPose[] pictures)
        {
            var seen = new List<HandPose>();
            for (var i = 0; i < pictures.Length; i++)
            {
                tracker.Update(PosedFrame(i + 1, i * Fps6Ms, PoseHand(0, 0.5f, 0.5f, PalmSize, pictures[i])));
                seen.Add(tracker.Hand(0).Pose);
            }
            return seen;
        }

        [Test]
        public void Tracker_SwitchesPose_OnlyAfterConfirmFrames_At6Fps()
        {
            var tracker = TrackerWith(2);

            var seen = PosesAfterEach(tracker,
                HandPose.Open, HandPose.Open,        // the first pose: Unknown until two pictures agree
                HandPose.Fist,                       // one picture of a fist changes nothing
                HandPose.Open,                       // and a picture of the old pose cancels it
                HandPose.Fist, HandPose.Fist,        // two in a row switch
                HandPose.Unknown,                    // half open is a pose of its own: one picture does not switch to it either
                HandPose.Fist,
                HandPose.Unknown, HandPose.Unknown);

            CollectionAssert.AreEqual(new[]
            {
                HandPose.Unknown, HandPose.Open,
                HandPose.Open,
                HandPose.Open,
                HandPose.Open, HandPose.Fist,
                HandPose.Fist,
                HandPose.Fist,
                HandPose.Fist, HandPose.Unknown,
            }, seen);
        }

        [Test]
        public void Tracker_ConfirmFramesOfOne_SwitchesAtOnce_AndThreeNeedsThree()
        {
            CollectionAssert.AreEqual(new[] { HandPose.Open, HandPose.Fist }, PosesAfterEach(TrackerWith(1), HandPose.Open, HandPose.Fist));
            CollectionAssert.AreEqual(new[] { HandPose.Unknown, HandPose.Unknown, HandPose.Open, HandPose.Open, HandPose.Open, HandPose.Fist },
                PosesAfterEach(TrackerWith(3), HandPose.Open, HandPose.Open, HandPose.Open, HandPose.Fist, HandPose.Fist, HandPose.Fist));
        }

        /// <summary>Feeds one picture per pose, 6 a second, and returns whether the tracker says the hand plays after each.</summary>
        static List<bool> CanPlayAfterEach(HandMotionTracker tracker, params HandPose[] pictures)
        {
            var seen = new List<bool>();
            for (var i = 0; i < pictures.Length; i++)
            {
                tracker.Update(PosedFrame(i + 1, i * Fps6Ms, PoseHand(0, 0.5f, 0.5f, PalmSize, pictures[i])));
                seen.Add(tracker.Hand(0).CanPlay);
            }
            return seen;
        }

        [Test]
        public void Tracker_AHandPlaysOnlyOnceItHasBeenTrackedForConfirmFrames_AndNeverAsAFist()
        {
            CollectionAssert.AreEqual(new[] { false, true, true }, CanPlayAfterEach(TrackerWith(2), HandPose.Open, HandPose.Open, HandPose.Open), "an open hand: from the second picture");
            CollectionAssert.AreEqual(new[] { false, false, false, false }, CanPlayAfterEach(TrackerWith(2), HandPose.Fist, HandPose.Fist, HandPose.Fist, HandPose.Fist), "a hand that arrives as a fist never plays");
            CollectionAssert.AreEqual(new[] { false, true, true }, CanPlayAfterEach(TrackerWith(2), HandPose.Unknown, HandPose.Unknown, HandPose.Unknown), "a hand whose pose stays unsure plays once the window is over");
            CollectionAssert.AreEqual(new[] { false, false, true, true }, CanPlayAfterEach(TrackerWith(3), HandPose.Open, HandPose.Open, HandPose.Open, HandPose.Open), "three confirming pictures: the third one");
            CollectionAssert.AreEqual(new[] { true, false }, CanPlayAfterEach(TrackerWith(1), HandPose.Open, HandPose.Fist), "one confirming picture: at once, and a fist stops it at once");
        }

        [Test]
        public void Tracker_AFistThatOpens_PlaysFromTheOpeningOn_AndAHandThatClosesStops()
        {
            var tracker = TrackerWith(2);

            var canPlay = CanPlayAfterEach(tracker,
                HandPose.Fist, HandPose.Fist,        // arrives as a fist
                HandPose.Open,                       // one picture of an open hand changes nothing
                HandPose.Open,                       // two in a row do
                HandPose.Open,
                HandPose.Fist, HandPose.Fist);       // and a fist stops it, after two pictures of a fist

            CollectionAssert.AreEqual(new[] { false, false, false, true, true, true, false }, canPlay);
        }

        [Test]
        public void Tracker_AHandThatCameBackAfterALongGap_DoesNotPlayForItsFirstPictures()
        {
            var tracker = TrackerWith(2);
            CanPlayAfterEach(tracker, HandPose.Open, HandPose.Open);
            Assert.IsTrue(tracker.Hand(0).CanPlay);

            tracker.Update(PosedFrame(10, 5000));
            tracker.Update(PosedFrame(11, 5000 + Fps6Ms, PoseHand(0, 0.5f, 0.5f, PalmSize, HandPose.Open)));
            var first = tracker.Hand(0).CanPlay;
            tracker.Update(PosedFrame(12, 5000 + 2 * Fps6Ms, PoseHand(0, 0.5f, 0.5f, PalmSize, HandPose.Open)));

            Assert.IsFalse(first, "a new hand: it does not play for its first picture");
            Assert.IsTrue(tracker.Hand(0).CanPlay);
        }

        [Test]
        public void Tracker_AHandBrieflyMissing_KeepsPlaying_AsFarAsTheTrackerKnows()
        {
            var tracker = TrackerWith(2);
            CanPlayAfterEach(tracker, HandPose.Open, HandPose.Open);

            tracker.Update(PosedFrame(3, 2 * Fps6Ms));

            Assert.IsTrue(tracker.TryGetById(0, out var held));
            Assert.IsFalse(held.IsTracked);
            Assert.IsTrue(held.CanPlay, "the answer is kept with the pose while the hand is held for the gap time");
        }

        [Test]
        public void Tracker_AHandThatCameBackAfterALongGap_StartsUnknownAgain()
        {
            var tracker = TrackerWith(2);
            PosesAfterEach(tracker, HandPose.Fist, HandPose.Fist);
            Assert.AreEqual(HandPose.Fist, tracker.Hand(0).Pose);

            tracker.Update(PosedFrame(10, 5000));                                                  // the hand is gone for 5 s
            tracker.Update(PosedFrame(11, 5000 + Fps6Ms, PoseHand(0, 0.5f, 0.5f, PalmSize, HandPose.Open)));

            Assert.AreEqual(HandPose.Unknown, tracker.Hand(0).Pose, "a new hand: its pose is not known yet");
        }

        [Test]
        public void Tracker_AHandBrieflyMissing_KeepsItsPose()
        {
            var tracker = TrackerWith(2);
            PosesAfterEach(tracker, HandPose.Open, HandPose.Open);

            tracker.Update(PosedFrame(3, 2 * Fps6Ms));                                             // one picture without the hand (it is held for the gap time)

            Assert.IsTrue(tracker.TryGetById(0, out var motion));
            Assert.IsFalse(motion.IsTracked);
            Assert.AreEqual(HandPose.Open, motion.Pose);
        }

        [Test]
        public void Tracker_TwoHands_KeepTheirOwnPoses()
        {
            var tracker = TrackerWith(2);
            for (var i = 0; i < 3; i++)
            {
                tracker.Update(PosedFrame(i + 1, i * Fps6Ms, PoseHand(0, 0.3f, 0.5f, PalmSize, HandPose.Fist), PoseHand(1, 0.7f, 0.5f, PalmSize, HandPose.Open)));
            }

            Assert.IsTrue(tracker.TryGetById(0, out var left));
            Assert.IsTrue(tracker.TryGetById(1, out var right));
            Assert.AreEqual(HandPose.Fist, left.Pose);
            Assert.AreEqual(HandPose.Open, right.Pose);
        }

        // ---- the engine: a fist plays nothing ------------------------------------------------------------------------------------------

        // a fist in the sky row on the left (the harp), the open hand beside it on the ground row on the right (the hand drum)
        const Part FistPart = Part.Harp;
        const Part OpenPart = Part.HandDrum;

        [TestCase(1.0 / 30.0)]
        [TestCase(1.0 / 6.0)]
        public void ConductEngine_FistHand_PlaysNothing_OpenHandBesideIt_Plays(double pictureEverySec)
        {
            var alone = new ConductRig();
            var beside = new ConductRig();

            alone.Run(8.0, ConductRig.StillIn(FistPart, 0, HandPose.Fist), pictureEverySec);
            beside.Run(8.0, ConductRig.Both(ConductRig.StillIn(FistPart, 0, HandPose.Fist), ConductRig.StillIn(OpenPart, 1, HandPose.Open)), pictureEverySec);

            Assert.IsEmpty(alone.Notes, "a fist is not a conducting hand");
            Assert.IsEmpty(beside.Of(FistPart), "the fist plays nothing");
            Assert.GreaterOrEqual(beside.Of(OpenPart).Count(), 8, "the open hand beside it plays on, one note a beat");
        }

        [TestCase(1.0 / 30.0)]
        [TestCase(1.0 / 6.0)]
        public void ConductEngine_HalfOpenHand_Plays(double pictureEverySec)
        {
            var rig = new ConductRig();

            rig.Run(6.0, ConductRig.StillIn(FistPart, 0, HandPose.Unknown), pictureEverySec);

            Assert.GreaterOrEqual(rig.Of(FistPart).Count(), 6, "only a fist is left out: a hand the classifier is unsure of still plays");
        }

        [TestCase(1.0 / 30.0)]
        [TestCase(1.0 / 6.0)]
        public void ConductEngine_AHandThatClosesToAFist_StopsPlaying_AndPlaysAgainWhenItOpens(double pictureEverySec)
        {
            const double ClosesAt = 6.0;
            const double OpensAt = 14.0;
            const double FadedBy = ClosesAt + 1.0;     // the two pictures the pose needs to be sure (up to 0.33 s at 6 a second), and the quick fade of 0.3 s
            var rig = new ConductRig();
            HandTestData.Pose[] Hand(double t) => new[] { new HandTestData.Pose(0, ConductRig.CentreOf(FistPart).x, ConductRig.CentreOf(FistPart).y, shape: t >= ClosesAt && t < OpensAt ? HandPose.Fist : HandPose.Open) };

            rig.Run(20.0, Hand, pictureEverySec);

            var madeAt = rig.MadeAt.Select(t => t - (ConductRig.GridOrigin - 1.0)).ToList();
            Assert.GreaterOrEqual(madeAt.Count(t => t < ClosesAt), 5, "it plays while open");
            Assert.AreEqual(0, madeAt.Count(t => t > FadedBy && t < OpensAt), "nothing is played while the hand is a fist");
            Assert.GreaterOrEqual(madeAt.Count(t => t > OpensAt + 1.5), 4, "it plays again once the hand is open");
        }

        // ---- the recorded demo hand -----------------------------------------------------------------------------------------------------

        [TestCase(1)]
        [TestCase(5)]
        public void DemoHands_AreOpen_MostOfTheTime(int everyNthPicture)
        {
            var replay = JsonUtility.FromJson<ReplayDto>(System.IO.File.ReadAllText(TestContent.RepoReplayPath));
            var player = new ReplayPlayer(replay);
            var frames = new List<HandFrame>();
            player.Advance(player.DurationMs + 1.0, frames);
            var tracker = TrackerWith();

            var open = 0;
            var seen = 0;
            for (var i = 0; i < frames.Count; i += everyNthPicture)
            {
                tracker.Update(frames[i]);
                if (tracker.Count == 0) continue;
                seen++;
                if (tracker.Hand(0).Pose == HandPose.Open) open++;
            }

            Assert.Greater(seen, 50);
            Assert.GreaterOrEqual(open / (double)seen, 0.8, $"{open} of {seen} pictures");
        }

        [Test]
        public void DemoHands_NeverCountAsAFist()
        {
            var replay = JsonUtility.FromJson<ReplayDto>(System.IO.File.ReadAllText(TestContent.RepoReplayPath));
            var frames = new List<HandFrame>();
            new ReplayPlayer(replay).Advance(double.MaxValue, frames);

            var fists = frames.Sum(f => Enumerable.Range(0, f.HandCount).Count(i => f.Hand(i).Pose == HandPose.Fist));

            Assert.AreEqual(0, fists, "the music of the demo does not stop because a hand reads as a fist");
        }

        [Test]
        public void ReplayOf_WritesAllTheHandPoints_SoAReplayedFistIsAFist()
        {
            var replay = ReplayOf(t => new[] { PoseHand(0, 0.4f, 0.5f, PalmSize, t < 1.0 ? HandPose.Open : HandPose.Fist) }, 2.0, 6.0);
            var player = new ReplayPlayer(replay);
            var frames = new List<HandFrame>();
            player.Advance(double.MaxValue, frames);

            Assert.IsEmpty(ReplayValidator.Validate(replay));
            Assert.AreEqual(13, frames.Count);
            Assert.AreEqual(HandPose.Open, frames[0].Hand(0).Pose);
            Assert.AreEqual(HandPose.Fist, frames[12].Hand(0).Pose);
        }

        [Test]
        public void TheReplayPlayer_ReadsThePoseWithTheThresholdsOfTheLoadedContent()
        {
            var replay = ReplayOf(t => new[] { PoseHand(0, 0.4f, 0.5f, PalmSize, HandPose.Unknown) }, 1.0, 6.0);
            var strict = new HandSettingsHolder();
            var lenient = new HandSettingsHolder();
            lenient.Set(new HandSettingsDto(), new PoseDto { openRatio = 1.1f, fistRatio = 0.5f });
            var strictFrames = new List<HandFrame>();
            var lenientFrames = new List<HandFrame>();

            new ReplayPlayer(replay, strict).Advance(double.MaxValue, strictFrames);
            new ReplayPlayer(replay, lenient).Advance(double.MaxValue, lenientFrames);

            Assert.AreEqual(HandPose.Unknown, strictFrames[0].Hand(0).Pose);
            Assert.AreEqual(HandPose.Open, lenientFrames[0].Hand(0).Pose);
        }

        // ---- the content block ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void OldContentWithoutPoseBlock_UsesDefaults()
        {
            var json = JsonUtility.ToJson(TestContent.LoadShipped());
            json = Regex.Replace(json, "\"pose\":\\{[^}]*\\},?", string.Empty);
            Assert.IsFalse(json.Contains("\"pose\""), "the test content has no pose block");

            var content = JsonUtility.FromJson<ContentDto>(json);
            var settings = PoseSettings.From(content.pose);

            Assert.IsEmpty(ContentValidator.Validate(content), "an older content.json still passes the checks");
            Assert.AreEqual(1.35f, settings.OpenRatio, 1e-6f);
            Assert.AreEqual(1.15f, settings.FistRatio, 1e-6f);
            Assert.AreEqual(2, settings.ConfirmFrames);
        }

        [Test]
        public void APoseBlockWithOnlyOneNumber_KeepsTheDefaultsForTheRest()
        {
            var content = JsonUtility.FromJson<ContentDto>("{\"pose\":{\"openRatio\":1.7}}");

            var settings = PoseSettings.From(content.pose);

            Assert.AreEqual(1.7f, settings.OpenRatio, 1e-6f);
            Assert.AreEqual(PoseSettings.DefaultFistRatio, settings.FistRatio, 1e-6f);
            Assert.AreEqual(PoseSettings.DefaultConfirmFrames, settings.ConfirmFrames);
        }

        [Test]
        public void TheShippedPoseBlock_EqualsTheDefaultsOfTheDataClass()
        {
            var shipped = TestContent.LoadShipped().pose;
            var defaults = new PoseDto();

            Assert.IsNotNull(shipped, "content.json carries the block (tools/lib/content-build.mjs has the same numbers)");
            Assert.AreEqual(defaults.openRatio, shipped.openRatio, 1e-6f);
            Assert.AreEqual(defaults.fistRatio, shipped.fistRatio, 1e-6f);
            Assert.AreEqual(defaults.confirmFrames, shipped.confirmFrames);
        }
    }
}
