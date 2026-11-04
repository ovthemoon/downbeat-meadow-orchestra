#!/usr/bin/env python3
"""Measures the hand tracker on a video file (never a camera): fps and per-frame detection time, with the datagrams received over UDP.

Run with the tracker's venv:  tracker/.venv/bin/python tools/measure_tracker_video.py [--seconds 30] [--out measurements/tracker/mac-video.json]

Two runs of the same looping video (tests/fixtures/video/hand-strikes.mp4, 640x480, 30 fps, one hand):
  paced     --max-fps 30    what the exhibition would run at; fps shows whether the tracker keeps up
  unpaced   --max-fps 1000  as fast as it can go; fps shows the capacity of this machine
"detection ms" = RGB conversion + MediaPipe detect_for_video for one frame (the tracker's capture -> inference end).
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import socket
import subprocess
import sys
import tempfile
import threading
import time
from importlib import metadata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tracker"))

from downbeat_tracker.protocol import ProtocolError, decode_frame  # noqa: E402

VIDEO = ROOT / "tests" / "fixtures" / "video" / "hand-strikes.mp4"
RUNS = (("paced", 30.0), ("unpaced", 1000.0))
RECEIVE_TIMEOUT_SEC = 0.25
GRACE_SEC = 60


def receive_all(sock: socket.socket, done: threading.Event, packets: list[bytes]) -> None:
    while True:
        try:
            packets.append(sock.recv(2048))
        except socket.timeout:
            if done.is_set():
                return


def run_once(name: str, max_fps: float, seconds: float, work: Path) -> dict:
    receiver = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    receiver.bind(("127.0.0.1", 0))
    receiver.settimeout(RECEIVE_TIMEOUT_SEC)
    receiver.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 8 * 1024 * 1024)
    port = receiver.getsockname()[1]
    stats_path = work / f"{name}.stats.json"
    packets: list[bytes] = []
    done = threading.Event()
    thread = threading.Thread(target=receive_all, args=(receiver, done, packets))
    thread.start()
    command = [sys.executable, "-m", "downbeat_tracker", "--video", str(VIDEO), "--loop", "--port", str(port), "--max-fps", str(max_fps),
               "--max-seconds", str(seconds), "--stats-json", str(stats_path), "--log", str(work / f"{name}.log")]
    load_before = os.getloadavg()
    started = time.monotonic()
    environment = {**os.environ, "DOWNBEAT_NO_CAMERA": "1"}  # this tool only ever reads the video file
    subprocess.run(command, check=True, timeout=seconds + GRACE_SEC, cwd=ROOT / "tracker", env=environment)
    wall = time.monotonic() - started
    load_after = os.getloadavg()
    done.set()
    thread.join()
    receiver.close()

    frames = []
    for packet in packets:
        try:
            frames.append(decode_frame(packet))
        except ProtocolError:
            pass
    seqs = [f.seq for f in frames]
    stats = json.loads(stats_path.read_text(encoding="utf-8"))
    return {
        "name": name,
        "maxFps": max_fps,
        "requestedSeconds": seconds,
        "wallSecondsIncludingStartup": round(wall, 2),
        "loadAvgBefore_1_5_15min": [round(v, 2) for v in load_before],
        "loadAvgAfter_1_5_15min": [round(v, 2) for v in load_after],
        "tracker": {key: (round(value, 3) if isinstance(value, float) else value) for key, value in stats.items()},
        "udp": {
            "datagramsReceived": len(frames),
            "undecodable": len(packets) - len(frames),
            "seqStrictlyIncreasing": seqs == sorted(set(seqs)),
            "seqGaps": (seqs[-1] - seqs[0] + 1 - len(seqs)) if seqs else None,
            "handFrameRatio": round(sum(1 for f in frames if f.hands) / len(frames), 4) if frames else None,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seconds", type=float, default=30.0)
    parser.add_argument("--out", type=Path, default=ROOT / "measurements" / "tracker" / "mac-video.json")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory() as tmp:
        runs = [run_once(name, fps, args.seconds, Path(tmp)) for name, fps in RUNS]
    report = {
        "what": "hand tracker on a looping video file (no camera): fps and detection ms per frame, plus the UDP datagrams received",
        "video": str(VIDEO.relative_to(ROOT)),
        "videoFacts": "640x480, 30 fps, 6 s, one CC0 hand photo moved by tools/make_strike_video.py (a picture of a hand)",
        "machine": {"platform": platform.platform(), "machine": platform.machine(), "python": platform.python_version(), "processor": platform.processor()},
        "versions": {name: metadata.version(name) for name in ("mediapipe", "numpy", "opencv-contrib-python")},
        "delegate": "CPU (MediaPipe Tasks default on macOS; XNNPACK)",
        "note": "shared Mac (other Unity editors and apps were running, see the load averages); the MediaPipe C++ start-up lines on stderr are its own logging",
        "runs": runs,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
