"""The tracker's own heartbeat file, written from the frame loop. The supervisor reads it: when the loop is stuck (a camera read() that never returns, as Media Foundation can do after a USB
unplug) the beats stop and the tracker is restarted. A tracker without a camera keeps beating: its loop that tries to open the camera again every second is alive.

Line 1 is the UTC time (ISO 8601 with milliseconds, the same form the Unity player writes), line 2 `camera=starting|ok|lost|model_failed` for people to read (starting: loading the model, which a first run downloads; ok: a frame was read; lost: no camera, the loop tries again; model_failed: the model cannot be used); the supervisor only looks at the time."""

from __future__ import annotations

import logging
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable, Protocol

LOG = logging.getLogger(__name__)

HEARTBEAT_INTERVAL_SEC = 2.0   # a beat at least this often while the loop runs; the supervisor allows tens of seconds


class HeartbeatSink(Protocol):
    """What the pipeline reports to: `status` is `starting` (before the model is loaded), `ok` (a frame was read), `lost` (no camera, the loop tries again) or `model_failed`; `now` is the pipeline clock in seconds."""

    def beat(self, status: str, now: float) -> None: ...


class HeartbeatWriter:
    def __init__(self, path: Path, utc_now: Callable[[], datetime] | None = None, interval_sec: float = HEARTBEAT_INTERVAL_SEC) -> None:
        self._path = path
        self._utc_now = utc_now or (lambda: datetime.now(timezone.utc))
        self._interval = interval_sec
        self._last_now = float("-inf")
        self._last_status = ""
        self._failure_logged = False

    def beat(self, status: str, now: float) -> None:
        """Writes the file unless it was written less than an interval ago with the same state. A file that cannot be written is one warning, never an error: the tracker's job is the hands."""
        if status == self._last_status and now - self._last_now < self._interval:
            return
        self._last_now = now
        self._last_status = status
        stamp = self._utc_now().astimezone(timezone.utc)
        text = f"{stamp:%Y-%m-%dT%H:%M:%S}.{stamp.microsecond // 1000:03d}Z\ncamera={status}\n"
        try:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            self._path.write_text(text, encoding="utf-8")
        except OSError as error:
            if not self._failure_logged:
                self._failure_logged = True
                LOG.warning("the heartbeat file %s cannot be written (%s); the supervisor will take the tracker for hung", self._path, error)
