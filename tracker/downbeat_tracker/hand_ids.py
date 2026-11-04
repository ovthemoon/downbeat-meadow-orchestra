"""Hand id continuity: the detector returns hands in no particular order, so each detection is matched to the hand that was nearest
in the previous frames and keeps that id (0 or 1). Same rule as the web prototype's hand-ids.js, with the plan's numbers:
a hand that jumps farther than MAX_JUMP between two frames is a new hand, and an id stays reserved for HOLD_MS for a hand that is not seen.
The clock is an argument, so the class is deterministic."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence

from .protocol import MAX_HANDS

MAX_JUMP = 0.25  # normalized image units (the image is 1 x 1 in these units)
HOLD_MS = 300.0
WRIST = 0  # MediaPipe landmark index of the wrist; ids follow the wrist


@dataclass(frozen=True)
class Hand2D:
    """Where a hand is, for matching: the wrist in normalized image coordinates."""

    x: float
    y: float

    @staticmethod
    def from_landmarks(landmarks: Sequence[Sequence[float]]) -> "Hand2D":
        wrist = landmarks[WRIST]
        return Hand2D(float(wrist[0]), float(wrist[1]))


@dataclass(frozen=True)
class _Track:
    id: int
    x: float
    y: float
    last_seen_ms: float


class HandIdAssigner:
    def __init__(self, max_hands: int = MAX_HANDS, max_jump: float = MAX_JUMP, hold_ms: float = HOLD_MS) -> None:
        self._max_hands = max_hands
        self._max_jump = max_jump
        self._hold_ms = hold_ms
        self._tracks: tuple[_Track, ...] = ()

    def reset(self) -> None:
        self._tracks = ()

    def assign(self, hands: Sequence[Hand2D], t_ms: float) -> list[int]:
        """One id per detection, in the same order, never the same id twice. Detections beyond max_hands are ignored."""
        live = tuple(track for track in self._tracks if t_ms - track.last_seen_ms <= self._hold_ms)
        detections = list(hands[: self._max_hands])
        pairs = self._best_matching(detections, live)
        ids = [-1] * len(detections)
        used: set[int] = set()
        for index, track_index in enumerate(pairs):
            if track_index != -1:
                ids[index] = live[track_index].id
                used.add(ids[index])
        for index in range(len(detections)):
            if ids[index] == -1:
                ids[index] = self._free_id(used, live)
                used.add(ids[index])
        seen = {ids[index]: _Track(ids[index], detections[index].x, detections[index].y, t_ms) for index in range(len(detections))}
        self._tracks = tuple(track for track in live if track.id not in seen) + tuple(seen.values())
        return ids

    def _best_matching(self, detections: list[Hand2D], tracks: tuple[_Track, ...]) -> list[int]:
        """Detection -> track index (-1 = new hand). Most matches first, then the smallest total distance (at most 2 x 2 to try)."""
        best_matches, best_cost, best_pairs = -1, float("inf"), [-1] * len(detections)
        pairs = [-1] * len(detections)

        def search(index: int, matches: int, cost: float, taken: frozenset[int]) -> None:
            nonlocal best_matches, best_cost, best_pairs
            if index == len(detections):
                if matches > best_matches or (matches == best_matches and cost < best_cost):
                    best_matches, best_cost, best_pairs = matches, cost, list(pairs)
                return
            pairs[index] = -1
            search(index + 1, matches, cost, taken)
            for track_index, track in enumerate(tracks):
                if track_index in taken:
                    continue
                distance = _distance(detections[index], track)
                if distance > self._max_jump:
                    continue
                pairs[index] = track_index
                search(index + 1, matches + 1, cost + distance, taken | {track_index})
                pairs[index] = -1

        search(0, 0, 0.0, frozenset())
        return best_pairs

    def _free_id(self, used: set[int], live: tuple[_Track, ...]) -> int:
        """The smallest id nobody holds; when all are held by unseen hands, the id that has been unseen the longest."""
        held = {track.id for track in live}
        for candidate in range(self._max_hands):
            if candidate not in used and candidate not in held:
                return candidate
        stale = sorted((track for track in live if track.id not in used), key=lambda track: track.last_seen_ms)
        return stale[0].id if stale else 0


def _distance(hand: Hand2D, track: _Track) -> float:
    return ((hand.x - track.x) ** 2 + (hand.y - track.y) ** 2) ** 0.5
