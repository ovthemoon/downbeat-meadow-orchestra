using System;
using Downbeat.Core;
using UnityEngine;
using VContainer.Unity;

namespace Downbeat.App
{
    /// <summary>
    /// <c>--log-frame-stats SEC</c>: writes one line per SEC seconds with the render frame times of that window (fps, p50/p95/p99/max, frames
    /// over 25 and 50 ms) and what the hand input is doing (tracker state, hands in view, datagrams, notes scheduled), and a TOTAL line when the app
    /// closes. Counting starts when the audio clock runs, i.e. after loading. Without the option it is not even registered.
    /// </summary>
    public sealed class FrameStatsLogger : ITickable, IDisposable
    {
        const double MsPerSecond = 1000.0;

        readonly double _periodSec;
        readonly IAudioEngine _audio;
        readonly IHandSource _source;
        readonly IHandMotionView _motion;
        readonly FrameStats _window = new FrameStats(1024);
        readonly FrameStats _total = new FrameStats(16384);
        double _windowStart = double.NaN;
        double _totalStart = double.NaN;
        int _handsSeenFrames;
        int _frames;
        readonly GpuFrameTimer _gpu = new GpuFrameTimer();
        double _gpuSum;
        int _gpuSamples;
        double _gpuMax;
        double _totalGpuSum;
        int _totalGpuSamples;

        public FrameStatsLogger(LaunchArgs args, IAudioEngine audio, IHandSource source, IHandMotionView motion)
        {
            _periodSec = args.TryGetDouble("log-frame-stats", out var period) && period > 0 ? period : 5.0;
            _audio = audio;
            _source = source;
            _motion = motion;
        }

        public void Tick()
        {
            if (!_audio.IsRunning) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (double.IsNaN(_windowStart))
            {
                _windowStart = now;
                _totalStart = now;
                return; // the first frame after loading is not a typical one
            }

            if (_gpu.Poll() && _gpu.HasGpuTime)
            {
                _gpuSum += _gpu.LatestGpuMs;
                _gpuSamples++;
                _gpuMax = System.Math.Max(_gpuMax, _gpu.LatestGpuMs);
                _totalGpuSum += _gpu.LatestGpuMs;
                _totalGpuSamples++;
            }
            var frameMs = Time.unscaledDeltaTime * MsPerSecond;
            _window.Add(frameMs);
            _total.Add(frameMs);
            _frames++;
            if (_motion.Count > 0) _handsSeenFrames++;
            if (now - _windowStart >= _periodSec)
            {
                Log("window", _window.Summarize(), now - _windowStart, _gpuSum, _gpuSamples);
                _gpuSum = 0.0;
                _gpuSamples = 0;
                _window.Clear();
                _windowStart = now;
            }
        }

        public void Dispose()
        {
            if (double.IsNaN(_totalStart) || _total.Count == 0) return;
            Log("TOTAL", _total.Summarize(), Time.realtimeSinceStartupAsDouble - _totalStart, _totalGpuSum, _totalGpuSamples);
        }

        void Log(string label, FrameSummary s, double seconds, double gpuSumMs, int gpuSamples)
        {
            var gpu = gpuSamples > 0 ? $" gpu_ms={gpuSumMs / gpuSamples:0.00} gpu_samples={gpuSamples}" : " gpu_ms=n/a";
            var look = $" quality={Downbeat.Meadow.RenderLevers.Describe()}";
            var udp = _source as UdpHandSource;
            var packets = udp == null ? string.Empty : $" bad_packets={udp.BadPackets} dropped_packets={udp.DroppedPackets}";
            Debug.Log($"[frame-stats] {label} seconds={seconds:0.0} frames={s.Count} fps={s.MeanFps:0.0} p50={s.P50Ms:0.0} p95={s.P95Ms:0.0} p99={s.P99Ms:0.0} max={s.MaxMs:0.0} " +
                $"over25ms={s.Over25Ms} over50ms={s.Over50Ms}{gpu}{look} tracker={_source.State} hands_now={_motion.Count} frames_with_hands={_handsSeenFrames}/{_frames} notes={_audio.Stats.Scheduled}{packets}");
        }
    }
}
