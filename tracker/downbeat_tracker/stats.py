"""Once-a-second stats line (fps, inference ms p50, hands) and the final numbers of a run."""

from __future__ import annotations

import math
import random
from dataclasses import dataclass
from typing import Sequence

REPORT_INTERVAL_SEC = 1.0
RESERVOIR_SIZE = 100_000   # inference times kept for the percentiles: exact for a run under about 55 minutes at 30 fps, a uniform sample of the whole run beyond that (an 8 h run kept 27 MB of floats)
SAMPLE_SEED = 20261030     # the sample is repeatable


def percentile(values: Sequence[float], fraction: float) -> float:
    """Nearest-rank percentile; 0.0 for no values."""
    if not values:
        return 0.0
    ordered = sorted(values)
    rank = max(1, math.ceil(fraction * len(ordered)))
    return ordered[min(rank, len(ordered)) - 1]


@dataclass(frozen=True)
class RunSummary:
    """seconds / fps: from the first to the last processed frame (frames - 1 intervals)."""

    frames: int
    frames_with_hands: int
    seconds: float
    fps: float
    infer_ms_p50: float
    infer_ms_p95: float
    infer_ms_max: float
    infer_ms_mean: float


class StatsReporter:
    """Collects one sample per processed frame. `line()` is the log line of the last window; `summary()` covers the whole run."""

    def __init__(self, start_sec: float, reservoir_size: int = RESERVOIR_SIZE) -> None:
        self._window_start = start_sec
        self._first_frame_sec: float | None = None
        self._last_frame_sec = start_sec
        self._window_infer: list[float] = []
        self._window_hands: list[int] = []
        self._reservoir: list[float] = []     # at most reservoir_size values, a uniform sample of every inference time so far (algorithm R)
        self._reservoir_size = reservoir_size
        self._random = random.Random(SAMPLE_SEED)
        self._infer_sum = 0.0
        self._infer_max = 0.0
        self._frames = 0
        self._frames_with_hands = 0

    def record(self, infer_ms: float, hand_count: int, now_sec: float) -> None:
        if self._first_frame_sec is None:
            self._first_frame_sec = now_sec
        self._last_frame_sec = now_sec
        self._window_infer.append(infer_ms)
        self._window_hands.append(hand_count)
        self._keep_sample(infer_ms)
        self._infer_sum += infer_ms
        self._infer_max = max(self._infer_max, infer_ms)
        self._frames += 1
        if hand_count > 0:
            self._frames_with_hands += 1

    @property
    def stored_samples(self) -> int:
        return len(self._reservoir)

    def _keep_sample(self, infer_ms: float) -> None:
        if len(self._reservoir) < self._reservoir_size:
            self._reservoir.append(infer_ms)
            return
        slot = self._random.randrange(self._frames + 1)    # this is the (frames + 1)-th value
        if slot < self._reservoir_size:
            self._reservoir[slot] = infer_ms

    def line(self, now_sec: float) -> str | None:
        """The stats line for the window that just ended, or None while less than a second has passed."""
        elapsed = now_sec - self._window_start
        if elapsed < REPORT_INTERVAL_SEC:
            return None
        fps = len(self._window_infer) / elapsed
        hands = self._window_hands[-1] if self._window_hands else 0
        text = f"fps={fps:.1f} infer_ms_p50={percentile(self._window_infer, 0.5):.1f} hands={hands}"
        self._window_start = now_sec
        self._window_infer = []
        self._window_hands = []
        return text

    def summary(self) -> RunSummary:
        """Whole-run numbers. `seconds` and `fps` span the first to the last processed frame, so start-up and a model download do not count."""
        seconds = self._last_frame_sec - self._first_frame_sec if self._first_frame_sec is not None else 0.0
        intervals = max(self._frames - 1, 0)
        infer = self._reservoir
        return RunSummary(
            frames=self._frames,
            frames_with_hands=self._frames_with_hands,
            seconds=seconds,
            fps=intervals / seconds if seconds > 0 else 0.0,
            infer_ms_p50=percentile(infer, 0.5),
            infer_ms_p95=percentile(infer, 0.95),
            infer_ms_max=self._infer_max,                                  # exact: kept on the side, not taken from the sample
            infer_ms_mean=self._infer_sum / self._frames if self._frames else 0.0,   # exact too
        )
