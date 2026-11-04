using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>The live camera: pulls the hand buffer the page tracker fills (one interop call and a 1 KB copy per frame, no allocation).</summary>
    public sealed class JsHandSource : IHandSource
    {
        readonly double[] _buffer = new double[HandBufferLayout.Length];
        long _lastSeq;

        public TrackerState State { get; private set; } = TrackerState.Off;
        public int Generation => 0;

        public void Poll(List<HandFrame> frames)
        {
            var seq = PagePort.ReadHands(_buffer);
            if (seq < 0)
            {
                State = TrackerState.Off; // no tracker on this page
                return;
            }
            State = HandFrameReader.ReadState(_buffer);
            if (seq == 0 || seq == _lastSeq) return;
            _lastSeq = seq;
            if (HandFrameReader.TryRead(_buffer, PagePort.NowMs(), out var frame)) frames.Add(frame);
        }

        public double RequestTimeFor(double captureMs) => double.NaN;
    }
}
