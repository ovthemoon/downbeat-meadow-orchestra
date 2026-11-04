#!/usr/bin/env python3
"""Puts the pictures and the sound of a player run (RunRecorder: --record-dir) together into one MP4, by their clock stamps.

usage: tools/assemble_run_video.py RUN_DIR OUT.mp4 [--start SEC] [--seconds SEC] [--fps 24] [--maxrate 5M]

RUN_DIR holds audio.wav, frames/fNNNNN.jpg, frames.csv (index, visualNow) and recording.json. Every picture was stamped with the audio-clock time that was being
heard when it was drawn (VisualNow), and sample i of audio.wav was mixed at audioFirstSampleTime + i / rate, so the picture belongs at
    pts = visualNow - audioFirstSampleTime    (seconds on the WAV's own timeline).
The output has a constant frame rate; for every output instant the latest picture whose pts is not later than that instant is shown (a picture stays until the next
one is due), so pictures that the capture was too slow to make are repeated rather than shifted. Needs ffmpeg (set FFMPEG to a path if it is not on PATH).
A recording that the app ended early (recording.json says "endedEarly": true: the window was closed before --record-seconds) is shorter than planned and works the same way: without --seconds the video
is as long as the sound after --start (30 s at most), and a longer --seconds is cut to that.

Wall-clock times in recording.json (UTC, ISO 8601, milliseconds), for lining the video up with another recording by its own wall-clock stamp:
  startedUtc       when the audio thread mixed sample 0 of audio.wav. That is when the sound was made, not when it was heard: do not line videos up with this.
  outputLatencySec how long after being mixed a sample leaves the speakers (the engine's estimate from its buffers). The pictures are stamped on this heard timeline.
  videoZeroUtc     startedUtc + outputLatencySec: when second 0 of a video made with --start 0 is on the screen and in the speakers. Use this one.
The summary printed at the end passes these on and adds videoStartUtc = videoZeroUtc + --start: the wall clock of second 0 of the video that was just made.
What is left over is random and not corrected: startedUtc is only known to one audio block (about 20 ms), the Windows clock steps up to about 16 ms when its timer resolution is not raised
(about 1 ms on macOS), and the latency is an estimate. Altogether a few tens of milliseconds: enough to line up by eye, not frame exact.
"""
import argparse
import csv
import json
import os
import shutil
import subprocess
import sys
from bisect import bisect_right
from datetime import datetime, timedelta
from pathlib import Path


DEFAULT_SECONDS = 30.0


def load_meta(run: Path) -> dict:
    path = run / "recording.json"
    if not path.exists():
        raise SystemExit(f"{path} is missing: the app was closed before it wrote the recording (a build from before the early-finish fix keeps only the pictures), so there is no sound and no frame time to put together")
    return json.loads(path.read_text())


def available_seconds(meta: dict, start: float) -> float:
    """Seconds of sound after `start` (the sound is what ends first: pictures are repeated, sound cannot be). Unknown (infinite) when recording.json does not say."""
    frames, rate = meta.get("audioFrames"), meta.get("audioSampleRate")
    if frames is None or rate is None:
        return float("inf")
    if rate <= 0:
        raise SystemExit(f"recording.json has audioSampleRate {rate}: it is not a recording this tool can read")
    if frames <= 0:
        raise SystemExit("recording.json says audioFrames 0: the recording has no sound (the app was closed before the first audio block was mixed), so there is nothing to put a video to")
    return frames / rate - start


def resolve_seconds(requested, start: float, meta: dict) -> float:
    available = available_seconds(meta, start)
    if available <= 0:
        raise SystemExit(f"the recording has only {available + start:.2f} s of sound: --start {start:g} is past its end")
    wanted = DEFAULT_SECONDS if requested is None else requested
    if wanted > available:
        print(f"note: the recording has {available:.2f} s of sound after --start {start:g}: making {available:.2f} s, not {wanted:g} s", file=sys.stderr)
        return available
    return wanted


def shifted_utc(text, seconds: float):
    """`text` (UTC, ISO 8601 with milliseconds, as RunRecorder writes it) plus `seconds`, in the same format; None when recording.json has no such time (an older recording)."""
    if not text:
        return None
    try:
        moment = datetime.strptime(text, "%Y-%m-%dT%H:%M:%S.%fZ")
    except ValueError:
        raise SystemExit(f"recording.json has a time that is not like 2026-11-02T01:23:45.678Z: {text!r}")
    return (moment + timedelta(seconds=seconds)).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def load_pictures(run: Path, first_sample_time: float):
    frames_csv = run / "frames.csv"
    if not frames_csv.exists():
        raise SystemExit(f"{frames_csv} is missing: the recording has no frame times (it was made with --record-frames 0, or the app was closed before it wrote them)")
    with open(frames_csv, newline="") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise SystemExit(f"{frames_csv} has no pictures: the app was closed before the first one was taken")
    return [(float(r["visualNow"]) - first_sample_time, run / "frames" / f"f{int(r['index']):05d}.jpg") for r in rows]


def build_concat_list(pictures, start: float, seconds: float, fps: int, list_path: Path) -> dict:
    pts = [p for p, _ in pictures]
    step = 1.0 / fps
    count = int(round(seconds * fps))
    shown = []
    with open(list_path, "w") as out:
        for k in range(count):
            t = start + k * step
            i = bisect_right(pts, t) - 1
            if i < 0:
                raise SystemExit(f"no picture was drawn yet at {t:.3f} s (the first one is at {pts[0]:.3f} s): start later")
            shown.append(i)
            out.write(f"file '{pictures[i][1]}'\nduration {step:.6f}\n")
        out.write(f"file '{pictures[shown[-1]][1]}'\n")  # the concat demuxer needs the last file repeated
    unique = len(set(shown))
    return {"outputFrames": count, "distinctPictures": unique, "repeatedFrames": count - unique}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("run_dir")
    parser.add_argument("out")
    parser.add_argument("--start", type=float, default=0.0)
    parser.add_argument("--seconds", type=float, default=None, help=f"length of the video (default {DEFAULT_SECONDS:g}, or less when the sound after --start is shorter)")
    parser.add_argument("--fps", type=int, default=24)
    parser.add_argument("--maxrate", default="5M")
    args = parser.parse_args()

    ffmpeg = os.environ.get("FFMPEG") or shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
    run = Path(args.run_dir).resolve()
    meta = load_meta(run)
    seconds = resolve_seconds(args.seconds, args.start, meta)
    pictures = load_pictures(run, meta["audioFirstSampleTime"])
    out = Path(args.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    list_path = out.with_suffix(".concat.txt")
    stats = build_concat_list(pictures, args.start, seconds, args.fps, list_path)

    command = [
        ffmpeg, "-y", "-loglevel", "error",
        "-f", "concat", "-safe", "0", "-i", str(list_path),
        "-ss", f"{args.start:.6f}", "-t", f"{seconds:.6f}", "-i", str(run / "audio.wav"),
        "-map", "0:v", "-map", "1:a",
        "-vf", f"fps={args.fps},scale=1920:1080:flags=lanczos,format=yuv420p",
        "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-maxrate", args.maxrate, "-bufsize", "10M", "-movflags", "+faststart",
        "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-shortest",
        str(out),
    ]
    subprocess.run(command, check=True)
    list_path.unlink()
    size = out.stat().st_size
    print(json.dumps({"out": str(out), "bytes": size, "megabytes": round(size / 1e6, 2), "start": args.start, "seconds": round(seconds, 6), "fps": args.fps,
                      "endedEarly": bool(meta.get("endedEarly", False)), "startedUtc": meta.get("startedUtc"), "outputLatencySec": meta.get("outputLatencySec"),
                      "videoZeroUtc": meta.get("videoZeroUtc"), "videoStartUtc": shifted_utc(meta.get("videoZeroUtc"), args.start), **stats}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
