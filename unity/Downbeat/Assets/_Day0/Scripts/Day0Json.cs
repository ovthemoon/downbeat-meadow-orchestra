using System;

namespace Downbeat.Day0
{
    /// <summary>Argument and result shapes exchanged with the test page as JSON (JsonUtility).</summary>
    [Serializable]
    public sealed class BeatArgs
    {
        public string path = "scheduled"; // "scheduled" | "play"
        public string label = "";
        public double bpm = 96;
        public int slotsPerBeat = 4;
        public double durationSec = 60;
        public double lookaheadSec = 0.1;
        public double startDelaySec = 0.6;
        public int poolSize = 6;
    }

    [Serializable]
    public sealed class AheadArgs
    {
        public double[] aheadSec = { 0.25, 0.5, 1.0, 2.0 };
        public double spacingSec = 3.0;
    }

    [Serializable]
    public sealed class FrameWindowArgs
    {
        public string label = "";
        public double seconds = 20;
        public int skipFrames = 10;
        public bool pullHands = false;
    }

    [Serializable]
    public sealed class BridgeArgs
    {
        public int calls = 10000;
    }

    [Serializable]
    public sealed class VsyncArgs
    {
        public int count = 1;
    }

    [Serializable]
    public sealed class RunInBackgroundArgs
    {
        public bool value = true;
    }

    [Serializable]
    public sealed class SinkArgs
    {
        public bool parse;
    }

    [Serializable]
    public sealed class HandFrameJson
    {
        public double[] v;
    }

    [Serializable]
    public sealed class DspSample
    {
        public string label;
        public double realtimeSec;
        public double dspSec;
        public double ctxSec;
        public double perfMs;
        public int frame;
    }

    [Serializable]
    public sealed class ScheduleRecord
    {
        public long slot;
        public double targetDsp;
        public double dspAtCall;
        public double perfMsAtCall;
        public double ctxAtCall;
    }

    [Serializable]
    public sealed class BeatSummary
    {
        public string label;
        public string path;
        public double bpm;
        public int slotsPerBeat;
        public long totalSlots;
        public long issued;
        public long skippedLate;
        public double originClock;
        public double originPerfMs;
        public double originCtx;
        public double endPerfMs;
        public int frames;
        public double maxDspStallSec;
        public ScheduleRecord[] records;
    }

    [Serializable]
    public sealed class AheadRecord
    {
        public double aheadSec;
        public double dsp0;
        public double perf0;
        public double ctx0;
        public double targetDsp;
    }

    [Serializable]
    public sealed class AheadSummary
    {
        public AheadRecord[] records;
    }

    [Serializable]
    public sealed class FrameWindowSummary
    {
        public string label;
        public bool pullHands;
        public int count;
        public double meanMs;
        public double meanFps;
        public double p50Ms;
        public double p95Ms;
        public double p99Ms;
        public double maxMs;
        public int over25Ms;
        public int over50Ms;
        public int newHandFrames;
        public double deliveryP50Ms;
        public double deliveryP95Ms;
        public double deliveryMaxMs;
        public int deliveryCount;
    }

    [Serializable]
    public sealed class BridgeSummary
    {
        public int calls;
        public double nowMsLoopMs;
        public double readHandsLoopMs;
        public double readHandsUsPerCall;
        public double nowMsUsPerCall;
        public long managedBytesDuringReadHands;
        public bool writeBackOk;
        public bool writeBackAfterGrowthOk;
        public int alignmentError;
        public double jsonLoopMs;
        public double jsonUsPerCall;
        public long managedBytesDuringJson;
        public int jsonLength;
    }
}
