using System;

namespace Downbeat.Core
{
    /// <summary>Why a piece was ended (<see cref="CutoffDirector.EndPiece"/>).</summary>
    public enum PieceEndReason
    {
        /// <summary>The player ended it with both fists.</summary>
        Hands,
        /// <summary>The last bar of a song was over.</summary>
        SongEnd,
    }

    /// <summary>
    /// The two-fists ending as the app lives it, and the one way a piece ends: every frame it asks <see cref="CutoffGesture"/> about the hands, and when the answer is yes it ends the piece all at once -
    /// the visit is over now (<see cref="ExperienceStateMachine.EndNow"/>, which makes the card), the voices of the players fade out (<see cref="IAudioEngine.FadeOutAll"/>), the conducting engine forgets
    /// its hands and voices, the performance energy drops to nothing (the nature layers fade as they do when a performance ends) and <see cref="Cut"/> is raised.
    /// A cut with no visit going on (the follow-along guide shows the gesture) does the same except that there is no card.
    /// The end of a song's last bar asks for the same through <see cref="EndPiece"/>, so the word of the end and the card follow one path whatever ended the piece.
    /// </summary>
    public sealed class CutoffDirector
    {
        readonly CutoffSettings _settings;
        readonly CutoffGesture _gesture;
        readonly ExperienceStateMachine _experience;
        readonly IAudioEngine _audio;
        readonly PerformanceEnergy _energy;
        readonly Action _resetPlayers;
        int _generation;
        double _lastEndAt = double.NaN;

        /// <param name="resetPlayers">Makes the conducting engine forget its hands and voices (<see cref="ConductPlayer.Reset"/>).</param>
        /// <param name="handHoldSec">How long a hand that is missing from the pictures goes on counting where it was last seen (<see cref="HandCellTracker.HoldSecOf"/>).</param>
        public CutoffDirector(CutoffSettings settings, ExperienceStateMachine experience, IAudioEngine audio, PerformanceEnergy energy, Action resetPlayers, double handHoldSec = CutoffGesture.DefaultHandHoldSec)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _experience = experience ?? throw new ArgumentNullException(nameof(experience));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _energy = energy ?? throw new ArgumentNullException(nameof(energy));
            _resetPlayers = resetPlayers ?? throw new ArgumentNullException(nameof(resetPlayers));
            _gesture = new CutoffGesture(settings, handHoldSec);
        }

        /// <summary>A piece was ended (with the hands or at the end of a song), at this audio time. Raised after everything else was done; <see cref="LastEndReason"/> says why.</summary>
        public event Action<double> Cut;

        /// <summary>How many times the piece was ended with the hands (<see cref="PieceEndReason.Hands"/>); the end of a song is not counted.</summary>
        public int CutCount { get; private set; }

        /// <summary>Why the piece was ended the last time (<see cref="PieceEndReason.Hands"/> until it was ended for the first time).</summary>
        public PieceEndReason LastEndReason { get; private set; }

        public CutoffSettings Settings => _settings;

        /// <param name="now">The audio clock.</param>
        /// <param name="generation">Changes whenever the stream of pictures starts over (<see cref="IHandSource.Generation"/>): what was seen before does not count.</param>
        /// <returns>True in the frame in which the piece was ended: notes the players made in that frame must not be played.</returns>
        public bool Update(double now, int generation, IHandMotionView hands, float aspect)
        {
            if (generation != _generation)
            {
                _generation = generation;
                _gesture.Reset();
            }
            if (!_gesture.Update(now, hands, aspect)) return false;

            return EndPiece(now, PieceEndReason.Hands);
        }

        /// <summary>
        /// Ends the piece now, for either reason, all at once as described for the class. Asking again for the same <paramref name="now"/> (the hands and the end of a song in one frame) does nothing:
        /// the fade, the reset and <see cref="Cut"/> are not repeated.
        /// </summary>
        /// <param name="now">The audio clock.</param>
        /// <returns>True when the piece was ended by this call, false when it had already been ended at this same time.</returns>
        public bool EndPiece(double now, PieceEndReason reason)
        {
            if (now == _lastEndAt) return false;

            _lastEndAt = now;
            LastEndReason = reason;
            _experience.EndNow(now);
            _audio.FadeOutAll(_settings.FadeSec);
            _resetPlayers();
            _energy.Set(0f);
            if (reason == PieceEndReason.Hands) CutCount++;
            Cut?.Invoke(now);
            return true;
        }
    }
}
