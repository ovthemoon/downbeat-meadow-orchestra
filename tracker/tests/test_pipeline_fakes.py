"""The tracker loop with a fake clock, source, detector and sender: what is sent, and when, in every situation."""

from __future__ import annotations

import logging

import numpy as np
import pytest

from downbeat_tracker.config import TrackerConfig
from downbeat_tracker.landmarker import Detection, ModelError
from downbeat_tracker.pipeline import TrackerPipeline
from downbeat_tracker.protocol import TrackedFrame, TrackerState

BLANK = np.zeros((4, 4, 3), dtype=np.uint8)
WRIST_LEFT = Detection(0.9, tuple((0.2, 0.5, 0.0) for _ in range(21)))
WRIST_RIGHT = Detection(0.8, tuple((0.8, 0.5, 0.0) for _ in range(21)))


class FakeClock:
    def __init__(self) -> None:
        self.t = 100.0

    def now(self) -> float:
        return self.t

    def sleep(self, seconds: float) -> None:
        self.t += seconds


class CollectingSender:
    def __init__(self) -> None:
        self.frames: list[TrackedFrame] = []

    def send(self, frame: TrackedFrame) -> None:
        self.frames.append(frame)

    def close(self) -> None:
        pass


class FakeDetector:
    def __init__(self, script=None, fail_on=()) -> None:
        self._script = list(script or [])
        self._fail_on = set(fail_on)
        self.timestamps: list[int] = []
        self.closed = False

    def detect(self, rgb, timestamp_ms):
        self.timestamps.append(timestamp_ms)
        call = len(self.timestamps) - 1
        if call in self._fail_on:
            raise RuntimeError("detector exploded")
        return self._script[call] if call < len(self._script) else ()

    def close(self) -> None:
        self.closed = True


class FakeVideoSource:
    is_live = False
    aspect = 1.5
    failure_state = TrackerState.CAMERA_MISSING

    def __init__(self, frame_count: int, opens: bool = True) -> None:
        self._frame_count = frame_count
        self._left = frame_count
        self._opens = opens
        self.rewinds = 0

    def open(self) -> bool:
        return self._opens

    def read(self):
        if self._left == 0:
            return None
        self._left -= 1
        return BLANK

    def rewind(self) -> bool:
        self.rewinds += 1
        self._left = self._frame_count
        return True

    def close(self) -> None:
        pass


class FakeCamera:
    """A live camera that works until `lost_at` (clock time), is gone until `back_at`, and then works again."""

    is_live = True
    aspect = 1.5

    def __init__(self, clock: FakeClock, lost_at: float, back_at: float, opens_at_start: bool = True, picture: np.ndarray = BLANK) -> None:
        self._clock = clock
        self._lost_at = lost_at
        self._back_at = back_at
        self._opens_at_start = opens_at_start
        self._picture = picture
        self._open = False
        self.failure_state = TrackerState.CAMERA_MISSING
        self.open_calls = 0

    def _available(self) -> bool:
        now = self._clock.now()
        return now < self._lost_at or now >= self._back_at

    def open(self) -> bool:
        self.open_calls += 1
        self._open = self._available() and (self._opens_at_start or self.open_calls > 1)
        if not self._open:
            self.failure_state = TrackerState.CAMERA_MISSING
        return self._open

    def read(self):
        if not self._open:
            return None
        if not self._available():
            self._open = False
            self.failure_state = TrackerState.CAMERA_ERROR
            return None
        return self._picture

    def rewind(self) -> bool:
        return False

    def close(self) -> None:
        pass


class FakeHeldCamera:
    """A camera another program holds, as OpenCV's DirectShow backend sees it: it opens, and every read waits 1 s and gives an almost black picture."""

    is_live = True
    aspect = 4.0 / 3.0
    failure_state = TrackerState.CAMERA_ERROR

    def __init__(self, clock: FakeClock) -> None:
        self._clock = clock

    def open(self) -> bool:
        return True

    def read(self):
        self._clock.t += 1.0
        return np.full((48, 64, 3), 6, dtype=np.uint8)

    def rewind(self) -> bool:
        return False

    def close(self) -> None:
        pass


def run(source, detector=None, clock=None, config=None, factory=None):
    clock = clock or FakeClock()
    sender = CollectingSender()
    detector = detector or FakeDetector()
    pipeline = TrackerPipeline(config or TrackerConfig(max_fps=30.0, max_seconds=10.0), source, factory or (lambda _c: detector), sender, clock)
    summary = pipeline.run()
    return sender.frames, summary, detector


def test_every_frame_is_sent_even_when_there_is_no_hand():
    frames, summary, detector = run(FakeVideoSource(5))

    assert len(frames) == 5
    assert all(f.state == TrackerState.RUNNING and f.hands == () for f in frames)
    assert [f.seq for f in frames] == [0, 1, 2, 3, 4]
    assert summary.frames == 5
    assert detector.closed


def test_hands_get_ids_from_the_assigner_and_keep_them():
    script = [(WRIST_LEFT, WRIST_RIGHT), (WRIST_RIGHT, WRIST_LEFT), (WRIST_LEFT,)]

    frames, _, _ = run(FakeVideoSource(3), FakeDetector(script))

    assert [[h.id for h in f.hands] for f in frames] == [[0, 1], [1, 0], [0]]
    assert frames[0].hands[0].score == pytest.approx(0.9)


def test_times_never_go_backwards_and_the_model_timestamps_strictly_increase():
    frames, _, detector = run(FakeVideoSource(20), config=TrackerConfig(max_fps=1000.0, max_seconds=10.0))

    assert all(a.capture_ms <= b.capture_ms for a, b in zip(frames, frames[1:]))
    assert all(f.capture_ms <= f.infer_end_ms <= f.send_ms for f in frames)
    assert all(b > a for a, b in zip(detector.timestamps, detector.timestamps[1:]))


def test_frames_are_paced_to_max_fps():
    frames, _, _ = run(FakeVideoSource(31), config=TrackerConfig(max_fps=30.0, max_seconds=10.0))

    spans = frames[-1].capture_ms - frames[0].capture_ms
    assert spans == pytest.approx(30 * 1000.0 / 30.0, rel=0.02)


def test_a_detector_error_costs_one_frame_of_hands_not_the_tracker():
    frames, _, _ = run(FakeVideoSource(4), FakeDetector([(WRIST_LEFT,)] * 4, fail_on={1}))

    assert [len(f.hands) for f in frames] == [1, 0, 1, 1]
    assert all(f.state == TrackerState.RUNNING for f in frames)


def test_a_video_stops_at_its_end_without_loop():
    source = FakeVideoSource(3)

    frames, _, _ = run(source)

    assert len(frames) == 3
    assert source.rewinds == 0


def test_a_video_restarts_with_loop_until_max_seconds():
    source = FakeVideoSource(3)

    frames, _, _ = run(source, config=TrackerConfig(loop=True, max_fps=30.0, max_seconds=1.0))

    assert source.rewinds >= 2
    assert len(frames) > 3
    assert [f.seq for f in frames] == list(range(len(frames)))  # seq keeps counting across loops


def test_a_stop_file_ends_the_run_at_the_next_frame(tmp_path, caplog):
    stop_file = tmp_path / "stop"

    class StopAfterThree(FakeVideoSource):
        def read(self):
            if self._frame_count - self._left == 3:
                stop_file.touch()
            return super().read()

    with caplog.at_level(logging.INFO):
        frames, summary, _ = run(StopAfterThree(100), config=TrackerConfig(max_fps=30.0, max_seconds=10.0, stop_file=stop_file))

    assert len(frames) == 4, "the frame read when the file appeared is still sent, then the loop ends"
    assert summary.frames == 4
    assert "stop file" in caplog.text


def test_a_video_that_does_not_open_sends_nothing():
    frames, summary, _ = run(FakeVideoSource(3, opens=False))

    assert frames == []
    assert summary.frames == 0


def test_camera_loss_is_announced_twice_a_second_and_the_camera_is_reopened_every_second():
    clock = FakeClock()
    camera = FakeCamera(clock, lost_at=clock.t + 1.0, back_at=clock.t + 4.0)

    frames, _, _ = run(camera, clock=clock, config=TrackerConfig(max_fps=30.0, max_seconds=6.0))

    lost = [f for f in frames if f.state in (TrackerState.CAMERA_ERROR, TrackerState.CAMERA_MISSING)]
    assert lost[0].state == TrackerState.CAMERA_ERROR
    assert 5 <= len(lost) <= 7, "about two notices per second for the 3 s the camera is gone"
    assert all(f.hands == () for f in lost)
    assert 3 <= camera.open_calls <= 5
    assert frames[-1].state == TrackerState.RUNNING, "frames resume when the camera is back"
    assert [f.seq for f in frames] == list(range(len(frames)))


def test_a_camera_that_is_missing_from_the_start_reports_state_4():
    clock = FakeClock()
    camera = FakeCamera(clock, lost_at=clock.t - 1.0, back_at=clock.t + 100.0)

    frames, _, _ = run(camera, clock=clock, config=TrackerConfig(max_fps=30.0, max_seconds=2.0))

    assert frames
    assert all(f.state == TrackerState.CAMERA_MISSING for f in frames)


def camera_check_lines(caplog) -> list[logging.LogRecord]:
    return [r for r in caplog.records if r.getMessage().startswith("camera check:")]


def test_a_held_camera_is_named_in_one_warning_and_its_pictures_are_still_sent(caplog):
    clock = FakeClock()
    caplog.set_level(logging.INFO, logger="downbeat_tracker")

    frames, _, _ = run(FakeHeldCamera(clock), clock=clock, config=TrackerConfig(max_fps=30.0, max_seconds=8.0))

    checks = camera_check_lines(caplog)
    assert len(checks) == 1
    assert checks[0].levelno == logging.WARNING
    assert checks[0].getMessage().startswith("camera check: slow frames=3 read_ms=1000")
    assert frames and all(f.state == TrackerState.RUNNING for f in frames)


def test_a_working_camera_logs_an_ok_check_line(caplog):
    clock = FakeClock()
    caplog.set_level(logging.INFO, logger="downbeat_tracker")
    camera = FakeCamera(clock, lost_at=clock.t + 100.0, back_at=clock.t + 200.0, picture=np.full((48, 64, 3), 120, dtype=np.uint8))

    run(camera, clock=clock, config=TrackerConfig(max_fps=30.0, max_seconds=5.0))

    checks = camera_check_lines(caplog)
    assert [(r.levelno, r.getMessage().split(" frames=")[0]) for r in checks] == [(logging.INFO, "camera check: ok")]


def test_a_camera_that_never_opens_is_reported_once_as_none(caplog):
    clock = FakeClock()
    caplog.set_level(logging.INFO, logger="downbeat_tracker")
    camera = FakeCamera(clock, lost_at=clock.t - 1.0, back_at=clock.t + 100.0)

    run(camera, clock=clock, config=TrackerConfig(max_fps=30.0, max_seconds=6.0))

    checks = camera_check_lines(caplog)
    assert len(checks) == 1
    assert checks[0].getMessage().startswith("camera check: none frames=0")


def test_a_video_file_gets_no_camera_check(caplog):
    caplog.set_level(logging.INFO, logger="downbeat_tracker")

    run(FakeVideoSource(200), config=TrackerConfig(max_fps=30.0, max_seconds=10.0))

    assert camera_check_lines(caplog) == []


def test_a_model_that_cannot_load_is_reported_as_state_6_until_stopped():
    def failing_factory(_config):
        raise ModelError("no model")

    source = FakeVideoSource(10)
    frames, summary, _ = run(source, factory=failing_factory, config=TrackerConfig(max_fps=30.0, max_seconds=2.0))

    assert 3 <= len(frames) <= 5
    assert all(f.state == TrackerState.MODEL_FAILED and f.hands == () for f in frames)
    assert summary.frames == 0
