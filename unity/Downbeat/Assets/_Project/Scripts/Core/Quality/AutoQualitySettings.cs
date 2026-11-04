using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>One step of the ladder: the tier of <c>meadow.quality</c> and the render scale used with it.</summary>
    public readonly struct LadderStep
    {
        public string Tier { get; }
        public float RenderScale { get; }

        public LadderStep(string tier, float renderScale)
        {
            Tier = tier;
            RenderScale = renderScale;
        }

        public override string ToString() => $"{Tier}@{RenderScale:0.00}";
    }

    /// <summary>Immutable snapshot of <see cref="AutoQualityDto"/> with the frame budget of every step worked out.</summary>
    public sealed class AutoQualitySettings
    {
        readonly LadderStep[] _ladder;
        readonly double[] _budgetMs;

        AutoQualitySettings(AutoQualityDto d, LadderStep[] ladder, double[] budgetMs, bool scalePinned)
        {
            Enabled = d.enabled;
            SlowFrameFactor = d.slowFrameFactor;
            OverFraction = d.overFraction;
            WindowSec = d.windowSec;
            DownDwellSec = d.downDwellSec;
            ConfirmSec = d.confirmSec;
            ConfirmDownDwellSec = d.confirmDownDwellSec;
            UpDwellSec = d.upDwellSec;
            MaxUpDwellSec = d.maxUpDwellSec;
            UpHoldSec = d.upHoldSec;
            HeadroomShare = d.headroomShare;
            TrackerMinFps = d.trackerMinFps;
            TrackerStarveSec = d.trackerStarveSec;
            BusyGpuShare = d.busyGpuShare;
            PendingScaleStep = d.pendingScaleStep;
            PendingScaleFloor = d.pendingScaleFloor;
            PerformingEvidenceSec = d.performingEvidenceSec;
            StarveGainShare = d.starveGainShare;
            StarveGainWindowSec = d.starveGainWindowSec;
            StarveGiveUpAfter = d.starveGiveUpAfter;
            StarveIgnoreSec = d.starveIgnoreSec;
            StarveIgnoreMaxSec = d.starveIgnoreMaxSec;
            StarveCancelsAsFailure = d.starveCancelsAsFailure;
            ScalePinned = scalePinned;
            _ladder = ladder;
            _budgetMs = budgetMs;
        }

        public bool Enabled { get; }
        public double SlowFrameFactor { get; }
        public double OverFraction { get; }
        public double WindowSec { get; }
        public double DownDwellSec { get; }
        public double ConfirmSec { get; }
        public double ConfirmDownDwellSec { get; }
        public double UpDwellSec { get; }
        public double MaxUpDwellSec { get; }
        /// <summary>How long a step up has to stay (besides the performing evidence) before it counts as held; a step down inside this time is a failed try.</summary>
        public double UpHoldSec { get; }
        public double HeadroomShare { get; }
        public double TrackerMinFps { get; }
        public double TrackerStarveSec { get; }
        public double BusyGpuShare { get; }
        public double PendingScaleStep { get; }
        public double PendingScaleFloor { get; }
        public double PerformingEvidenceSec { get; }
        public double StarveGainShare { get; }
        public double StarveGainWindowSec { get; }
        public int StarveGiveUpAfter { get; }
        public double StarveIgnoreSec { get; }
        /// <summary>The longest a starved tracker is ignored, however often the experiment has failed again.</summary>
        public double StarveIgnoreMaxSec { get; }
        /// <summary>How many experiments in a row may be cancelled by the tracker going away before they count as one that failed.</summary>
        public int StarveCancelsAsFailure { get; }
        /// <summary>The render scale was fixed by hand (<c>--render-scale</c>): it is no lever, and the controller does not claim a scale.</summary>
        public bool ScalePinned { get; }

        public int Steps => _ladder.Length;
        public LadderStep Step(int level) => _ladder[level];

        /// <summary>The time a frame of this step may take: the cap of its tier, or the vertical sync of the display when the tier has none.</summary>
        public double BudgetMs(int level) => _budgetMs[level];

        /// <summary>The step of the ladder that has this tier (the first one), or -1.</summary>
        public int LevelOfTier(string tier)
        {
            for (var i = 0; i < _ladder.Length; i++)
            {
                if (string.Equals(_ladder[i].Tier, tier, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary>
        /// The settings of a content block (null: the defaults) and the tiers it names (their frame caps give the budgets). <paramref name="pins"/> are the numbers that a launch option fixed by hand:
        /// a pinned render scale leaves the ladder with one step per tier (the scale is no lever then), a pinned frame cap is the budget of every step.
        /// </summary>
        public static AutoQualitySettings From(AutoQualityDto dto, IReadOnlyList<MeadowQualityDto> tiers, QualityPins pins = default)
        {
            var d = dto ?? new AutoQualityDto();
            var steps = d.ladder != null && d.ladder.Length > 0 ? d.ladder : DefaultLadder();
            if (pins.RenderScale) steps = OnePerTier(steps);
            var ladder = new LadderStep[steps.Length];
            var budget = new double[steps.Length];
            for (var i = 0; i < steps.Length; i++)
            {
                ladder[i] = new LadderStep(steps[i].tier, steps[i].renderScale);
                var cap = pins.MaxFps;
                if (cap <= 0 && tiers != null)
                {
                    foreach (var tier in tiers)
                    {
                        if (tier.name == steps[i].tier) cap = tier.targetFps;
                    }
                }
                budget[i] = 1000.0 / (cap > 0 ? cap : Math.Max(1f, d.vsyncBudgetFps));
            }
            return new AutoQualitySettings(d, ladder, budget, pins.RenderScale);
        }

        static AutoQualityStepDto[] OnePerTier(AutoQualityStepDto[] steps)
        {
            var kept = new List<AutoQualityStepDto>(steps.Length);
            foreach (var step in steps)
            {
                if (!kept.Exists(k => k.tier == step.tier)) kept.Add(step);
            }
            return kept.ToArray();
        }

        public static AutoQualityStepDto[] DefaultLadder() => new[]
        {
            new AutoQualityStepDto { tier = "High", renderScale = 1f },
            new AutoQualityStepDto { tier = "High", renderScale = 0.85f },
            new AutoQualityStepDto { tier = "Low", renderScale = 1f },
            new AutoQualityStepDto { tier = "Low", renderScale = 0.85f },
            new AutoQualityStepDto { tier = "Low", renderScale = 0.7f },
            new AutoQualityStepDto { tier = "Low", renderScale = 0.6f },
        };
    }

    /// <summary>The numbers that a launch option fixed by hand (<c>--render-scale</c>, <c>--max-fps</c>): they are no levers of the controller, which still picks the tier.</summary>
    public readonly struct QualityPins
    {
        public bool RenderScale { get; }
        /// <summary>The frame cap in frames a second, 0 when the tier's own is used.</summary>
        public int MaxFps { get; }

        public QualityPins(bool renderScale, int maxFps)
        {
            RenderScale = renderScale;
            MaxFps = Math.Max(0, maxFps);
        }

        public bool Any => RenderScale || MaxFps > 0;
    }

    /// <summary>The graphics device, as far as the first choice of a step needs it (filled from <c>SystemInfo</c>).</summary>
    public readonly struct GpuInfo
    {
        public string Name { get; }
        public int MemoryMb { get; }

        public GpuInfo(string name, int memoryMb)
        {
            Name = name ?? string.Empty;
            MemoryMb = memoryMb;
        }
    }

    /// <summary>Which step of the ladder to start at: a weak or integrated graphics chip starts low, any other starts at the top; the first seconds of frame times confirm it (see <see cref="AutoQualityController"/>).</summary>
    public static class StartStepPicker
    {
        public static int Pick(GpuInfo gpu, AutoQualityStartDto start, int steps, out string reason)
        {
            var s = start ?? DefaultStart();
            var weakStep = Math.Min(Math.Max(0, s.weakStartStep), steps - 1);
            var strongStep = Math.Min(Math.Max(0, s.strongStartStep), steps - 1);
            var name = gpu.Name;
            if (Holds(name, s.strongNames))
            {
                reason = $"graphics '{name}' is a discrete card";
                return strongStep;
            }
            var word = FirstWordIn(name, s.weakNames);
            if (word != null)
            {
                reason = $"graphics '{name}' looks integrated or weak ('{word}')";
                return weakStep;
            }
            if (s.weakMemoryMb > 0 && gpu.MemoryMb > 0 && gpu.MemoryMb < s.weakMemoryMb)
            {
                reason = $"graphics '{name}' reports only {gpu.MemoryMb} MB (under {s.weakMemoryMb} MB)";
                return weakStep;
            }
            reason = $"graphics '{name}' ({gpu.MemoryMb} MB) does not look weak";
            return strongStep;
        }

        public static AutoQualityStartDto DefaultStart() => new AutoQualityStartDto
        {
            weakNames = new[] { "Intel", "UHD", "HD Graphics", "Iris", "Vega", "Microsoft Basic", "llvmpipe", "SwiftShader", "Radeon Graphics" },
            strongNames = new[] { "Arc", "GeForce", "RTX", "GTX", "Radeon RX", "Apple M", "Quadro" },
            weakMemoryMb = 2048,
            weakStartStep = 4,
            strongStartStep = 0,
        };

        static bool Holds(string name, string[] words) => FirstWordIn(name, words) != null;

        static string FirstWordIn(string name, string[] words)
        {
            if (words == null) return null;
            foreach (var word in words)
            {
                if (!string.IsNullOrEmpty(word) && name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return word;
            }
            return null;
        }
    }
}
