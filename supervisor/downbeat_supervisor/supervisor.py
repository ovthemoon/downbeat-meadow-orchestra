"""The watch itself: start every configured process, start it again (after a short wait) when it stops, restart a process whose heartbeat stops (the player, and the tracker when a camera read
hangs) or whose sound has been gone or never came up too long, and rest when something keeps failing: a minute after too many restarts in a window, and longer and longer (up to a cap)
after several starts in a row that never stayed healthy, so that a missing sound device does not mean a restart every minute all night. It only reads the clock, the heartbeat file and the
processes through the functions it is given, so it is tested without waiting."""
from __future__ import annotations

import time
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Callable, Protocol

from .config import ProcessSpec, Settings
from .events import EventLog
from .heartbeat import Heartbeat, age_sec

FRESHNESS_TOLERANCE_SEC = 2.0   # a beat that is a moment older than the start still belongs to it (clocks of two programs)
OUTPUT_TRIM_EVERY_SEC = 60.0    # how often a running child is asked to keep its output file small (a no-op on Windows: see PopenProcess.trim_output)


class Managed(Protocol):
    pid: int

    def poll(self) -> int | None: ...

    def stop(self) -> None: ...


@dataclass
class _State:
    spec: ProcessSpec
    process: Managed | None = None
    started_at: float = 0.0
    started_utc: datetime | None = None
    next_start_at: float = 0.0
    restarts: deque = field(default_factory=deque)
    audio_bad_since: float | None = None   # since when the beat says the sound / the app is bad (stalled, failed)
    last_beat: Heartbeat | None = None   # the newest readable beat of THIS start; None until the player has written one
    healthy_since: float | None = None   # since when this start has been judged healthy without a break
    run_stable: bool = False             # this start has been healthy for stable_after_sec
    unstable_runs: int = 0               # starts in a row that ended before they were stable
    escalated: bool = False              # a backoff_escalated event was logged since the last stable start
    last_reason: str = "exit"            # why the last start ended: exit, heartbeat_missing, audio_failed ...
    last_trim_at: float = 0.0


class Supervisor:
    def __init__(
        self,
        settings: Settings,
        events: EventLog,
        spawn: Callable[[ProcessSpec], Managed],
        monotonic: Callable[[], float],
        utc_now: Callable[[], datetime],
        read_heartbeat: Callable[[str], Heartbeat | None],
    ) -> None:
        self.settings = settings
        self.events = events
        self.spawn = spawn
        self._monotonic = monotonic
        self._utc_now = utc_now
        self._read_heartbeat = read_heartbeat
        self._states = [_State(spec) for spec in settings.processes]
        self.events.log("supervisor", "supervisor_start", ", ".join(spec.name for spec in settings.processes))

    # ---- one pass -----------------------------------------------------------------------------------------------------------------------

    def step(self) -> None:
        now = self._monotonic()
        for state in self._states:
            if state.process is None:
                self._maybe_start(state, now)
            else:
                self._check(state, now)
                self._trim_output(state, now)

    def run(self, should_stop: Callable[[], bool], sleep: Callable[[float], None] = time.sleep) -> None:
        while not should_stop():
            self.step()
            sleep(self.settings.poll_sec)

    def shutdown(self) -> None:
        for state in self._states:
            if state.process is not None:
                self._stop(state)
                self.events.log(state.spec.name, "stopped", "supervisor is ending")
                state.process = None
        self.events.log("supervisor", "supervisor_stop")

    # ---- the rules ----------------------------------------------------------------------------------------------------------------------

    def _maybe_start(self, state: _State, now: float) -> None:
        if now < state.next_start_at:
            return
        try:
            state.process = self.spawn(state.spec)
        except OSError as error:
            self.events.log(state.spec.name, "start_failed", str(error))
            self._after_failed_run(state, now, "start_failed")   # a program that cannot be started is a start that never got healthy
            return
        state.started_at = now
        state.started_utc = self._utc_now()
        state.audio_bad_since = None
        state.last_beat = None
        state.healthy_since = None
        state.run_stable = False
        state.last_trim_at = now
        self.events.log(state.spec.name, "start", f"pid={state.process.pid}")

    def _check(self, state: _State, now: float) -> None:
        code = state.process.poll()
        if code is not None:
            self.events.log(state.spec.name, "exit", f"code={code}")
            self._schedule_restart(state, now, "exit")
            return
        healthy = self._check_heartbeat(state, now) if state.spec.heartbeat_file else True
        if healthy is None:
            return   # it was killed in this pass
        self._track_stability(state, now, healthy)

    def _trim_output(self, state: _State, now: float) -> None:
        trim = getattr(state.process, "trim_output", None) if state.process is not None else None
        if trim is None or now - state.last_trim_at < OUTPUT_TRIM_EVERY_SEC:
            return
        state.last_trim_at = now
        try:
            trim()
        except OSError as error:
            self.events.log(state.spec.name, "trim_failed", str(error))

    def _check_heartbeat(self, state: _State, now: float) -> bool | None:
        """Judges the beats of one process. True: healthy now. False: alive but not (yet) healthy. None: it was killed in this pass."""
        settings = self.settings
        name = state.spec.name
        timeout = state.spec.heartbeat_timeout_sec or settings.heartbeat_timeout_sec
        self._remember_beat(state)
        beat = state.last_beat
        if beat is None:
            if now - state.started_at > settings.heartbeat_grace_sec:
                self.events.log(name, "heartbeat_missing", f"no heartbeat {now - state.started_at:.0f} s after the start")
                self._kill_and_restart(state, now, "heartbeat_missing")
                return None
            return False
        age = age_sec(beat, self._utc_now())
        if age > timeout:
            self.events.log(name, "heartbeat_stale", f"last beat {age:.0f} s ago")
            self._kill_and_restart(state, now, "heartbeat_stale")
            return None
        problem = _problem_of(beat)
        if problem is not None:
            if state.audio_bad_since is None:
                state.audio_bad_since = now
            elif now - state.audio_bad_since > settings.audio_stall_restart_sec:
                self.events.log(name, problem, f"{_describe(problem)} for {now - state.audio_bad_since:.0f} s")
                self._kill_and_restart(state, now, problem)
                return None
            return False
        state.audio_bad_since = None
        if beat.app == "loading" or beat.audio == "starting":
            # Not up yet. A running audio clock proves nothing about the app (a Boot that threw leaves it loading for ever with the clock going), so only app=running ends the grace period.
            if now - state.started_at > settings.heartbeat_grace_sec:
                if beat.app == "loading":
                    self.events.log(name, "app_not_ready", f"the app was still loading {now - state.started_at:.0f} s after the start (audio={beat.audio or 'unknown'})")
                    self._kill_and_restart(state, now, "app_not_ready")
                else:
                    self.events.log(name, "audio_not_started", f"the sound was still starting {now - state.started_at:.0f} s after the start")
                    self._kill_and_restart(state, now, "audio_not_started")
                return None
            return False
        return True

    def _track_stability(self, state: _State, now: float, healthy: bool) -> None:
        """A start is stable once it has been healthy, without a break, for stable_after_sec: that ends the count of starts in a row that never got there."""
        if not healthy:
            state.healthy_since = None
            return
        if state.healthy_since is None:
            state.healthy_since = now
        if state.run_stable or now - state.healthy_since < self.settings.stable_after_sec:
            return
        state.run_stable = True
        if state.escalated:
            self.events.log(state.spec.name, "recovered", f"healthy for {self.settings.stable_after_sec:.0f} s after {state.unstable_runs} start(s) that never were: the rests are back to {self.settings.restart_delay_sec:.0f} s")
        state.unstable_runs = 0
        state.escalated = False

    def _remember_beat(self, state: _State) -> None:
        """A read that gives nothing (the file is empty while the player rewrites it, or cannot be read for a moment) is no news, not a death:
        the last good beat of this start stays, and only its growing age can condemn the player. A beat older than the start belongs to a previous run and is ignored."""
        beat = self._read_heartbeat(state.spec.heartbeat_file)
        if beat is None or state.started_utc is None:
            return
        if beat.timestamp < state.started_utc - timedelta(seconds=FRESHNESS_TOLERANCE_SEC):
            return
        if state.last_beat is None or beat.timestamp >= state.last_beat.timestamp:
            state.last_beat = beat

    def _kill_and_restart(self, state: _State, now: float, reason: str) -> None:
        self._stop(state)
        self.events.log(state.spec.name, "kill", "restarting")
        self._schedule_restart(state, now, reason)

    def _stop(self, state: _State) -> None:
        try:
            state.process.stop()
        except OSError as error:
            self.events.log(state.spec.name, "stop_failed", str(error))

    def _schedule_restart(self, state: _State, now: float, reason: str) -> None:
        state.process = None
        state.restarts.append(now)
        self._after_failed_run(state, now, reason)

    def _after_failed_run(self, state: _State, now: float, reason: str) -> None:
        """Decides when the process is started again. Two rules, the longer rest wins: more than backoff_max_restarts in backoff_window_sec rest backoff_rest_sec; and from
        backoff_unstable_runs starts in a row that never were stable on, the rest doubles with every further one, up to backoff_max_rest_sec."""
        settings = self.settings
        if state.run_stable:
            state.unstable_runs = 0
        else:
            state.unstable_runs += 1
        state.run_stable = False
        state.healthy_since = None
        state.last_reason = reason
        delay = settings.restart_delay_sec

        while state.restarts and now - state.restarts[0] > settings.backoff_window_sec:
            state.restarts.popleft()
        if len(state.restarts) > settings.backoff_max_restarts:
            self.events.log(state.spec.name, "backoff", f"{len(state.restarts)} restarts in {settings.backoff_window_sec:.0f} s: resting {settings.backoff_rest_sec:.0f} s")
            delay = max(delay, settings.backoff_rest_sec)
            state.restarts.clear()

        if state.unstable_runs >= settings.backoff_unstable_runs:
            rest = min(settings.backoff_max_rest_sec, settings.backoff_rest_sec * 2 ** (state.unstable_runs - settings.backoff_unstable_runs))
            delay = max(delay, rest)
            state.escalated = True
            self.events.log(
                state.spec.name,
                "backoff_escalated",
                f"{state.unstable_runs} starts in a row never stayed healthy for {settings.stable_after_sec:.0f} s (last: {reason}): resting {delay:.0f} s; "
                f"the rest doubles with every further one up to {settings.backoff_max_rest_sec:.0f} s, and one healthy start ends it",
            )
        state.next_start_at = now + delay


def _problem_of(beat: Heartbeat) -> str | None:
    """What a beat says is wrong although the process is alive and beating: the name of the event it leads to."""
    if beat.audio == "failed":
        return "audio_failed"
    if beat.audio == "stalled":
        return "audio_stalled"
    if beat.app_failed:
        return "app_failed"
    return None


def _describe(problem: str) -> str:
    return {
        "audio_failed": "the sound was given up",
        "audio_stalled": "the audio clock stopped",
        "app_failed": "the app says it failed",
    }[problem]
