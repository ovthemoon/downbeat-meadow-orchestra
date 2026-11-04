"""The files a child's output goes to (logs/<name>.out.txt) must not grow for ever: they are rotated when a process is started (everywhere) and, on Mac and Linux only, trimmed while it runs.
On Windows a running child's file is never emptied in place: the child's handle has its own file pointer, so its next write would land at the old offset and leave a hole of NUL bytes."""
import sys
import time

import pytest

from conftest import TRACKER, run_for
from downbeat_supervisor.config import ProcessSpec
from downbeat_supervisor.process import PopenProcess, rotate_output


def test_rotate_output_keeps_the_newest_generations_and_starts_a_fresh_file(tmp_path):
    log = tmp_path / "tracker.out.txt"
    for generation in ("one", "two", "three", "four"):
        log.write_text(generation * 100, encoding="utf-8")
        assert rotate_output(str(log), max_bytes=50, keep=2) is True
        assert not log.exists(), "the active file is moved away: the next process starts a new one"

    assert (tmp_path / "tracker.out.1.txt").read_text(encoding="utf-8") == "four" * 100
    assert (tmp_path / "tracker.out.2.txt").read_text(encoding="utf-8") == "three" * 100
    assert not (tmp_path / "tracker.out.3.txt").exists(), "only two generations are kept"


def test_rotate_output_leaves_a_small_or_missing_file_alone(tmp_path):
    log = tmp_path / "unity.out.txt"
    assert rotate_output(str(log), max_bytes=50, keep=2) is False
    log.write_text("short", encoding="utf-8")

    assert rotate_output(str(log), max_bytes=50, keep=2) is False
    assert log.read_text(encoding="utf-8") == "short"


SPIKE = "import sys, time\nsys.stdout.write('x' * 20000 + '\\n')\nsys.stdout.flush()\ntime.sleep(30)\n"   # one burst of 20 kB, then silence: the file is over the limit and nothing writes to it
SPIKE_BYTES = 20001
SMALL_LIMIT = 10_000


def start_spike(tmp_path):
    """A real child that has written its burst; returns (process, the active output file)."""
    spec = ProcessSpec(name="spike", command=(sys.executable, "-c", SPIKE), cwd=None, heartbeat_file=None)
    process = PopenProcess(spec, str(tmp_path))
    log = tmp_path / "spike.out.txt"
    deadline = time.monotonic() + 30
    while (not log.exists() or log.stat().st_size < SPIKE_BYTES) and time.monotonic() < deadline:
        time.sleep(0.05)
    assert log.stat().st_size == SPIKE_BYTES, "the child wrote its burst into the output file"
    return process, log


def test_on_windows_a_running_childs_output_file_is_not_emptied_in_place(tmp_path, monkeypatch):
    process, log = start_spike(tmp_path)
    try:
        before = log.read_bytes()
        with monkeypatch.context() as patched:
            patched.setattr(sys, "platform", "win32")
            trimmed = process.trim_output(max_bytes=SMALL_LIMIT, keep=2)
    finally:
        process.stop()

    assert trimmed is False
    assert log.read_bytes() == before, "the file the child holds open is left exactly as it is"
    assert not (tmp_path / "spike.out.1.txt").exists(), "no generation is made while the child runs: only the start of a process rotates"


@pytest.mark.parametrize("platform", ["darwin", "linux"])
def test_on_posix_a_running_childs_output_file_is_still_copied_and_emptied(tmp_path, monkeypatch, platform):
    process, log = start_spike(tmp_path)
    try:
        before = log.read_bytes()
        with monkeypatch.context() as patched:
            patched.setattr(sys, "platform", platform)
            trimmed = process.trim_output(max_bytes=SMALL_LIMIT, keep=2)
    finally:
        process.stop()

    assert trimmed is True
    assert (tmp_path / "spike.out.1.txt").read_bytes() == before, "the content moved to generation 1"
    assert log.stat().st_size == 0, "the active file is empty again"


def test_a_process_that_is_started_gets_a_fresh_file_when_the_old_one_is_over_the_limit(tmp_path):
    """The rotation at the start is the only one on Windows: it must happen before the child gets the file."""
    log = tmp_path / "fresh.out.txt"
    old = b"old output\n" * (5 * 1024 * 1024 // 11 + 1)
    log.write_bytes(old)
    spec = ProcessSpec(name="fresh", command=(sys.executable, "-c", "print('new run')"), cwd=None, heartbeat_file=None)

    process = PopenProcess(spec, str(tmp_path))
    deadline = time.monotonic() + 30
    while process.poll() is None and time.monotonic() < deadline:
        time.sleep(0.05)
    process.stop()

    assert (tmp_path / "fresh.out.1.txt").read_bytes() == old
    assert log.read_text(encoding="utf-8").strip() == "new run", "the new child wrote into a file that holds nothing of the old run"


CHATTY = "import sys, time\nfor i in range(400):\n    sys.stdout.write('line %05d ' % i + 'x' * 90 + '\\n')\n    sys.stdout.flush()\n    time.sleep(0.005)\n"


@pytest.mark.skipif(sys.platform == "win32", reason="a running child's file is not trimmed in place on Windows")
def test_a_running_child_cannot_fill_the_disk_with_its_output(tmp_path):
    spec = ProcessSpec(name="chatty", command=(sys.executable, "-c", CHATTY), cwd=None, heartbeat_file=None)
    process = PopenProcess(spec, str(tmp_path))
    log = tmp_path / "chatty.out.txt"
    try:
        deadline = time.monotonic() + 30
        trimmed = False
        while process.poll() is None and time.monotonic() < deadline:
            time.sleep(0.2)
            trimmed |= process.trim_output(max_bytes=10_000, keep=2)
    finally:
        process.stop()

    assert trimmed, "the output went over 10 kB and was moved to a generation of its own"
    assert (tmp_path / "chatty.out.1.txt").stat().st_size >= 10_000
    assert log.stat().st_size < 400 * 100, "the active file holds only what came after the last trim"
    assert "line 00399" in log.read_text(encoding="utf-8") + (tmp_path / "chatty.out.1.txt").read_text(encoding="utf-8"), "the child went on writing after the trim"


def test_the_supervisor_asks_every_child_to_trim_its_output_about_once_a_minute(world, make):
    supervisor = make(TRACKER)
    supervisor.step()

    run_for(world, supervisor, 185)

    assert 2 <= world.of("tracker")[0].trims <= 4


# ---- the creation flags on Windows (patched platform, as for the trim) -------------------------------------------------------------------------

def _start_with_patched_platform(monkeypatch, tmp_path, platform, priority):
    seen = {}

    class FakePopen:
        pid = 1

        def __init__(self, command, **kwargs):
            seen.update(kwargs)

        def poll(self):
            return None

    with monkeypatch.context() as patched:
        patched.setattr(sys, "platform", platform)
        patched.setattr("downbeat_supervisor.process.subprocess.Popen", FakePopen)
        spec = ProcessSpec(name="tracker", command=("python",), cwd=None, heartbeat_file=None, priority=priority)
        process = PopenProcess(spec, str(tmp_path))
        process._output.close()
    return seen


def test_on_windows_the_tracker_gets_a_process_group_and_the_above_normal_priority_class(monkeypatch, tmp_path):
    from downbeat_supervisor.process import ABOVE_NORMAL_PRIORITY_CLASS, CREATE_NEW_PROCESS_GROUP

    flags = _start_with_patched_platform(monkeypatch, tmp_path, "win32", "aboveNormal")["creationflags"]

    assert flags & CREATE_NEW_PROCESS_GROUP, "the group of its own is kept"
    assert flags & ABOVE_NORMAL_PRIORITY_CLASS
    assert flags == 0x200 | 0x8000, "the two flags of the Windows API, ORed"


def test_on_windows_a_process_without_a_priority_gets_only_its_group(monkeypatch, tmp_path):
    flags = _start_with_patched_platform(monkeypatch, tmp_path, "win32", None)["creationflags"]

    assert flags == 0x200


@pytest.mark.parametrize("platform", ["darwin", "linux"])
def test_elsewhere_there_are_no_creation_flags_and_a_session_of_its_own(monkeypatch, tmp_path, platform):
    seen = _start_with_patched_platform(monkeypatch, tmp_path, platform, "aboveNormal")

    assert "creationflags" not in seen
    assert seen["start_new_session"] is True
