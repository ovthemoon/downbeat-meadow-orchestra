using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// Which song a visit plays, where it is, and when it is over. The songs go round in the order of the book, one for each visit that makes a card: a visit that ends with fewer than <c>minNotes</c>
    /// notes (a passer-by) leaves the cursor where it was. When a visit begins the session prepares the song and starts it on the first bar line after that moment (<see cref="ConductPlayer.StartSongAfter"/>);
    /// from then on the clock of the song is the beat grid. When the last bar is over (<see cref="Tick"/>) it ends the piece through <see cref="CutoffDirector.EndPiece"/>, the way two fists do, so the card,
    /// the fade and the word of the end follow one path.
    /// The switch (<see cref="Enabled"/>), a book without songs and the strike style all mean free play: the player is told <see cref="ConductPlayer.ClearSong"/> and nothing else changes.
    /// The app tells the session about the visit (<see cref="OnSessionStarted"/>, <see cref="OnSessionEnded"/>), every note that is played (<see cref="OnNote"/>) and every frame (<see cref="Tick"/>).
    /// </summary>
    public sealed class SongSession
    {
        readonly SongScore[] _songs;
        readonly ConductPlayer _conduct;
        readonly CutoffDirector _ending;
        readonly ScaleSpec _scale;
        readonly int _scaleRootMidi;
        readonly int _minNotes;
        bool _enabled;
        int _cursor;
        bool _visitOpen;
        int _visitNotes;
        SongScore _visitSong;
        bool _playing;
        double _endAt;
        long _firstBar;
        int[][] _skyStepsByBar;

        /// <param name="songs">The songs in the order they are played (<see cref="SongBook"/>); the list is copied.</param>
        /// <param name="ending">Ends the piece at the end of the last bar.</param>
        /// <param name="scale">The scale the sky plays in, and <paramref name="scaleRootMidi"/> the MIDI note of its step 0: the notes of a bar's chord that are degrees of it are the notes the sky plays.</param>
        /// <param name="minNotes">How many notes make a card (<c>record.minNotes</c>); the session counts the notes itself, so the order of the songs does not depend on the recorder being there.</param>
        /// <param name="enabled">The song switch at the start.</param>
        public SongSession(IReadOnlyList<SongScore> songs, ConductPlayer conduct, CutoffDirector ending, ScaleSpec scale, int scaleRootMidi, int minNotes, bool enabled)
        {
            if (songs == null) throw new ArgumentNullException(nameof(songs));
            _conduct = conduct ?? throw new ArgumentNullException(nameof(conduct));
            _ending = ending ?? throw new ArgumentNullException(nameof(ending));
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            if (minNotes < 1) throw new ArgumentOutOfRangeException(nameof(minNotes), minNotes, "must be at least 1");

            _songs = new SongScore[songs.Count];
            for (var i = 0; i < _songs.Length; i++) _songs[i] = songs[i] ?? throw new ArgumentException($"song {i} is missing", nameof(songs));
            _scaleRootMidi = scaleRootMidi;
            _minNotes = minNotes;
            _enabled = enabled;
            PrepareNextVisit();
        }

        /// <summary>The song switch: songs are played when it is on (and there are songs, and the hands conduct).</summary>
        public bool Enabled => _enabled;

        /// <summary>The song of the visit that is going on, or of the last one when none is; null when that visit was free play (and before the first visit).</summary>
        public SongScore VisitSong => _visitSong;

        /// <summary>The song of <see cref="VisitSong"/> as the card names it; <c>default</c> (no song) for free play. Never throws.</summary>
        public SongLabel VisitLabel => _visitSong == null ? default : new SongLabel(_visitSong.Id, _visitSong.Title);

        /// <summary>A song began: the score and the audio time of its bar 1.</summary>
        public event Action<SongScore, double> SongStarted;

        /// <summary>
        /// Turns songs on or off. A visit that is going on keeps what it has (a song in play is played to its end); the next visit follows the switch. When nobody is there the player is told at once,
        /// so the lower row of the next visit already plays the chord of its first bar.
        /// </summary>
        public void SetEnabled(bool on)
        {
            _enabled = on;
            if (!_visitOpen) PrepareNextVisit();
        }

        /// <summary>A visit began at this audio time: takes the song at the cursor and starts it on the next bar line, or leaves the visit free play.</summary>
        public void OnSessionStarted(double now)
        {
            _visitOpen = true;
            _visitNotes = 0;
            _playing = false;
            _skyStepsByBar = null;
            if (!CanPlaySongs)
            {
                _visitSong = null;
                _conduct.ClearSong();
                return;
            }

            var song = _songs[_cursor];
            _visitSong = song;
            _conduct.PrepareSong(song);
            var startSlot = _conduct.StartSongAfter(now);
            _firstBar = startSlot / _conduct.SlotsPerBar;
            _endAt = _conduct.SlotTime(_conduct.SongEndSlot);
            _skyStepsByBar = SkyStepsOf(song);
            _playing = true;
            SongStarted?.Invoke(song, _conduct.SlotTime(startSlot));
        }

        /// <summary>
        /// The visit is over, however it ended (the hands left, two fists, the end of the song; the last also reaches here from inside <see cref="Tick"/>). The cursor moves on when the visit played a song and
        /// made enough notes for a card; <see cref="VisitSong"/> stays the song of the visit that ended, for whoever asks for the card's song after this.
        /// </summary>
        public void OnSessionEnded(double now)
        {
            if (!_visitOpen) return;

            _visitOpen = false;
            _playing = false;
            _skyStepsByBar = null;
            if (_visitSong != null && _visitNotes >= _minNotes) _cursor = (_cursor + 1) % _songs.Length;
            PrepareNextVisit();
        }

        /// <summary>A note was played (every note that reaches the scheduler, the mouse's too); the notes of a visit decide whether it uses up its song.</summary>
        public void OnNote()
        {
            if (_visitOpen && _visitNotes < _minNotes) _visitNotes++;
        }

        /// <summary>
        /// Call once a frame, after the cutoff director looked at the hands. At the first frame at or after the end of the last bar it ends the piece (<see cref="PieceEndReason.SongEnd"/>).
        /// </summary>
        /// <returns>True in the frame in which it ended the piece: the notes of that frame must not be played. False when there is nothing to end, and when the hands ended the piece in this very frame.</returns>
        public bool Tick(double now)
        {
            if (!_playing || now < _endAt) return false;

            _playing = false;   // ending the piece ends the visit, which comes back to OnSessionEnded before EndPiece returns
            return _ending.EndPiece(now, PieceEndReason.SongEnd);
        }

        /// <summary>
        /// The scale steps of the chord the sky plays on the bar line of an absolute bar of the grid (bars are counted from slot 0): the chord of the bar of the song, the first bar's before the song begins,
        /// the last bar's from the end on. Null when no song is playing (the sky then plays its own chords); empty for a bar whose chord has no note in the scale (the conductor takes that for "no word" as well). Never throws.
        /// </summary>
        public int[] SkyStepsForBar(long absoluteBar)
        {
            var byBar = _skyStepsByBar;
            if (byBar == null) return null;

            if (absoluteBar <= _firstBar) return (int[])byBar[0].Clone();   // compared before subtracting: a bar far before the song must not wrap around

            var offset = absoluteBar - _firstBar;
            return (int[])byBar[offset >= byBar.Length ? byBar.Length - 1 : (int)offset].Clone();
        }

        bool CanPlaySongs => _enabled && _songs.Length > 0 && _conduct.IsConducting;

        /// <summary>Makes the player ready for a visit that has not begun: the song at the cursor (its first bar's chord), or free play.</summary>
        void PrepareNextVisit()
        {
            if (CanPlaySongs) _conduct.PrepareSong(_songs[_cursor]);
            else _conduct.ClearSong();
        }

        int[][] SkyStepsOf(SongScore song)
        {
            var steps = new int[song.BarCount][];
            for (var i = 0; i < steps.Length; i++) steps[i] = SongChords.SkyStepsFor(song.Bars[i].Chord, _scale, _scaleRootMidi);
            return steps;
        }
    }
}
