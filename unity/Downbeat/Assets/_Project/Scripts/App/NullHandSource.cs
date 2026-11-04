using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>
    /// Stand-in while no hand tracker is connected: reports <see cref="TrackerState.Off"/> and never produces a frame, so the
    /// input falls back to the mouse. The UDP hand source (Task 4) replaces it for the real hand input.
    /// </summary>
    public sealed class NullHandSource : IHandSource
    {
        public TrackerState State => TrackerState.Off;
        public int Generation => 0;

        public void Poll(List<HandFrame> frames)
        {
        }

        public double RequestTimeFor(double captureMs) => double.NaN;
    }
}
