"""The tracker loop: frame -> hand landmarks -> ids -> UDP datagram. Everything that touches the world (clock, camera, model, socket) is passed in."""

from __future__ import annotations

import logging
import time
from typing import Callable, Protocol, Sequence

import numpy as np

from .camera_check import CameraCheck
from .capture import FrameSource
from .config import TrackerConfig
from .hand_ids import Hand2D, HandIdAssigner
from .heartbeat import HeartbeatSink
from .landmarker import Detection, HandDetector, ModelError
from .protocol import Hand, TrackedFrame, TrackerState
from .sender import FrameSender
from .stats import RunSummary, StatsReporter

LOG = logging.getLogger(__name__)

LOST_NOTICE_INTERVAL_SEC = 0.5  # "camera lost" is sent twice a second
REOPEN_INTERVAL_SEC = 1.0
LOST_POLL_SEC = 0.05
MS_PER_SEC = 1000.0
DETECT_ERROR_LOG_LIMIT = 5

DetectorFactory = Callable[[TrackerConfig], HandDetector]
PreviewCallback = Callable[[np.ndarray, Sequence[Hand]], bool]


class Clock(Protocol):
    def now(self) -> float: ...

    def sleep(self, seconds: float) -> None: ...


class MonotonicClock:
    def now(self) -> float:
        return time.monotonic()

    def sleep(self, seconds: float) -> None:
        if seconds > 0:
            time.sleep(seconds)


class TrackerPipeline:
    def __init__(
        self,
        config: TrackerConfig,
        source: FrameSource,
        detector_factory: DetectorFactory,
        sender: FrameSender,
        clock: Clock | None = None,
        preview: PreviewCallback | None = None,
        heartbeat: HeartbeatSink | None = None,
    ) -> None:
        self._config = config
        self._source = source
        self._detector_factory = detector_factory
        self._sender = sender
        self._clock = clock or MonotonicClock()
        self._preview = preview
        self._heartbeat = heartbeat
        self._ids = HandIdAssigner()
        self._start = self._clock.now()
        self._stats = StatsReporter(self._start)
        self._seq = 0
        self._last_timestamp = -1
        self._detect_errors = 0
        self._stopped = False
        self._camera_check = CameraCheck() if source.is_live else None

    def stop(self) -> None:
        self._stopped = True

    def summary(self) -> RunSummary:
        return self._stats.summary()

    def run(self) -> RunSummary:
        """Runs until the video ends (without --loop), --max-seconds passes or stop() is called. Returns the numbers of the run."""
        self._beat("starting")   # the model may have to be downloaded on a first run: say that the tracker is there before that long wait
        try:
            detector = self._detector_factory(self._config)
        except ModelError as error:
            LOG.error("%s", error)
            self._run_model_failed()
            return self.summary()
        try:
            self._run_frames(detector)
        finally:
            detector.close()
            self._source.close()
        return self.summary()

    def _run_frames(self, detector: HandDetector) -> None:
        if not self._source.open():
            LOG.warning("the source could not be opened (%s)", self._source.failure_state.name)
            if not self._source.is_live:
                return  # a video file that does not open is a mistake of the caller, not something to retry
        if self._camera_check is not None:
            self._camera_check.start(self._clock.now())
        interval = 1.0 / self._config.max_fps
        deadline = self._clock.now()
        last_lost_notice = float("-inf")
        last_reopen = self._clock.now()
        while not self._should_stop():
            read_start = self._clock.now()
            frame = self._source.read()
            self._beat("lost" if frame is None and self._source.is_live else "ok")   # after the read: a read() that never returns leaves no beat, and that is what the supervisor looks for
            self._check_camera(frame, read_start)
            if frame is not None:
                self._process(detector, frame)
            elif self._source.is_live:
                last_lost_notice, last_reopen = self._handle_lost_camera(last_lost_notice, last_reopen)
                continue
            elif not (self._config.loop and self._source.rewind()):
                return
            deadline = self._pace(deadline, interval)

    def _beat(self, status: str) -> None:
        if self._heartbeat is not None:
            self._heartbeat.beat(status, self._clock.now())

    def _check_camera(self, frame: np.ndarray | None, read_start: float) -> None:
        """Feeds the start-up camera check and logs its one result line, which play-with-hands.ps1 waits for."""
        check = self._camera_check
        if check is None:
            return
        now = self._clock.now()
        if frame is not None:
            check.record(frame, now - read_start)
        result = check.result(now)
        if result is not None:
            LOG.log(logging.INFO if result.ok else logging.WARNING, "%s", result.line())

    def _should_stop(self) -> bool:
        if self._stopped:
            return True
        if self._config.stop_file is not None and self._config.stop_file.exists():
            LOG.info("stop file %s found: ending", self._config.stop_file)
            self._stopped = True
            return True
        limit = self._config.max_seconds
        return limit is not None and self._clock.now() - self._start >= limit

    def _pace(self, deadline: float, interval: float) -> float:
        """Sleeps until the next frame is due. Never tries to catch up after a slow frame."""
        now = self._clock.now()
        self._report(now)
        deadline += interval
        if deadline > now:
            self._clock.sleep(deadline - now)
            return deadline
        return now

    def _report(self, now: float) -> None:
        line = self._stats.line(now)
        if line is not None:
            LOG.info("%s", line)

    def _now_ms(self) -> float:
        return (self._clock.now() - self._start) * MS_PER_SEC

    def _process(self, detector: HandDetector, bgr: np.ndarray) -> None:
        capture_ms = self._now_ms()
        detections = self._detect(detector, bgr[:, :, ::-1], capture_ms)
        infer_end_ms = self._now_ms()
        hands = self._to_hands(detections, capture_ms)
        self._send(TrackerState.RUNNING, capture_ms, infer_end_ms, hands)
        self._stats.record(infer_end_ms - capture_ms, len(hands), self._clock.now())
        if self._preview is not None and self._preview(bgr, hands):
            self.stop()

    def _detect(self, detector: HandDetector, rgb: np.ndarray, capture_ms: float) -> Sequence[Detection]:
        timestamp = max(self._last_timestamp + 1, int(capture_ms))  # VIDEO mode needs strictly increasing timestamps
        self._last_timestamp = timestamp
        try:
            return detector.detect(rgb, timestamp)
        except Exception:  # one bad frame must not stop the tracker
            self._detect_errors += 1
            if self._detect_errors <= DETECT_ERROR_LOG_LIMIT:
                LOG.exception("detection failed for a frame")
            return ()

    def _to_hands(self, detections: Sequence[Detection], t_ms: float) -> tuple[Hand, ...]:
        ids = self._ids.assign([Hand2D.from_landmarks(d.landmarks) for d in detections], t_ms)
        return tuple(Hand(ids[index], detections[index].score, detections[index].landmarks) for index in range(len(ids)))

    def _send(self, state: TrackerState, capture_ms: float, infer_end_ms: float, hands: tuple[Hand, ...]) -> None:
        frame = TrackedFrame(self._seq, state, capture_ms, infer_end_ms, self._now_ms(), self._source.aspect, hands)
        self._seq += 1
        self._sender.send(frame)

    def _handle_lost_camera(self, last_notice: float, last_reopen: float) -> tuple[float, float]:
        now = self._clock.now()
        if now - last_notice >= LOST_NOTICE_INTERVAL_SEC:
            now_ms = self._now_ms()
            self._send(self._source.failure_state, now_ms, now_ms, ())
            last_notice = now
        if now - last_reopen >= REOPEN_INTERVAL_SEC:
            self._source.open()
            last_reopen = now
        self._report(now)
        self._clock.sleep(LOST_POLL_SEC)
        return last_notice, last_reopen

    def _run_model_failed(self) -> None:
        """The model cannot be used: keep telling Unity (state 6) so it can show why there are no hands."""
        while not self._should_stop():
            self._beat("model_failed")   # alive, but with nothing to do: not a hung tracker
            now_ms = self._now_ms()
            self._send(TrackerState.MODEL_FAILED, now_ms, now_ms, ())
            self._clock.sleep(LOST_NOTICE_INTERVAL_SEC)
