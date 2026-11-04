using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--log-bloom-sync PATH</c>: at quit writes how far the moment a flower is half open was from the moment its sound is heard, for every flower that opened in the run, and the
    /// frame time of the run, so that the difference can be read in frames. The moment "half open" is when the bloom first reaches 0.5 on a frame of the heard clock.
    /// </summary>
    [Serializable]
    public sealed class BloomSyncReport
    {
        public int flowersOpened;
        public double medianLagMs;
        public double medianAbsLagMs;
        public double p95AbsLagMs;
        public double maxAbsLagMs;
        public double medianFrameMs;
        public double medianAbsLagFrames;
        public int framesMeasured;
        public string note;

        public static BloomSyncReport From(IReadOnlyList<float> lagsSec, IReadOnlyList<float> frameSec)
        {
            var report = new BloomSyncReport { flowersOpened = lagsSec.Count, framesMeasured = frameSec.Count };
            if (lagsSec.Count == 0 || frameSec.Count == 0) return report;
            var lags = new List<double>(lagsSec.Count);
            var abs = new List<double>(lagsSec.Count);
            foreach (var lag in lagsSec)
            {
                lags.Add(lag * 1000.0);
                abs.Add(Math.Abs(lag * 1000.0));
            }
            var frames = new List<double>(frameSec.Count);
            foreach (var f in frameSec) frames.Add(f * 1000.0);
            lags.Sort(); abs.Sort(); frames.Sort();
            report.medianLagMs = Round(Percentile(lags, 0.5));
            report.medianAbsLagMs = Round(Percentile(abs, 0.5));
            report.p95AbsLagMs = Round(Percentile(abs, 0.95));
            report.maxAbsLagMs = Round(abs[abs.Count - 1]);
            report.medianFrameMs = Round(Percentile(frames, 0.5));
            report.medianAbsLagFrames = Round(report.medianAbsLagMs / report.medianFrameMs, 2);
            return report;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(this, true) + "\n");
        }

        static double Percentile(List<double> sorted, double p) => sorted[Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1))];

        static double Round(double v, int digits = 1) => Math.Round(v, digits);
    }
}
