#!/usr/bin/env python3
"""Checks picture against sound in the recorded demo: when does each musician's picture change in the mp4, compared with the
scheduled audio time of the nearest note (mapped to the video timeline through the browser's output timestamp)?

Rising brightness edges in the three performer regions (the play sprite + glow appear) are matched to the closest scheduled note.
Resolution is one video frame (33 ms at 30 fps) plus the screencast timestamp jitter, so this is a sanity check of the
recording, not a precise measurement (the precise one is the in-page log, tools/run_day1_measure.mjs).
Usage: python3 tools/check_demo_sync.py docs/media/day1-demo.mp4 measurements/day1/demo/demo-meta.json
"""
import json
import re
import statistics
import subprocess
import sys
import tempfile

REGIONS = {"harp": "crop=320:380:40:170", "recorder": "crop=280:380:400:190", "marimba": "crop=300:380:750:180"}
FPS = 30.0
RISE_THRESHOLD = 2.0
MATCH_WINDOW_SEC = 0.12


def brightness(video: str, crop: str) -> list:
    with tempfile.NamedTemporaryFile(suffix=".txt") as out:
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", video, "-vf",
                        f"{crop},signalstats,metadata=print:key=lavfi.signalstats.YAVG:file={out.name}", "-f", "null", "-"], check=True)
        return [float(m.group(1)) for m in re.finditer(r"YAVG=([\d.]+)", open(out.name).read())]


def rising_edges(values: list) -> list:
    return [i / FPS for i in range(1, len(values)) if values[i] - values[i - 1] > RISE_THRESHOLD and values[i - 1] - (values[i - 2] if i > 1 else values[i - 1]) <= RISE_THRESHOLD]


def main(video: str, meta_path: str) -> int:
    meta = json.load(open(meta_path))
    expected = [((meta["timeOriginMs"] + when * 1000 + meta["heardOffsetMs"]) / 1000) - meta["videoStartEpochSec"] for when in meta["scheduledAudioTimes"]]
    expected = [t for t in expected if 0 < t < meta["videoSeconds"]]
    edges = sorted(t for crop in REGIONS.values() for t in rising_edges(brightness(video, crop)))
    residuals = []
    for edge in edges:
        nearest = min(expected, key=lambda x: abs(x - edge))
        if abs(nearest - edge) <= MATCH_WINDOW_SEC:
            residuals.append((edge - nearest) * 1000)
    result = {"scheduledNotesInVideo": len(expected), "visualEdges": len(edges), "matched": len(residuals)}
    if residuals:
        ordered = sorted(residuals)
        result.update({"medianMs": statistics.median(ordered), "p10Ms": ordered[int(0.1 * (len(ordered) - 1))], "p90Ms": ordered[int(0.9 * (len(ordered) - 1))],
                       "minMs": ordered[0], "maxMs": ordered[-1], "note": "positive = the picture changes after the scheduled (heard) audio time"})
    print(json.dumps(result, indent=2))
    json.dump(result, open(meta_path.replace("demo-meta.json", "demo-sync.json"), "w"), indent=2)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
