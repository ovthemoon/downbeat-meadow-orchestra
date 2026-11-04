using System;
using Downbeat.Core;
using NUnit.Framework;

namespace Downbeat.Tests
{
    public class FrameStatsTests
    {
        [Test]
        public void Summarize_of_empty_collector_is_all_zero()
        {
            var s = new FrameStats().Summarize();
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0, s.MeanFps);
        }

        [Test]
        public void Summarize_reports_mean_percentiles_and_threshold_counts()
        {
            var stats = new FrameStats();
            for (var i = 0; i < 98; i++) stats.Add(16.0);
            stats.Add(30.0);
            stats.Add(60.0);
            var s = stats.Summarize();
            Assert.AreEqual(100, s.Count);
            Assert.AreEqual(16.0, s.P50Ms, 1e-9);
            Assert.AreEqual(60.0, s.MaxMs, 1e-9);
            Assert.AreEqual(2, s.Over25Ms);
            Assert.AreEqual(1, s.Over50Ms);
            Assert.AreEqual((98 * 16.0 + 90.0) / 100, s.MeanMs, 1e-9);
        }

        [Test]
        public void Summarize_does_not_change_the_collector()
        {
            var stats = new FrameStats();
            stats.Add(20);
            stats.Add(10);
            stats.Summarize();
            Assert.AreEqual(2, stats.Count);
            Assert.AreEqual(15.0, stats.Summarize().P50Ms, 1e-9);
        }

        [Test]
        public void Add_rejects_negative_and_NaN()
        {
            var stats = new FrameStats();
            Assert.Throws<ArgumentOutOfRangeException>(() => stats.Add(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stats.Add(double.NaN));
        }
    }
}
