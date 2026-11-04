"""The real chain on a video file: MediaPipe -> hand ids -> UDP datagrams received by a socket. The video is a CC0 photo of a hand that
tools/make_strike_video.py moves and strikes (see NOTICE); it is a picture of a hand, not a person. No camera is involved."""

from __future__ import annotations

import json
import socket
import subprocess
import sys
import threading
from pathlib import Path

import pytest

from downbeat_tracker.__main__ import make_detector
from downbeat_tracker.capture import VideoFileSource
from downbeat_tracker.config import TrackerConfig
from downbeat_tracker.pipeline import TrackerPipeline
from downbeat_tracker.protocol import TrackerState, decode_frame
from downbeat_tracker.sender import UdpSender

VIDEO = Path(__file__).resolve().parents[2] / "tests" / "fixtures" / "video" / "hand-strikes.mp4"
MIN_HAND_FRAME_RATIO = 0.5
MIN_FRAMES = 30
RECEIVE_TIMEOUT_SEC = 0.5

pytestmark = pytest.mark.slow


def bind_receiver() -> socket.socket:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind(("127.0.0.1", 0))
    receiver.settimeout(RECEIVE_TIMEOUT_SEC)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4 * 1024 * 1024)
    return receiver


def receive_until(done: threading.Event, receiver: socket.socket) -> list[bytes]:
    packets: list[bytes] = []
    while True:
        try:
            packets.append(receiver.recv(2048))
        except socket.timeout:
            if done.is_set():
                return packets


def test_pipeline_video():
    assert VIDEO.is_file(), f"missing test video {VIDEO}"
    receiver = bind_receiver()
    port = receiver.getsockname()[1]
    config = TrackerConfig(video=VIDEO, loop=False, port=port, max_fps=240.0)
    sender = UdpSender(port)
    pipeline = TrackerPipeline(config, VideoFileSource(VIDEO), make_detector, sender)
    done = threading.Event()

    def work() -> None:
        try:
            pipeline.run()
        finally:
            done.set()

    worker = threading.Thread(target=work)
    worker.start()
    packets = receive_until(done, receiver)
    worker.join()
    sender.close()
    receiver.close()

    frames = [decode_frame(p) for p in packets]
    seqs = [f.seq for f in frames]
    with_hands = [f for f in frames if f.hands]
    assert len(frames) >= MIN_FRAMES
    assert seqs == sorted(set(seqs)), "seq increases and no datagram arrived twice"
    assert len(with_hands) / len(frames) >= MIN_HAND_FRAME_RATIO
    assert all(f.state == TrackerState.RUNNING for f in frames)
    assert all(h.id in (0, 1) for f in frames for h in f.hands)
    assert all(0.0 <= x <= 1.0 and 0.0 <= y <= 1.0 for f in with_hands for h in f.hands for x, y, _ in h.landmarks[:1]), "wrist inside the image"
    assert all(f.capture_ms <= f.infer_end_ms <= f.send_ms for f in frames)


def test_cli_runs_on_a_video_and_writes_its_numbers(tmp_path):
    receiver = bind_receiver()
    port = receiver.getsockname()[1]
    stats_path = tmp_path / "stats.json"
    log_path = tmp_path / "tracker.log"
    heartbeat_path = tmp_path / "logs" / "tracker-heartbeat.txt"

    result = subprocess.run(
        [sys.executable, "-m", "downbeat_tracker", "--video", str(VIDEO), "--port", str(port), "--max-fps", "240", "--log", str(log_path), "--stats-json", str(stats_path), "--heartbeat", str(heartbeat_path)],
        capture_output=True,
        text=True,
        timeout=120,
        check=False,
    )
    receiver.close()

    assert result.returncode == 0, result.stderr
    stats = json.loads(stats_path.read_text(encoding="utf-8"))
    assert stats["frames"] >= MIN_FRAMES
    assert stats["frames_with_hands"] / stats["frames"] >= MIN_HAND_FRAME_RATIO
    assert stats["infer_ms_p50"] > 0
    assert "tracker stopped" in log_path.read_text(encoding="utf-8")
    heartbeat = heartbeat_path.read_text(encoding="utf-8").splitlines()
    assert heartbeat[0].endswith("Z") and heartbeat[1] == "camera=ok", "the real command line writes the heartbeat file, folder and all"
