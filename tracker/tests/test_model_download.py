"""The model is downloaded on a first run without the cached file. That can take longer than the supervisor's grace period, so the download reports its progress and the loop can keep beating."""

from __future__ import annotations

import hashlib

import pytest

from downbeat_tracker import landmarker
from downbeat_tracker.landmarker import ModelError, ensure_model

FAKE_MODEL = bytes(range(256)) * 1000        # 256,000 bytes


class FakeResponse:
    def __init__(self, data: bytes, fail_after: int | None = None) -> None:
        self._data = data
        self._position = 0
        self._fail_after = fail_after

    def __enter__(self) -> "FakeResponse":
        return self

    def __exit__(self, *_: object) -> None:
        return None

    def read(self, size: int = -1) -> bytes:
        if self._fail_after is not None and self._position >= self._fail_after:
            raise OSError("the connection was reset")
        end = len(self._data) if size < 0 else self._position + size
        chunk = self._data[self._position:end]
        self._position += len(chunk)
        return chunk


@pytest.fixture
def cache(tmp_path, monkeypatch):
    monkeypatch.setenv("DOWNBEAT_CACHE_DIR", str(tmp_path))
    monkeypatch.setattr(landmarker, "MODEL_SHA256", hashlib.sha256(FAKE_MODEL).hexdigest())
    return tmp_path


def test_the_download_reports_progress_many_times_and_keeps_the_file(cache, monkeypatch):
    monkeypatch.setattr(landmarker.urllib.request, "urlopen", lambda *_a, **_k: FakeResponse(FAKE_MODEL))
    calls: list[int] = []

    path = ensure_model(progress=lambda: calls.append(1))

    assert path.read_bytes() == FAKE_MODEL
    assert len(calls) >= 3, "once per piece that arrives, not once at the end"
    assert not (cache / "hand_landmarker.part").exists()


def test_a_cached_model_needs_no_progress_and_no_network(cache, monkeypatch):
    (cache / "hand_landmarker.task").write_bytes(FAKE_MODEL)

    def no_network(*_a, **_k):
        raise AssertionError("the network must not be used")

    monkeypatch.setattr(landmarker.urllib.request, "urlopen", no_network)
    calls: list[int] = []

    assert ensure_model(progress=lambda: calls.append(1)).read_bytes() == FAKE_MODEL
    assert calls == []


def test_a_download_that_breaks_off_leaves_no_part_file_and_is_a_model_error(cache, monkeypatch):
    monkeypatch.setattr(landmarker.urllib.request, "urlopen", lambda *_a, **_k: FakeResponse(FAKE_MODEL, fail_after=100_000))

    with pytest.raises(ModelError, match="could not download"):
        ensure_model()

    assert not (cache / "hand_landmarker.part").exists()


def test_make_detector_hands_the_progress_on_to_the_download(monkeypatch):
    from downbeat_tracker import __main__ as entry
    from downbeat_tracker.config import TrackerConfig

    seen: dict = {}

    def fake_ensure(path=None, progress=None):
        seen["progress"] = progress
        raise ModelError("stop here")

    monkeypatch.setattr(entry, "ensure_model", fake_ensure)
    marker = lambda: None  # noqa: E731

    with pytest.raises(ModelError):
        entry.make_detector(TrackerConfig(), progress=marker)

    assert seen["progress"] is marker
