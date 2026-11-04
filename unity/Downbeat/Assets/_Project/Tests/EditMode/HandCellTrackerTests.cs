using System;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using NUnit.Framework;
using static Downbeat.Tests.HandTestData;

namespace Downbeat.Tests
{
    /// <summary>The cell each hand is in for the guide: the same cell as the conducting play picks, a fist in none, and the name of a cell showing once when a replayed hand moves into it.</summary>
    public class HandCellTrackerTests
    {
        const double RenderDt = 1.0 / 60.0;
        const float PartHysteresis = 0.03f;
        const double HoldSec = 0.8;

        /// <summary>The tracker and the motion tracker of a session of pictures of hands, on a clock of its own.</summary>
        sealed class Session
        {
            readonly HandSettingsHolder _settings = new HandSettingsHolder();
            long _seq = 1;

            public Session(CellSettings cells = null, double holdSec = HoldSec)
            {
                _settings.Set(new HandSettingsDto(), null, null);
                Motion = new HandMotionTracker(_settings);
                Cells = new HandCellTracker(cells, PartHysteresis, holdSec);
            }

            public HandMotionTracker Motion { get; }
            public HandCellTracker Cells { get; }
            public double Now { get; private set; }

            /// <summary>A picture of the hands, then one drawn frame later.</summary>
            public void Picture(params PosedHand[] hands)
            {
                Motion.Update(PosedFrame(_seq++, Now * 1000.0, hands));
                Cells.Update(Now, Motion);
                Now += RenderDt;
            }

            /// <summary>The hands come in and stay for the pictures that the tracker needs before a hand plays (<see cref="PoseSettings.ConfirmFrames"/>): the cell is asked after the last of them.</summary>
            public void Arrive(params PosedHand[] hands)
            {
                for (var i = 0; i < PoseSettings.DefaultConfirmFrames; i++) Picture(hands);
            }

            /// <summary>Drawn frames with no new picture (the tracker is slower than the screen).</summary>
            public void Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    Cells.Update(Now, Motion);
                    Now += RenderDt;
                }
            }
        }

        static PosedHand Open(int id, float x, float y) => PoseHand(id, x, y, PalmSize, HandPose.Open);

        static PosedHand Fist(int id, float x, float y) => PoseHand(id, x, y, PalmSize, HandPose.Fist);

        [TestCase(Part.Harp)]
        [TestCase(Part.Recorder)]
        [TestCase(Part.Bell)]
        [TestCase(Part.LowString)]
        [TestCase(Part.Marimba)]
        [TestCase(Part.HandDrum)]
        public void AHandInTheMiddleOfACell_IsInThatCell(Part part)
        {
            var run = new Session();
            var (x, y) = ConductRig.CentreOf(part);

            run.Arrive(Open(0, x, y));

            Assert.AreEqual((int)part, run.Cells.CellOf(0));
            Assert.AreEqual(HandCellTracker.NoCell, run.Cells.CellOf(1), "the second hand is not there");
        }

        [Test]
        public void AHandTremblingOnAColumnLineOrTheRowLine_KeepsItsCell()
        {
            var run = new Session();
            run.Arrive(Open(0, 0.30f, 0.25f));   // the harp's cell, near the line to the recorder's
            var cells = new HashSet<int>();

            for (var i = 0; i < 40; i++)
            {
                run.Picture(Open(0, 1f / 3f + (i % 2 == 0 ? 0.01f : -0.01f), 0.25f));
                cells.Add(run.Cells.CellOf(0));
            }
            run.Picture(Open(0, 0.30f, 0.45f));   // and the same on the row line
            for (var i = 0; i < 40; i++)
            {
                run.Picture(Open(0, 0.30f, 0.5f + (i % 2 == 0 ? 0.01f : -0.01f)));
                cells.Add(run.Cells.CellOf(0));
            }

            CollectionAssert.AreEquivalent(new[] { (int)Part.Harp }, cells, "no flicker between two cells");
        }

        [Test]
        public void AFist_IsInNoCell_AndAnOpenHandBesideIt_IsInItsOwn()
        {
            var run = new Session();

            for (var i = 0; i < 4; i++) run.Picture(Fist(0, 0.17f, 0.25f), Open(1, 0.83f, 0.75f));

            Assert.AreEqual(HandCellTracker.NoCell, run.Cells.CellOf(0), "a fist plays nothing, so no cell is lit for it");
            Assert.AreEqual((int)Part.HandDrum, run.Cells.CellOf(1));
        }

        [Test]
        public void AHandThatClosesIntoAFist_LeavesItsCell_AndOpeningAgainComesBackToIt()
        {
            var run = new Session();
            for (var i = 0; i < 4; i++) run.Picture(Open(0, 0.5f, 0.25f));
            Assert.AreEqual((int)Part.Recorder, run.Cells.CellOf(0));

            for (var i = 0; i < 4; i++) run.Picture(Fist(0, 0.5f, 0.25f));
            var closed = run.Cells.CellOf(0);
            for (var i = 0; i < 4; i++) run.Picture(Open(0, 0.5f, 0.25f));

            Assert.AreEqual(HandCellTracker.NoCell, closed);
            Assert.AreEqual((int)Part.Recorder, run.Cells.CellOf(0));
        }

        [Test]
        public void AHandThatIsMissingForAMoment_KeepsItsCell_AndOneThatStaysAwayLosesIt()
        {
            var run = new Session();
            for (var i = 0; i < 3; i++) run.Picture(Open(0, 0.83f, 0.25f));
            var seenAt = run.Now;

            run.Picture();                       // a picture without the hand
            var shortlyAfter = run.Cells.CellOf(0);
            while (run.Now - seenAt < HoldSec + 0.1) run.Picture();
            var longAfter = run.Cells.CellOf(0);

            Assert.AreEqual((int)Part.Bell, shortlyAfter, "the conducting play goes on playing a hand that the tracker lost for a moment");
            Assert.AreEqual(HandCellTracker.NoCell, longAfter);
        }

        [Test]
        public void AHandThatHasJustComeIn_IsInNoCell_UntilItHasBeenThereForTheConfirmingPictures()
        {
            var run = new Session();

            run.Picture(Open(0, 0.5f, 0.75f));
            var first = run.Cells.CellOf(0);
            run.Picture(Open(0, 0.5f, 0.75f));

            Assert.AreEqual(HandCellTracker.NoCell, first, "its instrument is not heard yet, so no cell is lit");
            Assert.AreEqual((int)Part.Marimba, run.Cells.CellOf(0), "and from the next picture on it is");
        }

        [Test]
        public void AFistThatArrives_IsInNoCell_ForGood()
        {
            var run = new Session();

            for (var i = 0; i < 10; i++)
            {
                run.Picture(Fist(0, 0.5f, 0.75f));
                Assert.AreEqual(HandCellTracker.NoCell, run.Cells.CellOf(0), "picture " + i);
            }
        }

        [Test]
        public void AHandThatTheTrackerLostForAMomentAndFoundAgain_StaysInItsCell_ThroughItsFirstNewPicture()
        {
            var run = new Session();
            run.Arrive(Open(0, 0.83f, 0.25f));
            run.Frames(24);                       // 0.4 s without a picture: more than the tracker keeps a hand (hand.maxGapMs), less than the music holds it
            run.Picture();                        // the tracker lets go of the hand
            run.Frames(2);

            run.Picture(Open(0, 0.83f, 0.25f));   // what the tracker takes for a new hand

            Assert.IsFalse(run.Motion.Hand(0).CanPlay, "its first picture");
            Assert.AreEqual((int)Part.Bell, run.Cells.CellOf(0), "but for the music it is the hand that was playing: the instrument does not stop");
        }

        [Test]
        public void AHandThatCameBackAfterTheHoldTime_IsANewHand_InNoCellForItsFirstPicture()
        {
            var run = new Session();
            run.Arrive(Open(0, 0.83f, 0.25f));
            run.Frames((int)(1.0 / RenderDt));    // a second without a picture: longer than the hold time
            run.Picture();

            run.Picture(Open(0, 0.83f, 0.25f));
            var first = run.Cells.CellOf(0);
            run.Picture(Open(0, 0.83f, 0.25f));

            Assert.AreEqual(HandCellTracker.NoCell, first);
            Assert.AreEqual((int)Part.Bell, run.Cells.CellOf(0));
        }

        [Test]
        public void ChangedThisFrame_IsTrueWhenAHandMovesToAnotherCell_AndNotWhenItAppearsOrGoes()
        {
            var run = new Session();

            run.Picture(Open(0, 0.17f, 0.25f));
            var appeared = run.Cells.ChangedThisFrame;
            run.Picture(Open(0, 0.17f, 0.26f));
            var stayed = run.Cells.ChangedThisFrame;
            run.Picture(Open(0, 0.5f, 0.25f));
            var moved = run.Cells.ChangedThisFrame;
            run.Frames(3);
            var afterwards = run.Cells.ChangedThisFrame;
            run.Picture(Fist(0, 0.5f, 0.25f));
            run.Picture(Fist(0, 0.5f, 0.25f));
            var closed = run.Cells.ChangedThisFrame;

            Assert.IsFalse(appeared, "a hand that comes in has not moved from a cell to another");
            Assert.IsFalse(stayed);
            Assert.IsTrue(moved);
            Assert.IsFalse(afterwards, "it is true for the frame of the change only");
            Assert.IsFalse(closed);
        }

        [Test]
        public void TheCellsOfTheContentFile_AreUsed_AsTheConductingPlayUsesThem()
        {
            var content = TestContent.LoadShipped();
            content.cells.rowSplit = 0.3f;
            var tracker = HandCellTracker.From(content);
            var run = new Session(CellSettings.From(content.cells));
            run.Arrive(Open(0, 0.5f, 0.4f));

            tracker.Update(0.0, run.Motion);

            Assert.AreEqual((int)Part.Marimba, tracker.CellOf(0), "y 0.4 is under a row line at 0.3: the ground row");
            Assert.AreEqual((int)Part.Marimba, run.Cells.CellOf(0));
        }

        [TestCase(30.0)]
        [TestCase(6.0)]
        public void TheLitCell_IsTheCellOfTheInstrumentThatPlays_ForTheSameHandPath(double picturesPerSecond)
        {
            // Arrange: a hand that dwells in each of the six cells and then trembles on a column line, played by the conducting engine and followed by the guide
            var rig = new ConductRig();
            var tracker = HandCellTracker.From(TestContent.LoadShipped());
            var order = new[] { Part.Harp, Part.Recorder, Part.Bell, Part.HandDrum, Part.Marimba, Part.LowString };
            Func<double, Pose[]> path = t =>
            {
                var slot = (int)(t / 2.0);
                if (slot < order.Length)
                {
                    var (x, y) = ConductRig.CentreOf(order[slot]);
                    return new[] { new Pose(0, x + 0.02f * (float)Math.Sin(t * 7.0), y + 0.02f * (float)Math.Cos(t * 5.0)) };
                }
                return new[] { new Pose(0, 1f / 3f + (((long)(t * 10.0)) % 2 == 0 ? 0.01f : -0.01f), 0.75f) };
            };
            var cellsSeen = new HashSet<int>();
            var lastChangeAt = double.NegativeInfinity;
            var cellBeforeChange = -1;
            var previousCell = -1;
            var notesChecked = 0;

            // Act
            while (rig.AudioNow < ConductRig.GridOrigin - 1.0 + 16.0)
            {
                var before = rig.Notes.Count;
                rig.Run(ConductRig.AudioFrameSec, path, 1.0 / picturesPerSecond);
                tracker.Update(rig.AudioNow, rig.Motion);
                var cell = tracker.CellOf(0);
                if (tracker.ChangedThisFrame)
                {
                    lastChangeAt = rig.AudioNow;
                    cellBeforeChange = previousCell;
                }
                previousCell = cell;
                for (var i = before; i < rig.Notes.Count; i++)
                {
                    // a part that the hand has just left goes on for a moment while it fades out (the crossfade)
                    var fading = rig.AudioNow - lastChangeAt < 0.6 && (int)rig.Notes[i].Part == cellBeforeChange;
                    Assert.IsTrue((int)rig.Notes[i].Part == cell || fading, $"at {rig.AudioNow - (ConductRig.GridOrigin - 1.0):0.00} s the engine plays {rig.Notes[i].Part} and the guide lights cell {cell}");
                    cellsSeen.Add(cell);
                    notesChecked++;
                }
            }

            // Assert
            Assert.Greater(notesChecked, 20, "the engine played");
            Assert.GreaterOrEqual(cellsSeen.Count, 5, "in the cells the hand went through");
        }

        [Test]
        public void CellLabel_ShowsWhenAReplayedHandMovesIntoANewCell_OnceForEachCell()
        {
            // Arrange: a recording at 6 pictures a second of a hand that dwells in the harp's cell for 3 s, then moves across to the marimba's and dwells there
            Func<double, PosedHand[]> hands = t =>
            {
                var along = Math.Min(1.0, Math.Max(0.0, (t - 3.0) / 0.6));
                var x = (float)(0.17 + (0.5 - 0.17) * along);
                var y = (float)(0.25 + (0.75 - 0.25) * along);
                return new[] { Open(0, x, y) };
            };
            var settings = new HandSettingsHolder();
            settings.Set(new HandSettingsDto(), null, null);
            var motion = new HandMotionTracker(settings);
            var player = new ReplayPlayer(ReplayOf(hands, 8.0, 6.0), settings);
            var tracker = new HandCellTracker(null, PartHysteresis, HoldSec);
            var timer = new CellLabelTimer(1.2f);
            var frames = new List<HandFrame>();
            var shown = new Dictionary<int, int>();
            var visible = new bool[CellLabelTimer.CellCount];
            float labelOfHarpAt2 = -1f;
            float labelOfMarimbaAt6 = -1f;

            // Act
            for (var i = 0; i * RenderDt <= 8.0; i++)
            {
                var now = i * RenderDt;
                frames.Clear();
                player.Advance(now * 1000.0, frames);
                foreach (var frame in frames) motion.Update(frame);
                tracker.Update(now, motion);
                timer.Update(now, tracker.CellOf(0), tracker.CellOf(1));
                for (var cell = 0; cell < CellLabelTimer.CellCount; cell++)
                {
                    var on = timer.LabelAlpha(cell, now) > 0.5f;
                    if (on && !visible[cell]) shown[cell] = shown.TryGetValue(cell, out var n) ? n + 1 : 1;
                    visible[cell] = on;
                }
                if (i == 120) labelOfHarpAt2 = timer.LabelAlpha((int)Part.Harp, now);
                if (i == 360) labelOfMarimbaAt6 = timer.LabelAlpha((int)Part.Marimba, now);
            }

            // Assert
            Assert.AreEqual(1, shown[(int)Part.Harp], "the harp's name came up once, when the hand came in");
            Assert.AreEqual(1, shown[(int)Part.Marimba], "and the marimba's once, when the hand moved into its cell");
            Assert.AreEqual(0f, labelOfHarpAt2, 1e-6f, "the hand staying in the harp's cell for 3 s shows its name no more after labelSec");
            Assert.AreEqual(0f, labelOfMarimbaAt6, 1e-6f, "nor the marimba's, 2.4 s after it came in");
            foreach (var pair in shown) Assert.LessOrEqual(pair.Value, 1, $"cell {pair.Key} showed its name once at most");
        }

        [Test]
        public void TheOtherHandInTheSameCell_LightsItToo_AndTheNameShowsOnceNotTwice()
        {
            var run = new Session();
            var timer = new CellLabelTimer(1.2f);
            var rises = 0;
            var visible = false;

            for (var i = 0; i < 60; i++)
            {
                run.Picture(Open(0, 0.5f, 0.25f), Open(1, 0.55f, 0.3f));
                timer.Update(run.Now, run.Cells.CellOf(0), run.Cells.CellOf(1));
                var on = timer.LabelAlpha((int)Part.Recorder, run.Now) > 0.5f;
                if (on && !visible) rises++;
                visible = on;
            }

            Assert.AreEqual(1, rises);
            Assert.IsTrue(timer.IsOccupied((int)Part.Recorder));
        }
    }
}
