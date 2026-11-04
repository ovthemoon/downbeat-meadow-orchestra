using System.Collections.Generic;

namespace Downbeat.Core
{
    /// <summary>The checks of the <c>trail</c> block of content.json (called by <see cref="ContentValidator"/>). The block is optional.</summary>
    public static class TrailContentValidator
    {
        public static void Check(TrailDto trail, List<string> errors)
        {
            if (trail == null) return; // the defaults apply
            if (!(trail.notesPerSec >= 0f && trail.notesPerSec <= 30f)) errors.Add("trail.notesPerSec must be 0..30");
            if (!(trail.maxNotes >= 0 && trail.maxNotes <= 200)) errors.Add("trail.maxNotes must be 0..200");
            if (!(trail.lifeSec >= 0.2f && trail.lifeSec <= 4f)) errors.Add("trail.lifeSec must be 0.2..4");
            if (!(trail.size >= 0.01f && trail.size <= 0.1f)) errors.Add("trail.size must be 0.01..0.1");
            if (!(trail.labelSec >= 0f && trail.labelSec <= 5f)) errors.Add("trail.labelSec must be 0..5");
            if (!(trail.lowCountScale >= 0f && trail.lowCountScale <= 1f)) errors.Add("trail.lowCountScale must be 0..1");
        }
    }
}
