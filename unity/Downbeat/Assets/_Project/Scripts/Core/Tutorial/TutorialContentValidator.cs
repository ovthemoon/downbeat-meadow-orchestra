using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>tutorial</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional: a content file without it gets the defaults.</summary>
    public static class TutorialContentValidator
    {
        public const int MaxLineLength = 60;
        public const int MaxEndWordLength = 8;

        public static void Check(TutorialDto tutorial, List<string> errors)
        {
            if (tutorial == null) return;
            if (!(tutorial.idleDelaySec >= 3f && tutorial.idleDelaySec <= 120f)) errors.Add("tutorial.idleDelaySec must be 3..120");
            if (!(tutorial.stepSec >= 2f && tutorial.stepSec <= 20f)) errors.Add("tutorial.stepSec must be 2..20");
            if (!(tutorial.fadeInSec >= 0f && tutorial.fadeInSec <= 3f)) errors.Add("tutorial.fadeInSec must be 0..3");
            if (!(tutorial.fadeOutSec >= 0f && tutorial.fadeOutSec <= 3f)) errors.Add("tutorial.fadeOutSec must be 0..3");
            if (!(tutorial.dim >= 0f && tutorial.dim <= 0.9f)) errors.Add("tutorial.dim must be 0..0.9");
            CheckTexts(tutorial.texts, errors);
            if (string.IsNullOrWhiteSpace(tutorial.endWord) || tutorial.endWord.Length > MaxEndWordLength) errors.Add($"tutorial.endWord must be 1..{MaxEndWordLength} characters");
            CheckImage(tutorial.openHandImage, "tutorial.openHandImage", errors);
            CheckImage(tutorial.fistImage, "tutorial.fistImage", errors);
        }

        /// <summary>An empty path is allowed (the hand is drawn in code); otherwise a plain relative path to a PNG or JPEG inside the content folder.</summary>
        static void CheckImage(string path, string name, List<string> errors)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (IsPlainImagePath(path)) return;
            errors.Add($"{name} must be a path like tutorial/hand-open.png (a .png or .jpg inside the content folder, no '..', no drive or leading slash), or empty to draw the hand in code (got '{path}')");
        }

        public static bool IsPlainImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path != path.Trim()) return false;
            if (path.StartsWith("/") || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.Contains("..") || path.Contains("//")) return false;
            var lower = path.ToLowerInvariant();
            return lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg");
        }

        static void CheckTexts(string[] texts, List<string> errors)
        {
            if (texts == null || texts.Length != TutorialSettings.StepCount)
            {
                errors.Add($"tutorial.texts must hold {TutorialSettings.StepCount} lines, one for each step");
                return;
            }
            for (var i = 0; i < texts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(texts[i]) || texts[i].Length > MaxLineLength) errors.Add($"tutorial.texts[{i}] must be 1..{MaxLineLength} characters");
            }
        }
    }
}
