"""Saves what the camera sees to a local video file, for demo videos (--save-video). Off unless asked; nothing is sent anywhere.

The picture is mirrored, like a mirror and like the app (hand.mirrorX): the hand that moves to the player's right moves to the right in the video too. With --save-video-points the 21 hand
points and their bones are drawn in. The file has a constant frame rate (--max-fps): when the tracker is slower than that, a picture is repeated until the next one comes, so the video runs
in real time and can be laid next to a recording of the app.
"""

from __future__ import annotations

import logging
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable, Protocol, Sequence

import numpy as np

from .protocol import Hand

LOG = logging.getLogger(__name__)

# the bones of a MediaPipe hand: the wrist (0) to each finger base, then along each finger, and across the palm
HAND_BONES = (
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (5, 9), (9, 10), (10, 11), (11, 12),
    (9, 13), (13, 14), (14, 15), (15, 16),
    (13, 17), (17, 18), (18, 19), (19, 20),
    (0, 17),
)
HAND_COLORS = ((40, 190, 255), (255, 200, 60))   # BGR: amber for hand 0, sky blue for hand 1
POINT_RADIUS = 4
BONE_WIDTH = 2


class FrameWriter(Protocol):
    def write(self, picture: np.ndarray) -> None: ...

    def release(self) -> None: ...


WriterFactory = Callable[[Path, float, int, int], FrameWriter]


def open_cv_writer(path: Path, fps: float, width: int, height: int) -> FrameWriter:
    """An OpenCV video file: MJPG in .avi, otherwise MPEG-4 (mp4v), which the opencv wheels write without anything else installed."""
    import cv2

    codec = "MJPG" if path.suffix.lower() == ".avi" else "mp4v"
    writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*codec), fps, (width, height))
    if not writer.isOpened():
        raise OSError(f"the video file {path} could not be opened for writing ({codec})")
    return writer


def draw_hands(picture: np.ndarray, hands: Sequence[Hand]) -> None:
    """Draws the bones and the points of each hand onto an already mirrored picture (x is mirrored here too)."""
    import cv2

    height, width = picture.shape[:2]
    for hand in hands:
        color = HAND_COLORS[hand.id % len(HAND_COLORS)]
        points = [(int(round((1.0 - x) * (width - 1))), int(round(y * (height - 1)))) for x, y, _z in hand.landmarks]
        for a, b in HAND_BONES:
            cv2.line(picture, points[a], points[b], color, BONE_WIDTH, cv2.LINE_AA)
        for point in points:
            cv2.circle(picture, point, POINT_RADIUS, color, -1, cv2.LINE_AA)


class VideoRecorder:
    """A preview callback (frame, hands) -> False that writes the frames to a video file at a constant rate. close() finishes the file."""

    def __init__(
        self,
        path: Path,
        fps: float,
        draw_points: bool = False,
        clock: Callable[[], float] = time.monotonic,
        writer_factory: WriterFactory = open_cv_writer,
    ) -> None:
        self._path = path
        self._fps = fps
        self._step = 1.0 / fps
        self._draw_points = draw_points
        self._clock = clock
        self._writer_factory = writer_factory
        self._writer: FrameWriter | None = None
        self._failed = False
        self._next = 0.0
        self._last: np.ndarray | None = None
        self._size: tuple[int, int] | None = None
        self.frames_written = 0
        self.pictures_seen = 0

    def __call__(self, bgr: np.ndarray, hands: Sequence[Hand]) -> bool:
        if self._failed:
            return False
        now = self._clock()
        picture = np.ascontiguousarray(bgr[:, ::-1])
        if self._draw_points:
            draw_hands(picture, hands)
        if self._writer is None and not self._open(picture, now):
            return False
        if picture.shape[1::-1] != self._size:
            return False   # a camera that came back with another size: its pictures are left out rather than written at the wrong size
        self.pictures_seen += 1
        # every output slot that is due: the last one shows the new picture, the ones before it the picture that was there then
        while self._next <= now:
            due_later = self._next + self._step <= now
            self._writer.write(self._last if due_later and self._last is not None else picture)
            self.frames_written += 1
            self._next += self._step
        self._last = picture
        return False

    def _open(self, picture: np.ndarray, now: float) -> bool:
        height, width = picture.shape[:2]
        try:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            self._writer = self._writer_factory(self._path, self._fps, width, height)
        except OSError as error:
            LOG.error("not saving the camera video: %s", error)
            self._failed = True
            return False
        self._size = (width, height)
        self._next = now
        LOG.info("saving the camera video to %s (%dx%d, %.0f fps, mirrored%s), first picture at %s UTC", self._path, width, height, self._fps,
                 ", with the hand points" if self._draw_points else "", datetime.now(timezone.utc).isoformat(timespec="milliseconds"))
        return True

    def close(self) -> None:
        if self._writer is None:
            return
        self._writer.release()
        self._writer = None
        LOG.info("camera video saved: %s, %d frames (%.1f s) from %d pictures", self._path, self.frames_written, self.frames_written / self._fps, self.pictures_seen)


def chain(callbacks: Sequence[Callable[[np.ndarray, Sequence[Hand]], bool]]) -> Callable[[np.ndarray, Sequence[Hand]], bool] | None:
    """One preview callback that calls each of them in turn (all of them, every frame); it asks to stop when any of them does. None when there are none."""
    if not callbacks:
        return None
    if len(callbacks) == 1:
        return callbacks[0]

    def call_all(bgr: np.ndarray, hands: Sequence[Hand]) -> bool:
        stop = False
        for callback in callbacks:
            stop = callback(bgr, hands) or stop
        return stop

    return call_all
