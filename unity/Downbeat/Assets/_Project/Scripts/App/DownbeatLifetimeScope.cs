using Downbeat.Audio;
using Downbeat.Content;
using Downbeat.Controls;
using Downbeat.Core;
using Downbeat.Meadow;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Downbeat.App
{
    /// <summary>Composition root: which implementation plays the role of each interface on this platform.</summary>
    public sealed class DownbeatLifetimeScope : LifetimeScope
    {
        [SerializeField] MeadowStage meadow;

        /// <summary>
        /// --input hand (default) | mouse | replay [--file replay/x.json] [--loop 0]. Hand = the Python tracker over UDP on 127.0.0.1 (content hand.udpPort);
        /// when it is not running, or stops, the input falls back to the mouse.
        /// </summary>
        static void ConfigureInput(IContainerBuilder builder, LaunchArgs launch)
        {
            var requested = InputModeParser.FromArgs(launch);
            var file = launch.Get("file");
            if (string.IsNullOrEmpty(file)) file = ReplayPath.DefaultFile;
            var loop = launch.Get("loop") != "0";
            var plays = launch.TryGetDouble("replay-loops", out var loops) ? Mathf.Max(0, (int)loops) : 0; // --replay-loops N: play the recording N times in all, then stop (0 = for ever)
            var firstDelay = launch.TryGetDouble("replay-delay", out var delay) ? delay : ReplayHandSource.DefaultFirstStartDelaySec; // --replay-delay SEC: the meadow stays idle this long before the first play

            builder.Register<HandSettingsHolder>(Lifetime.Singleton);
            // the mouse picks the cell the way the lit cell is drawn: the sky row above the horizon of the meadow, the ground row below it
            // and a click on the open Tab panel plays no note
            builder.RegisterComponentOnNewGameObject<SettingsPanelView>(Lifetime.Singleton, "SettingsPanel");
            builder.Register<DebugPointerInput>(r => new DebugPointerInput(r.Resolve<IViewLayout>(), r.Resolve<PlayableRange>()) { Screen = r.Resolve<MeadowStage>(), IsBlockedAt = r.Resolve<SettingsPanelView>().IsOver }, Lifetime.Singleton);
            switch (requested)
            {
                case InputMode.Replay:
                    builder.Register<IHandSource>(r => new ReplayHandSource(r.Resolve<IAudioEngine>(), file, loop, plays, ReplayGapSchedule.Parse(launch.Get("replay-gaps")), firstDelay, r.Resolve<HandSettingsHolder>()), Lifetime.Singleton);
                    break;
                case InputMode.Mouse:
                    builder.Register<IHandSource, NullHandSource>(Lifetime.Singleton); // nobody listens on the UDP port when the mouse was asked for
                    break;
                default:
                    builder.Register<IHandSource>(r => new UdpHandSource(new UdpSocketReceiver(), r.Resolve<HandSettingsHolder>(), NowMs), Lifetime.Singleton);
                    break;
            }
            builder.Register<ConductPlayer>(r => new ConductPlayer(() => r.Resolve<IAudioEngine>().Now), Lifetime.Singleton);
            builder.Register<HandMotionTracker>(Lifetime.Singleton).As<IHandMotionView>().AsSelf();
            builder.Register<IHandInterpreter>(_ => new HandInterpreter(), Lifetime.Singleton);
            builder.Register<HandPlayInput>(Lifetime.Singleton);
            builder.Register<IPlayInput>(r => new SwitchingPlayInput(r.Resolve<DebugPointerInput>(), r.Resolve<HandPlayInput>(), requested, file, label => Debug.Log("[input] " + label)), Lifetime.Singleton);
        }

        static double NowMs() => Time.realtimeSinceStartupAsDouble * 1000.0;

        protected override void Configure(IContainerBuilder builder)
        {
            var launch = LaunchArgs.Current;
            builder.RegisterInstance(launch);
            builder.RegisterInstance(meadow);
            builder.RegisterComponentOnNewGameObject<CoroutineRunner>(Lifetime.Singleton, "CoroutineRunner");
            builder.Register<IViewLayout, ScreenViewLayout>(Lifetime.Singleton);
            builder.Register<PlayableRange>(Lifetime.Singleton);
            builder.Register<IContentRepository, ContentRepository>(Lifetime.Singleton);
            builder.Register<IAudioEngine>(_ => new NativeAudioEngine(new UnityAudioBackend()), Lifetime.Singleton);
            if (launch.Has("measure-clock"))
            {
                builder.RegisterEntryPoint<ClockMeasurement>(); // the beat-clock measurement replaces the game for this run
                return;
            }
            ConfigureInput(builder, launch);
            builder.RegisterEntryPoint<DownbeatApp>().AsSelf();
            builder.RegisterEntryPoint<RunAutomation>();
            if (launch.Has("log-frame-stats")) builder.RegisterEntryPoint<FrameStatsLogger>();
        }
    }
}
