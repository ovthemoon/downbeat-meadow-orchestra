using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>
    /// What the hand input needs for conducting play: the settings (from the content, or from <c>--play-mode</c>), the <see cref="ConductEngine"/> and the audio clock. The app configures it
    /// when the content is loaded; <c>HandPlayInput</c> asks it for notes every frame instead of running the strike interpreter when <see cref="IsConducting"/>.
    /// </summary>
    public sealed class ConductPlayer
    {
        readonly Func<double> _audioNow;
        readonly ConductEngine _engine = new ConductEngine();

        public ConductPlayer(Func<double> audioNow)
        {
            _audioNow = audioNow ?? throw new ArgumentNullException(nameof(audioNow));
        }

        Func<bool> _mayPlay;
        SongPitch _songPitch;

        public ConductSettings Settings { get; private set; } = ConductSettings.Default;

        /// <summary>Slots in a bar of the beat grid the player was configured for; 0 before <see cref="Configure"/>. A bar of the grid is the slot divided by this (<see cref="StartSongAfter"/> gives a slot).</summary>
        public int SlotsPerBar { get; private set; }

        /// <summary>
        /// The hands play nothing while the gate says no: neither notes (<see cref="Update"/>) nor strikes (<c>HandPlayInput</c> asks). The app hands it the state of the visit
        /// (<c>!ExperienceStateMachine.AwaitsOpenHand</c>), so the sound and the meadow agree about when a new piece may begin. Null (the default): always open.
        /// </summary>
        public void SetPlayGate(Func<bool> mayPlay) => _mayPlay = mayPlay;

        /// <summary>The hands may play now (the gate is open or there is none).</summary>
        public bool MayPlay => _mayPlay == null || _mayPlay();

        /// <summary>True once configured with the conduct style. Before that (and in strike mode) the hand plays by striking.</summary>
        public bool IsConducting => _engine.IsConfigured && Settings.Style == PlayStyle.Conduct;

        /// <param name="modeOverride"><c>--play-mode</c>: "conduct", "strike", or null for what the content says.</param>
        /// <returns>A problem to log (an unknown mode name), or null.</returns>
        public string Configure(ContentDto content, string modeOverride)
        {
            Settings = ConductSettings.From(content.conduct, modeOverride, out var problem);
            var song = content.song;
            var hand = new HandInterpreterSettings(content.hand);
            var slotsPerBar = song.beatsPerBar * song.slotsPerBeat;
            _engine.Configure(Settings, song.slotsPerBeat, slotsPerBar, MinGapSlots(song), content.audio.minLeadSec, hand.PartHysteresis, hand.MaxGapMs, CellSettings.From(content.cells));
            _songPitch = SongPitch.From(content);   // the song itself is chosen per visit (PrepareSong); configuring never starts one
            SlotsPerBar = slotsPerBar;
            return problem;
        }

        /// <summary>The fewest slots between two notes of each part, in the order of <see cref="Part"/>.</summary>
        public static int[] MinGapSlots(SongDto song)
        {
            var gaps = new int[Parts.Count];
            for (var i = 0; i < gaps.Length; i++) gaps[i] = 1;
            if (song.partMinGapSlots == null) return gaps;
            foreach (var entry in song.partMinGapSlots)
            {
                if (Parts.TryParse(entry.part, out var part)) gaps[(int)part] = Math.Max(1, entry.slots);
            }
            return gaps;
        }

        public void OnGridReady(in BeatGrid grid) => _engine.OnGridReady(grid);

        /// <summary>A song is prepared (it plays from the bar line <see cref="StartSongAfter"/> picks, until <see cref="ClearSong"/>).</summary>
        public bool HasSong => _engine.HasSong;

        /// <summary>The bar line at which the started song is over; <see cref="long.MaxValue"/> while there is none.</summary>
        public long SongEndSlot => _engine.SongEndSlot;

        /// <summary>Time on the audio clock of a slot of the beat grid.</summary>
        public double SlotTime(long slot) => _engine.SlotTime(slot);

        /// <summary>Takes the song to play on the next <see cref="StartSongAfter"/>; the notes of the hands stay free play until then, except that the lower row already plays the chord of the first bar.</summary>
        public void PrepareSong(SongScore score)
        {
            if (_songPitch == null) throw new InvalidOperationException("configure the player with the content before preparing a song");
            _engine.PrepareSong(score, _songPitch);
        }

        /// <summary>
        /// Starts the prepared song on the first bar line strictly after what the engine has already scanned and after the slot of <paramref name="audioNowSec"/>, and returns that slot.
        /// Call it when a visit begins, whether the engine has made this frame's notes yet or not.
        /// </summary>
        public long StartSongAfter(double audioNowSec)
        {
            var startSlot = _engine.FirstBarLineAfter(audioNowSec);
            _engine.StartSong(startSlot);
            return startSlot;
        }

        /// <summary>Back to free play.</summary>
        public void ClearSong() => _engine.ClearSong();

        public void Update(IHandMotionView hands, float aspect, List<NoteIntent> output)
        {
            if (!MayPlay) return;   // the engine was reset when the gate closed (the piece ended): its hands and voices begin again when it opens
            _engine.Update(_audioNow(), hands, aspect, output);
        }

        public void Reset() => _engine.Reset();
    }
}
