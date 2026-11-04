"""The hand model reaches MediaPipe as bytes, not as a path. MediaPipe opens a model path with a native call that can fail when the path holds
Korean letters (a Windows user name such as C:\\Users\\홍길동 is enough). Reading the file in Python has no such limit. No camera is involved."""

from __future__ import annotations

import shutil

import numpy as np
import pytest

from downbeat_tracker.landmarker import MediaPipeHandDetector, ensure_model

pytestmark = pytest.mark.slow


def test_the_model_is_given_to_mediapipe_as_bytes_and_loads_from_a_korean_folder(tmp_path, monkeypatch):
    from mediapipe.tasks import python as mp_python

    model = tmp_path / "손 모델" / "hand_landmarker.task"
    model.parent.mkdir()
    shutil.copyfile(ensure_model(), model)
    real_options = mp_python.BaseOptions
    seen: dict = {}

    def recording_options(**kwargs):
        seen.update(kwargs)
        return real_options(**kwargs)

    monkeypatch.setattr(mp_python, "BaseOptions", recording_options)

    detector = MediaPipeHandDetector(model)
    try:
        detections = detector.detect(np.zeros((120, 160, 3), dtype=np.uint8), 1)
    finally:
        detector.close()

    assert "model_asset_path" not in seen
    assert seen["model_asset_buffer"] == model.read_bytes()
    assert len(detections) == 0
