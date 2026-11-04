# downbeat-tracker

The hand tracker of the Downbeat meadow orchestra. It is the only program that touches the webcam. It runs MediaPipe Hand Landmarker
(Tasks API, VIDEO mode, 2 hands) and sends **only hand coordinates** to the Unity app over local UDP (`127.0.0.1:47800`, format
[hand-udp-v1](../docs/protocol/hand-udp-v1.md)). Camera frames are never sent anywhere, and saved only when asked with `--save-video` (a local file for demo videos).

**Network: MediaPipe sends usage telemetry to Google while the tracker runs, and there is no switch for it.** The tracker's own code goes
online only to fetch the hand model (setup, or a run that finds it missing). But the `mediapipe==0.10.35` wheels on PyPI are built inside
Google with a clearcut logger that posts to `https://play.googleapis.com/log` (strings in `mediapipe/tasks/c/libmediapipe.dll`); the open-source
tree builds a no-op logger instead (`mediapipe/tasks/cc/core/logging/factory/logging_factory.cc` at v0.10.35, created unconditionally by
`TaskRunner::Create`), and Python's `BaseOptions` hands the DLL certifi's CA bundle "to use for usage logging" (`mediapipe/tasks/c/core/base_options.h`).
What it records (field names of the `logs.proto.mediapipe.MediaPipeLogExtension` descriptor in the DLL): task (`TASKS_HANDLANDMARKER`) and
running mode, platform, MediaPipe and Python version (its app id/version fields have no input in the C API), and per session the start, init latency, invocation counts, average and
peak latency, elapsed time, dropped frames and error codes. Google says input data (frames) is never sent and that apps must tell their users
([MediaPipe Tasks privacy notice](https://developers.google.com/edge/mediapipe/solutions/tasks#mediapipe_tasks_privacy_notice));
the Korean guide does that in section 8 of `../docs/windows-hand-setup.md`. A MediaPipe maintainer declined to add an opt-out
([google-ai-edge/mediapipe#6291](https://github.com/google-ai-edge/mediapipe/issues/6291), 2026-07-06) and named the two ways out:
build MediaPipe from source, or block the host. Measured on the Windows laptop (2026-11-01, `--video` run, no camera, model already cached):
72 s after start the interpreter process held an HTTPS connection to `172.217.112.4:443`, one of the A records of `play.googleapis.com`,
and opened another one 17 min after start, right after the deferral below ran out. The stderr lines `portable_clearcut_uploader.cc:90] Failed
to send to clearcut: FAILED_PRECONDITION: Not valid for uploading until: …` (once a minute from 2-3 min after start, "until" about 16 min after
start in every logged run) are later uploads being deferred, not a sign that nothing was sent.

```sh
uv venv --python 3.12 .venv && uv pip install --python .venv/bin/python -e ".[dev]"    # Windows: .venv\Scripts\python.exe
.venv/bin/python -m pytest -q

# real camera
.venv/bin/python -m downbeat_tracker --camera 0 --port 47800
# a video file instead of a camera (tests, demos without a camera)
.venv/bin/python -m downbeat_tracker --video ../tests/fixtures/video/hand-strikes.mp4 --loop --port 47800
```

Windows: the one-time setup, the start script for tracker plus app, and the removal script are in `../tools/windows/`, explained in Korean in `../docs/windows-hand-setup.md`.

`requirements.lock.txt` is the universal lock (macOS arm64 and Windows amd64, Python 3.12) that the Windows setup installs from.
Why `mediapipe==0.10.35`: `../measurements/env/tracker-python.txt`. Measured speed on video: `../measurements/tracker/mac-video.json` (`tools/measure_tracker_video.py`).
The test video `../tests/fixtures/video/hand-strikes.mp4` is a picture of a hand (CC0 photo, see `../NOTICE`), not a person; no test opens a camera.

Options: `--camera N`, `--backend auto|dshow|msmf` (camera API: `auto` is DirectShow on Windows, because the default Media Foundation API can take many seconds to open a camera, and OpenCV's own choice elsewhere; measured on the Windows laptop with OpenCV 5.0: DirectShow gave the Integrated Webcam 15 fps at 640x480 YUY2 whatever FOURCC/FPS was asked, Media Foundation 30 fps, opening in 0.3 s with `OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS=0`, so the Korean guide's play command passes `-Backend msmf`), `--port P` (the target is always 127.0.0.1; there is no `--host`), `--video PATH`, `--loop`, `--preview` (debug window, nothing is saved), `--save-video PATH` (for demo videos: the camera picture, mirrored like the app, into a local `.mp4` or `.avi` at `--max-fps` frames a second in real time, a picture repeated while the tracker is slower; `--save-video-points` draws the 21 hand points and their bones in; the file is finished when the tracker ends cleanly, so a caller that would kill it passes `--stop-file PATH` and creates that file first, as `play-with-hands.ps1 -SaveVideo` does), `--max-fps 30`, `--log PATH` (rotated: 5 MB, 3 older files), `--heartbeat PATH` (a heartbeat file written from the frame loop every 2 s: line 1 the UTC time, line 2 `camera=starting|ok|lost|model_failed`, the first one written before the model is loaded and while a first run downloads it; with no camera the loop that opens it again keeps beating, a `read()` that never returns stops it, which is what `supervisor` watches),
`--max-seconds N` (stop after N s), `--stats-json PATH` (write the final numbers). The hand model (`hand_landmarker.task`) is fetched once from
Google's official storage into `~/.cache/downbeat/` and is not part of the repository.

With a camera, the first 3 s are a camera check (`downbeat_tracker/camera_check.py`): one log line `camera check: ok|slow|dark|none frames=N read_ms=R brightness=B`,
INFO when ok, WARNING otherwise. It exists because a camera that another program holds still opens: on Windows, OpenCV's DirectShow backend then returns an
almost black picture each time its 1 s wait for a frame runs out (`slow`), and Media Foundation fails every read (`none`). `play-with-hands.ps1` waits for this line.

The shared UDP fixtures (`../tests/fixtures/hand-udp-v1/`) are only compared by the tests. After a deliberate format change, rewrite them with `.venv/bin/python -m pytest -q tests/test_protocol.py --regen-fixtures` (and bump the protocol version).
