using System;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Runs the <see cref="AutoQualityController"/> in the app: it chooses the first step from the graphics device, feeds the controller every frame and applies what it asks for (the tier of
    /// <see cref="MeadowStage"/>, the render scale of <see cref="RenderLevers"/>, which goes in small steps so that the picture does not pop). Every change is a <c>[quality]</c> line in the log.
    /// With <c>--quality High|Low</c> there is no controller and the tier stays where it was put. <c>--render-scale</c> and <c>--max-fps</c> alone fix only that number: the controller still picks the tier.
    /// </summary>
    public sealed class AutoQualityRunner
    {
        const double WarmupSec = 2.0;               // the first frames after loading carry the boot hitch
        const float ScalePerSec = 0.3f;             // a step of 0.15 takes half a second
        const float ScaleGranule = 0.025f;          // the pipeline's render targets are made again at every change: not more often than this

        readonly MeadowStage _meadow;
        readonly AutoQualityController _controller;
        readonly GpuFrameTimer _gpu = new GpuFrameTimer();
        readonly AutoQualitySettings _settings;
        readonly bool _scalePinned;
        double _warmupUntil = double.NaN;
        float _targetScale = 1f;
        float _scale = 1f;
        float _lastApplied = 1f;
        int _changes;

        AutoQualityRunner(MeadowStage meadow, AutoQualitySettings settings, AutoQualityController controller, bool scalePinned = false)
        {
            _meadow = meadow;
            _settings = settings;
            _controller = controller;
            _scalePinned = scalePinned;
            if (controller == null) return;
            _targetScale = _scale = _lastApplied = controller.Applied.RenderScale;
            meadow.QualityApplied += SyncScale;   // the meadow was built again (a content reload, the meadow switched off and on): its tier brought its own scale; the controller's stays
        }

        public bool IsAuto => _controller != null;
        /// <summary>The step of the ladder (0 = lightest) in force now, which is what the health log says (a pending change to a cheaper tier shows when it is made); -1 when the tier is locked.</summary>
        public int Level => _controller == null ? -1 : _controller.AppliedLevel;
        public float RenderScale => RenderLevers.RenderScale;
        public AutoQualityController Controller => _controller;
        /// <summary>A starved hand tracker is being ignored: stepping down did not help it.</summary>
        public bool StarveIgnored => _controller != null && _controller.StarveIgnored;

        /// <summary>How many times the picture was changed since this was last asked (for the health log).</summary>
        public int TakeChangeCount()
        {
            var count = _changes;
            _changes = 0;
            return count;
        }

        /// <summary>
        /// Decides the mode and the first tier. <paramref name="startTier"/> is the tier to build the meadow with: the first step of the ladder when automatic, otherwise what <c>--quality</c> asked for (null: the first tier).
        /// </summary>
        public static AutoQualityRunner Create(ContentDto content, LaunchArgs launch, MeadowStage meadow, GpuInfo gpu, out string startTier)
        {
            var dto = content.autoQuality;
            var mode = QualityMode.Resolve(launch, dto == null || dto.enabled);
            if (!mode.IsAuto)
            {
                startTier = mode.LockedTier;
                Debug.Log($"[quality] {(mode.LockedTier != null ? $"locked to {mode.LockedTier}" : "tier of the content")} ({mode.Reason}); no automatic changes");
                return new AutoQualityRunner(meadow, null, null);
            }

            var full = AutoQualitySettings.From(dto, content.meadow.quality);
            int picked;
            string reason;
            if (launch.TryGetDouble("quality-level", out var asked))
            {
                picked = Math.Min(Math.Max(0, (int)asked), full.Steps - 1);
                reason = $"--quality-level {(int)asked}";
            }
            else
            {
                picked = StartStepPicker.Pick(gpu, dto?.start, full.Steps, out reason);
            }
            // a number fixed by hand is no lever: the ladder keeps one step per tier for a pinned scale, and the budget is the pinned frame cap
            var settings = mode.Pins.Any ? AutoQualitySettings.From(dto, content.meadow.quality, mode.Pins) : full;
            var level = mode.Pins.RenderScale ? Math.Max(0, settings.LevelOfTier(full.Step(picked).Tier)) : picked;
            var controller = new AutoQualityController(settings, level);
            startTier = settings.Step(level).Tier;
            var startText = mode.Pins.RenderScale ? settings.Step(level).Tier : settings.Step(level).ToString();   // the render scale is not claimed when it is fixed by hand
            Debug.Log($"[quality] automatic ({mode.Reason}): starts at step {level} = {startText} of {settings.Steps}; {reason}; the first {settings.ConfirmSec:0} s of frames confirm it");
            return new AutoQualityRunner(meadow, settings, controller, mode.Pins.RenderScale);
        }

        /// <summary>After the meadow is built: the render scale of the first step (the tier's own scale was set while building).</summary>
        public void ApplyStart()
        {
            if (_controller == null) return;
            _targetScale = _scale = _lastApplied = _controller.Applied.RenderScale;
            SyncScale();
        }

        /// <summary>Puts the scale of the controller on the picture (not when a number fixed by hand holds the scale).</summary>
        void SyncScale()
        {
            if (_controller == null || _scalePinned) return;
            RenderLevers.SetRenderScale(_scale);
        }

        /// <summary>Once per frame while the app runs.</summary>
        public void Tick(double realtimeSec, float frameMs, bool trackerRunning, bool trackerFrame, bool idle)
        {
            if (_controller == null) return;
            RampScale(Time.unscaledDeltaTime);
            if (double.IsNaN(_warmupUntil)) _warmupUntil = realtimeSec + WarmupSec;
            _gpu.Poll();
            if (realtimeSec < _warmupUntil) return;
            var change = _controller.Update(new QualitySample(realtimeSec, frameMs, _gpu.HasGpuTime ? _gpu.LatestGpuMs : double.NaN, trackerRunning, trackerFrame, idle));
            if (change != null) Apply(change);
        }

        /// <summary>Goes to a step now (tests, diagnostics); a tier change still needs the meadow to be idle.</summary>
        public void ForceLevel(int level, double realtimeSec, bool idle)
        {
            if (_controller == null) return;
            var change = _controller.Force(level, realtimeSec, idle);
            if (change != null) Apply(change);
        }

        void Apply(QualityChange change)
        {
            var now = _scalePinned ? change.Step.Tier : change.Step.ToString();
            Debug.Log($"[quality] {change.Reason}{(change.IsNote ? string.Empty : $"; now {now} (step {change.Level}, frame budget {_settings.BudgetMs(change.Level):0.0} ms)")}");
            if (change.IsNote) return;
            _changes++;
            if (change.TierChanged)
            {
                _scale = _lastApplied = _targetScale = change.Step.RenderScale;
                _meadow.ApplyQuality(change.Step.Tier); // the grass and the flowers are built again: the meadow is idle, so nobody sees it; the meadow says when it is done and SyncScale puts the scale back
                return;
            }
            _targetScale = change.Step.RenderScale;
        }

        void RampScale(float dt)
        {
            if (_scalePinned) return;
            if (Mathf.Abs(_scale - _targetScale) < 1e-4f) return;
            var step = Mathf.Min(ScalePerSec * dt, Mathf.Abs(_targetScale - _scale));
            _scale += Mathf.Sign(_targetScale - _scale) * step;
            if (Mathf.Abs(_scale - _lastApplied) >= ScaleGranule || Mathf.Abs(_scale - _targetScale) < 1e-4f)
            {
                _lastApplied = _scale;
                SyncScale();
            }
        }
    }
}
