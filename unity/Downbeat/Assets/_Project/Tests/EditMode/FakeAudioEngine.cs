using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.Tests
{
    /// <summary>An audio clock the test moves by hand.</summary>
    sealed class FakeAudioEngine : IAudioEngine
    {
        public bool IsRunning => true;
        public bool SamplesReady => true;
        public int SamplesFailed => 0;
        public double Now { get; set; }
        public double HeardNow => Now;
        public double VisualNow => Now;
        public AudioStats Stats => new AudioStats(0, 0, 0, 0);

        public void Configure(float masterGainLinear, int maxVoices) { }
        public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples) { }
        public void Tick() { }
        public double Schedule(int sampleIndex, double when, double playbackRate, double gain) => when;
        public readonly List<double> FadeOutAllCalls = new List<double>();
        public void FadeOutAll(double seconds) => FadeOutAllCalls.Add(seconds);
        public void LogFlash(double when, double heardNow) { }
    }

    /// <summary>A hand source that hands out whatever the test queued, and reports a state the test sets.</summary>
    sealed class FakeHandSource : IHandSource
    {
        public readonly List<HandFrame> Pending = new List<HandFrame>();
        public TrackerState State { get; set; }
        public int Generation { get; set; }

        public void Poll(List<HandFrame> frames)
        {
            frames.AddRange(Pending);
            Pending.Clear();
        }

        public double RequestTimeFor(double captureMs) => double.NaN;
    }
}
