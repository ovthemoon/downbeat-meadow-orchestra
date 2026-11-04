"""A short check of a live camera right after it opens: does it really deliver pictures, how fast, and how bright.

A camera that another program holds (on Windows for example the Camera app, a video call or OBS) still "opens". OpenCV's DirectShow
backend then hands out an almost black picture once a second, each time its wait for a new frame times out, and Media Foundation
fails every read. Nothing raises an error, so without this check the tracker runs quietly and no hand is ever found.
The result is one log line that tools/windows/play-with-hands.ps1 waits for:
"camera check: ok|slow|dark|none frames=N read_ms=R brightness=B (what to check)"."""

from __future__ import annotations

import statistics
from dataclasses import dataclass

import numpy as np

CHECK_SECONDS = 3.0
SLOW_READ_SEC = 0.2  # a working webcam hands out a picture every 33-133 ms; DirectShow's timeout picture comes after 1 s
DARK_BRIGHTNESS = 16.0  # mean of 0-255: a closed lens cover, or a picture the camera never filled
SAMPLE_STEP = 8  # every 8th pixel in both directions is plenty for a mean brightness
MS_PER_SEC = 1000.0

HINTS = {
    "none": "no picture at all: another program may be using the camera, or the privacy settings block desktop apps",
    "slow": "each picture took about {read_s:.1f} s: another program (for example the Windows Camera app or a video call) is probably holding the camera",
    "dark": "the picture is almost black: check the lens cover and the light",
}


@dataclass(frozen=True)
class CameraCheckResult:
    status: str  # ok, slow, dark or none
    frames: int
    read_ms: float  # median time one read waited for a picture
    brightness: float  # mean of 0-255 over the pictures

    @property
    def ok(self) -> bool:
        return self.status == "ok"

    def line(self) -> str:
        text = f"camera check: {self.status} frames={self.frames} read_ms={self.read_ms:.0f} brightness={self.brightness:.0f}"
        hint = HINTS.get(self.status)
        return f"{text} ({hint.format(read_s=self.read_ms / MS_PER_SEC)})" if hint else text


class CameraCheck:
    """Collects the pictures of the first CHECK_SECONDS after start() and gives its result once."""

    def __init__(self, seconds: float = CHECK_SECONDS) -> None:
        self._seconds = seconds
        self._start: float | None = None
        self._read_sec: list[float] = []
        self._brightness: list[float] = []
        self._done = False

    def start(self, now_sec: float) -> None:
        if self._start is None:
            self._start = now_sec

    def record(self, frame: np.ndarray, read_sec: float) -> None:
        """One picture and how long the read waited for it."""
        if self._start is None or self._done:
            return
        self._read_sec.append(read_sec)
        self._brightness.append(float(frame[::SAMPLE_STEP, ::SAMPLE_STEP].mean()))

    def result(self, now_sec: float) -> CameraCheckResult | None:
        """The result once CHECK_SECONDS have passed since start(); None before that and every time after the first."""
        if self._start is None or self._done or now_sec - self._start < self._seconds:
            return None
        self._done = True
        frames = len(self._read_sec)
        if frames == 0:
            return CameraCheckResult("none", 0, 0.0, 0.0)
        read_sec = statistics.median(self._read_sec)
        brightness = statistics.fmean(self._brightness)
        if read_sec >= SLOW_READ_SEC:
            status = "slow"
        elif brightness < DARK_BRIGHTNESS:
            status = "dark"
        else:
            status = "ok"
        return CameraCheckResult(status, frames, read_sec * MS_PER_SEC, brightness)
