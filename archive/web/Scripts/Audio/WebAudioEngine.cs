#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Downbeat.Core;

namespace Downbeat.Audio
{
    /// <summary>
    /// Sound through the browser's Web Audio (page script <c>js/downbeat-audio.js</c>). Day 0 showed that
    /// AudioSettings.dspTime is not the audio clock in Web, so Unity never schedules sound itself: it asks the page for
    /// the audio clock and hands over absolute audio times. Every call here uses primitive arguments (no allocation).
    /// </summary>
    public sealed class WebAudioEngine : IAudioEngine
    {
        const int StateClockAdvancing = 4;
        const int StatLate = 1;
        const int StatStolen = 2;
        const int StatMissingSample = 3;
        const int StatScheduled = 4;

        [DllImport("__Internal")] static extern int Downbeat_Audio_State();
        [DllImport("__Internal")] static extern double Downbeat_Audio_Now();
        [DllImport("__Internal")] static extern double Downbeat_Audio_HeardNow();
        [DllImport("__Internal")] static extern void Downbeat_Audio_Configure(double masterGain, int maxVoices);
        [DllImport("__Internal")] static extern void Downbeat_Audio_LoadSample(int index, string url);
        [DllImport("__Internal")] static extern int Downbeat_Audio_SamplesLoaded();
        [DllImport("__Internal")] static extern int Downbeat_Audio_SamplesFailed();
        [DllImport("__Internal")] static extern double Downbeat_Audio_Schedule(int sampleIndex, double when, double rate, double gain);
        [DllImport("__Internal")] static extern void Downbeat_Audio_LogFlash(double when, double heardNow);
        [DllImport("__Internal")] static extern int Downbeat_Audio_Stat(int which);

        int _expectedSamples;
        bool _clockAdvancing;

        public bool IsRunning => SamplesReady && _clockAdvancing;
        public bool SamplesReady { get; private set; }
        public int SamplesFailed { get; private set; }
        public double Now { get; private set; }
        public double HeardNow { get; private set; }

        public AudioStats Stats => new AudioStats(
            Downbeat_Audio_Stat(StatScheduled), Downbeat_Audio_Stat(StatLate), Downbeat_Audio_Stat(StatStolen), Downbeat_Audio_Stat(StatMissingSample));

        public void Configure(float masterGainLinear, int maxVoices) => Downbeat_Audio_Configure(masterGainLinear, maxVoices);

        public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples)
        {
            _expectedSamples = samples.Count;
            for (var i = 0; i < samples.Count; i++) Downbeat_Audio_LoadSample(samples[i].Index, samples[i].Url);
        }

        public void Tick()
        {
            Now = Downbeat_Audio_Now();
            HeardNow = Downbeat_Audio_HeardNow();
            _clockAdvancing = (Downbeat_Audio_State() & StateClockAdvancing) != 0;
            SamplesFailed = Downbeat_Audio_SamplesFailed();
            SamplesReady = _expectedSamples > 0 && Downbeat_Audio_SamplesLoaded() == _expectedSamples;
        }

        public double Schedule(int sampleIndex, double when, double playbackRate, double gain) =>
            Downbeat_Audio_Schedule(sampleIndex, when, playbackRate, gain);

        public void LogFlash(double when, double heardNow) => Downbeat_Audio_LogFlash(when, heardNow);
    }
}
#endif
