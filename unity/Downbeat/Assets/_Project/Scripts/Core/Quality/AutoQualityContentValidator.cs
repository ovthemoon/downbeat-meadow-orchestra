using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>autoQuality</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional.</summary>
    public static class AutoQualityContentValidator
    {
        public const int MaxSteps = 12;

        public static void Check(AutoQualityDto a, MeadowDto meadow, List<string> errors)
        {
            if (a == null) return;
            Range(a.vsyncBudgetFps, 20f, 240f, "vsyncBudgetFps", errors);
            Range(a.slowFrameFactor, 1.05f, 4f, "slowFrameFactor", errors);
            Range(a.overFraction, 0.05f, 1f, "overFraction", errors);
            Range(a.windowSec, 1f, 30f, "windowSec", errors);
            Range(a.downDwellSec, 1f, 300f, "downDwellSec", errors);
            Range(a.confirmSec, 0f, 60f, "confirmSec", errors);
            Range(a.confirmDownDwellSec, 1f, 60f, "confirmDownDwellSec", errors);
            Range(a.upDwellSec, 10f, 3600f, "upDwellSec", errors);
            Range(a.maxUpDwellSec, 10f, 7200f, "maxUpDwellSec", errors);
            Range(a.upHoldSec, 0f, 3600f, "upHoldSec", errors);
            Range(a.headroomShare, 0.05f, 0.95f, "headroomShare", errors);
            Range(a.trackerMinFps, 1f, 60f, "trackerMinFps", errors);
            Range(a.trackerStarveSec, 1f, 120f, "trackerStarveSec", errors);
            Range(a.busyGpuShare, 0f, 1f, "busyGpuShare", errors);
            Range(a.pendingScaleStep, 0.02f, 0.5f, "pendingScaleStep", errors);
            Range(a.pendingScaleFloor, 0.3f, 1f, "pendingScaleFloor", errors);
            Range(a.performingEvidenceSec, 0f, 600f, "performingEvidenceSec", errors);
            Range(a.starveGainShare, 0.02f, 2f, "starveGainShare", errors);
            Range(a.starveGainWindowSec, 3f, 120f, "starveGainWindowSec", errors);
            Range(a.starveIgnoreSec, 10f, 7200f, "starveIgnoreSec", errors);
            Range(a.starveIgnoreMaxSec, 10f, 86400f, "starveIgnoreMaxSec", errors);
            if (a.starveIgnoreMaxSec < a.starveIgnoreSec) errors.Add("autoQuality.starveIgnoreMaxSec must be at least starveIgnoreSec");
            if (a.starveGiveUpAfter < 1 || a.starveGiveUpAfter > 10) errors.Add("autoQuality.starveGiveUpAfter must be 1..10");
            if (a.starveCancelsAsFailure < 1 || a.starveCancelsAsFailure > 10) errors.Add("autoQuality.starveCancelsAsFailure must be 1..10");
            if (a.maxUpDwellSec < a.upDwellSec) errors.Add("autoQuality.maxUpDwellSec must be at least upDwellSec");
            if (a.confirmDownDwellSec > a.downDwellSec) errors.Add("autoQuality.confirmDownDwellSec must not be more than downDwellSec");

            var steps = a.ladder;
            if (steps != null && steps.Length > 0)
            {
                if (steps.Length < 2 || steps.Length > MaxSteps) errors.Add($"autoQuality.ladder must have 2..{MaxSteps} steps");
                var tiers = new HashSet<string>();
                if (meadow?.quality != null) foreach (var tier in meadow.quality) tiers.Add(tier.name);
                for (var i = 0; i < steps.Length; i++)
                {
                    var step = steps[i];
                    if (step == null || !tiers.Contains(step.tier)) errors.Add($"autoQuality.ladder[{i}].tier '{step?.tier}' is not a tier of meadow.quality");
                    else if (!(step.renderScale >= 0.3f && step.renderScale <= 1f)) errors.Add($"autoQuality.ladder[{i}].renderScale must be 0.3..1");
                }
            }

            var start = a.start;
            if (start == null) return;
            var count = steps != null && steps.Length > 0 ? steps.Length : AutoQualitySettings.DefaultLadder().Length;
            if (start.weakStartStep < 0 || start.weakStartStep >= count) errors.Add($"autoQuality.start.weakStartStep must be 0..{count - 1}");
            if (start.strongStartStep < 0 || start.strongStartStep >= count) errors.Add($"autoQuality.start.strongStartStep must be 0..{count - 1}");
            if (start.weakMemoryMb < 0 || start.weakMemoryMb > 65536) errors.Add("autoQuality.start.weakMemoryMb must be 0..65536");
        }

        static void Range(float value, float min, float max, string name, List<string> errors)
        {
            if (!(value >= min && value <= max)) errors.Add($"autoQuality.{name} must be {min}..{max}");
        }
    }
}
