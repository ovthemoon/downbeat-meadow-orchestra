using System;
using System.Threading;
using UnityEngine;

namespace Downbeat.Audio
{
    /// <summary>
    /// Records what Unity's mixer outputs, for the beat-clock measurement. Put it on the object that has the AudioListener: Unity then hands
    /// the final mix to <see cref="OnAudioFilterRead"/>. The first channel is copied into a pre-allocated buffer (nothing is allocated on
    /// the audio thread), and the output can be silenced after the copy so a measurement run makes no noise. Not used during normal play.
    /// </summary>
    public sealed class AudioOutputTap : MonoBehaviour
    {
        const int Recording = 1;
        const int Idle = 0;
        const double GapToleranceBlocks = 1.5;

        float[] _mono = new float[0];
        int _state = Idle;
        int _frames;
        long _firstFrame = -1;
        long _firstBlockUtcTicks = -1;
        float _peak;
        int _gaps;
        int _sampleRate;
        volatile bool _muteOutput;

        /// <summary>Zero the output after copying it (also while not recording).</summary>
        public bool MuteOutput { get => _muteOutput; set => _muteOutput = value; }
        public int SampleRate => _sampleRate;
        public int FramesRecorded => Volatile.Read(ref _frames);
        public float Peak => _peak;
        /// <summary>Times the mixer's clock jumped away from the recorded sample count by more than a block and a half.</summary>
        public int Gaps => Volatile.Read(ref _gaps);
        /// <summary>The DSP clock (in frames) of the first recorded frame; -1 until the first block arrives.</summary>
        public long FirstFrame => Volatile.Read(ref _firstFrame);
        /// <summary>The wall clock (UTC ticks) at which the first recorded block was mixed, that is of the first recorded frame; -1 until the first block arrives. It is when the sound was made, not heard: the output buffer adds a few tens of milliseconds.</summary>
        public long FirstBlockUtcTicks => Volatile.Read(ref _firstBlockUtcTicks);
        /// <summary>Recorded mono samples; only the first <see cref="FramesRecorded"/> are valid.</summary>
        public float[] Samples => _mono;

        public void Begin(int capacityFrames)
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _mono = new float[capacityFrames];
            _peak = 0f;
            Volatile.Write(ref _gaps, 0);
            Volatile.Write(ref _frames, 0);
            Volatile.Write(ref _firstFrame, -1);
            Volatile.Write(ref _firstBlockUtcTicks, -1);
            Volatile.Write(ref _state, Recording);
        }

        /// <summary>Stops recording and returns the number of frames captured.</summary>
        public int End()
        {
            Volatile.Write(ref _state, Idle);
            return Volatile.Read(ref _frames);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (Volatile.Read(ref _state) == Recording && channels > 0) Copy(data, channels);
            if (_muteOutput) Array.Clear(data, 0, data.Length);
        }

        void Copy(float[] data, int channels)
        {
            var blockFrames = data.Length / channels;
            var written = Volatile.Read(ref _frames);
            var dspFrame = (long)Math.Round(AudioSettings.dspTime * _sampleRate);
            if (Volatile.Read(ref _firstFrame) < 0)
            {
                Volatile.Write(ref _firstBlockUtcTicks, DateTime.UtcNow.Ticks);
                Volatile.Write(ref _firstFrame, dspFrame);
            }
            else if (Math.Abs(dspFrame - (_firstFrame + written)) > blockFrames * GapToleranceBlocks) Interlocked.Increment(ref _gaps);

            var room = Math.Min(blockFrames, _mono.Length - written);
            for (var i = 0; i < room; i++)
            {
                var v = data[i * channels];
                _mono[written + i] = v;
                var a = v < 0f ? -v : v;
                if (a > _peak) _peak = a;
            }
            Volatile.Write(ref _frames, written + room);
        }
    }
}
