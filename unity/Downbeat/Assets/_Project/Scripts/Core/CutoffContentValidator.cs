using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>cutoff</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional.</summary>
    public static class CutoffContentValidator
    {
        public static void Check(CutoffDto cutoff, List<string> errors)
        {
            if (cutoff == null) return;
            if (!(cutoff.spreadPalms >= 0.5f && cutoff.spreadPalms <= 8f)) errors.Add("cutoff.spreadPalms must be 0.5..8");
            if (!(cutoff.minApartPalms >= 1f && cutoff.minApartPalms <= 12f)) errors.Add("cutoff.minApartPalms must be 1..12");
            if (!(cutoff.windowSec >= 0.3f && cutoff.windowSec <= 3f)) errors.Add("cutoff.windowSec must be 0.3..3");
            if (!(cutoff.holdSec >= 0f && cutoff.holdSec <= 1f)) errors.Add("cutoff.holdSec must be 0..1");
            if (!(cutoff.fadeSec >= 0.05f && cutoff.fadeSec <= 1f)) errors.Add("cutoff.fadeSec must be 0.05..1");
            if (!(cutoff.restartLockSec >= 0f && cutoff.restartLockSec <= 5f)) errors.Add("cutoff.restartLockSec must be 0..5");
            if (!(cutoff.endWordSec >= 0.3f && cutoff.endWordSec <= 5f)) errors.Add("cutoff.endWordSec must be 0.3..5");
        }
    }
}
