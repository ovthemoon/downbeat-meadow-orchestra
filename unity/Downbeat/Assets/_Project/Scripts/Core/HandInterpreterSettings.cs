namespace Downbeat.Core
{
    /// <summary>Immutable snapshot of <see cref="HandSettingsDto"/> (the content file can be replaced while the interpreter keeps running).</summary>
    public readonly struct HandInterpreterSettings
    {
        public float MinScore { get; }
        public float TriggerSpeed { get; }
        public float RearmSpeed { get; }
        public float FullSpeed { get; }
        public float MinVelocity { get; }
        public double PeakWaitMs { get; }
        public float PeakDropRatio { get; }
        public double VelocityWindowMs { get; }
        public double MinRetriggerMs { get; }
        public double MaxGapMs { get; }
        public float HeightHysteresis { get; }
        public float PartHysteresis { get; }
        /// <summary>The six cells (where the rows meet); a strike picks its part from them as conducting does.</summary>
        public CellSettings Cells { get; }

        public HandInterpreterSettings(HandSettingsDto dto, CellSettings cells = null)
        {
            Cells = cells ?? CellSettings.Default;
            var d = dto ?? new HandSettingsDto();
            MinScore = d.minScore;
            TriggerSpeed = d.triggerSpeed;
            RearmSpeed = d.rearmSpeed;
            FullSpeed = d.fullSpeed;
            MinVelocity = d.minVelocity;
            PeakWaitMs = d.peakWaitMs;
            PeakDropRatio = d.peakDropRatio;
            VelocityWindowMs = d.velocityWindowMs;
            MinRetriggerMs = d.minRetriggerMs;
            MaxGapMs = d.maxGapMs;
            HeightHysteresis = d.heightHysteresis;
            PartHysteresis = d.partHysteresis;
        }

        public static HandInterpreterSettings Default => new HandInterpreterSettings(new HandSettingsDto());
    }
}
