"""The same rules with real child processes (small Python programs), on the real clock with short waits."""
import sys
import time
from datetime import datetime, timezone

from conftest import make_settings
from downbeat_supervisor.config import ProcessSpec
from downbeat_supervisor.events import EventLog
from downbeat_supervisor.heartbeat import read_heartbeat
from downbeat_supervisor.process import popen_factory
from downbeat_supervisor.supervisor import Supervisor


def real_supervisor(tmp_path, *specs, **overrides):
    settings = make_settings(*specs, restart_delay_sec=0.05, poll_sec=0.02, logs_dir=str(tmp_path / "logs"), **overrides)
    events = EventLog(settings.logs_dir, lambda: datetime.now(timezone.utc))
    return Supervisor(settings, events, popen_factory(settings.logs_dir), time.monotonic, lambda: datetime.now(timezone.utc), read_heartbeat)


def spec(name, code):
    return ProcessSpec(name=name, command=(sys.executable, "-c", code), cwd=None, heartbeat_file=None, env={})


def test_a_child_that_exits_is_started_again(tmp_path):
    supervisor = real_supervisor(tmp_path, spec("quick", "import sys; sys.exit(2)"))
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline and sum(1 for r in supervisor.events.rows if r[2] == "start") < 3:
        supervisor.step()
        time.sleep(0.02)
    supervisor.shutdown()

    starts = [r for r in supervisor.events.rows if r[2] == "start"]
    exits = [r for r in supervisor.events.rows if r[2] == "exit"]
    assert len(starts) >= 3
    assert all("code=2" in r[3] for r in exits)


def test_shutdown_ends_a_child_that_would_run_for_a_minute(tmp_path):
    supervisor = real_supervisor(tmp_path, spec("sleeper", "import time; time.sleep(60)"))
    supervisor.step()
    started = time.monotonic()

    supervisor.shutdown()

    assert time.monotonic() - started < 10, "the child is stopped, not waited for"
    assert [r[2] for r in supervisor.events.rows][-1] == "supervisor_stop"


def test_a_program_that_does_not_exist_is_logged_and_retried(tmp_path):
    missing = ProcessSpec(name="ghost", command=(str(tmp_path / "no-such-program.exe"),), cwd=None, heartbeat_file=None, env={})
    supervisor = real_supervisor(tmp_path, missing)

    supervisor.step()
    time.sleep(0.1)
    supervisor.step()
    supervisor.shutdown()

    assert sum(1 for r in supervisor.events.rows if r[2] == "start_failed") == 2


def test_the_output_of_a_child_is_kept_in_a_file(tmp_path):
    supervisor = real_supervisor(tmp_path, spec("talker", "print('hello from the child')"))
    supervisor.step()
    time.sleep(0.5)
    supervisor.step()
    supervisor.shutdown()

    assert "hello from the child" in (tmp_path / "logs" / "talker.out.txt").read_text(encoding="utf-8")
