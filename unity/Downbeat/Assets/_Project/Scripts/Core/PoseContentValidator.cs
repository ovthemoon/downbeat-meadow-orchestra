using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>pose</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional.</summary>
    public static class PoseContentValidator
    {
        public static void Check(PoseDto pose, List<string> errors)
        {
            if (pose == null) return; // the defaults apply
            if (!(pose.openRatio >= 1.1f && pose.openRatio <= 3f)) errors.Add("pose.openRatio must be 1.1..3");
            if (!(pose.fistRatio >= 0.5f && pose.fistRatio <= 1.5f)) errors.Add("pose.fistRatio must be 0.5..1.5");
            if (!(pose.fistRatio < pose.openRatio)) errors.Add("pose.fistRatio must be less than pose.openRatio");
            if (!(pose.confirmFrames >= 1 && pose.confirmFrames <= 10)) errors.Add("pose.confirmFrames must be 1..10");
        }
    }
}
