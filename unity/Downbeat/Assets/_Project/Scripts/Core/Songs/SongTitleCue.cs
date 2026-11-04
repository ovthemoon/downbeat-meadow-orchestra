using System;

namespace Downbeat.Core
{
    /// <summary>
    /// When the title of a song is to be shown: the app arms the cue when a song is started with the audio time of its first bar, and takes the title once the clock reaches that bar (the visit begins up to a bar
    /// before it, and the title belongs to the music, not to the hand coming in). A visit that ends before that cancels it. Also keeps the time the content asks for inside a sane range.
    /// </summary>
    public sealed class SongTitleCue
    {
        /// <summary>The shortest time a title is shown, in seconds: less is not read.</summary>
        public const double MinDurationSec = 0.5;
        /// <summary>The longest time a title is shown: more is a sign on the wall.</summary>
        public const double MaxDurationSec = 10.0;
        /// <summary>The time used when the content gives no number (<c>experience.songTitleSec</c> as shipped).</summary>
        public const double DefaultDurationSec = 3.0;

        string _title;
        double _audioTime;

        /// <summary>A title is waiting for its bar.</summary>
        public bool IsArmed => _title != null;

        /// <summary>Waits for <paramref name="audioTimeOfBarOne"/> to show <paramref name="title"/>; replaces a cue that was waiting. A title that is null or blank arms nothing.</summary>
        public void Arm(string title, double audioTimeOfBarOne)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                Cancel();
                return;
            }
            _title = title;
            _audioTime = audioTimeOfBarOne;
        }

        /// <summary>The song will not be heard (the visit ended before its first bar): the title is not shown.</summary>
        public void Cancel() => _title = null;

        /// <summary>True once, in the first call at or after the time of the first bar; <paramref name="title"/> is then the text to show.</summary>
        public bool TryTake(double audioNow, out string title)
        {
            title = null;
            if (_title == null || audioNow < _audioTime) return false;

            title = _title;
            _title = null;
            return true;
        }

        /// <summary>How long a title is shown: the content value kept between <see cref="MinDurationSec"/> and <see cref="MaxDurationSec"/>; no number (NaN) means <see cref="DefaultDurationSec"/>.</summary>
        public static double DurationSec(float configured)
        {
            if (float.IsNaN(configured)) return DefaultDurationSec;
            return Math.Max(MinDurationSec, Math.Min(MaxDurationSec, configured));
        }
    }
}
