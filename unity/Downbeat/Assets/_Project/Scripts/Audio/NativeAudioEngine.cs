using System;
using System.Collections.Generic;
using Downbeat.Core;

namespace Downbeat.Audio
{
    /// <summary>
    /// Sound for the native players: every note is handed to Unity's audio system as a scheduled start on the DSP clock
    /// (<c>AudioSource.PlayScheduled</c>), so its timing does not depend on the frame rate. This class holds the policy
    /// (what counts as late, which voice gives way when too many would sound together); <see cref="IAudioBackend"/> holds Unity.
    /// </summary>
    public sealed class NativeAudioEngine : IAudioEngine
    {
        /// <summary>The audio system needs this much notice to start a sound on time; a request closer than this is late.</summary>
        const double MinLeadSec = 0.005;
        const double ClockStartedAfterSec = 0.2;
        const int DefaultVoices = 32;
        /// <summary>Extra sources on top of <c>maxVoices</c>: when a sounding voice is cut it fades out on its own source while the new note plays on a spare.</summary>
        public const int SpareVoices = 4;
        const double MinPlaybackRate = 0.01;
        /// <summary>A voice that has to give way fades out over this long: short enough not to be heard as a gap, long enough not to click.</summary>
        const double StealFadeSec = 0.008;

        readonly IAudioBackend _backend;
        VoicePool _pool;
        /// <summary>For every voice: the sound it was last given belongs to the nature layers (<see cref="IAudioEngine.FadeOutAll"/> leaves it alone).</summary>
        bool[] _voiceIsNature = new bool[0];
        readonly HashSet<int> _natureSamples = new HashSet<int>();
        float _masterGain = 1f;
        int _expectedSamples;
        int _scheduled;
        int _late;
        int _stolen;
        int _missing;

        readonly SmoothClock _smoothClock = new SmoothClock();
        readonly Func<double> _wallClock;

        /// <param name="wallClock">Seconds of a clock that moves every frame (default: the real time since start-up); only the smoothed visual clock uses it.</param>
        public NativeAudioEngine(IAudioBackend backend, Func<double> wallClock = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _wallClock = wallClock ?? (() => UnityEngine.Time.realtimeSinceStartupAsDouble);
        }

        public bool SamplesReady { get; private set; }
        public int SamplesFailed { get; private set; }
        public double Now { get; private set; }
        /// <summary>The audio time of the sample leaving the speakers now: the clock minus the estimated output latency.</summary>
        public double HeardNow { get; private set; }
        /// <summary>The audio time leaving the speakers, interpolated between the audio clock's block steps (for the eyes).</summary>
        public double VisualNow { get; private set; }
        public bool IsRunning => SamplesReady && Now > ClockStartedAfterSec;
        public AudioStats Stats => new AudioStats(_scheduled, _late, _stolen, _missing);

        public void Configure(float masterGainLinear, int maxVoices)
        {
            _masterGain = masterGainLinear;
            var voices = maxVoices > 0 ? maxVoices : DefaultVoices;
            var sources = voices + SpareVoices;
            _backend.CreateVoices(sources);
            _pool = new VoicePool(sources, voices);
            _voiceIsNature = new bool[sources];
        }

        public void LoadSamples(IReadOnlyList<SampleLoadRequest> samples)
        {
            _expectedSamples = samples.Count;
            _natureSamples.Clear();
            foreach (var sample in samples)
            {
                if (sample.IsNature) _natureSamples.Add(sample.Index);
            }
            _backend.BeginLoading(samples);
        }

        public void Tick()
        {
            _backend.Poll();
            Now = _backend.DspTime;
            HeardNow = Now - _backend.OutputLatencySec;
            VisualNow = _smoothClock.Update(Now, _wallClock()) - _backend.OutputLatencySec;
            SamplesFailed = _backend.FailedCount;
            SamplesReady = _expectedSamples > 0 && _backend.LoadedCount == _expectedSamples;
        }

        public double Schedule(int sampleIndex, double when, double playbackRate, double gain)
        {
            if (_pool == null || !_backend.HasSample(sampleIndex))
            {
                _missing++;
                return when;
            }

            var start = when;
            var earliest = _backend.DspTime + MinLeadSec;
            if (start < earliest)
            {
                start = earliest;
                _late++;
            }

            var rate = Math.Max(playbackRate, MinPlaybackRate);
            var choice = _pool.Acquire(_backend.DspTime, start, start + _backend.SampleSeconds(sampleIndex) / rate, StealFadeSec + _backend.FadeReleaseMarginSec);
            if (choice.StopVoice >= 0) _backend.Stop(choice.StopVoice);
            if (choice.FadeVoice >= 0) _backend.FadeOut(choice.FadeVoice, StealFadeSec);
            if (choice.Stolen) _stolen++;
            var voice = choice.Voice;
            _voiceIsNature[voice] = _natureSamples.Contains(sampleIndex);

            _backend.Play(voice, sampleIndex, start, rate, (float)(gain * _masterGain));
            _scheduled++;
            return start;
        }

        public void FadeOutAll(double seconds)
        {
            if (_pool == null) return;
            var now = _backend.DspTime;
            var fade = Math.Max(seconds, StealFadeSec); // never shorter than the fade of a voice that gives way: a shorter one clicks
            for (var voice = 0; voice < _pool.Count; voice++)
            {
                if (_voiceIsNature[voice]) continue;
                switch (_pool.Silence(voice, now, fade + _backend.FadeReleaseMarginSec))
                {
                    case VoicePool.Silenced.Cancelled:
                        _backend.Stop(voice);
                        break;
                    case VoicePool.Silenced.Fading:
                        _backend.FadeOut(voice, fade);
                        break;
                }
            }
        }

        public void LogFlash(double when, double heardNow)
        {
            // the clock measurement records its own timeline (AudioOutputTap); nothing to do here
        }
    }
}
