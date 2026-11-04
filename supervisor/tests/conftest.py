"""Fakes for the supervisor tests: a clock that is moved by hand, processes that end when the test says so, and a heartbeat file that is a dictionary."""
from __future__ import annotations

import itertools
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone

import pytest

from downbeat_supervisor.config import ProcessSpec, Settings
from downbeat_supervisor.events import EventLog
from downbeat_supervisor.heartbeat import Heartbeat
from downbeat_supervisor.supervisor import Supervisor

UTC0 = datetime(2026, 10, 30, 1, 0, 0, tzinfo=timezone.utc)


class FakeClock:
    """Monotonic seconds and the matching UTC time, moved together."""

    def __init__(self) -> None:
        self.seconds = 1000.0

    def monotonic(self) -> float:
        return self.seconds

    def utc(self) -> datetime:
        return UTC0 + timedelta(seconds=self.seconds - 1000.0)

    def advance(self, seconds: float) -> None:
        self.seconds += seconds


@dataclass
class FakeProcess:
    spec: ProcessSpec
    pid: int
    exit_code: int | None = None
    stopped: bool = False
    trims: int = 0

    def poll(self) -> int | None:
        return self.exit_code

    def stop(self) -> None:
        self.stopped = True
        self.exit_code = -9

    def die(self, code: int = 1) -> None:
        self.exit_code = code

    def trim_output(self) -> None:
        self.trims += 1


@dataclass
class World:
    clock: FakeClock = field(default_factory=FakeClock)
    spawned: list[FakeProcess] = field(default_factory=list)
    heartbeats: dict[str, Heartbeat | None] = field(default_factory=dict)
    _pids: itertools.count = field(default_factory=lambda: itertools.count(100))

    def spawn(self, spec: ProcessSpec) -> FakeProcess:
        process = FakeProcess(spec=spec, pid=next(self._pids))
        self.spawned.append(process)
        return process

    def read_heartbeat(self, path: str) -> Heartbeat | None:
        return self.heartbeats.get(path)

    def beat(self, path: str = "hb.txt", age: float = 0.0, audio: str | None = "ok", app: str | None = "running") -> None:
        self.heartbeats[path] = Heartbeat(timestamp=self.clock.utc() - timedelta(seconds=age), audio=audio, app=app)

    def of(self, name: str) -> list[FakeProcess]:
        return [p for p in self.spawned if p.spec.name == name]


UNITY = ProcessSpec(name="unity", command=("Downbeat.exe",), cwd=None, heartbeat_file="hb.txt", env={})
TRACKER = ProcessSpec(name="tracker", command=("python", "-m", "downbeat_tracker"), cwd=None, heartbeat_file=None, env={})
TRACKER_WITH_BEATS = ProcessSpec(name="tracker", command=("python", "-m", "downbeat_tracker"), cwd=None, heartbeat_file="tracker-hb.txt", env={}, heartbeat_timeout_sec=20.0)


def make_settings(*processes: ProcessSpec, **overrides) -> Settings:
    values = dict(
        restart_delay_sec=3.0, heartbeat_timeout_sec=30.0, heartbeat_grace_sec=60.0, audio_stall_restart_sec=20.0,
        backoff_max_restarts=5, backoff_window_sec=600.0, backoff_rest_sec=60.0, poll_sec=1.0, logs_dir="logs", processes=tuple(processes),
        backoff_unstable_runs=3, backoff_max_rest_sec=600.0, stable_after_sec=120.0,
    )
    values.update(overrides)
    return Settings(**values)


@pytest.fixture
def world() -> World:
    return World()


@pytest.fixture
def make(world, tmp_path):
    def build(*processes: ProcessSpec, **overrides) -> Supervisor:
        settings = make_settings(*(processes or (UNITY, TRACKER)), **overrides)
        events = EventLog(str(tmp_path / "logs"), world.clock.utc)
        return Supervisor(settings, events, world.spawn, world.clock.monotonic, world.clock.utc, world.read_heartbeat)

    return build


def run_for(world: World, supervisor: Supervisor, seconds: float, step: float = 1.0) -> None:
    """Passes of the supervisor once a second for this long (the clock moves between them)."""
    for _ in range(int(seconds / step)):
        supervisor.step()
        world.clock.advance(step)
    supervisor.step()
