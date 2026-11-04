"""Which OpenCV camera API the tracker asks for: DirectShow on Windows (the default Media Foundation API can need many seconds to open a
camera, opencv/opencv#17687), the system default everywhere else. Nothing here opens a camera: cv2.VideoCapture is replaced by a recorder."""

from __future__ import annotations

import os
from pathlib import Path

import cv2
import pytest

from downbeat_tracker.__main__ import make_source
from downbeat_tracker.capture import CameraSource, VideoFileSource, resolve_camera_api
from downbeat_tracker.config import TrackerConfig, parse_args


class RecordingCapture:
    """Stands in for cv2.VideoCapture and remembers how it was called."""

    calls: list[tuple] = []

    def __init__(self, *args) -> None:
        RecordingCapture.calls.append(args)

    def isOpened(self) -> bool:  # noqa: N802 (cv2 naming)
        return True

    def get(self, _prop: int) -> float:
        return 0.0

    def release(self) -> None:
        pass


@pytest.fixture
def recorder(monkeypatch):
    RecordingCapture.calls = []
    monkeypatch.setattr(cv2, "VideoCapture", RecordingCapture)
    return RecordingCapture


@pytest.mark.parametrize(
    ("backend", "platform", "expected"),
    [
        ("auto", "win32", "dshow"),
        ("auto", "darwin", None),
        ("auto", "linux", None),
        ("dshow", "win32", "dshow"),
        ("msmf", "win32", "msmf"),
        ("msmf", "darwin", "msmf"),
    ],
)
def test_the_camera_api_follows_the_choice_and_the_platform(backend, platform, expected):
    assert resolve_camera_api(backend, platform) == expected


def test_auto_opens_the_camera_with_directshow_on_windows(recorder):
    source = CameraSource(1, backend="auto", platform="win32")

    assert source.open()
    assert recorder.calls == [(1, cv2.CAP_DSHOW)]


def test_msmf_can_still_be_chosen_on_windows(recorder):
    assert CameraSource(0, backend="msmf", platform="win32").open()
    assert recorder.calls == [(0, cv2.CAP_MSMF)]


def test_auto_keeps_the_plain_open_call_on_macos(recorder):
    assert CameraSource(0, backend="auto", platform="darwin").open()
    assert recorder.calls == [(0,)]


def test_a_video_file_never_gets_a_camera_api(recorder, tmp_path: Path):
    path = tmp_path / "x.mp4"

    assert VideoFileSource(path).open()
    assert recorder.calls == [(str(path),)]


def test_the_default_backend_is_auto():
    assert parse_args([]).backend == "auto"


def test_the_backend_option_accepts_only_known_names():
    assert parse_args(["--backend", "msmf"]).backend == "msmf"
    with pytest.raises(SystemExit):
        parse_args(["--backend", "v4l2"])


def test_make_source_hands_the_backend_to_the_camera(monkeypatch):
    seen: dict = {}

    class Recorder:
        def __init__(self, index: int, backend: str) -> None:
            seen.update(index=index, backend=backend)

    monkeypatch.delenv("DOWNBEAT_NO_CAMERA")  # safe: the camera class is replaced, nothing can open
    monkeypatch.setattr("downbeat_tracker.__main__.CameraSource", Recorder)

    make_source(TrackerConfig(camera=2, backend="dshow"))

    assert seen == {"index": 2, "backend": "dshow"}


MSMF_ENV = "OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS"


class EnvWatchingCapture(RecordingCapture):
    """Remembers what the environment said at the moment OpenCV was asked for a camera: the backend reads it while it starts."""

    seen: list[str | None] = []

    def __init__(self, *args) -> None:
        super().__init__(*args)
        EnvWatchingCapture.seen.append(os.environ.get(MSMF_ENV))


@pytest.fixture
def watcher(monkeypatch):
    EnvWatchingCapture.calls = []
    EnvWatchingCapture.seen = []
    monkeypatch.delenv(MSMF_ENV, raising=False)
    monkeypatch.setattr(cv2, "VideoCapture", EnvWatchingCapture)
    return EnvWatchingCapture


def test_media_foundation_gets_its_slow_start_workaround_before_the_camera_is_asked_for(watcher):
    assert CameraSource(0, backend="msmf", platform="win32").open()

    assert watcher.seen == ["0"], "set when OpenCV starts the camera, the way play-with-hands.ps1 did for -Backend msmf"


@pytest.mark.parametrize(("backend", "platform"), [("auto", "win32"), ("dshow", "win32"), ("auto", "darwin")])
def test_the_other_camera_apis_are_left_alone(watcher, backend, platform):
    assert CameraSource(0, backend=backend, platform=platform).open()

    assert watcher.seen == [None]


def test_a_value_the_user_chose_is_kept(watcher, monkeypatch):
    monkeypatch.setenv(MSMF_ENV, "1")

    assert CameraSource(0, backend="msmf", platform="win32").open()

    assert watcher.seen == ["1"]


def test_make_source_prepares_the_environment_before_any_camera_module_is_loaded(monkeypatch):
    monkeypatch.delenv(MSMF_ENV, raising=False)
    monkeypatch.delenv("DOWNBEAT_NO_CAMERA")  # safe: the camera class is replaced, nothing can open
    monkeypatch.setattr("downbeat_tracker.__main__.CameraSource", lambda index, backend: object())
    monkeypatch.setattr("downbeat_tracker.__main__.resolve_camera_api", lambda backend, platform=None: "msmf")

    make_source(TrackerConfig(camera=0, backend="msmf"))

    assert os.environ.get(MSMF_ENV) == "0"
