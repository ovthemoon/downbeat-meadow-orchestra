"""The tracker sends to this PC only: the target is fixed to the loopback address and cannot be changed from the command line."""

from __future__ import annotations

import socket

import pytest

from downbeat_tracker.config import build_parser, parse_args
from downbeat_tracker.protocol import encode_frame
from downbeat_tracker.sender import LOOPBACK_HOST, UdpSender


def test_host_option_is_gone():
    with pytest.raises(SystemExit):
        parse_args(["--host", "192.168.0.10"])


def test_parser_has_no_host_argument():
    options = {action.dest for action in build_parser()._actions}

    assert "host" not in options


def test_sender_always_targets_loopback(two_hands):
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind((LOOPBACK_HOST, 0))
    receiver.settimeout(2.0)
    sender = UdpSender(receiver.getsockname()[1])
    try:
        sender.send(two_hands)
        data = receiver.recv(2048)
    finally:
        sender.close()
        receiver.close()

    assert LOOPBACK_HOST == "127.0.0.1"
    assert data == encode_frame(two_hands)
