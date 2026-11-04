using UnityEngine;

namespace Downbeat.Audio
{
    /// <summary>
    /// A short linear fade-out of the one AudioSource on the same object, done on the audio thread sample by sample, so that a voice that has to give way
    /// does not stop with a click. <see cref="BeginFade"/> and <see cref="Reset"/> are called from the main thread. The state is changed under a lock and carries a
    /// generation number: the audio thread works on a copy for the length of a buffer and writes its result back only if no Reset or BeginFade came in meanwhile,
    /// so the next note on a reused source never inherits (or loses) the state of the fade before it.
    /// </summary>
    public sealed class VoiceFader : MonoBehaviour
    {
        readonly object _gate = new object();
        int _totalSamples;
        int _remainingSamples;   // 0 = not fading
        bool _fadeDone;          // true once the gain has reached zero
        int _generation;

        public void BeginFade(int samples)
        {
            lock (_gate)
            {
                _generation++;
                _fadeDone = false;
                _totalSamples = samples < 1 ? 1 : samples;
                _remainingSamples = _totalSamples;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _generation++;
                _remainingSamples = 0;
                _fadeDone = false;
            }
        }

        void OnAudioFilterRead(float[] data, int channels) => Process(data, channels);

        /// <summary>The audio thread's work on one buffer of interleaved samples; public so that a test can run it by hand.</summary>
        public void Process(float[] data, int channels) => Process(data, channels, null);

        /// <summary><paramref name="midCallback"/> runs after the samples are done and before the state is written back (a test starts the next note there).</summary>
        public void Process(float[] data, int channels, System.Action midCallback)
        {
            int generation, remaining, total;
            bool done;
            lock (_gate)
            {
                generation = _generation;
                remaining = _remainingSamples;
                total = _totalSamples;
                done = _fadeDone;
            }

            if (done)
            {
                System.Array.Clear(data, 0, data.Length); // faded out and still playing until the main thread stops it
                midCallback?.Invoke();
                return;
            }
            if (remaining <= 0)
            {
                midCallback?.Invoke();
                return;
            }

            var frames = data.Length / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var gain = remaining > 0 ? (float)remaining / total : 0f;
                for (var channel = 0; channel < channels; channel++) data[frame * channels + channel] *= gain;
                if (remaining > 0) remaining--;
            }

            midCallback?.Invoke();
            lock (_gate)
            {
                if (_generation != generation) return; // the main thread started the next note while this buffer was being made: its state stands
                _remainingSamples = remaining;
                if (remaining == 0) _fadeDone = true;
            }
        }
    }
}
