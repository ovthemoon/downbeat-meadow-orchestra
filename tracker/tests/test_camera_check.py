"""The start-up camera check: what it decides from read times and brightness, and that it answers exactly once."""

from __future__ import annotations

import numpy as np
import pytest

from downbeat_tracker.camera_check import CHECK_SECONDS, CameraCheck

BRIGHT = np.full((48, 64, 3), 120, dtype=np.uint8)
NEARLY_BLACK = np.full((48, 64, 3), 6, dtype=np.uint8)
START = 50.0


def checked(frame, read_sec: float, count: int):
    check = CameraCheck()
    check.start(START)
    for _ in range(count):
        check.record(frame, read_sec)
    return check.result(START + CHECK_SECONDS)


def test_a_working_camera_is_ok():
    result = checked(BRIGHT, 0.033, 90)

    assert result.status == "ok" and result.ok
    assert result.frames == 90
    assert result.read_ms == pytest.approx(33.0)
    assert result.brightness == pytest.approx(120.0)
    assert result.line() == "camera check: ok frames=90 read_ms=33 brightness=120"


def test_a_held_camera_that_hands_out_a_black_picture_once_a_second_is_slow():
    """What OpenCV's DirectShow backend did on Windows while the Camera app held the webcam: 1.0 s per read, brightness 2-10."""
    result = checked(NEARLY_BLACK, 1.0, 3)

    assert result.status == "slow"
    assert not result.ok
    assert "holding the camera" in result.line()
    assert result.line().startswith("camera check: slow frames=3 read_ms=1000 brightness=6 (")


def test_a_fast_but_black_picture_is_dark():
    result = checked(NEARLY_BLACK, 0.033, 90)

    assert result.status == "dark"
    assert "lens cover" in result.line()


def test_no_picture_at_all_is_none():
    result = checked(BRIGHT, 0.033, 0)

    assert (result.status, result.frames) == ("none", 0)
    assert "privacy settings" in result.line()


def test_one_slow_first_read_does_not_make_a_working_camera_slow():
    check = CameraCheck()
    check.start(START)
    check.record(BRIGHT, 1.2)  # a webcam can take a moment for its first picture
    for _ in range(60):
        check.record(BRIGHT, 0.033)

    assert check.result(START + CHECK_SECONDS).status == "ok"


def test_the_result_comes_once_and_only_after_the_check_time():
    check = CameraCheck()
    check.start(START)
    check.record(BRIGHT, 0.033)

    assert check.result(START + CHECK_SECONDS - 0.01) is None
    assert check.result(START + CHECK_SECONDS) is not None
    assert check.result(START + CHECK_SECONDS + 5.0) is None


def test_nothing_is_decided_before_start():
    check = CameraCheck()
    check.record(BRIGHT, 0.033)

    assert check.result(START + 100.0) is None
