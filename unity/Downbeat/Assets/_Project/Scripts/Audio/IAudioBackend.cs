using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.Audio
{
    /// <summary>
    /// Unity's audio system as <see cref="NativeAudioEngine"/> needs it: the DSP clock, a fixed set of voices, sample loading and
    /// scheduled playback. The engine keeps the policy (late notes, which voice to cut); a fake replaces this in the tests.
    /// </summary>
    public interface IAudioBackend
    {
        /// <summary>The audio system's clock in seconds (AudioSettings.dspTime).</summary>
        double DspTime { get; }
        /// <summary>Estimated time from a sample being mixed to it leaving the speakers.</summary>
        double OutputLatencySec { get; }
        int LoadedCount { get; }
        int FailedCount { get; }

        void CreateVoices(int count);
        void BeginLoading(IReadOnlyList<SampleLoadRequest> samples);
        /// <summary>Call once per frame: collects the samples that finished loading.</summary>
        void Poll();
        bool HasSample(int index);
        double SampleSeconds(int index);
        /// <summary>Starts <paramref name="voice"/> playing the sample at the DSP time <paramref name="startDsp"/>, replacing whatever it was doing.</summary>
        void Play(int voice, int sample, double startDsp, double rate, float volume);
        void Stop(int voice);
        /// <summary>Lowers <paramref name="voice"/> to silence over <paramref name="seconds"/> and then stops it (no click); the voice may be played again after that time.</summary>
        void FadeOut(int voice, double seconds);
        /// <summary>How long after its fade a faded source is kept before it is stopped: the voice may be played again only then.</summary>
        double FadeReleaseMarginSec { get; }
    }
}
