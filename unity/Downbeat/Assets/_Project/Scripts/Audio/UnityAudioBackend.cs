using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Downbeat.Audio
{
    /// <summary>
    /// The real audio system: one AudioSource per voice, samples read from file:// URLs with UnityWebRequestMultimedia, and the
    /// DSP clock of <see cref="AudioSettings"/>. Owns an AudioListener when the scene has none (nothing is heard without one).
    /// </summary>
    public sealed class UnityAudioBackend : IAudioBackend
    {
        const string HostName = "NativeAudioEngine";
        const string VoicesName = "Voices";
        const double FadeStopMarginSec = 0.03; // the fade runs in the audio thread; the source is stopped a little after it has certainly finished

        readonly List<UnityWebRequest> _requests = new List<UnityWebRequest>();
        readonly List<int> _requestIndexes = new List<int>();
        AudioClip[] _clips = new AudioClip[0];
        AudioSource[] _voices = new AudioSource[0];
        VoiceFader[] _faders = new VoiceFader[0];
        double[] _fadeStopAt = new double[0];
        double _outputLatencySec;

        public double DspTime => AudioSettings.dspTime;
        public double OutputLatencySec => _outputLatencySec;
        public int LoadedCount { get; private set; }
        public int FailedCount { get; private set; }
        /// <summary>The root object (it carries the AudioListener when this backend created one). The voices are on a child object: a filter such as
        /// AudioOutputTap on the listener's object must not also be inserted into every voice.</summary>
        public GameObject Host { get; private set; }

        public void CreateVoices(int count)
        {
            Host = new GameObject(HostName);
            Object.DontDestroyOnLoad(Host);
            if (Object.FindFirstObjectByType<AudioListener>() == null) Host.AddComponent<AudioListener>();

            var voiceHost = new GameObject(VoicesName);
            voiceHost.transform.SetParent(Host.transform, false);
            _voices = new AudioSource[count];
            _faders = new VoiceFader[count];
            _fadeStopAt = new double[count];
            for (var i = 0; i < count; i++)
            {
                // one object per voice: the fader filters the audio of its own source only
                var voiceObject = new GameObject("Voice" + i);
                voiceObject.transform.SetParent(voiceHost.transform, false);
                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _voices[i] = source;
                _faders[i] = voiceObject.AddComponent<VoiceFader>();
                _fadeStopAt[i] = double.PositiveInfinity;
            }

            AudioSettings.GetDSPBufferSize(out var bufferLength, out var bufferCount);
            _outputLatencySec = (double)bufferLength * bufferCount / AudioSettings.outputSampleRate;
        }

        public void BeginLoading(IReadOnlyList<SampleLoadRequest> samples)
        {
            _clips = new AudioClip[samples.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                var request = UnityWebRequestMultimedia.GetAudioClip(samples[i].Url, AudioType.WAV);
                request.SendWebRequest();
                _requests.Add(request);
                _requestIndexes.Add(samples[i].Index);
            }
        }

        public void Poll()
        {
            var dsp = AudioSettings.dspTime;
            for (var i = 0; i < _fadeStopAt.Length; i++)
            {
                if (dsp < _fadeStopAt[i]) continue;
                _voices[i].Stop(); // the fade is over: the source is silent, free it for the next note
                _faders[i].Reset();
                _fadeStopAt[i] = double.PositiveInfinity;
            }

            for (var i = _requests.Count - 1; i >= 0; i--)
            {
                var request = _requests[i];
                if (!request.isDone) continue;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    _clips[_requestIndexes[i]] = DownloadHandlerAudioClip.GetContent(request);
                    LoadedCount++;
                }
                else
                {
                    Debug.LogError($"[NativeAudioEngine] sample load failed: {request.url}: {request.error}");
                    FailedCount++;
                }
                request.Dispose();
                _requests.RemoveAt(i);
                _requestIndexes.RemoveAt(i);
            }
        }

        public bool HasSample(int index) => index >= 0 && index < _clips.Length && _clips[index] != null;

        public double SampleSeconds(int index) => _clips[index].length;

        public void Play(int voice, int sample, double startDsp, double rate, float volume)
        {
            var source = _voices[voice];
            source.Stop();
            _faders[voice].Reset();
            _fadeStopAt[voice] = double.PositiveInfinity;
            source.clip = _clips[sample];
            source.pitch = (float)rate;
            source.volume = volume;
            source.PlayScheduled(startDsp);
        }

        public double FadeReleaseMarginSec => FadeStopMarginSec;

        public void Stop(int voice) => _voices[voice].Stop();

        public void FadeOut(int voice, double seconds)
        {
            if (!_voices[voice].isPlaying)
            {
                _voices[voice].Stop(); // waiting for its start: nothing to fade, just cancel
                return;
            }
            _faders[voice].BeginFade((int)(seconds * AudioSettings.outputSampleRate));
            _fadeStopAt[voice] = AudioSettings.dspTime + seconds + FadeStopMarginSec;
        }
    }
}
