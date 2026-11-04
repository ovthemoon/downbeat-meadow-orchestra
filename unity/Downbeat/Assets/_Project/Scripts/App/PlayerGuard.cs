using System;
using System.Globalization;
using System.Threading;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>
    /// Makes sure that a player whose graphics device is lost, or whose main thread is frozen, does not stay that way. On the Windows soak (2026-11-03) the Intel driver timed out after 6 h 57 min, the player
    /// logged <c>d3d11: failed to create buffer ... [0x887A0005]</c> and then froze for the rest of the night, deaf to the close signal. Two ways to notice, one way to end (<see cref="SelfExit"/>):
    /// <list type="bullet">
    /// <item>a log line that says the device is lost (<see cref="DeviceLostLog"/>), seen by a hook on <c>Application.logMessageReceivedThreaded</c> in whatever thread logs it. Only if the hook receives the line:
    /// Unity may write its native lines (the d3d11 ones) straight to Player.log without passing them to the hook, so this path is not relied on;</item>
    /// <item>the backstop: a thread that looks once a second whether the main thread still runs frames (<see cref="StallWatchdog"/>), and ends the player 20 s after the last frame. The main thread beats every
    /// frame (<see cref="Beat"/>); the watchdog starts to count when the app is running (<see cref="MarkRunning"/>) and again when the app starts to quit (<see cref="NoteQuitting"/>).</item>
    /// </list>
    /// What a hang during quitting is covered by: the watchdog thread, for as long as the runtime keeps its background threads alive. Mono may stop them before the graphics device is torn down at the end of the
    /// shutdown, and a hang after that is not covered. The thread says when it ends (a line in the player log), and <see cref="NoteQuitting"/> says that the watch goes on, so a Player.log of a hung quit shows which case it was.
    /// With a supervisor the player is started again within seconds and the supervisor log shows the exit code; on its own it is gone instead of frozen. Never in the editor (<see cref="Begin"/> makes no guard there).
    /// <c>--stall-exit-sec 0</c> turns it off (<see cref="StallExitSetting"/>).
    /// </summary>
    public sealed class PlayerGuard
    {
        const double MillisecondsPerSecond = 1000.0;
        const string LogPrefix = "[selfexit] ";

        static PlayerGuard _current;
        static Action<int> _terminateForTests;

        readonly StallWatchdog _watchdog;
        readonly SelfExit _exit;
        readonly int _checkIntervalMs;
        readonly Action<string> _log;
        Thread _thread;
        int _stopped;
        int _quitting;

        /// <param name="log">Writes one line to the player log (the watch says when it ends); null: nothing.</param>
        public PlayerGuard(StallWatchdog watchdog, SelfExit exit, int checkIntervalMs, Action<string> log = null)
        {
            _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
            _exit = exit ?? throw new ArgumentNullException(nameof(exit));
            if (checkIntervalMs < 1) throw new ArgumentOutOfRangeException(nameof(checkIntervalMs), checkIntervalMs, "must be positive");
            _checkIntervalMs = checkIntervalMs;
            _log = log ?? (_ => { });
        }

        public StallWatchdog Watchdog => _watchdog;

        /// <summary>
        /// The guard for this run, started; null when there is none: in the editor (a game must not end the editor), or when <c>--stall-exit-sec 0</c> turned it off. The guard of an earlier run of the app in this
        /// process (a scene that is loaded again) is stopped first. <paramref name="appendRecord"/> adds a line to the record file next to the heartbeat (null: no file); <paramref name="warn"/> gets what is wrong with the command line.
        /// </summary>
        public static PlayerGuard Begin(LaunchArgs launch, bool isEditor, Action<string> appendRecord, Action<string> warn)
        {
            _current?.Stop();
            var stallSec = StallExitSetting.Resolve(launch, out var problem);
            if (problem != null) warn?.Invoke(problem);
            var terminate = _terminateForTests ?? ProcessTermination.Terminate;
            var treatAsEditor = isEditor && _terminateForTests == null;   // a test that put a stub in place of the kill may turn the guard on in the editor: nothing can be ended there
            if (!StallExitSetting.IsActive(treatAsEditor, stallSec)) return null;

            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var exit = new SelfExit(
                () => DateTime.UtcNow,
                () => (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) / (double)System.Diagnostics.Stopwatch.Frequency,
                appendRecord ?? (_ => { }),
                message => Debug.LogError(message),
                terminate);
            var guard = new PlayerGuard(new StallWatchdog(stallSec, MonotonicMs), exit, (int)(StallWatchdog.CheckIntervalSec * MillisecondsPerSecond), message => Debug.Log(message));
            guard.Start();
            _current = guard;
            return guard;
        }

        /// <summary>For tests of the real scene: until the returned scope is disposed, a guard is made in the editor too, and it ends the process by calling <paramref name="terminate"/> instead of killing it.</summary>
        public static IDisposable OverrideTerminateForTests(Action<int> terminate)
        {
            var previous = _terminateForTests;
            _terminateForTests = terminate ?? throw new ArgumentNullException(nameof(terminate));
            return new Restore(() => _terminateForTests = previous);
        }

        /// <summary>Hooks the log and starts the watchdog thread (which watches nothing until <see cref="MarkRunning"/>).</summary>
        public void Start()
        {
            if (_thread != null) return;
            Application.logMessageReceivedThreaded += OnLogMessage;
            if (!_watchdog.IsEnabled) return;
            _thread = new Thread(Watch) { IsBackground = true, Name = "Downbeat.StallWatchdog" };   // a background thread: it never keeps a process alive that wants to end
            _thread.Start();
        }

        /// <summary>Ends the watch; the thread is gone after its next look.</summary>
        public void Stop()
        {
            Volatile.Write(ref _stopped, 1);
            Application.logMessageReceivedThreaded -= OnLogMessage;
            _watchdog.Disarm();
            if (ReferenceEquals(_current, this)) _current = null;
        }

        /// <summary>The app is running: from now on a main thread that stands still is a freeze.</summary>
        public void MarkRunning() => _watchdog.Arm();

        /// <summary>One frame of the main thread.</summary>
        public void Beat() => _watchdog.Beat();

        /// <summary>
        /// The app starts to quit: it has the limit, counted from now, to be gone; a shutdown that hangs is ended like a freeze, for as long as the watchdog thread lives (see the class summary: not necessarily to the
        /// end of the shutdown).
        /// </summary>
        public void NoteQuitting()
        {
            Volatile.Write(ref _quitting, 1);
            _watchdog.Arm();
            _log(LogPrefix + "quitting: the watchdog thread goes on watching until the runtime stops it");
        }

        /// <summary>The log hook (any thread). Public for the tests, which call it with lines.</summary>
        public void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!DeviceLostLog.IsDeviceLost(condition)) return;
            _exit.Request(SelfExitReason.GraphicsDeviceLost, condition);
        }

        void Watch()
        {
            var ending = "stopped";
            try
            {
                while (Volatile.Read(ref _stopped) == 0)
                {
                    Thread.Sleep(_checkIntervalMs);
                    if (Volatile.Read(ref _stopped) != 0) return;
                    if (!_watchdog.IsStalled(out var silentSec)) continue;
                    ending = "the main loop stalled, the end of the player is requested";
                    _exit.Request(SelfExitReason.MainThreadStalled, StallDetail(silentSec));
                    return;
                }
            }
            catch (Exception e)
            {
                ending = "it failed, nothing watches the main loop any more: " + e.GetType().Name + ": " + e.Message;   // shown in the line below; a thread must not take the process down with it
            }
            finally
            {
                _log(LogPrefix + "the watchdog thread ended (" + ending + ")");
            }
        }

        string StallDetail(double silentSec)
        {
            var quitting = Volatile.Read(ref _quitting) != 0 ? " while quitting" : string.Empty;
            var suspended = _watchdog.IsWaitingAfterSuspend ? ", counted from the wake-up of a suspended process, which gets " + StallWatchdog.ResumeWindowFactor.ToString("0.##", CultureInfo.InvariantCulture) + " times the limit" : string.Empty;
            return string.Format(CultureInfo.InvariantCulture, "the main loop has not advanced for {0:0.0} s (limit {1:0.##} s{2}){3}", silentSec, _watchdog.StallSec, suspended, quitting);
        }

        /// <summary>A clock that only goes forward and that any thread may read (Time.realtimeSinceStartup is the main thread's).</summary>
        static long MonotonicMs() => System.Diagnostics.Stopwatch.GetTimestamp() / (System.Diagnostics.Stopwatch.Frequency / 1000);

        sealed class Restore : IDisposable
        {
            readonly Action _restore;

            public Restore(Action restore)
            {
                _restore = restore;
            }

            public void Dispose() => _restore();
        }
    }
}
