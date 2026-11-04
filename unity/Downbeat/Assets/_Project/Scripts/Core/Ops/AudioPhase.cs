namespace Downbeat.Core
{
    /// <summary>
    /// The state of the sound as the heartbeat and the health log report it. Starting: the audio clock has not moved yet (the app is loading, or there is no sound device).
    /// Ok: the clock moves. Stalled: it moved and stopped (the device was lost while running). Failed: the app gave up waiting for it.
    /// </summary>
    public enum AudioPhase
    {
        Starting,
        Ok,
        Stalled,
        Failed,
    }

    public static class AudioPhaseWords
    {
        /// <summary>The word written after <c>audio=</c> in <c>heartbeat.txt</c> and in the <c>audio_state</c> column of the health CSV (the supervisor reads the same four words).</summary>
        public static string Word(this AudioPhase phase)
        {
            switch (phase)
            {
                case AudioPhase.Ok: return "ok";
                case AudioPhase.Stalled: return "stalled";
                case AudioPhase.Failed: return "failed";
                default: return "starting";
            }
        }
    }
}
