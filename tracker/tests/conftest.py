"""Shared test helpers."""

from __future__ import annotations

import pytest

from downbeat_tracker.protocol import LANDMARKS_PER_HAND, Hand, TrackedFrame, TrackerState


def dyadic_landmarks(seed: int) -> tuple[tuple[float, float, float], ...]:
    """21 landmarks whose values are exactly representable as float32 (multiples of 1/1024), so encode -> decode returns equal numbers."""
    return tuple(
        (
            (seed * 64 + index * 8) / 1024.0,
            (seed * 32 + (index * 3) % 17 * 16 + 256) / 1024.0,
            -((index + seed) % 9) / 256.0,
        )
        for index in range(LANDMARKS_PER_HAND)
    )


def two_hand_frame(seq: int = 1_234_567_890_123) -> TrackedFrame:
    return TrackedFrame(
        seq=seq,
        state=TrackerState.RUNNING,
        capture_ms=1000.5,
        infer_end_ms=1012.25,
        send_ms=1012.5,
        aspect=1.5,
        hands=(Hand(0, 0.875, dyadic_landmarks(1)), Hand(1, 0.625, dyadic_landmarks(5))),
    )


def pytest_addoption(parser: pytest.Parser) -> None:
    parser.addoption(
        "--regen-fixtures",
        action="store_true",
        default=False,
        help="rewrite the shared UDP fixtures in tests/fixtures/hand-udp-v1 from the encoder (the Unity tests read the same files); without it they are only compared",
    )


@pytest.fixture
def regen_fixtures(request: pytest.FixtureRequest) -> bool:
    return bool(request.config.getoption("--regen-fixtures"))


@pytest.fixture(autouse=True)
def never_open_a_camera(monkeypatch):
    """No test may open a camera: this machine's virtual camera belongs to the user."""
    monkeypatch.setenv("DOWNBEAT_NO_CAMERA", "1")


@pytest.fixture
def two_hands() -> TrackedFrame:
    return two_hand_frame()
