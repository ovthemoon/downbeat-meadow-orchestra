using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// The GPU time of recent frames from Unity's <see cref="FrameTimingManager"/> (the player setting "Frame Timing Stats" is on). The numbers arrive a few frames late and only where the
    /// graphics API reports them; where it does not, <see cref="HasGpuTime"/> stays false and <see cref="LatestGpuMs"/> is NaN, and the caller goes by the frame time instead.
    /// </summary>
    public sealed class GpuFrameTimer
    {
        const int FramesToRead = 1;

        readonly FrameTiming[] _timings = new FrameTiming[FramesToRead];

        /// <summary>The GPU time (ms) of the newest frame whose timing has arrived; NaN until one has, and for ever where the platform gives none.</summary>
        public double LatestGpuMs { get; private set; } = double.NaN;
        /// <summary>The CPU time (ms) of the same frame (the whole frame on the CPU side, as Unity measures it).</summary>
        public double LatestCpuMs { get; private set; } = double.NaN;
        public bool HasGpuTime => !double.IsNaN(LatestGpuMs);

        /// <summary>Whether the player has frame timing statistics on at all (the setting; no sample yet).</summary>
        public static bool IsEnabled => FrameTimingManager.IsFeatureEnabled();

        /// <summary>Call once per frame. True when it read a new sample.</summary>
        public bool Poll()
        {
            if (!IsEnabled) return false;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(FramesToRead, _timings) < FramesToRead) return false;
            var gpu = _timings[0].gpuFrameTime;
            LatestCpuMs = _timings[0].cpuFrameTime;
            LatestGpuMs = gpu > 0.0 ? gpu : double.NaN; // 0 means the platform did not measure it
            return true;
        }
    }
}
