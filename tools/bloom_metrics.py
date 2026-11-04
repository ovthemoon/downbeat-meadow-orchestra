#!/usr/bin/env python3
"""Measures how much glow a meadow picture has and how big the biggest glows are.

usage: tools/bloom_metrics.py IMAGE [IMAGE ...] [--json OUT.json]

Only the field is measured: the rows from 56 % of the picture height down (the plate behind it, with its bright sun and sky, is a finished picture).
For each picture (RGB, any size):
  clippedShare       share of pixels with a channel at 250 or more (white-hot)
  brightShare        share of pixels with luminance 235 or more (the glow, halos included)
  glowCount          separate glows (bright pixels joined by a 3 px dilation)
  glowMaxWidthPct    widest glow, as a share of the picture width
  glowP95WidthPct    the 95th percentile of the glow widths
  glowsOver1_5Pct    how many glows are wider than 1.5 % of the picture width
  fieldMean          mean luminance (0..255) of the field
  fieldDetail        mean local contrast of the field (how readable the grass and flowers are; blown-out glow lowers it)
"""
import argparse
import json
import sys

import cv2
import numpy as np

BRIGHT = 235
CLIPPED = 250
JOIN_PX = 3
WIDE_PCT = 1.5
FIELD_FROM = 0.56


def luminance(rgb: np.ndarray) -> np.ndarray:
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def measure(path: str) -> dict:
    bgr = cv2.imread(path, cv2.IMREAD_COLOR)
    if bgr is None:
        raise SystemExit(f"cannot read {path}")
    rgb = bgr[..., ::-1].astype(np.float32)
    height, width = rgb.shape[:2]
    rgb = rgb[int(height * FIELD_FROM):]
    lum = luminance(rgb)
    bright = (lum >= BRIGHT).astype(np.uint8)
    joined = cv2.dilate(bright, np.ones((JOIN_PX, JOIN_PX), np.uint8))
    count, _, stats, _ = cv2.connectedComponentsWithStats(joined, connectivity=8)
    widths = np.array([stats[i, cv2.CC_STAT_WIDTH] for i in range(1, count)], dtype=np.float32) / width * 100.0
    lower = lum
    blurred = cv2.GaussianBlur(lower, (0, 0), 4.0)
    detail = float(np.mean(np.abs(lower - blurred)))
    return {
        "file": path, "size": [width, height],
        "clippedShare": round(float(np.mean(rgb.max(axis=2) >= CLIPPED)), 4),
        "brightShare": round(float(np.mean(lum >= BRIGHT)), 4),
        "glowCount": int(len(widths)),
        "glowMaxWidthPct": round(float(widths.max()), 2) if len(widths) else 0.0,
        "glowP95WidthPct": round(float(np.percentile(widths, 95)), 2) if len(widths) else 0.0,
        "glowsOver1_5Pct": int(np.sum(widths > WIDE_PCT)),
        "fieldMean": round(float(lower.mean()), 1),
        "fieldDetail": round(detail, 2),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("images", nargs="+")
    parser.add_argument("--json")
    args = parser.parse_args()
    results = [measure(p) for p in args.images]
    for r in results:
        print(json.dumps(r))
    if args.json:
        with open(args.json, "w") as handle:
            json.dump(results, handle, indent=2)
            handle.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
