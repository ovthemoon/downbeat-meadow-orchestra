using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class LatencyFlashTests
    {
        [Test]
        public void IsLit_OnlyInTheFrameItWasArmedIn()
        {
            var flash = new LatencyFlash();

            flash.Arm(10);

            Assert.IsTrue(flash.IsLit(10));
            Assert.IsFalse(flash.IsLit(9));
            Assert.IsFalse(flash.IsLit(11), "one frame, so that one picture of a phone video shows it");
        }

        [Test]
        public void IsLit_IsFalseBeforeAnythingWasArmed()
        {
            Assert.IsFalse(new LatencyFlash().IsLit(0));
        }

        [Test]
        public void Arm_CountsEveryNoteEvenWhenTwoShareAFrame()
        {
            var flash = new LatencyFlash();

            flash.Arm(5);
            flash.Arm(5);
            flash.Arm(8);

            Assert.AreEqual(3, flash.Flashes);
            Assert.IsTrue(flash.IsLit(8));
            Assert.IsFalse(flash.IsLit(5));
        }

        [Test]
        public void Square_IsTheTopRightCorner_120BY120()
        {
            var square = LatencyFlash.Square(1920, 1080);

            Assert.AreEqual((1800, 0, 120), (square.x, square.y, square.size));
        }

        [Test]
        public void Square_NeverLeavesATinyWindow()
        {
            var square = LatencyFlash.Square(100, 80);

            Assert.AreEqual(100 - square.size, square.x);
            Assert.AreEqual(0, square.y);
            Assert.LessOrEqual(square.size, 80, "it fits the shorter side");
            Assert.Greater(square.size, 0);
        }
    }
}
