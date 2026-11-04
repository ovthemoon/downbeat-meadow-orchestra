using System;
using System.Globalization;
using System.Threading;
using Downbeat.Core;

namespace Downbeat.App
{
    /// <summary>
    /// The player ends itself: writes why (a line in <c>exit-reasons.txt</c> next to the heartbeat, a line in the player log), then ends the process with the exit code of the reason. It is asked for from the log hook,
    /// which runs inside a log call of whatever thread logged, and from the watchdog thread; <see cref="Request"/> only starts a thread of its own and returns, and only the first request counts. Every step is passed in, so the
    /// order and the failures are tested without ending anything.
    /// <para>
    /// The point of ending itself is that a frozen player must not stay frozen, so nothing that can fail may stop the end: a step that throws is reported in the next one, and a step that does not return (the log call of a
    /// process whose main thread holds its lock, a disk that hangs) is left behind after <c>stepWaitMs</c>.
    /// </para>
    /// </summary>
    public sealed class SelfExit
    {
        /// <summary>The longest each of the two steps (record, log) may take: with the limit of 20 s and the look of the watchdog thread the player is gone 20 + 1 + 1 + 1 = 23 s after a freeze, before the supervisor's 25 s at the earliest.</summary>
        public const int DefaultStepWaitMs = 1000;

        const string LogPrefix = "[selfexit] ";

        readonly Func<DateTime> _utcNow;
        readonly Func<double> _uptimeSec;
        readonly Action<string> _appendRecord;
        readonly Action<string> _log;
        readonly Action<int> _terminate;
        readonly int _stepWaitMs;
        int _requested;

        /// <param name="utcNow">The time stamped on the record line.</param>
        /// <param name="uptimeSec">How long the player has run, in seconds (a clock that is safe to read from any thread).</param>
        /// <param name="appendRecord">Adds one line to the record file; it may throw.</param>
        /// <param name="log">Writes one line to the player log.</param>
        /// <param name="terminate">Ends the process with this exit code.</param>
        public SelfExit(Func<DateTime> utcNow, Func<double> uptimeSec, Action<string> appendRecord, Action<string> log, Action<int> terminate, int stepWaitMs = DefaultStepWaitMs)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _uptimeSec = uptimeSec ?? throw new ArgumentNullException(nameof(uptimeSec));
            _appendRecord = appendRecord ?? throw new ArgumentNullException(nameof(appendRecord));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _terminate = terminate ?? throw new ArgumentNullException(nameof(terminate));
            _stepWaitMs = stepWaitMs;
        }

        /// <summary>Somebody asked for the end (it may not have happened yet).</summary>
        public bool IsRequested => Volatile.Read(ref _requested) != 0;

        /// <summary>Starts the end and returns at once. False when the end was already asked for: the line the player writes about it contains the very text that asks (a device-lost line), and must not ask again.</summary>
        public bool Request(SelfExitReason reason, string detail)
        {
            if (Interlocked.CompareExchange(ref _requested, 1, 0) != 0) return false;
            var thread = new Thread(() => Execute(reason, detail)) { IsBackground = true, Name = "Downbeat.SelfExit" };
            thread.Start();
            return true;
        }

        void Execute(SelfExitReason reason, string detail)
        {
            var description = SelfExitReasons.Describe(reason, _uptimeSec(), detail);
            var recordProblem = RunWithin(() => _appendRecord(_utcNow().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + " " + description));
            RunWithin(() => _log(LogPrefix + description + (recordProblem == null ? string.Empty : " (could not write the record file: " + recordProblem + ")")));
            _terminate(reason.ExitCode());
        }

        /// <summary>Runs the step on a thread of its own and waits for it at most <c>stepWaitMs</c>. Null when it finished; else why it did not (the exception, or that it did not return in time).</summary>
        string RunWithin(Action step)
        {
            string problem = null;
            var worker = new Thread(() =>
            {
                try
                {
                    step();
                }
                catch (Exception e)
                {
                    // the last act of a process that is being ended: there is nothing left to tell about a failure, except in the next step's line
                    problem = e.Message;
                }
            }) { IsBackground = true, Name = "Downbeat.SelfExit.Step" };
            worker.Start();
            return worker.Join(_stepWaitMs) ? Volatile.Read(ref problem) : "no answer within " + _stepWaitMs + " ms";
        }
    }
}
