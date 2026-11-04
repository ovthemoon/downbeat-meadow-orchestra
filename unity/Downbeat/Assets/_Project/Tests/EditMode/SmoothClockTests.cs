using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class SmoothClockTests
    {
        const double BlockSec = 1024.0 / 48000.0;   // the audio clock only moves once per audio block (21.3 ms)
        const double Offset = 5000.0;               // audio time = wall time + this, in this test

        /// <summary>The audio clock as Unity reports it: the true audio time rounded down to a whole block.</summary>
        static double Quantised(double wall) => System.Math.Floor((wall + Offset) / BlockSec) * BlockSec;

        [Test]
        public void ReturnsTheAudioTime_WhenTheWallClockStandsStill()
        {
            var clock = new SmoothClock();

            Assert.AreEqual(42.0, clock.Update(42.0, 7.0), 1e-12);
            Assert.AreEqual(42.0, clock.Update(42.0, 7.0), 1e-12);
        }

        [Test]
        public void FollowsTheTrueAudioTimeWithinAFewMilliseconds_WhereTheRawClockIsOffByUpToABlock()
        {
            var clock = new SmoothClock();
            double worstSmooth = 0, worstRaw = 0;
            var frame = 1.0 / 120.0;
            for (var i = 0; i < 1200; i++)
            {
                var wall = i * frame;
                var truth = wall + Offset;
                var raw = Quantised(wall);
                var smooth = clock.Update(raw, wall);
                if (i < 60) continue; // warm-up: half a second
                worstSmooth = System.Math.Max(worstSmooth, System.Math.Abs(smooth - truth));
                worstRaw = System.Math.Max(worstRaw, System.Math.Abs(raw - truth));
            }

            Assert.Greater(worstRaw, 0.018, "the raw clock is up to a block behind");
            Assert.Less(worstSmooth, 0.003, "the smoothed clock is within a few ms of the true audio time");
        }

        [Test]
        public void NeverGoesBackwards_AndNeverFallsBehindTheRawClock()
        {
            var clock = new SmoothClock();
            var previous = double.NegativeInfinity;
            var random = new System.Random(5);
            var wall = 0.0;
            for (var i = 0; i < 2000; i++)
            {
                wall += 0.004 + random.NextDouble() * 0.03; // irregular frames
                var raw = Quantised(wall);
                var smooth = clock.Update(raw, wall);

                Assert.GreaterOrEqual(smooth, previous - 1e-12, $"frame {i}");
                Assert.GreaterOrEqual(smooth, raw - 1e-12, $"frame {i}: never older than what the audio system says");
                previous = smooth;
            }
        }

        [Test]
        public void ADeadAudioClock_CannotRunAway()
        {
            var clock = new SmoothClock();
            for (var i = 0; i < 100; i++) clock.Update(Quantised(i * 0.01), i * 0.01);
            var frozen = Quantised(0.99);

            double last = 0;
            for (var i = 100; i < 600; i++) last = clock.Update(frozen, i * 0.01); // the audio device is gone: the clock stops, the wall clock does not

            Assert.LessOrEqual(last, frozen + SmoothClock.MaxAheadSec + 1e-9, "at most a few tens of ms ahead of the last audio time it saw");
        }

        [Test]
        public void ABackwardJumpOfTheAudioClock_IsFollowedAtOnce_NotPinnedInTheFuture()
        {
            var clock = new SmoothClock();
            for (var i = 0; i < 200; i++) clock.Update(1000.0 + i * 0.01, i * 0.01); // an audio device that has been running for 1000 s

            var after = clock.Update(5.0, 2.0); // the device was reset: the clock starts again near zero

            Assert.AreEqual(5.0, after, 0.06, "the picture follows the new clock at once");
            var later = clock.Update(5.05, 2.05);
            Assert.AreEqual(5.05, later, 0.06);
            Assert.Less(later, 6.0, "never stuck at the old, far larger time");
        }

        [Test]
        public void ASeekOfTheAudioClock_IsFollowedAtOnce()
        {
            var clock = new SmoothClock();
            for (var i = 0; i < 100; i++) clock.Update(Quantised(i * 0.01), i * 0.01);

            var jumped = clock.Update(Quantised(1.0) + 100.0, 1.0);

            Assert.AreEqual(Quantised(1.0) + 100.0, jumped, 0.05, "a clock that jumps forward is not smoothed back");
        }
    }
}
