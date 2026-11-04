"""Frame sources: a camera (reopened when lost) or a video file. Frames are BGR numpy arrays and never leave this process."""

from __future__ import annotations

import logging
import os
import sys
from pathlib import Path
from typing import Any, Callable, Protocol

import numpy as np

from .protocol import TrackerState

LOG = logging.getLogger(__name__)


class FrameSource(Protocol):
    """What the pipeline needs from a source."""

    is_live: bool

    def open(self) -> bool: ...

    def read(self) -> np.ndarray | None:
        """The next BGR frame, or None when there is none (camera lost, end of file)."""

    def rewind(self) -> bool:
        """Start a video again; False when that is not possible."""

    def close(self) -> None: ...

    @property
    def aspect(self) -> float: ...

    @property
    def failure_state(self) -> TrackerState:
        """What to tell Unity while read() returns None on a live source."""


Opener = Callable[[], Any]  # returns an object with isOpened / read / release / get (cv2.VideoCapture-like)


CAMERA_BACKENDS = ("auto", "dshow", "msmf")
_CV2_API_CONSTANTS = {"dshow": "CAP_DSHOW", "msmf": "CAP_MSMF"}


def resolve_camera_api(backend: str, platform: str = sys.platform) -> str | None:
    """The OpenCV camera API to ask for: "dshow", "msmf", or None for OpenCV's own choice.

    auto is DirectShow on Windows, because the default Media Foundation API can take many seconds to open a camera (opencv/opencv#17687);
    on every other system auto is None, which keeps the plain cv2.VideoCapture(index) call."""
    if backend == "auto":
        return "dshow" if platform == "win32" else None
    return backend


MSMF_HW_TRANSFORMS_ENV = "OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS"


def prepare_camera_environment(api: str | None) -> None:
    """Media Foundation can need many seconds to start a camera while it tries hardware transforms; turning them off is the known workaround (opencv/opencv#17687), and it is what
    play-with-hands.ps1 did for -Backend msmf. OpenCV reads the variable when it starts the camera, so it is set before cv2 is imported and again right before each open (the order then
    does not matter). A value the user already chose is kept; DirectShow and the other systems do not look at it, so they are left alone."""
    if api == "msmf":
        os.environ.setdefault(MSMF_HW_TRANSFORMS_ENV, "0")


def _cv2_opener(target: int | str, api: str | None = None) -> Opener:
    def open_capture() -> Any:
        prepare_camera_environment(api)
        import cv2

        if api is None:
            return cv2.VideoCapture(target)
        return cv2.VideoCapture(target, getattr(cv2, _CV2_API_CONSTANTS[api]))

    return open_capture


class CaptureSource:
    """Shared logic of cv2.VideoCapture-based sources."""

    is_live = False

    def __init__(self, opener: Opener) -> None:
        self._opener = opener
        self._capture: Any = None
        self._aspect = 4.0 / 3.0
        self._failure_state = TrackerState.CAMERA_MISSING

    @property
    def aspect(self) -> float:
        return self._aspect

    @property
    def failure_state(self) -> TrackerState:
        return self._failure_state

    def open(self) -> bool:
        self.close()
        try:
            capture = self._opener()
        except Exception as error:  # an OpenCV backend can raise instead of returning a closed capture
            LOG.warning("could not open the source: %s", error)
            self._failure_state = TrackerState.CAMERA_ERROR
            return False
        if capture is None or not capture.isOpened():
            self._failure_state = TrackerState.CAMERA_MISSING
            return False
        self._capture = capture
        self._read_aspect(capture)
        return True

    def read(self) -> np.ndarray | None:
        if self._capture is None:
            return None
        ok, frame = self._capture.read()
        if not ok or frame is None:
            if self.is_live:
                self._failure_state = TrackerState.CAMERA_ERROR
                self.close()
            return None
        return frame

    def rewind(self) -> bool:
        return False

    def close(self) -> None:
        if self._capture is not None:
            self._capture.release()
            self._capture = None

    def _read_aspect(self, capture: Any) -> None:
        import cv2

        width = capture.get(cv2.CAP_PROP_FRAME_WIDTH)
        height = capture.get(cv2.CAP_PROP_FRAME_HEIGHT)
        if width > 0 and height > 0:
            self._aspect = float(width) / float(height)


class CameraSource(CaptureSource):
    """A webcam. When it is lost, read() returns None and the pipeline reopens it about once a second."""

    is_live = True

    def __init__(self, index: int, opener: Opener | None = None, backend: str = "auto", platform: str = sys.platform) -> None:
        super().__init__(opener or _cv2_opener(index, resolve_camera_api(backend, platform)))


class VideoFileSource(CaptureSource):
    """A video file (tests and demos without a camera). Reading is as fast as the pipeline asks; --max-fps paces it."""

    is_live = False

    def __init__(self, path: Path, opener: Opener | None = None) -> None:
        super().__init__(opener or _cv2_opener(str(path)))
        self._path = path

    def rewind(self) -> bool:
        return self.open()
