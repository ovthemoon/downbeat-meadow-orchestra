using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Downbeat.Tests
{
    /// <summary>One measured run, as written to measurements/perf/grass-mac.json.</summary>
    [Serializable]
    public sealed class PerfRun
    {
        public string source;
        public string tier;
        public int width;
        public int height;
        public double seconds;
        public int frames;
        public double fpsAverage;
        public double frameMsP50;
        public double frameMsP95;
        public double frameMsP99;
        public double frameMsMax;
        public int framesOver25ms;
        public long grassBlades;
        public string note;
    }

    [Serializable]
    public sealed class PerfFile
    {
        public string machine;
        public string gpu;
        public string unity;
        public string os;
        public string note;
        public List<PerfRun> runs = new List<PerfRun>();
    }

    /// <summary>Reads, updates and writes the perf file; a run replaces an earlier run of the same source and tier, the rest stays.</summary>
    public static class PerfRecord
    {
        const double FrameBudgetOverMs = 25.0; // the same bar FrameStatsLogger counts (over25ms)
        const string SharedMachineNote = "Shared desktop: other apps (browsers, another Unity editor) were running; numbers are what this Mac did at that time, not a benchmark.";


        public static PerfRun Summarize(string source, string tier, int width, int height, IReadOnlyList<float> frameSeconds, long blades, string note)
        {
            var ms = new List<double>(frameSeconds.Count);
            double total = 0;
            var over = 0;
            foreach (var seconds in frameSeconds)
            {
                ms.Add(seconds * 1000.0);
                total += seconds;
                if (seconds * 1000.0 > FrameBudgetOverMs) over++;
            }
            ms.Sort();
            return new PerfRun
            {
                source = source, tier = tier, width = width, height = height, seconds = Math.Round(total, 2), frames = ms.Count,
                fpsAverage = Math.Round(ms.Count / total, 1),
                frameMsP50 = Math.Round(Percentile(ms, 0.50), 2), frameMsP95 = Math.Round(Percentile(ms, 0.95), 2),
                frameMsP99 = Math.Round(Percentile(ms, 0.99), 2), frameMsMax = Math.Round(ms[ms.Count - 1], 2),
                framesOver25ms = over, grassBlades = blades, note = note,
            };
        }

        /// <summary>
        /// Merges the run into the file <paramref name="fileName"/> of the git-ignored <c>captures/perf</c> folder. With <c>DOWNBEAT_KEEP_CAPTURES=1</c> it is merged into the tracked
        /// <c>measurements/perf</c> file too (the numbers that are kept as evidence); an ordinary test run never changes a tracked file.
        /// </summary>
        public static void Save(PerfRun run, string fileName = "grass-mac.json")
        {
            Merge(CaptureOutput.PerfDir, fileName, run);
            if (CaptureOutput.KeepTracked) Merge(CaptureOutput.TrackedPerfDir, fileName, run);
        }

        static void Merge(string directory, string fileName, PerfRun run)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            var file = File.Exists(path) ? JsonUtility.FromJson<PerfFile>(File.ReadAllText(path)) : new PerfFile();
            file.machine = SystemInfo.deviceModel;
            file.gpu = SystemInfo.graphicsDeviceName;
            file.unity = Application.unityVersion;
            file.os = SystemInfo.operatingSystem;
            file.note = SharedMachineNote;
            file.runs.RemoveAll(r => r.source == run.source && r.tier == run.tier);
            file.runs.Add(run);
            File.WriteAllText(path, JsonUtility.ToJson(file, true) + "\n");
        }

        static double Percentile(List<double> sorted, double p)
        {
            var index = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }
    }
}
