using System;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Day0
{
    /// <summary>
    /// S-2: records Unity frame times for a fixed window, optionally pulling the hand buffer every frame
    /// (the same call the real input would make) and recording the JS-inference-end to Unity-read delivery time.
    /// </summary>
    public sealed class FrameWindowRunner
    {
        const int SeqIndex = 0;
        const int InferEndMsIndex = 2;

        readonly FrameWindowArgs _args;
        readonly FrameStats _frames = new FrameStats(8192);
        readonly FrameStats _delivery = new FrameStats(4096);
        readonly double[] _hands = new double[Day0Bridge.HandBufferLength];
        readonly double _endRealtime;
        int _skipped;
        int _newFrames;
        double _lastSeq = -1;

        public FrameWindowRunner(FrameWindowArgs args)
        {
            _args = args ?? throw new ArgumentNullException(nameof(args));
            _endRealtime = Time.realtimeSinceStartupAsDouble + args.seconds;
            Day0Bridge.Mark($"frames-start:{args.label}");
        }

        public bool IsFinished => Time.realtimeSinceStartupAsDouble >= _endRealtime;

        public void Tick(double unscaledDeltaSec)
        {
            if (_args.pullHands) PullHands();
            if (_skipped < _args.skipFrames)
            {
                _skipped++;
                return;
            }
            _frames.Add(unscaledDeltaSec * 1000.0);
        }

        void PullHands()
        {
            var seq = Day0Bridge.ReadHands(_hands);
            if (seq < 0 || seq == _lastSeq) return;
            _lastSeq = seq;
            _newFrames++;
            var delivery = Day0Bridge.NowMs() - _hands[InferEndMsIndex];
            if (delivery >= 0) _delivery.Add(delivery);
        }

        public FrameWindowSummary Finish()
        {
            Day0Bridge.Mark($"frames-end:{_args.label}");
            var f = _frames.Summarize();
            var d = _delivery.Summarize();
            return new FrameWindowSummary
            {
                label = _args.label,
                pullHands = _args.pullHands,
                count = f.Count,
                meanMs = f.MeanMs,
                meanFps = f.MeanFps,
                p50Ms = f.P50Ms,
                p95Ms = f.P95Ms,
                p99Ms = f.P99Ms,
                maxMs = f.MaxMs,
                over25Ms = f.Over25Ms,
                over50Ms = f.Over50Ms,
                newHandFrames = _newFrames,
                deliveryCount = d.Count,
                deliveryP50Ms = d.P50Ms,
                deliveryP95Ms = d.P95Ms,
                deliveryMaxMs = d.MaxMs,
            };
        }
    }
}
