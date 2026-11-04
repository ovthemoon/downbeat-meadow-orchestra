using System;
using System.Collections.Generic;
using System.Globalization;

namespace Downbeat.Core
{
    /// <summary>What the controller is told every frame.</summary>
    public readonly struct QualitySample
    {
        public double TimeSec { get; }
        public float FrameMs { get; }
        /// <summary>The GPU time of a recent frame in ms, NaN where the platform does not measure it.</summary>
        public double GpuMs { get; }
        /// <summary>The hand tracker is the input and is connected (its pictures are expected).</summary>
        public bool TrackerRunning { get; }
        /// <summary>A new tracker picture arrived since the last frame.</summary>
        public bool TrackerFrame { get; }
        /// <summary>The meadow is idle: nobody is playing, so a change that rebuilds the grass is not seen.</summary>
        public bool Idle { get; }

        public QualitySample(double timeSec, float frameMs, double gpuMs, bool trackerRunning, bool trackerFrame, bool idle)
        {
            TimeSec = timeSec; FrameMs = frameMs; GpuMs = gpuMs; TrackerRunning = trackerRunning; TrackerFrame = trackerFrame; Idle = idle;
        }
    }

    /// <summary>A change the controller asks for: the tier and render scale to have now. <see cref="TierChanged"/> means the grass and the flowers are built again.</summary>
    public sealed class QualityChange
    {
        public int Level { get; }
        public LadderStep Step { get; }
        public bool TierChanged { get; }
        public string Reason { get; }
        /// <summary>Something for the log (the tracker did or did not answer a step down) where nothing changes in the picture.</summary>
        public bool IsNote { get; }

        public QualityChange(int level, LadderStep step, bool tierChanged, string reason, bool isNote = false)
        {
            Level = level; Step = step; TierChanged = tierChanged; Reason = reason; IsNote = isNote;
        }
    }

    /// <summary>
    /// Automatic quality. The TIER (High or Low: the grass and the flowers are built for it) is chosen at the start, from the graphics device and the frames of the first seconds. While running it never climbs
    /// to a better tier: after a mistake or a load that went away it stays where it is for the rest of the run (a mid PC that starts Low stays Low; the price of this rule is that it can sit too low, and to
    /// try High the app is started again, or locked with --quality High). It may go DOWN to a cheaper tier, once (that waits for an idle meadow; until then the render scale goes down, step by step and
    /// below the ladder's value if it has to, to a floor, so a performance on a machine that is too slow does not have to wait for the visitor to leave).
    /// Within the tier the RENDER SCALE moves both ways: down when the frames are too slow or the hand tracker is starved while the app is busy, up only on headroom that was seen while somebody played
    /// (an idle meadow costs far less), after a dwell that doubles every time such a step had to be taken back, up to <see cref="AutoQualityDto.maxUpDwellSec"/>. A step up counts as held only once it has stayed for
    /// <see cref="AutoQualityDto.upHoldSec"/> and the performing time at that step has shown it (never from the clock alone); a step down before that is a failed try, whatever was played at the step in the meantime.
    /// That bounds how often the scale can change its mind: a step up that is taken back within <see cref="AutoQualityDto.upHoldSec"/> doubles the wait before the next one, so a machine whose load sits on the
    /// budget changes its scale at most once in <see cref="AutoQualityDto.maxUpDwellSec"/> in the end, and one that holds a step for longer than <see cref="AutoQualityDto.upHoldSec"/> at most once in that time plus the dwell.
    /// A starved tracker is only blamed on the app while stepping down helps it (see <see cref="AutoQualityDto.starveGainShare"/>). Pure: it reads the samples it is given and knows no clock.
    /// </summary>
    public sealed class AutoQualityController
    {
        const double HeadroomFrameShare = 0.9;
        const double MinWindowFill = 0.9;
        const int MinFramesInWindow = 20;
        const double TrackerWindowSec = 3.0;
        const double MaxPerformingSlowShare = 0.02;
        /// <summary>No tracker pictures for this long inside the window of an experiment: the tracker was restarted or the camera dropped (at 6 pictures a second the gap between two is 0.17 s).</summary>
        const double TrackerGapSec = 2.0;
        const int MaxTierDowns = 1;

        readonly AutoQualitySettings _s;
        readonly Queue<(double t, double ms, bool slow, bool gpuKnown, bool gpuHeavy, bool gpuLight, double gpuMs)> _window = new Queue<(double, double, bool, bool, bool, bool, double)>(512);
        readonly Queue<double> _trackerFrames = new Queue<double>(64);
        int _known, _light;
        double _slowMs, _heavyMs, _totalMs;     // the time spent in slow frames, in frames that the GPU needs most of the budget for, and in all frames of the window
        double _gpuSum;
        int _level;
        LadderStep _applied;
        bool _started;
        double _startedAt;
        double _lastChangeAt;
        int _failedProbes;
        double _starvedSince = double.NaN;
        bool _probeOpen;
        double _stepUpAt;                           // when the step up of the open probe was made
        bool _tierPending;
        bool _pendingByStarvation;
        int _appliedLevel;                          // the step of the ladder that is in force (a pending change of tier is ahead of it in _level)
        int _tierDowns;                             // changes to a cheaper tier made in this run (at most one)
        double _lastTrackerFrameAt;
        string _pendingReason;
        double _performingMs, _performingSlowMs;    // the frames seen while somebody played, since the last change of the step
        double _starveCheckAt = double.NaN;         // when to see whether the tracker answered the last step down made because of starvation
        double _starveFpsBefore;
        int _starveNoGain;
        int _starveCancels;                         // experiments cancelled in a row (the tracker was away); starveCancelsAsFailure of them count as one failed experiment
        int _starveIgnoreRounds;                    // how many times the experiment has failed again after a cooldown: each doubles the next cooldown
        double _starveIgnoredUntil = double.NegativeInfinity;
        bool _starveIgnoring;
        QualityChange _noteWaiting;

        public AutoQualityController(AutoQualitySettings settings, int startLevel)
        {
            _s = settings ?? throw new ArgumentNullException(nameof(settings));
            _level = Math.Min(Math.Max(0, startLevel), settings.Steps - 1);
            _appliedLevel = _level;
            _applied = settings.Step(_level);
        }

        /// <summary>The step the controller wants (it may be ahead of <see cref="Applied"/> while a tier change waits for the meadow to be idle).</summary>
        public int Level => _level;
        /// <summary>The step of the ladder that is in force now: what the health log says. While a change to a cheaper tier waits for an idle meadow it is still the old step (the render scale goes down below the ladder's value, see <see cref="Applied"/>), and it jumps to the new one when the tier changes.</summary>
        public int AppliedLevel => _appliedLevel;
        /// <summary>The tier and render scale in force now.</summary>
        public LadderStep Applied => _applied;
        public bool TierChangePending => _tierPending;
        /// <summary>The tracker did not answer the steps down that were made for it: its slowness is taken as not the app's doing for a while, and only the frame times decide.</summary>
        public bool StarveIgnored => _starveIgnoring;
        /// <summary>Seconds of frames seen while somebody played since the step last changed (the evidence a step up needs, and what shows that a step up has held).</summary>
        public double PerformingSec => _performingMs / 1000.0;
        /// <summary>Steps up that had to be taken back: each doubles the time before the next try.</summary>
        public int FailedProbes => _failedProbes;

        /// <summary>The tracker pictures a second over the last three seconds (NaN before there is enough to say).</summary>
        public double TrackerFps { get; private set; } = double.NaN;

        public QualityChange Update(in QualitySample sample)
        {
            if (!_started)
            {
                _started = true;
                _startedAt = sample.TimeSec;
                _lastChangeAt = sample.TimeSec;
            }
            Record(sample);
            var now = sample.TimeSec;
            if (_noteWaiting != null)
            {
                var waiting = _noteWaiting;
                _noteWaiting = null;
                return waiting;
            }
            var note = CheckStarvationExperiment(now, sample);
            if (_tierPending)
            {
                var pending = sample.Idle ? ApplyPendingTier(now) : KeepWhilePending(sample);
                if (pending != null && note != null) _noteWaiting = note;   // one thing is said per frame; the other is said on the next
                return pending ?? note;
            }
            if (note != null) return note;
            var since = now - _lastChangeAt;
            var confirming = now - _startedAt < _s.ConfirmSec;
            var downDwell = confirming ? _s.ConfirmDownDwellSec : _s.DownDwellSec;

            var starved = TrackerStarved(now, sample);
            if (since >= downDwell && CanStepDown())
            {
                var why = WhyTooDear(now);
                var byStarvation = false;
                if (why == null && starved && !StarveCheckRunning)
                {
                    why = $"the hand tracker gets {TrackerFps:0.0} pictures a second (under {_s.TrackerMinFps:0}) while the app is busy (an experiment: it has to answer within {_s.StarveGainWindowSec:0} s)";
                    byStarvation = true;
                }
                if (why != null) return StepDown(sample, why, byStarvation);
            }
            if (!confirming && CanStepUp() && since >= UpDwellSec() && !starved && Headroom(now) && HasEvidenceForStepUp()) return StepUp(sample);
            return null;
        }

        /// <summary>Moves to a step at once (a test, <c>--quality-level</c>): the same rules for the tier as for any other change.</summary>
        public QualityChange Force(int level, double timeSec, bool idle)
        {
            var target = Math.Min(Math.Max(0, level), _s.Steps - 1);
            if (target == _level && !_tierPending) return null;
            if (!_started) { _started = true; _startedAt = timeSec; }
            _tierPending = false;
            return Move(new QualitySample(timeSec, 0f, double.NaN, false, false, idle), target, "forced", down: target > _level);
        }

        double UpDwellSec() => Math.Min(_s.MaxUpDwellSec, _s.UpDwellSec * Math.Pow(2.0, _failedProbes));

        // ---- the window of recent frames --------------------------------------------------------------------------------------------------

        /// <summary>The step whose frame budget judges the frames now: the one in force (while a change of tier waits, the tier still drawing is not the one that is wanted).</summary>
        int BudgetLevel() => _tierPending ? Math.Max(0, _s.LevelOfTier(_applied.Tier)) : _level;

        void Record(in QualitySample sample)
        {
            var budget = _s.BudgetMs(BudgetLevel());
            var slow = sample.FrameMs > budget * _s.SlowFrameFactor;
            var known = !double.IsNaN(sample.GpuMs);
            var heavy = known && sample.GpuMs > budget * _s.SlowFrameFactor * 0.7;
            var light = known && sample.GpuMs < budget * _s.HeadroomShare;
            _window.Enqueue((sample.TimeSec, sample.FrameMs, slow, known, heavy, light, known ? sample.GpuMs : 0.0));
            if (!sample.Idle)
            {
                _performingMs += sample.FrameMs;
                if (slow) _performingSlowMs += sample.FrameMs;
            }
            _totalMs += sample.FrameMs;
            if (slow) _slowMs += sample.FrameMs;
            if (known) { _known++; _gpuSum += sample.GpuMs; }
            if (heavy) _heavyMs += sample.FrameMs;
            if (light) _light++;
            while (_window.Count > 0 && sample.TimeSec - _window.Peek().t > _s.WindowSec)
            {
                var old = _window.Dequeue();
                _totalMs -= old.ms;
                if (old.slow) _slowMs -= old.ms;
                if (old.gpuKnown) { _known--; _gpuSum -= old.gpuMs; }
                if (old.gpuHeavy) _heavyMs -= old.ms;
                if (old.gpuLight) _light--;
            }

            if (sample.TrackerFrame) { _trackerFrames.Enqueue(sample.TimeSec); _lastTrackerFrameAt = sample.TimeSec; }
            while (_trackerFrames.Count > 0 && sample.TimeSec - _trackerFrames.Peek() > TrackerWindowSec) _trackerFrames.Dequeue();
            TrackerFps = sample.TimeSec - _startedAt >= TrackerWindowSec ? _trackerFrames.Count / TrackerWindowSec : double.NaN;
            CloseProbeIfItHeld(sample.TimeSec);
        }

        /// <summary>
        /// A step up has held once it has stayed for <see cref="AutoQualitySettings.UpHoldSec"/> and somebody has played at it for <see cref="AutoQualitySettings.PerformingEvidenceSec"/> seconds without trouble: a failed
        /// try is forgiven then (the wait before the next one halves). Never from the clock alone: a step up made for a performance proves nothing in a quarter of an hour of an empty meadow. Never before
        /// <see cref="AutoQualitySettings.UpHoldSec"/> either: a step down until then finds the probe open and counts as a failed try, so a machine that holds a step for the evidence time and then goes over
        /// budget is not forgiven on every round (the wait doubles instead).
        /// </summary>
        void CloseProbeIfItHeld(double now)
        {
            if (!_probeOpen || _performingMs < _s.PerformingEvidenceSec * 1000.0 || now - _stepUpAt < _s.UpHoldSec) return;
            _probeOpen = false;
            if (_failedProbes > 0) _failedProbes--;
        }

        bool WindowFull(double now) => _window.Count >= MinFramesInWindow && now - _window.Peek().t >= _s.WindowSec * MinWindowFill;

        string WhyTooDear(double now)
        {
            if (!WindowFull(now)) return null;
            var n = _window.Count;
            var budget = _s.BudgetMs(BudgetLevel());
            if (_slowMs >= _totalMs * _s.OverFraction) return $"{_slowMs * 100 / _totalMs:0}% of the last {_s.WindowSec:0} s went in frames over {budget * _s.SlowFrameFactor:0.0} ms (the budget of {(_tierPending ? _applied : _s.Step(_level))} is {budget:0.0} ms)";
            if (_known >= n * HeadroomFrameShare && _heavyMs >= _totalMs * _s.OverFraction) return $"the GPU needs {_gpuSum / _known:0.0} ms of a {budget:0.0} ms frame";
            return null;
        }

        bool Headroom(double now)
        {
            if (!WindowFull(now) || _slowMs > 0) return false;
            if (_known >= _window.Count * HeadroomFrameShare) return _light >= _known * HeadroomFrameShare;
            return true; // the GPU time is not measured: the slow frames (none) and the dwell, which grows with every failed try, are all there is
        }

        bool TrackerStarved(double now, in QualitySample sample)
        {
            if (_starveIgnoring || !sample.TrackerRunning || double.IsNaN(TrackerFps) || TrackerFps >= _s.TrackerMinFps)
            {
                _starvedSince = double.NaN;
                return false;
            }
            if (double.IsNaN(_starvedSince)) _starvedSince = now;
            var busy = _known < _window.Count * HeadroomFrameShare || _gpuSum / Math.Max(1, _known) > _s.BusyGpuShare * _s.BudgetMs(BudgetLevel());
            return busy && now - _starvedSince >= _s.TrackerStarveSec;
        }

        // ---- the steps --------------------------------------------------------------------------------------------------------------------

        /// <summary>A step down is possible while there is a cheaper step, and one that changes the tier only once in a run.</summary>
        bool CanStepDown()
        {
            if (_level >= _s.Steps - 1) return false;
            return TierSame(_s.Step(_level), _s.Step(_level + 1)) || _tierDowns < MaxTierDowns;
        }

        /// <summary>A step up is only one that stays in the tier: the tier never climbs while running.</summary>
        bool CanStepUp() => _level > 0 && TierSame(_s.Step(_level), _s.Step(_level - 1));

        QualityChange StepDown(in QualitySample sample, string why, bool byStarvation = false) => Move(sample, _level + 1, why, down: true, byStarvation);

        QualityChange StepUp(in QualitySample sample)
        {
            var reason = $"{_window.Count} frames in {_s.WindowSec:0} s without a slow one and with headroom, after {sample.TimeSec - _lastChangeAt:0} s at {_applied} ({_performingMs / 1000.0:0} s of it with somebody playing)";
            return Move(sample, _level - 1, reason, down: false);
        }

        /// <summary>The evidence a step up needs: frames seen while somebody played (an idle meadow costs far less than a performance), with almost no slow one.</summary>
        bool HasEvidenceForStepUp() => _performingMs >= _s.PerformingEvidenceSec * 1000.0 && _performingSlowMs <= _performingMs * MaxPerformingSlowShare;

        QualityChange Move(in QualitySample sample, int target, string why, bool down, bool byStarvation = false)
        {
            var now = sample.TimeSec;
            var from = _applied;
            var next = _s.Step(target);
            var crossesTier = !TierSame(from, next);
            if (down)
            {
                if (crossesTier) _tierDowns++;
                if (_probeOpen) _failedProbes++;   // the step up did not hold: the next try waits twice as long
                _probeOpen = false;
            }
            else
            {
                _probeOpen = true;
                _stepUpAt = now;
            }
            _level = target;
            var reason = $"{(down ? "step down" : "step up")} {Text(from)} -> {Text(next)}: {why}";
            if (!crossesTier || sample.Idle) return Commit(now, next, crossesTier, reason, byStarvation: byStarvation);

            // the tier changes (only ever to a cheaper one) and somebody is playing: it waits for the meadow to be idle, and the render scale goes down in the meantime
            _tierPending = true;
            _pendingByStarvation = byStarvation;
            _pendingReason = reason;
            var interim = _s.ScalePinned ? from.RenderScale : Math.Min(from.RenderScale, next.RenderScale);
            if (Math.Abs(interim - from.RenderScale) < 1e-4) return null;
            return Commit(now, new LadderStep(from.Tier, interim), false, reason + " (the tier waits for the meadow to be idle; the render scale goes first)", keepPending: true, byStarvation: byStarvation);
        }

        /// <summary>A step as the log says it: the tier alone when the render scale is fixed by hand (it is not the controller's to claim).</summary>
        string Text(LadderStep step) => _s.ScalePinned ? step.Tier : step.ToString();

        /// <summary>
        /// While a change to a cheaper tier waits for an idle meadow: the render scale goes on down within the tier (below the ladder's value, down to the floor) as long as the frames are too slow: the
        /// performance is the very thing that makes the tier wait. Not when the scale is fixed by hand.
        /// </summary>
        QualityChange KeepWhilePending(in QualitySample sample)
        {
            if (_s.ScalePinned) return null;
            var now = sample.TimeSec;
            var downDwell = now - _startedAt < _s.ConfirmSec ? _s.ConfirmDownDwellSec : _s.DownDwellSec;
            if (now - _lastChangeAt < downDwell) return null;
            var why = WhyTooDear(now);
            var byStarvation = false;
            if (why == null && TrackerStarved(now, sample) && !StarveCheckRunning)
            {
                why = $"the hand tracker gets {TrackerFps:0.0} pictures a second (under {_s.TrackerMinFps:0}) while the app is busy";
                byStarvation = true;
            }
            if (why == null) return null;
            var lowered = Math.Max((float)_s.PendingScaleFloor, _applied.RenderScale - (float)_s.PendingScaleStep);
            if (_applied.RenderScale - lowered < 1e-4f) return null;   // at the floor: nothing more to give until the tier can change
            return Commit(now, new LadderStep(_applied.Tier, lowered), false, $"the tier waits for the meadow to be idle and the frames are still too slow: the render scale goes lower within it ({why})", keepPending: true, byStarvation: byStarvation);
        }

        QualityChange ApplyPendingTier(double now)
        {
            _tierPending = false;
            return Commit(now, _s.Step(_level), true, _pendingReason + " (the meadow is idle now)", byStarvation: _pendingByStarvation);
        }

        QualityChange Commit(double now, LadderStep step, bool tierChanged, string reason, bool keepPending = false, bool byStarvation = false)
        {
            if (byStarvation && !StarveCheckRunning)
            {
                _starveCheckAt = now + _s.StarveGainWindowSec;
                _starveFpsBefore = double.IsNaN(TrackerFps) ? 0.0 : TrackerFps;
            }
            var onTheLadder = string.Equals(step.Tier, _s.Step(_level).Tier, StringComparison.Ordinal) && Math.Abs(step.RenderScale - _s.Step(_level).RenderScale) < 1e-4f;
            if (onTheLadder) _appliedLevel = _level;   // an interim scale of a pending tier change is not a step of the ladder: the applied step stays the old one
            _applied = step;
            _lastChangeAt = now;
            ResetWindow();
            if (!keepPending) _tierPending = false;
            return new QualityChange(_level, step, tierChanged, reason);
        }

        // ---- the experiment: does the tracker answer a step down? ---------------------------------------------------------------------------

        bool StarveCheckRunning => !double.IsNaN(_starveCheckAt);

        /// <summary>
        /// A step down made because of a starved tracker has to pay off. When its time is up the tracker's pictures a second are compared with what they were; after
        /// <see cref="AutoQualitySettings.StarveGiveUpAfter"/> in a row that bring no gain the starvation is ignored for a long while, twice as long every time the experiment fails again after a cooldown (up to
        /// <see cref="AutoQualitySettings.StarveIgnoreMaxSec"/>). An experiment whose window had the tracker away (it was not running, or its pictures stopped for <see cref="TrackerGapSec"/>: a restart, a camera that
        /// dropped) says nothing and is cancelled, neither a gain nor a failure; but <see cref="AutoQualitySettings.StarveCancelsAsFailure"/> cancelled experiments in a row count as one failure (a camera that stalls
        /// every few seconds would otherwise be walked down the ladder one cancelled experiment after the other, and the give-up would never come), and a completed experiment starts that count again.
        /// Returns a note for the log, or null.
        /// </summary>
        QualityChange CheckStarvationExperiment(double now, in QualitySample sample)
        {
            if (_starveIgnoring && now >= _starveIgnoredUntil)
            {
                _starveIgnoring = false;   // the count of failures stays: the next failure gives up at once, for twice as long
                return Note("the cooldown is over: a starved hand tracker counts again (one more experiment; if it fails again the cooldown is twice as long)");
            }
            if (!StarveCheckRunning) return null;
            if (!sample.TrackerRunning || now - _lastTrackerFrameAt > TrackerGapSec) return CancelStarvationExperiment(now);
            if (now < _starveCheckAt) return null;
            _starveCheckAt = double.NaN;
            _starveCancels = 0;
            var after = TrackerFps;
            if (!double.IsNaN(after) && after >= _starveFpsBefore * (1.0 + _s.StarveGainShare))
            {
                _starveNoGain = 0;
                _starveIgnoreRounds = 0;
                return Note($"the hand tracker answered the step down: {_starveFpsBefore:0.0} -> {after:0.0} pictures a second");
            }
            var text = double.IsNaN(after) ? "n/a" : after.ToString("0.0", CultureInfo.InvariantCulture);
            return CountStarveFailure(now, $"{_starveFpsBefore:0.0} -> {text} pictures a second", "its slowness is not caused by the app");
        }

        QualityChange CancelStarvationExperiment(double now)
        {
            _starveCheckAt = double.NaN;
            _starveCancels++;
            if (_starveCancels < _s.StarveCancelsAsFailure)
                return Note($"the experiment is cancelled: the hand tracker was away (not running, or no pictures for a while) inside its window, so it says nothing about the step down ({_starveCancels} of {_s.StarveCancelsAsFailure} in a row before they count as one that failed)");
            var cancelled = _starveCancels;
            _starveCancels = 0;
            return CountStarveFailure(now, $"the experiment was cancelled {cancelled} times in a row, the tracker kept going away", "the tracker is away too often to tell whether the app is the reason");
        }

        /// <summary>One experiment that failed (no gain, or too many cancelled in a row): after <see cref="AutoQualitySettings.StarveGiveUpAfter"/> of them the starvation is ignored, for twice as long every time it happens again.</summary>
        QualityChange CountStarveFailure(double now, string evidence, string cause)
        {
            _starveNoGain++;
            if (_starveNoGain < _s.StarveGiveUpAfter)
                return Note($"the hand tracker did not answer the step down ({evidence}; {_starveNoGain} of {_s.StarveGiveUpAfter} before it is given up)");
            var ignoreSec = Math.Min(_s.StarveIgnoreMaxSec, _s.StarveIgnoreSec * Math.Pow(2.0, _starveIgnoreRounds));
            _starveIgnoreRounds++;
            _starveIgnoring = true;
            _starveIgnoredUntil = now + ignoreSec;
            _starvedSince = double.NaN;
            return Note($"the hand tracker did not answer the steps down ({evidence}): {cause}; it is ignored for {ignoreSec:0} s and the frame times alone decide");
        }

        QualityChange Note(string text) => new QualityChange(_level, _applied, false, text, isNote: true);

        void ResetWindow()
        {
            _window.Clear();
            _known = _light = 0;
            _slowMs = _heavyMs = _totalMs = 0.0;
            _gpuSum = 0.0;
            _starvedSince = double.NaN;
            _performingMs = _performingSlowMs = 0.0;
        }

        static bool TierSame(LadderStep a, LadderStep b) => string.Equals(a.Tier, b.Tier, StringComparison.Ordinal);

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "level {0} ({1})", _level, _applied);
    }
}
