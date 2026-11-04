namespace Downbeat.Core
{
    /// <summary>
    /// How far the app has come, as the heartbeat reports it (<c>app=loading|running|failed</c>). Loading: Boot has not finished (content, meadow, samples, sound); Running: Boot finished and the
    /// game is being played; Failed: the app gave up, for any reason, including an error that escaped Boot. The audio clock can run while the app is still Loading (or for ever, when Boot
    /// stopped), so <c>audio=ok</c> never says the app is up: only <c>app=running</c> does.
    /// </summary>
    public enum AppPhase
    {
        Loading,
        Running,
        Failed,
    }

    public static class AppPhaseWords
    {
        public static string Word(this AppPhase phase)
        {
            switch (phase)
            {
                case AppPhase.Running: return "running";
                case AppPhase.Failed: return "failed";
                default: return "loading";
            }
        }
    }
}
