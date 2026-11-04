"""Command line of the tracker: python -m downbeat_tracker --camera 0 --port 47800 [--backend auto|dshow|msmf] [--video PATH] [--loop] [--preview] [--max-fps 30] [--log PATH] [--heartbeat PATH] [--save-video PATH [--save-video-points]]."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

from .capture import CAMERA_BACKENDS

DEFAULT_PORT = 47800
DEFAULT_MAX_FPS = 30.0
DEFAULT_BACKEND = "auto"


@dataclass(frozen=True)
class TrackerConfig:
    camera: int = 0
    backend: str = DEFAULT_BACKEND
    port: int = DEFAULT_PORT
    video: Path | None = None
    loop: bool = False
    preview: bool = False
    max_fps: float = DEFAULT_MAX_FPS
    log: Path | None = None
    heartbeat: Path | None = None
    model: Path | None = None
    max_seconds: float | None = None
    stats_json: Path | None = None
    save_video: Path | None = None
    save_video_points: bool = False
    stop_file: Path | None = None


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="downbeat_tracker", description="Sends hand coordinates (never video) to the Unity app over local UDP.")
    parser.add_argument("--camera", type=int, default=0, help="camera index (ignored with --video)")
    parser.add_argument("--backend", choices=CAMERA_BACKENDS, default=DEFAULT_BACKEND, help="camera API: auto (DirectShow on Windows, OpenCV's own choice elsewhere), dshow or msmf (Windows only; ignored with --video)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="UDP target port")
    parser.add_argument("--video", type=Path, default=None, help="read this video file instead of a camera (tests, demos)")
    parser.add_argument("--loop", action="store_true", help="restart the video when it ends")
    parser.add_argument("--preview", action="store_true", help="debug window with the hand points; nothing is saved")
    parser.add_argument("--max-fps", type=float, default=DEFAULT_MAX_FPS, help="upper limit of processed frames per second")
    parser.add_argument("--log", type=Path, default=None, help="write the log to this file instead of stderr")
    parser.add_argument("--heartbeat", type=Path, default=None, help="write a heartbeat file (UTC time, camera state) here from the frame loop; the supervisor restarts a tracker whose beats stop")
    parser.add_argument("--model", type=Path, default=None, help="hand_landmarker.task to use instead of the cached download")
    parser.add_argument("--max-seconds", type=float, default=None, help="stop after this many seconds")
    parser.add_argument("--stats-json", type=Path, default=None, help="write the final numbers (fps, inference ms) to this JSON file")
    parser.add_argument("--save-video", type=Path, default=None, help="for demo videos: save what the camera sees, mirrored, to this local file (.mp4, or .avi) at --max-fps frames a second; nothing is sent")
    parser.add_argument("--save-video-points", action="store_true", help="draw the 21 hand points and their bones into the --save-video file")
    parser.add_argument("--stop-file", type=Path, default=None, help="end cleanly (and finish the --save-video file) as soon as this file exists; one left over from before is removed at the start")
    return parser


def parse_args(argv: Sequence[str] | None = None) -> TrackerConfig:
    args = build_parser().parse_args(argv)
    if not 0 < args.port < 65536:
        raise SystemExit(f"--port must be 1..65535, got {args.port}")
    if args.max_fps <= 0:
        raise SystemExit(f"--max-fps must be positive, got {args.max_fps}")
    if args.loop and args.video is None:
        raise SystemExit("--loop only makes sense with --video")
    if args.save_video_points and args.save_video is None:
        raise SystemExit("--save-video-points only makes sense with --save-video")
    return TrackerConfig(
        camera=args.camera,
        backend=args.backend,
        port=args.port,
        video=args.video,
        loop=args.loop,
        preview=args.preview,
        max_fps=args.max_fps,
        log=args.log,
        heartbeat=args.heartbeat,
        model=args.model,
        max_seconds=args.max_seconds,
        stats_json=args.stats_json,
        save_video=args.save_video,
        save_video_points=args.save_video_points,
        stop_file=args.stop_file,
    )
