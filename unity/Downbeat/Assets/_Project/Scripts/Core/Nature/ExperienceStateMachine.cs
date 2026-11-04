using System;

namespace Downbeat.Core
{
    /// <summary>The flow of one visit: nobody is there (Idle), a hand came in (Waking), music is played (Playing), the hands left (Settling).</summary>
    public enum ExperienceState { Idle, Waking, Playing, Settling }

    public sealed class ExperienceSettings
    {
        /// <summary>The restart lock of a settings object that was not given one (the same as <c>cutoff.restartLockSec</c>).</summary>
        public const double DefaultRestartLockSec = 1.5;

        public ExperienceSettings(double wakingSec, double noHandsSec, float idleEnergy, double settlingMinSec, double restartLockSec = DefaultRestartLockSec)
        {
            if (!(wakingSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(wakingSec), wakingSec, "must not be negative");
            if (!(noHandsSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(noHandsSec), noHandsSec, "must be positive");
            if (!(idleEnergy >= 0f && idleEnergy <= 1f)) throw new ArgumentOutOfRangeException(nameof(idleEnergy), idleEnergy, "0..1");
            if (!(settlingMinSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(settlingMinSec), settlingMinSec, "must not be negative");
            if (!(restartLockSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(restartLockSec), restartLockSec, "must not be negative");

            WakingSec = wakingSec;
            NoHandsSec = noHandsSec;
            IdleEnergy = idleEnergy;
            SettlingMinSec = settlingMinSec;
            RestartLockSec = restartLockSec;
        }

        /// <summary>Waking turns into Playing at the first note or after this long.</summary>
        public double WakingSec { get; }
        /// <summary>This long without a hand turns Waking or Playing into Settling.</summary>
        public double NoHandsSec { get; }
        /// <summary>Settling turns into Idle when the energy is below this ...</summary>
        public float IdleEnergy { get; }
        /// <summary>... and Settling has lasted at least this long.</summary>
        public double SettlingMinSec { get; }
        /// <summary>After <see cref="ExperienceStateMachine.EndNow"/> no new visit starts for this long, and then only for an open hand.</summary>
        public double RestartLockSec { get; }

        public ExperienceSettings WithRestartLockSec(double restartLockSec) => new ExperienceSettings(WakingSec, NoHandsSec, IdleEnergy, SettlingMinSec, restartLockSec);
    }

    /// <summary>Whether somebody is there: a running tracker that sees a hand, or recent mouse and keyboard play (they have no hand of their own).</summary>
    public static class ExperiencePresence
    {
        public static bool HasHands(bool trackerRunning, int handCount, bool inputRecent) => (trackerRunning && handCount > 0) || inputRecent;
    }

    /// <summary>
    /// Idle -> Waking -> Playing -> Settling -> Idle. Pure logic on the time it is given, so it is tested without a scene. A tracker that stops counts as
    /// "no hands" (the caller says so through <c>hasHands</c>); a hand that comes back while Settling goes on with the same visit.
    /// </summary>
    public sealed class ExperienceStateMachine
    {
        readonly ExperienceSettings _settings;
        double _lastPresence = double.NegativeInfinity;
        bool _needsOpenHand;
        double _lockedUntil;

        public ExperienceStateMachine(ExperienceSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public ExperienceState State { get; private set; } = ExperienceState.Idle;

        /// <summary>
        /// True from <see cref="EndNow"/> until the next visit starts (the restart lock, then an open hand): the player ended the piece with the hands and the meadow waits for an open hand. It is the one fact
        /// that says "nothing plays now": the hands' notes and strikes are held back while it is true (<see cref="ConductPlayer.SetPlayGate"/>), so a hand whose pose is unsure cannot make the meadow
        /// sound while it stays Idle. A visit that ended because the hands left never sets it.
        /// </summary>
        public bool AwaitsOpenHand => _needsOpenHand;
        /// <summary>When the current state was entered.</summary>
        public double StateSince { get; private set; }

        /// <summary>A visit began (a hand came into an Idle meadow). The time is the audio/visual time passed to <see cref="Update"/>.</summary>
        public event Action<double> SessionStarted;
        /// <summary>A visit ended (Settling went quiet and the meadow is Idle again).</summary>
        public event Action<double> SessionEnded;

        /// <summary>Update for a caller that cannot tell an open hand from an unsure one: whoever is there (or plays a note) starts a visit, as before.</summary>
        public void Update(double now, bool hasHands, bool noteThisUpdate, float energy) => Update(now, hasHands, noteThisUpdate, energy, hasHands || noteThisUpdate);

        /// <param name="hasHands">
        /// Somebody is there: a hand that plays (<see cref="HandPlayRule"/>: an open hand, or one whose pose stays unsure once it has been tracked for the confirming pictures; never a fist, never a hand that has just come
        /// in), the mouse or the keyboard. It starts a visit and keeps one going, so two fists held up alone never start one.
        /// </param>
        /// <param name="openHandPresent">
        /// An open hand (or a mouse or keyboard player) is there. Only a visit that was ended with <see cref="EndNow"/> looks at it: that one starts again after the restart lock, and then only with this
        /// (a hand whose pose is unsure, such as a fist that is relaxing, does not start it).
        /// </param>
        public void Update(double now, bool hasHands, bool noteThisUpdate, float energy, bool openHandPresent)
        {
            if (hasHands || noteThisUpdate) _lastPresence = now;
            var gone = now - _lastPresence >= _settings.NoHandsSec;

            if (State == ExperienceState.Idle && (hasHands || noteThisUpdate) && MayStart(now, openHandPresent))
            {
                _needsOpenHand = false;
                Enter(ExperienceState.Waking, now);
                SessionStarted?.Invoke(now);
            }

            if (State == ExperienceState.Waking)
            {
                if (gone) Enter(ExperienceState.Settling, now);
                else if (noteThisUpdate || now - StateSince >= _settings.WakingSec) Enter(ExperienceState.Playing, now);
            }
            else if (State == ExperienceState.Playing)
            {
                if (gone) Enter(ExperienceState.Settling, now);
            }
            else if (State == ExperienceState.Settling)
            {
                if (!gone) Enter(ExperienceState.Playing, now);
                else if (now - StateSince >= _settings.SettlingMinSec && energy < _settings.IdleEnergy)
                {
                    Enter(ExperienceState.Idle, now);
                    SessionEnded?.Invoke(now);
                }
            }
        }

        /// <summary>
        /// The visit is over now (the player ended it with the hands): Idle at once, and <see cref="SessionEnded"/> as for a visit that went quiet. Then no new visit starts for
        /// <see cref="ExperienceSettings.RestartLockSec"/>, and after that only when <c>Update</c> is told that an open hand is there (neither fists held up nor a hand whose pose is unsure start one).
        /// A visit that ends because the hands left is not affected: a hand that comes back starts one as before. With no visit going on only the lock is set.
        /// </summary>
        public void EndNow(double now)
        {
            _needsOpenHand = true;
            _lockedUntil = now + _settings.RestartLockSec;
            if (State == ExperienceState.Idle) return;

            Enter(ExperienceState.Idle, now);
            SessionEnded?.Invoke(now);
        }

        bool MayStart(double now, bool openHandPresent) => !_needsOpenHand || (openHandPresent && now >= _lockedUntil);

        void Enter(ExperienceState next, double now)
        {
            State = next;
            StateSince = now;
        }
    }
}
