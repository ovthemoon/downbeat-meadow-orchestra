using System;
using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The numbers of the follow-along tutorial, as an immutable snapshot of <see cref="TutorialDto"/> (the content file can be replaced while the tutorial goes on).</summary>
    public sealed class TutorialSettings
    {
        /// <summary>The number of steps (and of lines) of the tutorial.</summary>
        public const int StepCount = 3;

        /// <summary>The lines of a content file without any: the defaults of <see cref="TutorialDto"/>.</summary>
        public static IReadOnlyList<string> DefaultTexts => new TutorialDto().texts;

        readonly string[] _texts;

        public TutorialSettings(double idleDelaySec, double stepSec, double fadeInSec, double fadeOutSec, float dim, IReadOnlyList<string> texts, string endWord, string openHandImage = "", string fistImage = "")
        {
            if (!(idleDelaySec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(idleDelaySec), idleDelaySec, "must not be negative");
            if (!(stepSec > 0.0)) throw new ArgumentOutOfRangeException(nameof(stepSec), stepSec, "must be positive");
            if (!(fadeInSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(fadeInSec), fadeInSec, "must not be negative");
            if (!(fadeOutSec >= 0.0)) throw new ArgumentOutOfRangeException(nameof(fadeOutSec), fadeOutSec, "must not be negative");
            if (!(dim >= 0f && dim <= 1f)) throw new ArgumentOutOfRangeException(nameof(dim), dim, "0..1");
            if (texts == null || texts.Count != StepCount) throw new ArgumentException($"the tutorial has {StepCount} lines", nameof(texts));

            IdleDelaySec = idleDelaySec;
            StepSec = stepSec;
            FadeInSec = fadeInSec;
            FadeOutSec = fadeOutSec;
            Dim = dim;
            _texts = new string[StepCount];
            for (var i = 0; i < StepCount; i++) _texts[i] = texts[i] ?? string.Empty;
            EndWord = endWord ?? string.Empty;
            OpenHandImage = openHandImage ?? string.Empty;
            FistImage = fistImage ?? string.Empty;
        }

        /// <summary>The meadow has been quiet this long before the tutorial begins to show.</summary>
        public double IdleDelaySec { get; }
        /// <summary>Each of the three steps is shown this long; then the next, and after the third the first again.</summary>
        public double StepSec { get; }
        public double FadeInSec { get; }
        public double FadeOutSec { get; }
        /// <summary>How dark the meadow gets behind the tutorial (0 = not at all, 1 = black).</summary>
        public float Dim { get; }
        /// <summary>The line of each step.</summary>
        public IReadOnlyList<string> Texts => _texts;
        /// <summary>The word that appears when the fists of the third step have spread apart.</summary>
        public string EndWord { get; }
        /// <summary>Where the picture of the open hand is in the content folder; empty: the hand is drawn in code.</summary>
        public string OpenHandImage { get; }
        /// <summary>Where the picture of the fist is in the content folder; empty: the fist is drawn in code.</summary>
        public string FistImage { get; }

        /// <summary>From the content block (null: the defaults). Values the content validator refuses are refused here too.</summary>
        public static TutorialSettings From(TutorialDto dto)
        {
            var d = dto ?? new TutorialDto();
            return new TutorialSettings(d.idleDelaySec, d.stepSec, d.fadeInSec, d.fadeOutSec, d.dim, d.texts ?? new TutorialDto().texts, d.endWord, d.openHandImage, d.fistImage);
        }
    }
}
