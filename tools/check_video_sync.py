#!/usr/bin/env python3
"""Measures, on the finished MP4 itself, how far the picture is from the sound.

usage: tools/check_video_sync.py VIDEO.mp4 [--json OUT.json] [--band 0.34 0.44]

The meadow gives two events that are drawn and played at the same moment and that are easy to find in a file:
  - picture: when a drum of the hills is heard, the haze over the far hills lights up at once (a jump in the brightness of a band of picture rows; the plate behind it
    does not move, so nothing else changes that band quickly),
  - sound: the drum itself, a low thump (110 Hz), found in the MP4's own audio track after a 140 Hz low-pass.
Every jump of the picture is paired with the nearest onset of the low band within +-150 ms (the swell of the sky pad also leaks a little below 140 Hz, about 250 ms after
its chord starts: such onsets have no picture jump and stay unpaired, they are counted in drumOnsetsInSound but not in pairs). The result is the list of differences (picture time minus sound time, ms) and their
median, spread and the number of pairs. A picture is shown for 1/fps, so differences between 0 and +1/fps are expected; a systematic offset of more than that would
mean the picture and the sound were put together wrong. Needs ffmpeg (FFMPEG env var or PATH) and opencv + numpy.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

import cv2
import numpy as np

LOWPASS_HZ = 140.0
ENVELOPE_SEC = 0.010
PAIR_WINDOW_SEC = 0.150
MIN_GAP_SEC = 0.25
JUMP_LEVELS = 2.0  # gray levels of brightness that make a picture jump
AUDIO_RATE = 48000


def decode_audio(video: str) -> np.ndarray:
    ffmpeg = os.environ.get("FFMPEG") or shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"
    raw = subprocess.run([ffmpeg, "-loglevel", "error", "-i", video, "-vn", "-ac", "1", "-ar", str(AUDIO_RATE), "-f", "f32le", "-"], check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype="<f4").astype(np.float64)


def drum_onsets(audio: np.ndarray) -> list:
    spectrum = np.fft.rfft(audio)
    freqs = np.fft.rfftfreq(len(audio), 1.0 / AUDIO_RATE)
    spectrum[freqs > LOWPASS_HZ] = 0
    low = np.fft.irfft(spectrum, len(audio))
    window = int(ENVELOPE_SEC * AUDIO_RATE)
    envelope = np.convolve(np.abs(low), np.ones(window) / window, mode="same")
    level = 0.35 * float(np.max(envelope))
    onsets = []
    above = envelope > level
    for i in np.nonzero(above[1:] & ~above[:-1])[0] + 1:
        t = i / AUDIO_RATE
        if not onsets or t - onsets[-1] >= MIN_GAP_SEC:
            onsets.append(t)
    return onsets


def picture_jumps(video: str, band: tuple):
    capture = cv2.VideoCapture(video)
    fps = capture.get(cv2.CAP_PROP_FPS)
    brightness = []
    while True:
        ok, frame = capture.read()
        if not ok:
            break
        h = frame.shape[0]
        rows = frame[int(band[0] * h):int(band[1] * h)]
        brightness.append(float(np.mean(cv2.cvtColor(rows, cv2.COLOR_BGR2GRAY))))
    capture.release()
    brightness = np.array(brightness)
    rises = np.diff(brightness)
    jumps = []
    for k in np.nonzero(rises > JUMP_LEVELS)[0] + 1:
        t = k / fps
        if not jumps or t - jumps[-1] >= MIN_GAP_SEC:
            jumps.append(t)
    return jumps, fps, brightness


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("video")
    parser.add_argument("--json")
    parser.add_argument("--band", type=float, nargs=2, default=(0.34, 0.44))
    args = parser.parse_args()

    onsets = drum_onsets(decode_audio(args.video))
    jumps, fps, brightness = picture_jumps(args.video, tuple(args.band))
    pairs = []
    for t in jumps:
        nearest = min(onsets, key=lambda o: abs(o - t), default=None)
        if nearest is not None and abs(nearest - t) <= PAIR_WINDOW_SEC:
            pairs.append((round(nearest, 4), round(t, 4), round((t - nearest) * 1000.0, 1)))
    lags = np.array([p[2] for p in pairs])
    result = {
        "video": args.video, "fps": fps, "frameMs": round(1000.0 / fps, 1), "band": list(args.band),
        "drumOnsetsInSound": len(onsets), "pictureJumps": len(jumps), "pairs": len(pairs),
        "lagMs": {
            "median": round(float(np.median(lags)), 1) if len(lags) else None,
            "min": round(float(np.min(lags)), 1) if len(lags) else None,
            "max": round(float(np.max(lags)), 1) if len(lags) else None,
        },
        "details": [{"soundSec": a, "pictureSec": b, "lagMs": c} for a, b, c in pairs],
        "note": "lag = time of the first picture frame that shows the haze jump minus time the drum starts in the file's own audio; 0..one frame is expected",
    }
    print(json.dumps(result, indent=2))
    if args.json:
        with open(args.json, "w") as handle:
            json.dump(result, handle, indent=2)
            handle.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
