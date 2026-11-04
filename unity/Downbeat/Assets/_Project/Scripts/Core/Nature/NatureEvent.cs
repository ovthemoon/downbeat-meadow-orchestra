namespace Downbeat.Core
{
    /// <summary>A nature note that has been put on the audio clock: the note and the audio time at which it is heard (the time of its slot).</summary>
    public readonly struct NatureEvent
    {
        public NatureEvent(NatureNote note, double audioTime)
        {
            Note = note;
            AudioTime = audioTime;
        }

        public NatureNote Note { get; }
        public double AudioTime { get; }
    }
}
