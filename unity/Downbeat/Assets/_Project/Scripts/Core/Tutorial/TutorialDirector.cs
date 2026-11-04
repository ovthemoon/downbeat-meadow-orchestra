using System;

namespace Downbeat.Core
{
    /// <summary>
    /// When the follow-along tutorial is on the screen and what step it is at. Pure logic on the time it is given, so it is tested without a scene. It shows once nobody has played for
    /// <see cref="TutorialSettings.IdleDelaySec"/> (after a visit has ended, and after the app started), goes round its three steps of <see cref="TutorialSettings.StepSec"/> each for as long as nobody is there,
    /// ticks a step when the visitor does what it asks (without hurrying the next one), and fades out as soon as an open hand shows (the music does not wait for it: this class only reports). A fist alone does not fade it.
    /// </summary>
    public sealed class TutorialDirector
    {
        readonly TutorialSettings _settings;
        bool _started;
        double _startedAt;
        double _lastNow;
        double _lastOpenHand = double.NegativeInfinity;
        double _beganAt = double.NaN;
        float _alpha;
        int _cycle;
        int _checks;

        public TutorialDirector(TutorialSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <param name="now">The time of the picture, in seconds (the clock of the experience state machine).</param>
        /// <param name="state">The flow of the visit; the tutorial is only ever on while it is Idle.</param>
        /// <param name="idleSince">When the meadow became Idle (<see cref="ExperienceStateMachine.StateSince"/>). Before the first visit this is whatever the machine starts with: the time of the first call counts as the start too.</param>
        /// <param name="openHandVisible">An open hand is in view (it fades the tutorial out; and it ticks the first step).</param>
        /// <param name="cellChanged">A hand has moved into another cell in this picture (ticks the second step).</param>
        /// <param name="bothFists">Both hands are fists (ticks the third step; does not fade the tutorial).</param>
        public TutorialFrame Update(double now, ExperienceState state, double idleSince, bool openHandVisible, bool cellChanged, bool bothFists)
        {
            if (!_started)
            {
                _started = true;
                _startedAt = now;
                _lastNow = now;
            }
            if (openHandVisible) _lastOpenHand = Math.Max(_lastOpenHand, now);

            // nobody has played since the latest of: the visit ended, the app started, the last open hand
            var quietSince = Math.Max(Math.Max(idleSince, _startedAt), _lastOpenHand);
            var onset = quietSince + _settings.IdleDelaySec;
            var show = state == ExperienceState.Idle && !openHandVisible && now >= onset;

            AdvanceAlpha(now, show, onset);
            _lastNow = Math.Max(_lastNow, now);

            if (!show && _alpha <= 0f)
            {
                Reset();
                return TutorialFrame.Hidden;
            }

            if (double.IsNaN(_beganAt)) _beganAt = onset;
            return Frame(now, openHandVisible, cellChanged, bothFists);
        }

        void AdvanceAlpha(double now, bool show, double onset)
        {
            // fading in counts from the moment the tutorial was due, even if this is the first picture since
            var from = show ? Math.Max(_lastNow, onset) : _lastNow;
            var dt = Math.Max(0.0, now - from);
            if (show)
            {
                _alpha = _settings.FadeInSec <= 0.0 ? 1f : (float)Math.Min(1.0, _alpha + dt / _settings.FadeInSec);
            }
            else
            {
                _alpha = _settings.FadeOutSec <= 0.0 ? 0f : (float)Math.Max(0.0, _alpha - dt / _settings.FadeOutSec);
            }
        }

        TutorialFrame Frame(double now, bool openHandVisible, bool cellChanged, bool bothFists)
        {
            var steps = TutorialSettings.StepCount;
            var elapsed = Math.Max(0.0, now - _beganAt);
            var cycleSec = steps * _settings.StepSec;
            var cycle = (int)Math.Floor(elapsed / cycleSec);
            var within = elapsed - cycle * cycleSec;
            var step = Math.Min(steps - 1, (int)(within / _settings.StepSec));
            var progress = (float)Math.Min(1.0, (within - step * _settings.StepSec) / _settings.StepSec);

            if (cycle != _cycle)
            {
                _cycle = cycle;
                _checks = 0;   // a new round starts without ticks
            }
            if ((step == 0 && openHandVisible) || (step == 1 && cellChanged) || (step == 2 && bothFists)) _checks |= 1 << step;

            return new TutorialFrame(_alpha, step, progress, (_checks & 1) != 0, (_checks & 2) != 0, (_checks & 4) != 0);
        }

        void Reset()
        {
            _beganAt = double.NaN;
            _cycle = 0;
            _checks = 0;
        }
    }
}
