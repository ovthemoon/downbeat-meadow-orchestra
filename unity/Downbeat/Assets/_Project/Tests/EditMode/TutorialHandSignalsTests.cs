using System.Collections.Generic;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    /// <summary>What the tutorial reads from the hands: an open hand (or a mouse note), a move into another cell, two fists.</summary>
    public class TutorialHandSignalsTests
    {
        // the centres of the cells of the top row and of the bottom row
        const float Left = 0.17f, Middle = 0.5f, Right = 0.83f, Top = 0.25f, Bottom = 0.75f;
        const float PartHysteresis = 0.03f;
        const double PictureSec = 0.1;
        const double HoldSec = 0.8;

        sealed class Hands : IHandMotionView
        {
            readonly List<HandMotion> _hands = new List<HandMotion>();
            public long FrameSeq { get; private set; }
            /// <summary>The time of the latest picture, in seconds (a picture every tenth of a second).</summary>
            public double Clock => FrameSeq * PictureSec;
            public int Count => _hands.Count;
            public HandMotion Hand(int index) => _hands[index];
            public bool TryGetById(int id, out HandMotion motion)
            {
                foreach (var hand in _hands)
                {
                    if (hand.Id != id) continue;
                    motion = hand;
                    return true;
                }
                motion = default;
                return false;
            }

            public Hands Show(params HandMotion[] hands)
            {
                FrameSeq++;
                _hands.Clear();
                foreach (var hand in hands) _hands.Add(Stamp(hand, Clock * 1000.0));
                return this;
            }
        }

        /// <param name="canPlay">null: everything but a fist plays (a hand that has been there for a while).</param>
        static HandMotion Hand(int id, float x, float y, HandPose pose, int epoch = 0, bool tracked = true, bool? canPlay = null) =>
            new HandMotion(id, tracked, 1f, x, y, 0.2f, false, 0f, 0f, 0f, 0f, 0.0, epoch, pose, canPlay);

        static HandMotion Stamp(HandMotion hand, double captureMs) =>
            new HandMotion(hand.Id, hand.IsTracked, hand.Score, hand.X, hand.Y, hand.Size, hand.HasVelocity, hand.VelocityX, hand.VelocityY, hand.SpeedPalms, hand.DownSpeedPalms, captureMs, hand.Epoch, hand.Pose, hand.CanPlay);

        static TutorialHandSignals Signals() => new TutorialHandSignals(new HandCellTracker(CellSettings.Default, PartHysteresis, HoldSec));

        static void Read(TutorialHandSignals signals, Hands hands, bool inputNote = false) => signals.Read(hands.Clock, hands, inputNote);

        [Test]
        public void NoHands_SayNothing()
        {
            var signals = Signals();

            Read(signals, new Hands());
            Assert.IsFalse(signals.OpenHandVisible || signals.CellChanged || signals.BothFists);

            signals.Read(0.2, null, false);
            Assert.IsFalse(signals.OpenHandVisible || signals.CellChanged || signals.BothFists, "no view at all reads as no hands");
        }

        [Test]
        public void AnOpenHand_IsSeen_AHalfOpenOneIsNot()
        {
            var signals = Signals();
            var hands = new Hands();

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Unknown)));
            Assert.IsFalse(signals.OpenHandVisible, "a half open hand plays but is not the open hand of the tutorial");

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Open)));
            Assert.IsTrue(signals.OpenHandVisible);

            Read(signals, hands.Show());
            Assert.IsFalse(signals.OpenHandVisible, "and it is gone with the hand");
        }

        [Test]
        public void AMouseOrKeyboardNote_CountsAsAnOpenHand()
        {
            var signals = Signals();

            Read(signals, new Hands(), inputNote: true);

            Assert.IsTrue(signals.OpenHandVisible, "they have no hand to show, and they play");
        }

        [Test]
        public void AFistAlone_IsNeitherAnOpenHandNorTwoFists()
        {
            var signals = Signals();

            Read(signals, new Hands().Show(Hand(0, Middle, Top, HandPose.Fist)));

            Assert.IsFalse(signals.OpenHandVisible);
            Assert.IsFalse(signals.BothFists, "one fist is not the end gesture");
        }

        [Test]
        public void TwoFists_AreBothFists_AFistAndAnOpenHandAreNot()
        {
            var signals = Signals();
            var hands = new Hands();

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Fist), Hand(1, Right, Top, HandPose.Fist)));
            Assert.IsTrue(signals.BothFists);
            Assert.IsFalse(signals.OpenHandVisible, "two fists are not an open hand: they do not fade the tutorial");

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Fist), Hand(1, Right, Top, HandPose.Open)));
            Assert.IsFalse(signals.BothFists);
            Assert.IsTrue(signals.OpenHandVisible);

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Fist), Hand(1, Right, Top, HandPose.Unknown)));
            Assert.IsFalse(signals.BothFists, "a hand that is half open is not a fist");
        }

        [Test]
        public void AHandThatIsMissingForAMoment_IsNotCounted()
        {
            var signals = Signals();

            Read(signals, new Hands().Show(Hand(0, Left, Top, HandPose.Fist), Hand(1, Right, Top, HandPose.Fist, tracked: false)));

            Assert.IsFalse(signals.BothFists, "only one hand is really there");
        }

        [Test]
        public void MovingIntoAnotherCell_IsOneChange_ForOnePicture()
        {
            var signals = Signals();
            var hands = new Hands();

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open)));
            Assert.IsFalse(signals.CellChanged, "arriving in a cell is not a change of cell");

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Open)));
            Assert.IsTrue(signals.CellChanged, "from the first column to the second");

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Open)));
            Assert.IsFalse(signals.CellChanged, "staying is not a change");

            Read(signals, hands.Show(Hand(0, Middle, Bottom, HandPose.Open)));
            Assert.IsTrue(signals.CellChanged, "from the top row to the bottom row");
        }

        [Test]
        public void ATremblingHand_OnTheLineBetweenTwoCells_IsNotAChange()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, 0.30f, 0.30f, HandPose.Open)));

            for (var i = 0; i < 40; i++)
            {
                var wobble = (i % 2 == 0 ? 1 : -1) * 0.01f;
                Read(signals, hands.Show(Hand(0, 1f / 3f + wobble, 0.5f + wobble, HandPose.Open)));
                Assert.IsFalse(signals.CellChanged, "picture " + i);
            }
        }

        [Test]
        public void AFist_ChangesNoCell_AndStartsAfresh_WhenItOpens()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open)));

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Fist)));
            Assert.IsFalse(signals.CellChanged, "a fist does not play, so it does not change instrument");

            Read(signals, hands.Show(Hand(0, Right, Bottom, HandPose.Open)));
            Assert.IsFalse(signals.CellChanged, "it opens somewhere else: that is an arrival, not a move");
        }

        [Test]
        public void AHandLostForAMoment_KeepsItsCellForTheMusic_AHandLostForLongArrivesAfresh()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open)));

            Read(signals, hands.Show());
            Read(signals, hands.Show(Hand(0, Right, Bottom, HandPose.Open)));
            Assert.IsTrue(signals.CellChanged, "lost for a picture only: the music kept the hand in its cell, so this is another instrument");

            for (var i = 0; i < 12; i++) Read(signals, hands.Show());   // more than the hold time without the hand
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open, epoch: 1)));
            Assert.IsFalse(signals.CellChanged, "a hand with a new history is a new hand");
        }

        [Test]
        public void EachHand_KeepsItsOwnCell()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open), Hand(1, Right, Bottom, HandPose.Open)));

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open), Hand(1, Right, Bottom, HandPose.Open)));
            Assert.IsFalse(signals.CellChanged, "two hands standing still");

            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open), Hand(1, Middle, Bottom, HandPose.Open)));
            Assert.IsTrue(signals.CellChanged, "the second hand moved");
        }

        [Test]
        public void AHandThatHasJustComeIn_IsInNoCell_UntilItPlays()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Open, canPlay: false)));

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Open, canPlay: false)));
            Assert.IsFalse(signals.CellChanged, "the instrument of a hand that does not play yet is not heard, so the cell is not changed either");

            Read(signals, hands.Show(Hand(0, Middle, Top, HandPose.Open)));
            Read(signals, hands.Show(Hand(0, Right, Top, HandPose.Open)));
            Assert.IsTrue(signals.CellChanged, "once it plays, it changes cell like any other");
        }

        [Test]
        public void AnUnsureHand_PlaysAndChangesCell_ButIsNotAnOpenHand()
        {
            var signals = Signals();
            var hands = new Hands();
            Read(signals, hands.Show(Hand(0, Left, Top, HandPose.Unknown)));

            Read(signals, hands.Show(Hand(0, Right, Bottom, HandPose.Unknown)));

            Assert.IsTrue(signals.CellChanged, "the same cell as the instrument: a hand of an unsure pose plays");
            Assert.IsFalse(signals.OpenHandVisible);
        }
    }
}
