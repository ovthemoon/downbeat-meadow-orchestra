"""The heartbeat file a watched process writes: line 1 the UTC time. The player (heartbeat.txt) adds `audio=starting|ok|stalled|failed` (the sound device: not yet up, running, the clock has
stopped, given up) and `app=loading|running|failed` (the app is still booting, has reached Running, or has given up for any reason); the audio clock can run while the app never gets to
Running, so only `app=running` says the app is up. The tracker writes only the time (and a `camera=` line for people to read). The writer's main loop writes it, so a frozen process stops it."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone


AUDIO_STATES = ("starting", "ok", "stalled", "failed")
APP_STATES = ("loading", "running", "failed")


@dataclass(frozen=True)
class Heartbeat:
    timestamp: datetime
    audio: str | None = None     # one of AUDIO_STATES; None: an older player, or a process that has no sound (the tracker), or a word this version does not know
    app: str | None = None       # one of APP_STATES; None: an older player (it did not say), a process that has no such state (the tracker), or a word this version does not know

    @property
    def app_failed(self) -> bool:
        """`app=failed`: the process gave up (content, samples, sound or an error while booting) and only waits to be restarted."""
        return self.app == "failed"

    @property
    def audio_ok(self) -> bool | None:
        """True while the sound runs, False when it stalled or was given up, None when it is not known (or still starting)."""
        if self.audio == "ok":
            return True
        if self.audio in ("stalled", "failed"):
            return False
        return None


def parse_heartbeat(text: str) -> Heartbeat | None:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    if not lines:
        return None
    try:
        stamp = datetime.strptime(lines[0], "%Y-%m-%dT%H:%M:%S.%fZ").replace(tzinfo=timezone.utc)
    except ValueError:
        try:
            stamp = datetime.strptime(lines[0], "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
        except ValueError:
            return None
    audio: str | None = None
    app: str | None = None
    for line in lines[1:]:
        if line.startswith("audio="):
            word = line[len("audio="):]
            audio = word if word in AUDIO_STATES else None
        elif line.startswith("app="):
            word = line[len("app="):]
            app = word if word in APP_STATES else None
    return Heartbeat(timestamp=stamp, audio=audio, app=app)


def read_heartbeat(path: str) -> Heartbeat | None:
    """None when the file is missing, being written or not a heartbeat."""
    try:
        with open(path, encoding="utf-8") as handle:
            return parse_heartbeat(handle.read())
    except (OSError, UnicodeDecodeError):
        return None


def age_sec(heartbeat: Heartbeat, now: datetime) -> float:
    return max(0.0, (now - heartbeat.timestamp).total_seconds())
