using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class WarningRateLimiterTests
    {
        [Test]
        public void TryAcquire_AllowsTheFirstWarning()
        {
            var limiter = new WarningRateLimiter(5.0);

            Assert.IsTrue(limiter.TryAcquire(100.0, out var suppressed));
            Assert.AreEqual(0, suppressed);
        }

        [Test]
        public void TryAcquire_SuppressesWarningsInsideTheInterval_AndCountsThem()
        {
            var limiter = new WarningRateLimiter(5.0);
            limiter.TryAcquire(100.0, out _);

            Assert.IsFalse(limiter.TryAcquire(100.1, out _));
            Assert.IsFalse(limiter.TryAcquire(104.9, out _));
            Assert.AreEqual(2, limiter.Suppressed);
        }

        [Test]
        public void TryAcquire_AllowsAgainAfterTheInterval_AndReportsHowManyWereHeldBack()
        {
            var limiter = new WarningRateLimiter(5.0);
            limiter.TryAcquire(100.0, out _);
            limiter.TryAcquire(101.0, out _);
            limiter.TryAcquire(102.0, out _);

            Assert.IsTrue(limiter.TryAcquire(105.0, out var suppressed));
            Assert.AreEqual(2, suppressed);
            Assert.AreEqual(0, limiter.Suppressed, "the count starts again");
        }

        [Test]
        public void TryAcquire_AThousandWarningsInOneSecond_LogOnce()
        {
            var limiter = new WarningRateLimiter(5.0);
            var allowed = 0;
            for (var i = 0; i < 1000; i++)
            {
                if (limiter.TryAcquire(50.0 + i / 1000.0, out _)) allowed++;
            }

            Assert.AreEqual(1, allowed);
        }
    }
}
