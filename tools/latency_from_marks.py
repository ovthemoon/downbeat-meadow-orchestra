#!/usr/bin/env python3
"""Turns the frame numbers read off a slow-motion phone video into latencies in milliseconds.

usage: tools/latency_from_marks.py --csv marks.csv --fps 240 [--out result.json]

marks.csv has one row per strike (see docs/latency-method.md for how to read the numbers off the video) and the columns
    trial,hand_down_frame,sound_frame,flash_frame
all counted in pictures of the same video:
    hand_down_frame  the picture in which the hand's strike is at its lowest point (the moment the app reads as the strike)
    sound_frame      the first picture in which the sound can be seen in the sound wave of the video (or heard, with the speaker's own picture)
    flash_frame      the first picture in which the white square at the top right of the meadow is lit (--latency-mode)
For every complete row: hand to sound, hand to screen and screen minus sound, in milliseconds (frames / fps * 1000). A row with an empty or
non-numeric cell is skipped and counted. A sound that comes before the hand is a misread: it is left out, counted and named in the warnings.
Then the median, the 90th percentile (linear between neighbours, like numpy's default), the minimum and the maximum of each. The JSON goes to stdout
and, with --out, to a file. Python standard library only.
"""
import argparse
import csv
import json
import math
import sys
from pathlib import Path

COLUMNS = ("trial", "hand_down_frame", "sound_frame", "flash_frame")
WANTED_TRIALS = 30
ROUND = 9   # digits kept: enough that a frame count converts back exactly, few enough to read


class MarksError(Exception):
    """The input cannot be analysed; the message says why in words for the person who made the file."""


def percentile(values, p):
    """The p-th percentile (0..100) with linear interpolation between the two nearest values."""
    ordered = sorted(values)
    if not ordered:
        raise ValueError("no values")
    if len(ordered) == 1:
        return ordered[0]
    rank = (len(ordered) - 1) * p / 100.0
    low = math.floor(rank)
    high = math.ceil(rank)
    return ordered[low] + (ordered[high] - ordered[low]) * (rank - low)


def _number(text):
    """A finite number from a cell, or None when the cell is empty or not a number."""
    try:
        value = float((text or "").strip())
    except ValueError:
        return None
    return value if math.isfinite(value) else None


def _stats(values):
    return {
        "median": round(percentile(values, 50), ROUND),
        "p90": round(percentile(values, 90), ROUND),
        "max": round(max(values), ROUND),
        "min": round(min(values), ROUND),
    }


def analyze(csv_path, fps):
    if not (isinstance(fps, (int, float)) and math.isfinite(fps) and fps > 0):
        raise MarksError(f"fps must be a positive number (got {fps})")
    frame_ms = 1000.0 / fps
    try:
        handle = open(csv_path, newline="", encoding="utf-8-sig")
    except OSError as error:
        raise MarksError(f"cannot read {csv_path}: {error.strerror or error}") from error
    with handle:
        reader = csv.DictReader(handle)
        missing = [name for name in COLUMNS if name not in (reader.fieldnames or [])]
        if missing:
            raise MarksError(f"the columns {', '.join(missing)} are missing (the file needs: {', '.join(COLUMNS)})")
        trials = []
        skipped = 0
        negative = []
        for row in reader:
            hand = _number(row["hand_down_frame"])
            sound = _number(row["sound_frame"])
            flash = _number(row["flash_frame"])
            if hand is None or sound is None or flash is None:
                skipped += 1
                continue
            if sound < hand or flash < hand:
                negative.append((row["trial"] or "?").strip() or "?")
                continue
            trials.append({
                "trial": (row["trial"] or "").strip(),
                "hand_to_sound_ms": round((sound - hand) * frame_ms, ROUND),
                "hand_to_screen_ms": round((flash - hand) * frame_ms, ROUND),
                "screen_minus_sound_ms": round((flash - sound) * frame_ms, ROUND),
            })
    if not trials:
        raise MarksError("no complete row: every row has an empty or non-numeric cell, or a sound or flash before the hand")

    warnings = []
    if negative:
        warnings.append(f"{len(negative)} row(s) left out because the sound or the flash comes before the hand (a misread frame number): trial {', '.join(negative)}")
    if len(trials) < WANTED_TRIALS:
        warnings.append(f"only {len(trials)} usable trials; the method asks for {WANTED_TRIALS} or more so that the 90th percentile means something")
    return {
        "fps": fps,
        "frame_ms": round(frame_ms, ROUND),
        "trials_used": len(trials),
        "rows_skipped": skipped,
        "rows_negative": len(negative),
        "hand_to_sound_ms": _stats([t["hand_to_sound_ms"] for t in trials]),
        "hand_to_screen_ms": _stats([t["hand_to_screen_ms"] for t in trials]),
        "screen_minus_sound_ms": _stats([t["screen_minus_sound_ms"] for t in trials]),
        "warnings": warnings,
        "trials": trials,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--csv", required=True, help="the marks file")
    parser.add_argument("--fps", required=True, type=float, help="pictures per second of the video (240 for a phone's slow motion)")
    parser.add_argument("--out", help="also write the JSON to this file")
    args = parser.parse_args(argv)
    try:
        result = analyze(Path(args.csv), args.fps)
    except MarksError as error:
        print(f"latency_from_marks: {error}", file=sys.stderr)
        return 2
    text = json.dumps(result, indent=2, ensure_ascii=False)
    if args.out:
        Path(args.out).write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
