"""hand-udp-v1: one UDP datagram per tracked frame, little endian (see docs/protocol/hand-udp-v1.md)."""

from __future__ import annotations

import struct
from dataclasses import dataclass
from enum import IntEnum

MAGIC = b"DBH1"
VERSION = 1
MAX_HANDS = 2
LANDMARKS_PER_HAND = 21

# magic | version u8 | state u8 | hand_count u8 | reserved u8 | seq u64 | capture_ms f64 | infer_end_ms f64 | send_ms f64 | aspect f32
_HEADER = struct.Struct("<4sBBBBQdddf")
# id i32 | score f32 | 21 x (x f32, y f32, z f32)
_HAND = struct.Struct("<if" + "f" * (LANDMARKS_PER_HAND * 3))
HEADER_SIZE = _HEADER.size
HAND_SIZE = _HAND.size


class ProtocolError(ValueError):
    """A datagram is not a valid hand-udp-v1 packet."""


class TrackerState(IntEnum):
    """Same numbers as Downbeat.Core.TrackerState in Unity."""

    OFF = 0
    STARTING = 1
    RUNNING = 2
    CAMERA_DENIED = 3
    CAMERA_MISSING = 4
    CAMERA_ERROR = 5
    MODEL_FAILED = 6


Landmark = tuple[float, float, float]


@dataclass(frozen=True)
class Hand:
    """One tracked hand: id 0 or 1, handedness score, 21 landmarks in image coordinates (x, y in 0..1, not mirrored)."""

    id: int
    score: float
    landmarks: tuple[Landmark, ...]


@dataclass(frozen=True)
class TrackedFrame:
    """Everything one datagram carries. Times are milliseconds on the tracker's own monotonic clock (0 = tracker start)."""

    seq: int
    state: TrackerState
    capture_ms: float
    infer_end_ms: float
    send_ms: float
    aspect: float
    hands: tuple[Hand, ...] = ()


def encode_frame(frame: TrackedFrame) -> bytes:
    if len(frame.hands) > MAX_HANDS:
        raise ValueError(f"at most {MAX_HANDS} hands per frame, got {len(frame.hands)}")
    parts = [
        _HEADER.pack(
            MAGIC,
            VERSION,
            int(frame.state),
            len(frame.hands),
            0,
            frame.seq,
            frame.capture_ms,
            frame.infer_end_ms,
            frame.send_ms,
            frame.aspect,
        )
    ]
    parts.extend(_encode_hand(hand) for hand in frame.hands)
    return b"".join(parts)


def _encode_hand(hand: Hand) -> bytes:
    if len(hand.landmarks) != LANDMARKS_PER_HAND:
        raise ValueError(f"a hand has {LANDMARKS_PER_HAND} landmarks, got {len(hand.landmarks)}")
    flat = [value for landmark in hand.landmarks for value in landmark]
    return _HAND.pack(hand.id, hand.score, *flat)


def decode_frame(data: bytes) -> TrackedFrame:
    """Parses and validates a datagram. Anything that does not fit the format exactly raises ProtocolError."""
    if len(data) < HEADER_SIZE:
        raise ProtocolError(f"packet too short for a header: {len(data)} bytes")
    magic, version, state, hand_count, _reserved, seq, capture_ms, infer_end_ms, send_ms, aspect = _HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise ProtocolError(f"bad magic {magic!r}")
    if version != VERSION:
        raise ProtocolError(f"unsupported version {version}")
    if hand_count > MAX_HANDS:
        raise ProtocolError(f"hand_count {hand_count} is more than {MAX_HANDS}")
    expected = HEADER_SIZE + HAND_SIZE * hand_count
    if len(data) != expected:
        raise ProtocolError(f"expected {expected} bytes for {hand_count} hand(s), got {len(data)}")
    try:
        tracker_state = TrackerState(state)
    except ValueError as error:
        raise ProtocolError(f"unknown state {state}") from error
    hands = tuple(_decode_hand(data, HEADER_SIZE + HAND_SIZE * index) for index in range(hand_count))
    return TrackedFrame(seq, tracker_state, capture_ms, infer_end_ms, send_ms, aspect, hands)


def _decode_hand(data: bytes, offset: int) -> Hand:
    values = _HAND.unpack_from(data, offset)
    hand_id, score = values[0], values[1]
    flat = values[2:]
    landmarks = tuple((flat[i], flat[i + 1], flat[i + 2]) for i in range(0, len(flat), 3))
    return Hand(hand_id, score, landmarks)
