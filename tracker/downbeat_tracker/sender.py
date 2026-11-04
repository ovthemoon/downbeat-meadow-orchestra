"""UDP sender: one datagram per frame to the Unity app on the same PC."""

from __future__ import annotations

import logging
import socket
import time
from typing import Protocol

from .protocol import TrackedFrame, encode_frame

LOG = logging.getLogger(__name__)
ERROR_LOG_INTERVAL_SEC = 5.0
# The hand coordinates stay on this PC: the target is not configurable.
LOOPBACK_HOST = "127.0.0.1"


class FrameSender(Protocol):
    def send(self, frame: TrackedFrame) -> None: ...

    def close(self) -> None: ...


class UdpSender:
    def __init__(self, port: int) -> None:
        self._target = (LOOPBACK_HOST, port)
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._last_error_log = float("-inf")
        self.sent = 0

    def send(self, frame: TrackedFrame) -> None:
        try:
            self._socket.sendto(encode_frame(frame), self._target)
            self.sent += 1
        except OSError as error:
            # a UDP send can fail when nobody listens (ICMP) or the network stack hiccups; the tracker keeps going
            self._log_error(error)

    def close(self) -> None:
        self._socket.close()

    def _log_error(self, error: OSError) -> None:
        now = time.monotonic()
        if now - self._last_error_log >= ERROR_LOG_INTERVAL_SEC:
            self._last_error_log = now
            LOG.warning("UDP send to %s:%d failed: %s", *self._target, error)
