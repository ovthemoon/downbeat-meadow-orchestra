namespace Downbeat.Core
{
    /// <summary>The numbers of the two-fists ending, as an immutable snapshot of <see cref="CutoffDto"/>.</summary>
    public sealed class CutoffSettings
    {
        CutoffSettings(float spreadPalms, float minApartPalms, double windowSec, double holdSec, double fadeSec, double restartLockSec, double endWordSec)
        {
            SpreadPalms = spreadPalms;
            MinApartPalms = minApartPalms;
            WindowSec = windowSec;
            HoldSec = holdSec;
            FadeSec = fadeSec;
            RestartLockSec = restartLockSec;
            EndWordSec = endWordSec;
        }

        /// <summary>The distance between the fists must grow by this many palm lengths within <see cref="WindowSec"/> ...</summary>
        public float SpreadPalms { get; }
        /// <summary>... and end at least this many palm lengths apart ...</summary>
        public float MinApartPalms { get; }
        public double WindowSec { get; }
        /// <summary>... and stay so, both hands still fists, for this long.</summary>
        public double HoldSec { get; }
        /// <summary>The players' voices fade out in this long.</summary>
        public double FadeSec { get; }
        /// <summary>No new visit for this long after the piece was ended with the hands.</summary>
        public double RestartLockSec { get; }
        /// <summary>The word "끝" stays this long.</summary>
        public double EndWordSec { get; }

        public static CutoffSettings Default => From(null);

        /// <summary>From the content block (null: the defaults). Values that the content validator would have refused are not checked again here.</summary>
        public static CutoffSettings From(CutoffDto dto)
        {
            var d = dto ?? new CutoffDto();
            return new CutoffSettings(d.spreadPalms, d.minApartPalms, d.windowSec, d.holdSec, d.fadeSec, d.restartLockSec, d.endWordSec);
        }
    }
}
