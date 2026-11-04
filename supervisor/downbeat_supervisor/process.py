"""A real child process: started in a group of its own, its output kept in a file, and stopped together with everything it started."""
from __future__ import annotations

import os
import shutil
import signal
import subprocess
import sys
import time
from typing import Callable

from .config import ProcessSpec

STOP_WAIT_SEC = 5.0
CREATE_NEW_PROCESS_GROUP = 0x00000200      # the values of the Windows constants; subprocess only has the names on Windows
ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000
OUTPUT_MAX_BYTES = 5 * 1024 * 1024   # an output file over this size is moved to generation 1 when a process starts (all platforms) and, on Mac and Linux only, once a minute while it runs ...
OUTPUT_KEEP = 3                      # ... and the newest 3 older generations are kept (<name>.out.1.txt ... .3.txt). On Windows a running child's file is not trimmed, so its size is bounded only by that one run (see trim_output)


def windows_creation_flags(spec: ProcessSpec) -> int:
    """The process creation flags on Windows: a group of its own (a Ctrl+C in the supervisor's window is not forwarded: the supervisor stops its children itself) and, for a process that asks for it,
    the above-normal priority class (the hand tracker: on a laptop whose processor and graphics chip share one power budget a busy app starves it). Both are stdlib, no administrator rights."""
    flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", CREATE_NEW_PROCESS_GROUP)
    if spec.priority == "aboveNormal":
        flags |= getattr(subprocess, "ABOVE_NORMAL_PRIORITY_CLASS", ABOVE_NORMAL_PRIORITY_CLASS)
    return flags


def _generation(path: str, number: int) -> str:
    root, extension = os.path.splitext(path)
    return f"{root}.{number}{extension}"


def _shift_generations(path: str, keep: int) -> None:
    """Frees <name>.out.1.txt: .1 becomes .2, .2 becomes .3 and the one beyond `keep` is deleted."""
    oldest = _generation(path, keep)
    if os.path.exists(oldest):
        os.remove(oldest)
    for number in range(keep - 1, 0, -1):
        source = _generation(path, number)
        if os.path.exists(source):
            os.replace(source, _generation(path, number + 1))


def rotate_output(path: str, max_bytes: int = OUTPUT_MAX_BYTES, keep: int = OUTPUT_KEEP) -> bool:
    """Before a process is started: an output file over `max_bytes` becomes generation 1 (older ones move up, the oldest goes), so the new process starts a fresh one. False when nothing was done."""
    try:
        if os.path.getsize(path) <= max_bytes:
            return False
    except OSError:
        return False
    _shift_generations(path, keep)
    os.replace(path, _generation(path, 1))
    return True


class PopenProcess:
    def __init__(self, spec: ProcessSpec, logs_dir: str) -> None:
        self.spec = spec
        os.makedirs(logs_dir, exist_ok=True)
        self._output_path = os.path.join(logs_dir, f"{spec.name}.out.txt")
        try:
            rotate_output(self._output_path)   # before the file is opened for the child: on Windows this is the only rotation, a running child's file is never emptied
        except OSError:
            pass   # a file that cannot be moved just goes on growing; the start must not fail for it
        self._output = open(self._output_path, "ab")
        env = {**os.environ, **dict(spec.env)}
        kwargs: dict = {}
        if sys.platform == "win32":
            kwargs["creationflags"] = windows_creation_flags(spec)
        else:
            kwargs["start_new_session"] = True
        try:
            self._process = subprocess.Popen(list(spec.command), cwd=spec.cwd, env=env, stdin=subprocess.DEVNULL, stdout=self._output, stderr=subprocess.STDOUT, **kwargs)
        except OSError:
            self._output.close()
            raise

    @property
    def pid(self) -> int:
        return self._process.pid

    def poll(self) -> int | None:
        code = self._process.poll()
        if code is not None:
            self._close_output()
        return code

    def trim_output(self, max_bytes: int = OUTPUT_MAX_BYTES, keep: int = OUTPUT_KEEP) -> bool:
        """Mac and Linux, while the child runs: when its output file is over `max_bytes` the content is copied to generation 1 and the file is emptied in place. The child keeps writing to the same open
        file description, which has O_APPEND (the operating system seeks to the end for every write), so what it writes after this lands at the new end; the few bytes it writes between the copy and the
        empty are lost, which is fine for a log of unexpected output.

        Windows: nothing is done and False is returned, because the file of a running child must not be emptied there. The child holds a duplicated handle that shares the file object and its file
        pointer with ours, and its WriteFile calls write at that pointer (the C runtime's emulated append only follows the writes of the parent). Python's truncate puts the position back after
        SetEndOfFile, so the child's next write would land at the old offset: the file grows back with a hole of zero bytes, the log is lost and the following copies hold mostly NULs. The only
        rotation on Windows is the one at the start of a process (rotate_output in __init__, before the file is opened for the child), so the file can grow for as long as that one run lasts."""
        if sys.platform == "win32":
            return False
        if self._output.closed:
            return False
        self._output.flush()
        if os.path.getsize(self._output_path) <= max_bytes:
            return False
        _shift_generations(self._output_path, keep)
        shutil.copyfile(self._output_path, _generation(self._output_path, 1))
        self._output.truncate(0)
        return True

    def stop(self) -> None:
        if self._process.poll() is None:
            if sys.platform == "win32":
                subprocess.run(["taskkill", "/PID", str(self._process.pid), "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
            else:
                self._signal_group(signal.SIGTERM)
                deadline = time.monotonic() + STOP_WAIT_SEC
                while self._process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.05)
                if self._process.poll() is None:
                    self._signal_group(signal.SIGKILL)
            try:
                self._process.wait(timeout=STOP_WAIT_SEC)
            except subprocess.TimeoutExpired:
                pass
        self._close_output()

    def _signal_group(self, number: int) -> None:
        try:
            os.killpg(os.getpgid(self._process.pid), number)
        except (ProcessLookupError, PermissionError):
            pass

    def _close_output(self) -> None:
        if not self._output.closed:
            self._output.close()


def popen_factory(logs_dir: str) -> Callable[[ProcessSpec], PopenProcess]:
    return lambda spec: PopenProcess(spec, logs_dir)
