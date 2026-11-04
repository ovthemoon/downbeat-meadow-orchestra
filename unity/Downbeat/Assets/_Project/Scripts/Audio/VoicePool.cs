namespace Downbeat.Audio
{
    /// <summary>
    /// Bookkeeping for a fixed set of sources ("voices"). At most <c>limit</c> sounds may be heard at the same time; the pool has a few sources more, so that
    /// a voice that has to give way can fade out on its own source while the new note plays on another.
    /// <list type="bullet">
    /// <item>A new sound needs a source that is idle now (its previous sound is over). A source whose sound has not started yet is never idle: an AudioSource holds
    /// one scheduled start, so reusing it would cancel the note that is about to play.</item>
    /// <item>When <c>limit</c> sounds are already heard at the moment the new one starts, the oldest of them gives way: a sound that is already sounding fades
    /// out, one that has not started yet is cancelled (nothing is cut).</item>
    /// <item>Only when no source is idle at all (every one is sounding or waiting) is a source taken by force: the one that ends before the new sound starts,
    /// else the oldest; it is stopped at once.</item>
    /// </list>
    /// </summary>
    sealed class VoicePool
    {
        public readonly struct Choice
        {
            /// <summary>The source the new sound plays on.</summary>
            public int Voice { get; }
            /// <summary>A sounding source that must fade out, or -1.</summary>
            public int FadeVoice { get; }
            /// <summary>A source to stop (cancel) before use, or -1.</summary>
            public int StopVoice { get; }
            /// <summary>A sound was cut or cancelled to make room.</summary>
            public bool Stolen { get; }

            public Choice(int voice, int fadeVoice, int stopVoice, bool stolen)
            {
                Voice = voice; FadeVoice = fadeVoice; StopVoice = stopVoice; Stolen = stolen;
            }
        }

        readonly double[] _start;
        readonly double[] _end;
        readonly int _limit;

        public VoicePool(int count, int limit)
        {
            _start = new double[count];
            _end = new double[count];
            _limit = limit;
            for (var i = 0; i < count; i++)
            {
                _start[i] = double.NegativeInfinity;
                _end[i] = double.NegativeInfinity;
            }
        }

        public int Count => _start.Length;

        /// <summary>What <see cref="Silence"/> found a voice doing.</summary>
        public enum Silenced
        {
            /// <summary>It had nothing to play any more.</summary>
            Nothing,
            /// <summary>It was waiting for its start: it is free at once and the caller stops (cancels) its source.</summary>
            Cancelled,
            /// <summary>It was sounding: it is taken for <c>releaseSec</c> and the caller fades its source out.</summary>
            Fading,
        }

        /// <summary>Ends what one voice does or is going to do (a piece that stops): the same bookkeeping as for a voice that gives way in <see cref="Acquire"/>.</summary>
        public Silenced Silence(int voice, double now, double releaseSec)
        {
            if (_end[voice] <= now) return Silenced.Nothing;
            if (_start[voice] > now)
            {
                _start[voice] = double.NegativeInfinity;
                _end[voice] = now;
                return Silenced.Cancelled;
            }
            _end[voice] = now + releaseSec;
            return Silenced.Fading;
        }

        /// <summary>
        /// Reserves a source for a sound that plays from <paramref name="start"/> to <paramref name="end"/> (audio seconds); <paramref name="now"/> is the audio clock
        /// at this moment and <paramref name="releaseSec"/> how long a voice that has to give way stays taken: its fade plus the margin the backend keeps it before it stops the source.
        /// </summary>
        public Choice Acquire(double now, double start, double end, double releaseSec)
        {
            var sounding = 0;
            var oldest = -1;
            for (var i = 0; i < _start.Length; i++)
            {
                if (_start[i] > start || _end[i] <= start) continue; // not heard when the new sound starts
                sounding++;
                if (oldest < 0 || _start[i] < _start[oldest]) oldest = i;
            }

            var fade = -1;
            var stop = -1;
            var stolen = false;
            if (sounding >= _limit && oldest >= 0)
            {
                stolen = true;
                if (_start[oldest] > now)
                {
                    stop = oldest; // still waiting for its start: cancel it, it is free at once
                    _start[oldest] = double.NegativeInfinity;
                    _end[oldest] = now;
                }
                else
                {
                    fade = oldest; // sounding: it fades, and is free again when the backend has stopped its source (fade + margin), not before: a note played on it earlier would cut the fade
                    _end[oldest] = now + releaseSec;
                }
            }

            var target = -1;
            for (var i = 0; i < _end.Length; i++)
            {
                if (_end[i] <= now && (target < 0 || _end[i] < _end[target])) target = i; // idle the longest
            }

            if (target < 0)
            {
                for (var i = 0; i < _end.Length; i++)
                {
                    if (_end[i] <= start && (target < 0 || _end[i] < _end[target])) target = i;
                }
                if (target < 0)
                {
                    target = 0;
                    for (var i = 1; i < _start.Length; i++)
                    {
                        if (_start[i] < _start[target]) target = i;
                    }
                }
                stolen = true;
                stop = target;
                if (fade == target) fade = -1;
            }

            _start[target] = start;
            _end[target] = end;
            return new Choice(target, fade, stop, stolen);
        }
    }
}
