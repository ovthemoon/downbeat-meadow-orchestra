using System;

namespace Downbeat.Core
{
    /// <summary>Whether the picture's quality is automatic or locked to a tier, from <c>--quality</c> (and the overrides that fix a number by hand).</summary>
    public readonly struct QualityMode
    {
        public const string AutoName = "Auto";

        public bool IsAuto { get; }
        /// <summary>The tier name when locked (null when automatic).</summary>
        public string LockedTier { get; }
        /// <summary>Why it is what it is, for the log.</summary>
        public string Reason { get; }
        /// <summary>The numbers that <c>--render-scale</c> and <c>--max-fps</c> fixed by hand; the controller leaves those levers alone and still picks the tier.</summary>
        public QualityPins Pins { get; }

        QualityMode(bool isAuto, string lockedTier, string reason, QualityPins pins = default)
        {
            IsAuto = isAuto;
            LockedTier = lockedTier;
            Reason = reason;
            Pins = pins;
        }

        /// <summary>
        /// <c>--quality Auto</c> or no <c>--quality</c> at all: automatic (the default), unless the content switches it off. <c>--quality High</c> (any tier name) locks that tier.
        /// <c>--render-scale</c> and <c>--max-fps</c> alone fix only that number by hand (for a measurement, or a cap for a slow screen): the controller leaves that lever alone and still picks the tier,
        /// with the start estimate and the runtime rules; together with <c>--quality High|Low</c> nothing is automatic.
        /// </summary>
        public static QualityMode Resolve(LaunchArgs launch, bool contentEnables)
        {
            var quality = launch?.Get("quality");
            var explicitTier = !string.IsNullOrEmpty(quality) && !string.Equals(quality, AutoName, StringComparison.OrdinalIgnoreCase);
            if (explicitTier) return new QualityMode(false, quality, $"--quality {quality} locks the tier");
            if (!contentEnables) return new QualityMode(false, null, "autoQuality.enabled is false in the content");
            var pins = new QualityPins(launch != null && launch.Has("render-scale"), launch != null && launch.TryGetDouble("max-fps", out var fps) ? (int)fps : 0);
            var why = string.IsNullOrEmpty(quality) ? "no --quality given" : "--quality Auto";
            if (pins.Any) why += $"; {(pins.RenderScale ? "--render-scale" : "--max-fps")}{(pins.RenderScale && pins.MaxFps > 0 ? " and --max-fps" : string.Empty)} fixes only that number by hand";
            return new QualityMode(true, null, why, pins);
        }
    }
}
