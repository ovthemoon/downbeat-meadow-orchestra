"""The guard that keeps tests (and shared machines) away from real cameras."""

from __future__ import annotations

import pytest

from downbeat_tracker.__main__ import make_source
from downbeat_tracker.capture import VideoFileSource
from downbeat_tracker.config import TrackerConfig, parse_args


def test_a_camera_request_is_refused_when_the_guard_is_set():
    with pytest.raises(SystemExit, match="DOWNBEAT_NO_CAMERA"):
        make_source(TrackerConfig(camera=0))


def test_a_video_is_still_allowed_when_the_guard_is_set(tmp_path):
    assert isinstance(make_source(TrackerConfig(video=tmp_path / "x.mp4")), VideoFileSource)


def test_the_default_command_line_asks_for_camera_0_on_port_47800():
    config = parse_args([])

    assert (config.camera, config.port, config.video, config.max_fps) == (0, 47800, None, 30.0)


def test_loop_needs_a_video():
    with pytest.raises(SystemExit):
        parse_args(["--loop"])


def test_a_bad_port_is_rejected():
    with pytest.raises(SystemExit):
        parse_args(["--port", "70000"])
