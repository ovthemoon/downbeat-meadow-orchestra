using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Downbeat.Core
{
    /// <summary>One click the measurement asked the audio engine to schedule.</summary>
    public readonly struct ClockScheduleRecord
    {
        public long Slot { get; }
        public double TargetDsp { get; }
        public double DspAtCall { get; }
        public double PerfMsAtCall { get; }

        public ClockScheduleRecord(long slot, double targetDsp, double dspAtCall, double perfMsAtCall)
        {
            Slot = slot; TargetDsp = targetDsp; DspAtCall = dspAtCall; PerfMsAtCall = perfMsAtCall;
        }
    }

    /// <summary>One deliberate stall of the main thread (the "stress" scenario).</summary>
    public readonly struct ClockBlockRecord
    {
        public double StartPerfMs { get; }
        public double DurationMs { get; }

        public ClockBlockRecord(double startPerfMs, double durationMs)
        {
            StartPerfMs = startPerfMs; DurationMs = durationMs;
        }
    }

    /// <summary>Everything one native clock run knows, in the shape tools/analyze_s1.mjs reads (a Day 0 S-1 run folder entry).</summary>
    public sealed class ClockRunData
    {
        public string Label { get; set; }
        public string Path { get; set; }
        public string RunDir { get; set; }
        /// <summary>Path of the raw float32 mono capture, relative to the measurements folder (analyze_s1.mjs joins it to that folder).</summary>
        public string RawFile { get; set; }
        public int Bpm { get; set; }
        public int SlotsPerBeat { get; set; }
        public double DurationSec { get; set; }
        public double LookaheadSec { get; set; }
        public double StartDelaySec { get; set; }
        public double StartedPerfMs { get; set; }
        public double EndedPerfMs { get; set; }

        public int SampleRate { get; set; }
        public long FirstFrame { get; set; }
        public long Frames { get; set; }
        public double Peak { get; set; }
        public int Gaps { get; set; }
        public double OutputLatencySec { get; set; }
        public double BaseLatencySec { get; set; }

        public double StartMarkPerfMs { get; set; }
        public double StartMarkDsp { get; set; }
        public double EndMarkPerfMs { get; set; }
        public double EndMarkDsp { get; set; }

        public long TotalSlots { get; set; }
        public long Issued { get; set; }
        public long SkippedLate { get; set; }
        public double OriginDsp { get; set; }
        public double OriginPerfMs { get; set; }
        public long FrameCount { get; set; }
        public double MaxDspStallSec { get; set; }
        public FrameSummary FrameTimes { get; set; }
        public double MinFrameMs { get; set; }

        public IReadOnlyList<ClockScheduleRecord> Records { get; set; } = new List<ClockScheduleRecord>();
        public IReadOnlyList<ClockBlockRecord> Blocks { get; set; } = new List<ClockBlockRecord>();
        public IReadOnlyDictionary<string, string> Runtime { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Writes <see cref="ClockRunData"/> as the JSON that tools/analyze_s1.mjs expects. Hand-written because JsonUtility cannot write the keyed objects.</summary>
    public static class ClockRunMeta
    {
        const string BeatStartPrefix = "beat-start:";
        const string BeatEndPrefix = "beat-end:";
        const string ContextTag = "unity-native";

        public static string ToJson(ClockRunData d)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\n");
            AppendConfig(sb, d);
            Num(sb, "startedPerfMs", d.StartedPerfMs); Num(sb, "endedPerfMs", d.EndedPerfMs);
            AppendRuntime(sb, d);
            sb.Append("\"tapMuted\":true,\n");
            AppendContexts(sb, d);
            AppendMarks(sb, d);
            sb.Append("\"pageEvents\":[],\n");
            AppendBlocks(sb, d);
            AppendFrameTimes(sb, d);
            AppendSummary(sb, d);
            sb.Append("\"unityEvents\":[]\n}\n");
            return sb.ToString();
        }

        static void AppendConfig(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"config\":{");
            sb.Append("\"bpm\":").Append(d.Bpm).Append(",\"slotsPerBeat\":").Append(d.SlotsPerBeat).Append(',');
            sb.Append("\"durationSec\":").Append(F(d.DurationSec)).Append(",\"lookaheadSec\":").Append(F(d.LookaheadSec)).Append(',');
            sb.Append("\"startDelaySec\":").Append(F(d.StartDelaySec)).Append(",\"block\":null,");
            sb.Append("\"runDir\":").Append(Q(d.RunDir)).Append(",\"label\":").Append(Q(d.Label)).Append(",\"path\":").Append(Q(d.Path));
            sb.Append("},\n");
        }

        static void AppendRuntime(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"runtime\":{");
            var first = true;
            foreach (var pair in d.Runtime)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Q(pair.Key)).Append(':').Append(Q(pair.Value));
            }
            sb.Append("},\n");
        }

        static void AppendContexts(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"contexts\":[{");
            sb.Append("\"id\":0,\"tag\":").Append(Q(ContextTag)).Append(",\"sampleRate\":").Append(d.SampleRate).Append(",\"state\":\"running\",");
            sb.Append("\"baseLatency\":").Append(F(d.BaseLatencySec)).Append(",\"outputLatency\":").Append(F(d.OutputLatencySec)).Append(',');
            sb.Append("\"firstFrame\":").Append(d.FirstFrame.ToString(CultureInfo.InvariantCulture)).Append(",\"gaps\":").Append(d.Gaps).Append(',');
            sb.Append("\"peak\":").Append(F(d.Peak)).Append(",\"frames\":").Append(d.Frames.ToString(CultureInfo.InvariantCulture)).Append(",\"workletError\":null,");
            sb.Append("\"rawFile\":").Append(Q(d.RawFile));
            sb.Append("}],\n");
        }

        static void AppendMarks(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"marks\":[");
            AppendMark(sb, BeatStartPrefix + d.Label + ":" + d.Path, d.StartMarkPerfMs, d.StartMarkDsp);
            sb.Append(',');
            AppendMark(sb, BeatEndPrefix + d.Label + ":" + d.Path, d.EndMarkPerfMs, d.EndMarkDsp);
            sb.Append("],\n");
        }

        static void AppendMark(StringBuilder sb, string label, double perfMs, double dsp)
        {
            sb.Append("{\"label\":").Append(Q(label)).Append(",\"perfMs\":").Append(F(perfMs)).Append(",\"ctxTimes\":{\"0\":").Append(F(dsp)).Append("}}");
        }

        static void AppendBlocks(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"blocks\":[");
            for (var i = 0; i < d.Blocks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"startPerfMs\":").Append(F(d.Blocks[i].StartPerfMs)).Append(",\"durationMs\":").Append(F(d.Blocks[i].DurationMs)).Append('}');
            }
            sb.Append("],\n");
        }

        static void AppendFrameTimes(StringBuilder sb, ClockRunData d)
        {
            var f = d.FrameTimes;
            sb.Append("\"rafIntervalsMs\":{");
            sb.Append("\"count\":").Append(f.Count).Append(",\"mean\":").Append(F(f.MeanMs)).Append(",\"p50\":").Append(F(f.P50Ms));
            sb.Append(",\"p95\":").Append(F(f.P95Ms)).Append(",\"p99\":").Append(F(f.P99Ms)).Append(",\"max\":").Append(F(f.MaxMs));
            sb.Append(",\"min\":").Append(F(d.MinFrameMs)).Append("},\n");
        }

        static void AppendSummary(StringBuilder sb, ClockRunData d)
        {
            sb.Append("\"summary\":{");
            sb.Append("\"label\":").Append(Q(d.Label)).Append(",\"path\":").Append(Q(d.Path)).Append(",\"bpm\":").Append(d.Bpm);
            sb.Append(",\"slotsPerBeat\":").Append(d.SlotsPerBeat).Append(",\"totalSlots\":").Append(d.TotalSlots.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"issued\":").Append(d.Issued.ToString(CultureInfo.InvariantCulture)).Append(",\"skippedLate\":").Append(d.SkippedLate.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"originClock\":").Append(F(d.OriginDsp)).Append(",\"originPerfMs\":").Append(F(d.OriginPerfMs)).Append(",\"originCtx\":").Append(F(d.OriginDsp));
            sb.Append(",\"endPerfMs\":").Append(F(d.EndedPerfMs)).Append(",\"frames\":").Append(d.FrameCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"maxDspStallSec\":").Append(F(d.MaxDspStallSec)).Append(",\"records\":[");
            for (var i = 0; i < d.Records.Count; i++)
            {
                var r = d.Records[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"slot\":").Append(r.Slot.ToString(CultureInfo.InvariantCulture)).Append(",\"targetDsp\":").Append(F(r.TargetDsp));
                sb.Append(",\"dspAtCall\":").Append(F(r.DspAtCall)).Append(",\"perfMsAtCall\":").Append(F(r.PerfMsAtCall)).Append('}');
            }
            sb.Append("]},\n");
        }

        static void Num(StringBuilder sb, string key, double value) => sb.Append('"').Append(key).Append("\":").Append(F(value)).Append(",\n");

        /// <summary>Round-trip ("R") invariant formatting; JSON has no NaN or Infinity, so those become null.</summary>
        static string F(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "null" : value.ToString("R", CultureInfo.InvariantCulture);

        static string Q(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
