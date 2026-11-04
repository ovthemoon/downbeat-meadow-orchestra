using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>Immutable summary of a window of frame times (milliseconds).</summary>
    public readonly struct FrameSummary
    {
        public int Count { get; }
        public double MeanMs { get; }
        public double P50Ms { get; }
        public double P95Ms { get; }
        public double P99Ms { get; }
        public double MaxMs { get; }
        public int Over25Ms { get; }
        public int Over50Ms { get; }

        public FrameSummary(int count, double meanMs, double p50, double p95, double p99, double maxMs, int over25, int over50)
        {
            Count = count; MeanMs = meanMs; P50Ms = p50; P95Ms = p95; P99Ms = p99; MaxMs = maxMs; Over25Ms = over25; Over50Ms = over50;
        }

        public double MeanFps => MeanMs > 0 ? 1000.0 / MeanMs : 0;
    }

    /// <summary>Collects frame times; <see cref="Summarize"/> returns a snapshot and leaves the collector untouched.</summary>
    public sealed class FrameStats
    {
        const double Over25Threshold = 25.0;
        const double Over50Threshold = 50.0;

        readonly List<double> _framesMs;

        public FrameStats(int capacity = 4096) => _framesMs = new List<double>(capacity);

        public int Count => _framesMs.Count;

        public void Add(double frameMs)
        {
            if (double.IsNaN(frameMs) || frameMs < 0) throw new ArgumentOutOfRangeException(nameof(frameMs));
            _framesMs.Add(frameMs);
        }

        public void Clear() => _framesMs.Clear();

        public FrameSummary Summarize()
        {
            if (_framesMs.Count == 0) return new FrameSummary(0, 0, 0, 0, 0, 0, 0, 0);
            var sorted = new List<double>(_framesMs);
            sorted.Sort();
            double sum = 0;
            int over25 = 0, over50 = 0;
            foreach (var v in _framesMs)
            {
                sum += v;
                if (v > Over25Threshold) over25++;
                if (v > Over50Threshold) over50++;
            }
            return new FrameSummary(
                _framesMs.Count, sum / _framesMs.Count,
                Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99),
                sorted[sorted.Count - 1], over25, over50);
        }

        /// <summary>Linear-interpolated percentile of an ascending list.</summary>
        public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
        {
            if (sortedAscending.Count == 0) throw new ArgumentException("empty list", nameof(sortedAscending));
            if (sortedAscending.Count == 1) return sortedAscending[0];
            var rank = p * (sortedAscending.Count - 1);
            var lo = (int)Math.Floor(rank);
            var hi = (int)Math.Ceiling(rank);
            return sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * (rank - lo);
        }
    }
}
