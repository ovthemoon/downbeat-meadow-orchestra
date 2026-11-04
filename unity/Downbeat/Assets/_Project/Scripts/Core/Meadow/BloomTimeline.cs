using System;

namespace Downbeat.Core
{
    /// <summary>How a bloom runs (content <c>meadow.flowers.bloom</c>): it starts <see cref="LeadSec"/> before the note is heard, opens in <see cref="OpenSec"/>, stays open for <see cref="HoldSec"/>, then closes over <see cref="CloseSec"/> to <see cref="RestLevel"/> (a half-closed bud).</summary>
    public readonly struct BloomSettings
    {
        public float LeadSec { get; }
        public float OpenSec { get; }
        public float HoldSec { get; }
        public float CloseSec { get; }
        public float RestLevel { get; }

        public BloomSettings(float leadSec, float openSec, float holdSec, float closeSec, float restLevel)
        {
            if (leadSec < 0f || leadSec > 1f) throw new ArgumentOutOfRangeException(nameof(leadSec));
            if (!(openSec > 0f)) throw new ArgumentOutOfRangeException(nameof(openSec));
            if (holdSec < 0f) throw new ArgumentOutOfRangeException(nameof(holdSec));
            if (!(closeSec > 0f)) throw new ArgumentOutOfRangeException(nameof(closeSec));
            if (restLevel < 0f || restLevel > 1f) throw new ArgumentOutOfRangeException(nameof(restLevel));
            LeadSec = leadSec; OpenSec = openSec; HoldSec = holdSec; CloseSec = closeSec; RestLevel = restLevel;
        }

        public BloomSettings(BloomDto dto) : this(dto.leadSec, dto.openSec, dto.holdSec, dto.closeSec, dto.restLevel) { }

        public float TotalSec => LeadSec + OpenSec + HoldSec + CloseSec;
    }

    /// <summary>0 = a closed bud, 1 = fully open, as a function of time for one flower.</summary>
    public static class BloomTimeline
    {
        /// <summary>The bloom of a flower that was a bud, for a note heard at <paramref name="audibleTime"/>.</summary>
        public static float Value(double now, double audibleTime, in BloomSettings settings) => Value(now, audibleTime, settings, 0f);

        /// <summary>
        /// The bloom for a note heard at <paramref name="audibleTime"/> on a flower that was at <paramref name="fromLevel"/> when the note was scheduled (a flower played
        /// again while it is still open or closing rises from where it is). The opening is quick at first (a pop): half open about 25 ms after the sound.
        /// </summary>
        public static float Value(double now, double audibleTime, in BloomSettings settings, float fromLevel)
        {
            var t = now - (audibleTime - settings.LeadSec);
            if (t <= 0.0) return fromLevel;
            if (t < settings.OpenSec)
            {
                var u = (float)(t / settings.OpenSec);
                var ease = 1f - (1f - u) * (1f - u) * (1f - u);
                return fromLevel + (1f - fromLevel) * ease;
            }
            t -= settings.OpenSec;
            if (t < settings.HoldSec) return 1f;
            t -= settings.HoldSec;
            if (t >= settings.CloseSec) return settings.RestLevel;
            var c = (float)(t / settings.CloseSec);
            var smooth = c * c * (3f - 2f * c);
            return 1f + (settings.RestLevel - 1f) * smooth;
        }

        /// <summary>True when the bloom of a note heard at <paramref name="audibleTime"/> has run its whole course and the value is the rest level for good.</summary>
        public static bool IsSettled(double now, double audibleTime, in BloomSettings settings) => now >= audibleTime - settings.LeadSec + settings.OpenSec + settings.HoldSec + settings.CloseSec;
    }
}

namespace Downbeat.Core
{
    /// <summary>
    /// The bloom of one flower as a run: its timeline (<see cref="BloomTimeline"/>) starting from the level the flower had, and, when the flower was struck again
    /// while an earlier bloom was still running, that earlier bloom, which goes on until the new one starts (a flower never stands still between two notes).
    /// The new bloom starts from the level the old one will have reached by then. Only one earlier bloom is remembered: a flower struck twice within the lead time
    /// of a bloom (50-80 ms, less than a 1/16 slot at 96 BPM) is not a case this has to be exact for.
    /// </summary>
    public readonly struct BloomRun
    {
        readonly double _previousAudible;
        readonly float _previousFrom;
        readonly bool _hasPrevious;

        BloomRun(double audibleTime, float fromLevel, double previousAudible, float previousFrom, bool hasPrevious)
        {
            AudibleTime = audibleTime; FromLevel = fromLevel; _previousAudible = previousAudible; _previousFrom = previousFrom; _hasPrevious = hasPrevious;
        }

        public double AudibleTime { get; }
        public float FromLevel { get; }

        /// <summary>This run came after another one (the flower was struck again while it was blooming).</summary>
        public bool HasEarlier => _hasPrevious;

        /// <summary>The run before this one: what the flower did until a note that is not heard (its sound was cancelled) asked for this one. Only when <see cref="HasEarlier"/>.</summary>
        public BloomRun Earlier() => new BloomRun(_previousAudible, _previousFrom, 0.0, 0f, false);

        /// <summary>The first bloom of a flower that is at <paramref name="fromLevel"/> (0 a bud, or the rest level of a flower that bloomed before).</summary>
        public static BloomRun First(double audibleTime, float fromLevel) => new BloomRun(audibleTime, fromLevel, 0.0, 0f, false);

        /// <summary>The run after a new note for the same flower, heard at <paramref name="newAudibleTime"/>.</summary>
        public BloomRun Retrigger(double newAudibleTime, in BloomSettings settings)
        {
            var from = Value(newAudibleTime - settings.LeadSec, settings);
            return new BloomRun(newAudibleTime, from, AudibleTime, FromLevel, true);
        }

        public float Value(double now, in BloomSettings settings)
        {
            if (_hasPrevious && now < AudibleTime - settings.LeadSec) return BloomTimeline.Value(now, _previousAudible, settings, _previousFrom);
            return BloomTimeline.Value(now, AudibleTime, settings, FromLevel);
        }

        public bool IsSettled(double now, in BloomSettings settings) => BloomTimeline.IsSettled(now, AudibleTime, settings);
    }
}
