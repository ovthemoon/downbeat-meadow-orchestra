"""Debug window with the hand points. Nothing is saved or sent: it only draws on the screen of the PC the tracker runs on."""

from __future__ import annotations

from typing import Sequence

import numpy as np

from .protocol import Hand

WINDOW = "downbeat tracker (q to close)"
POINT_RADIUS = 3
POINT_COLOR = (0, 200, 255)
QUIT_KEY = ord("q")


def show(bgr: np.ndarray, hands: Sequence[Hand]) -> bool:
    """Draws the frame with the landmarks; returns True when the user pressed q."""
    import cv2

    canvas = bgr.copy()
    height, width = canvas.shape[:2]
    for hand in hands:
        for x, y, _z in hand.landmarks:
            cv2.circle(canvas, (int(x * width), int(y * height)), POINT_RADIUS, POINT_COLOR, -1)
        cv2.putText(canvas, f"id {hand.id}", (int(hand.landmarks[0][0] * width), int(hand.landmarks[0][1] * height)), cv2.FONT_HERSHEY_SIMPLEX, 0.6, POINT_COLOR, 2)
    cv2.imshow(WINDOW, canvas)
    return (cv2.waitKey(1) & 0xFF) == QUIT_KEY
