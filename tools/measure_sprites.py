#!/usr/bin/env python3
"""Measures the stage sprites (alpha bounding box and foot anchor) so content.json does not need magic numbers.

Usage: python3 tools/measure_sprites.py archive/day2-stage/forest-img
Writes <dir>/sprites.json. The pairs are already foot-aligned (images-processed), so the idle picture defines the anchor.
"""
import json
import sys
from pathlib import Path

from PIL import Image

ALPHA_FLOOR = 16
FOOT_BAND_PX = 40
PAIRS = ("harp", "recorder", "marimba")


def measure(path: Path) -> dict:
    image = Image.open(path).convert("RGBA")
    mask = image.getchannel("A").point(lambda v: 255 if v > ALPHA_FLOOR else 0)
    box = mask.getbbox()
    if box is None:
        raise ValueError(f"{path.name}: no visible pixels")
    left, top, right, bottom = box
    band = mask.crop((0, bottom - FOOT_BAND_PX, image.width, bottom)).getbbox()
    foot_center_x = (band[0] + band[2]) / 2
    return {
        "width": image.width,
        "height": image.height,
        "bbox": [left, top, right, bottom],
        "bodyHeightPx": bottom - top,
        "bodyWidthPx": right - left,
        "pivotX": round(foot_center_x / image.width, 4),
        "pivotY": round((image.height - bottom) / image.height, 4),
    }


def main(folder: str) -> int:
    root = Path(folder)
    result = {}
    for name in PAIRS:
        idle, play = measure(root / f"{name}_idle.png"), measure(root / f"{name}_play.png")
        result[name] = {"idle": idle, "play": play, "feetAlignmentDxPx": round((idle["pivotX"] - play["pivotX"]) * idle["width"], 2),
                        "feetAlignmentDyPx": idle["bbox"][3] - play["bbox"][3]}
    (root / "sprites.json").write_text(json.dumps(result, indent=2) + "\n")
    for name, data in result.items():
        print(f"{name:9s} body {data['idle']['bodyWidthPx']}x{data['idle']['bodyHeightPx']} px, pivot ({data['idle']['pivotX']}, {data['idle']['pivotY']}), "
              f"idle/play foot offset dx {data['feetAlignmentDxPx']} dy {data['feetAlignmentDyPx']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
