"""python -m downbeat_tracker --camera 0 --port 47800 [--backend auto|dshow|msmf] [--video PATH] [--loop] [--preview] [--max-fps 30] [--log PATH] [--heartbeat PATH] [--save-video PATH [--save-video-points]]"""

from __future__ import annotations

import dataclasses
import json
import logging
import os
import sys
import time
from logging.handlers import RotatingFileHandler
from pathlib import Path
from typing import Callable, Sequence

from .capture import CameraSource, FrameSource, VideoFileSource, prepare_camera_environment, resolve_camera_api
from .config import TrackerConfig, parse_args
from .heartbeat import HeartbeatWriter
from .landmarker import HandDetector, MediaPipeHandDetector, ensure_model
from .pipeline import TrackerPipeline
from .sender import LOOPBACK_HOST, UdpSender
from .stats import RunSummary
from .video_record import VideoRecorder, chain

LOG = logging.getLogger("downbeat_tracker")


def make_detector(config: TrackerConfig, progress: Callable[[], None] | None = None) -> HandDetector:
    """The hand detector; `progress` is called while the model is downloaded on a first run (the heartbeat)."""
    return MediaPipeHandDetector(ensure_model(config.model, progress=progress))


NO_CAMERA_ENV = "DOWNBEAT_NO_CAMERA"
LOG_MAX_BYTES = 5 * 1024 * 1024   # the log file may grow to this size ...
LOG_BACKUPS = 3                   # ... and 3 older generations (tracker.log.1 ... .3) are kept: 20 MB at most
ROLLOVER_RETRY_SEC = 60.0         # a rotation that was refused (a file another program has open) is tried again after this long, not on every line


def make_source(config: TrackerConfig) -> FrameSource:
    """A video file when --video is given, otherwise the camera. DOWNBEAT_NO_CAMERA=1 makes opening a camera impossible (tests, shared machines)."""
    if config.video is not None:
        return VideoFileSource(config.video)
    if os.environ.get(NO_CAMERA_ENV):
        raise SystemExit(f"a camera was requested but {NO_CAMERA_ENV} is set; pass --video")
    prepare_camera_environment(resolve_camera_api(config.backend))   # before anything of OpenCV is imported in this process
    return CameraSource(config.camera, backend=config.backend)


class _KeepWritingRotatingHandler(RotatingFileHandler):
    """A log that cannot be rotated (on Windows another program may hold the file open) goes on in the same file: a line is never lost for it. The rotation is tried again after
    ROLLOVER_RETRY_SEC, not on every line."""

    def __init__(self, *args, clock: Callable[[], float] = time.monotonic, **kwargs) -> None:
        super().__init__(*args, **kwargs)
        self._clock = clock
        self._retry_at = float("-inf")

    def shouldRollover(self, record: logging.LogRecord) -> int:  # noqa: N802 (logging naming)
        if self._clock() < self._retry_at:
            return 0
        return super().shouldRollover(record)

    def doRollover(self) -> None:  # noqa: N802 (logging naming)
        try:
            super().doRollover()
        except OSError:
            self._retry_at = self._clock() + ROLLOVER_RETRY_SEC
            if self.stream is None:
                self.stream = self._open()


def make_log_handler(log_path: Path | None, max_bytes: int = LOG_MAX_BYTES, backups: int = LOG_BACKUPS, clock: Callable[[], float] = time.monotonic) -> logging.Handler:
    """The console when there is no path, otherwise the file, rotated by size so a night cannot fill the disk; its folder is made first (a freshly unzipped bundle has no tracker/logs)."""
    if log_path is None:
        return logging.StreamHandler()
    log_path.parent.mkdir(parents=True, exist_ok=True)
    return _KeepWritingRotatingHandler(log_path, maxBytes=max_bytes, backupCount=backups, encoding="utf-8", clock=clock)


def configure_logging(config: TrackerConfig) -> None:
    handler = make_log_handler(config.log)
    logging.basicConfig(level=logging.WARNING, handlers=[handler], format="%(asctime)s %(levelname)s %(name)s: %(message)s", force=True)
    logging.getLogger("downbeat_tracker").setLevel(logging.INFO)  # third-party libraries (matplotlib via mediapipe, ...) stay at WARNING


def write_stats(config: TrackerConfig, summary: RunSummary) -> None:
    if config.stats_json is None:
        return
    config.stats_json.parent.mkdir(parents=True, exist_ok=True)
    config.stats_json.write_text(json.dumps(dataclasses.asdict(summary), indent=2) + "\n", encoding="utf-8")


def main(argv: Sequence[str] | None = None) -> int:
    config = parse_args(argv)
    configure_logging(config)
    if config.stop_file is not None:
        config.stop_file.unlink(missing_ok=True)   # one left from an earlier run would end this one at once
    source = make_source(config)
    sender = UdpSender(config.port)
    callbacks = []
    recorder = None
    if config.save_video is not None:
        recorder = VideoRecorder(config.save_video, config.max_fps, draw_points=config.save_video_points)
        callbacks.append(recorder)
    if config.preview:
        from .preview import show

        callbacks.append(show)
    preview = chain(callbacks)

    heartbeat = HeartbeatWriter(config.heartbeat) if config.heartbeat is not None else None
    progress = (lambda: heartbeat.beat("starting", time.monotonic())) if heartbeat is not None else None   # the pipeline's clock is time.monotonic too
    pipeline = TrackerPipeline(config, source, lambda cfg: make_detector(cfg, progress=progress), sender, preview=preview, heartbeat=heartbeat)
    source_name = config.video or f"camera {config.camera} ({resolve_camera_api(config.backend) or 'default'} api)"
    LOG.info("tracker started: %s -> udp://%s:%d (max %.0f fps)", source_name, LOOPBACK_HOST, config.port, config.max_fps)
    try:
        summary = pipeline.run()
    except KeyboardInterrupt:
        summary = pipeline.summary()
    finally:
        sender.close()
        if recorder is not None:
            recorder.close()
    LOG.info("tracker stopped: %d frames, %.1f fps, inference p50 %.1f ms", summary.frames, summary.fps, summary.infer_ms_p50)
    write_stats(config, summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
