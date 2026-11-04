"""hand-udp-v1 encode/decode, and the shared fixtures that the Unity tests also read."""

from __future__ import annotations

import json
import struct
from pathlib import Path

import pytest
from conftest import two_hand_frame

from downbeat_tracker.protocol import (
    HAND_SIZE,
    HEADER_SIZE,
    MAGIC,
    Hand,
    ProtocolError,
    TrackedFrame,
    TrackerState,
    decode_frame,
    encode_frame,
)

FIXTURE_DIR = Path(__file__).resolve().parents[2] / "tests" / "fixtures" / "hand-udp-v1"


def frame_to_json(frame: TrackedFrame) -> dict:
    """The .json fixture format: landmarks are flat [x0, y0, z0, x1, ...] (Unity's JsonUtility reads flat arrays, not nested ones)."""
    return {
        "seq": frame.seq,
        "state": int(frame.state),
        "capture_ms": frame.capture_ms,
        "infer_end_ms": frame.infer_end_ms,
        "send_ms": frame.send_ms,
        "aspect": frame.aspect,
        "hands": [
            {"id": hand.id, "score": hand.score, "landmarks": [value for landmark in hand.landmarks for value in landmark]}
            for hand in frame.hands
        ],
    }


def frame_from_json(data: dict) -> TrackedFrame:
    hands = tuple(
        Hand(h["id"], h["score"], tuple(tuple(h["landmarks"][i : i + 3]) for i in range(0, len(h["landmarks"]), 3)))
        for h in data["hands"]
    )
    return TrackedFrame(data["seq"], TrackerState(data["state"]), data["capture_ms"], data["infer_end_ms"], data["send_ms"], data["aspect"], hands)


def test_sizes_match_the_documented_format():
    assert HEADER_SIZE == 44
    assert HAND_SIZE == 260


def test_roundtrip_two_hands(two_hands):
    data = encode_frame(two_hands)

    assert len(data) == 44 + 2 * 260
    assert decode_frame(data) == two_hands


def test_roundtrip_no_hands():
    frame = TrackedFrame(seq=7, state=TrackerState.CAMERA_MISSING, capture_ms=1.0, infer_end_ms=2.0, send_ms=3.0, aspect=1.0, hands=())

    data = encode_frame(frame)

    assert len(data) == 44
    assert decode_frame(data) == frame


def test_header_layout_is_pinned(two_hands):
    data = encode_frame(two_hands)

    assert data[0:4] == MAGIC
    assert data[4] == 1  # version
    assert data[5] == int(TrackerState.RUNNING)
    assert data[6] == 2  # hand_count
    assert data[7] == 0  # reserved
    assert struct.unpack_from("<Q", data, 8)[0] == two_hands.seq
    assert struct.unpack_from("<d", data, 16)[0] == two_hands.capture_ms
    assert struct.unpack_from("<d", data, 24)[0] == two_hands.infer_end_ms
    assert struct.unpack_from("<d", data, 32)[0] == two_hands.send_ms
    assert struct.unpack_from("<f", data, 40)[0] == two_hands.aspect
    assert struct.unpack_from("<i", data, 44)[0] == 0  # first hand id
    assert struct.unpack_from("<f", data, 48)[0] == 0.875  # first hand score
    assert struct.unpack_from("<f", data, 52)[0] == two_hands.hands[0].landmarks[0][0]  # first landmark x


def test_rejects_bad_magic(two_hands):
    data = bytearray(encode_frame(two_hands))
    data[0:4] = b"NOPE"

    with pytest.raises(ProtocolError, match="magic"):
        decode_frame(bytes(data))


def test_rejects_truncated(two_hands):
    data = encode_frame(two_hands)

    with pytest.raises(ProtocolError):
        decode_frame(data[:-1])
    with pytest.raises(ProtocolError, match="too short"):
        decode_frame(data[:10])
    with pytest.raises(ProtocolError, match="too short"):
        decode_frame(b"")


def test_rejects_trailing_bytes(two_hands):
    with pytest.raises(ProtocolError):
        decode_frame(encode_frame(two_hands) + b"\x00")


def test_rejects_unknown_version(two_hands):
    data = bytearray(encode_frame(two_hands))
    data[4] = 2

    with pytest.raises(ProtocolError, match="version"):
        decode_frame(bytes(data))


def test_rejects_more_than_two_hands(two_hands):
    data = bytearray(encode_frame(two_hands))
    data[6] = 3

    with pytest.raises(ProtocolError, match="hand_count"):
        decode_frame(bytes(data))


def test_rejects_unknown_state(two_hands):
    data = bytearray(encode_frame(two_hands))
    data[5] = 99

    with pytest.raises(ProtocolError, match="state"):
        decode_frame(bytes(data))


def test_encode_rejects_three_hands(two_hands):
    too_many = TrackedFrame(1, TrackerState.RUNNING, 0.0, 0.0, 0.0, 1.0, two_hands.hands + (two_hands.hands[0],))

    with pytest.raises(ValueError):
        encode_frame(too_many)


def test_encode_rejects_wrong_landmark_count():
    broken = TrackedFrame(1, TrackerState.RUNNING, 0.0, 0.0, 0.0, 1.0, (Hand(0, 0.5, ((0.0, 0.0, 0.0),)),))

    with pytest.raises(ValueError, match="landmarks"):
        encode_frame(broken)


def fixture_frames(two_hands: TrackedFrame) -> dict[str, TrackedFrame]:
    no_hands = TrackedFrame(seq=7, state=TrackerState.RUNNING, capture_ms=2000.25, infer_end_ms=2004.5, send_ms=2004.75, aspect=1.5, hands=())
    return {"two-hands": two_hands, "no-hands": no_hands}


def test_fixtures_match_the_encoder(two_hands, regen_fixtures):
    """The shared fixtures (Unity reads the same files) are what the encoder writes today. They are only compared; run
    `pytest --regen-fixtures` to rewrite them on purpose, after a format change."""
    frames = fixture_frames(two_hands)
    if regen_fixtures:
        FIXTURE_DIR.mkdir(parents=True, exist_ok=True)
        for name, frame in frames.items():
            (FIXTURE_DIR / f"{name}.bin").write_bytes(encode_frame(frame))
            (FIXTURE_DIR / f"{name}.json").write_text(json.dumps(frame_to_json(frame), indent=1) + "\n", encoding="utf-8")

    for name, frame in frames.items():
        bin_bytes = (FIXTURE_DIR / f"{name}.bin").read_bytes()
        json_text = (FIXTURE_DIR / f"{name}.json").read_text(encoding="utf-8")
        assert bin_bytes == encode_frame(frame), f"{name}.bin differs from the encoder output (format change? rerun with --regen-fixtures)"
        assert json_text == json.dumps(frame_to_json(frame), indent=1) + "\n", f"{name}.json differs from the expected content"
        assert decode_frame(bin_bytes) == frame_from_json(json.loads(json_text)) == frame


def test_fixture_check_leaves_the_files_alone(two_hands, regen_fixtures):
    """Without --regen-fixtures a test run does not touch the fixtures (they are shared with Unity and tracked by git)."""
    if regen_fixtures:
        pytest.skip("regenerating on purpose")
    before = {path.name: path.stat().st_mtime_ns for path in FIXTURE_DIR.iterdir()}

    test_fixtures_match_the_encoder(two_hands, False)

    assert {path.name: path.stat().st_mtime_ns for path in FIXTURE_DIR.iterdir()} == before


def test_two_hands_fixture_is_the_conftest_frame():
    assert two_hand_frame() == decode_frame((FIXTURE_DIR / "two-hands.bin").read_bytes())
