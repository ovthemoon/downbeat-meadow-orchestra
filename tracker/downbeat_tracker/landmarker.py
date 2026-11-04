"""MediaPipe Hand Landmarker (Tasks API, VIDEO mode, 2 hands) and the model file cache."""

from __future__ import annotations

import hashlib
import logging
import os
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Protocol, Sequence

import numpy as np

from .protocol import LANDMARKS_PER_HAND, MAX_HANDS, Landmark

LOG = logging.getLogger(__name__)

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_SHA256 = "fbc2a30080c3c557093b5ddfc334698132eb341044ccee322ccf8bcf3607cde1"  # the file the web prototype was measured with
MODEL_BYTES = 7_819_105
DOWNLOAD_TIMEOUT_SEC = 60
DOWNLOAD_CHUNK_BYTES = 64 * 1024   # the model arrives in pieces of this size; each one is reported as progress
MIN_DETECTION_CONFIDENCE = 0.5
MIN_PRESENCE_CONFIDENCE = 0.5
MIN_TRACKING_CONFIDENCE = 0.5


class ModelError(RuntimeError):
    """The hand model could not be found, downloaded or loaded."""


@dataclass(frozen=True)
class Detection:
    """One detected hand: handedness score and 21 landmarks in image coordinates."""

    score: float
    landmarks: tuple[Landmark, ...]


class HandDetector(Protocol):
    def detect(self, rgb: np.ndarray, timestamp_ms: int) -> Sequence[Detection]: ...

    def close(self) -> None: ...


def default_model_path() -> Path:
    return Path(os.environ.get("DOWNBEAT_CACHE_DIR", Path.home() / ".cache" / "downbeat")) / "hand_landmarker.task"


def ensure_model(path: Path | None = None, progress: Callable[[], None] | None = None) -> Path:
    """Returns the model file, downloading it once from the official address when it is not cached. The checksum is verified.
    `progress` is called for every piece that arrives (the tracker's heartbeat, so that a slow first download is not taken for a hung tracker)."""
    target = path or default_model_path()
    if target.is_file() and _sha256(target) == MODEL_SHA256:
        return target
    if path is not None:
        raise ModelError(f"model file missing or not the expected one: {path}")
    target.parent.mkdir(parents=True, exist_ok=True)
    LOG.info("downloading the hand model (%d bytes) from %s", MODEL_BYTES, MODEL_URL)
    temporary = target.with_suffix(".part")
    try:
        with urllib.request.urlopen(MODEL_URL, timeout=DOWNLOAD_TIMEOUT_SEC) as response, temporary.open("wb") as out:  # noqa: S310 (fixed https URL)
            while True:
                chunk = response.read(DOWNLOAD_CHUNK_BYTES)
                if not chunk:
                    break
                out.write(chunk)
                if progress is not None:
                    progress()
    except OSError as error:
        temporary.unlink(missing_ok=True)
        raise ModelError(f"could not download the hand model: {error}") from error
    if _sha256(temporary) != MODEL_SHA256:
        temporary.unlink(missing_ok=True)
        raise ModelError("the downloaded hand model does not match the expected checksum")
    temporary.replace(target)
    return target


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


class MediaPipeHandDetector:
    """Wraps mediapipe.tasks.python.vision.HandLandmarker in VIDEO mode (timestamps must increase)."""

    def __init__(self, model_path: Path) -> None:
        try:
            import mediapipe as mp
            from mediapipe.tasks import python as mp_python
            from mediapipe.tasks.python import vision

            # The model goes in as bytes: MediaPipe opens a model *path* with a native call that can fail on Korean letters in a Windows user name.
            options = vision.HandLandmarkerOptions(
                base_options=mp_python.BaseOptions(model_asset_buffer=model_path.read_bytes()),
                running_mode=vision.RunningMode.VIDEO,
                num_hands=MAX_HANDS,
                min_hand_detection_confidence=MIN_DETECTION_CONFIDENCE,
                min_hand_presence_confidence=MIN_PRESENCE_CONFIDENCE,
                min_tracking_confidence=MIN_TRACKING_CONFIDENCE,
            )
            self._mp = mp
            self._landmarker = vision.HandLandmarker.create_from_options(options)
        except Exception as error:  # any load failure means "model failed" to the caller
            raise ModelError(f"could not load the hand model: {error}") from error

    def detect(self, rgb: np.ndarray, timestamp_ms: int) -> Sequence[Detection]:
        image = self._mp.Image(image_format=self._mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb))
        result = self._landmarker.detect_for_video(image, timestamp_ms)
        detections = []
        for landmarks, handedness in zip(result.hand_landmarks, result.handedness):
            points = tuple((float(p.x), float(p.y), float(p.z)) for p in landmarks)
            if len(points) != LANDMARKS_PER_HAND:
                continue
            detections.append(Detection(float(handedness[0].score) if handedness else 0.0, points))
        return detections

    def close(self) -> None:
        self._landmarker.close()
